using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinGridTradingService : IYouPinGridTradingService, IDisposable
    {
        internal const int MaximumStrategyCount = 20;
        private static readonly TimeSpan QuoteFreshness = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan InventoryFreshness = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan MaximumFutureClockSkew = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan AutomaticRefreshInterval = TimeSpan.FromMinutes(1);
        private const string InventoryRefreshConsumerKey = "YouPinGridTrading";

        private readonly IYouPinGridStrategyStore _store;
        private readonly IYouPinGridMarketGateway _marketGateway;
        private readonly IYouPinInventoryService _inventoryService;
        private readonly IYouPinGridExecutionJournal? _executionJournal;
        private readonly YouPinGridExecutionModule? _executionModule;
        private readonly Func<DateTime> _now;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly object _stateLock = new();
        private YouPinGridState _state;
        private YouPinGridRuntimeSnapshot _snapshot = new();
        private Settings? _settings;
        private long _stateVersion;
        private int _backgroundRefreshActive;

        public YouPinGridTradingService(
            IYouPinGridStrategyStore store,
            IYouPinGridMarketGateway marketGateway,
            IYouPinInventoryService inventoryService)
            : this(store, marketGateway, inventoryService, null, null, () => DateTime.Now)
        {
        }

        public YouPinGridTradingService(
            IYouPinGridStrategyStore store,
            IYouPinGridMarketGateway marketGateway,
            IYouPinInventoryService inventoryService,
            IYouPinGridExecutionJournal executionJournal,
            YouPinGridExecutionModule executionModule)
            : this(
                store,
                marketGateway,
                inventoryService,
                executionJournal,
                executionModule,
                () => DateTime.Now)
        {
        }

        internal YouPinGridTradingService(
            IYouPinGridStrategyStore store,
            IYouPinGridMarketGateway marketGateway,
            IYouPinInventoryService inventoryService,
            Func<DateTime> now)
            : this(store, marketGateway, inventoryService, null, null, now)
        {
        }

        internal YouPinGridTradingService(
            IYouPinGridStrategyStore store,
            IYouPinGridMarketGateway marketGateway,
            IYouPinInventoryService inventoryService,
            IYouPinGridExecutionJournal? executionJournal,
            YouPinGridExecutionModule? executionModule,
            Func<DateTime> now)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _marketGateway = marketGateway ?? throw new ArgumentNullException(nameof(marketGateway));
            _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
            _executionJournal = executionJournal;
            _executionModule = executionModule;
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _state = CloneState(_store.Load());
            _snapshot = BuildUnrefreshedSnapshot(_state.Strategies);
            _inventoryService.DataUpdated += OnInventoryDataUpdated;
        }

        public event Action? DataUpdated;

        public void Configure(Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            lock (_stateLock)
            {
                _settings = settings;
                UpdateInventoryRefreshRegistrationLocked();
            }
        }

        public YouPinGridRuntimeSnapshot GetSnapshot()
        {
            lock (_stateLock)
                return _snapshot;
        }

        public async Task<YouPinGridRuntimeSnapshot> RefreshAsync(
            Settings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Configure(settings);
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                YouPinGridStrategy[] strategies;
                long stateVersion;
                lock (_stateLock)
                {
                    strategies = _state.Strategies.Select(CloneStrategy).ToArray();
                    stateVersion = _stateVersion;
                }

                YouPinInventoryState inventory = _inventoryService.GetState();
                bool inventoryFresh = IsInventoryFresh(inventory, _now());
                var rows = new List<YouPinGridStrategySnapshot>(strategies.Length);
                foreach (YouPinGridStrategy initialStrategy in strategies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    YouPinGridStrategy strategy = initialStrategy;
                    int holdings = inventory.Items
                        .Where(item =>
                            string.Equals(item.TemplateId, strategy.TemplateId, StringComparison.Ordinal)
                            && string.Equals(item.Name, strategy.ItemName, StringComparison.Ordinal))
                        .Sum(item => Math.Max(1, item.Quantity));
                    YouPinGridExecutionRecord? activeExecution = _executionJournal?.FindActive(strategy.Id);

                    YouPinGridMarketQuote quote;
                    try
                    {
                        quote = await _marketGateway.ReadLowestValidListingAsync(
                            settings,
                            strategy,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        quote = new YouPinGridMarketQuote
                        {
                            TemplateId = strategy.TemplateId,
                            ItemName = strategy.ItemName,
                            CapturedAt = _now(),
                            Message = YouPinMobileApiClient.Sanitize(ex.Message)
                        };
                    }

                    TimeSpan quoteAge = _now() - quote.CapturedAt;
                    bool fresh = quote.Available
                        && quote.CapturedAt != DateTime.MinValue
                        && quoteAge >= -MaximumFutureClockSkew
                        && quoteAge <= QuoteFreshness;
                    bool evaluationFresh = fresh && (strategy.ObserveOnly || inventoryFresh);
                    YouPinGridPlan plan = YouPinGridTradingPlanner.Plan(
                        strategy,
                        new YouPinGridEvaluationInput
                        {
                            ObservationPrice = quote.LowestPrice,
                            AvailableHoldings = holdings,
                            ReservedCapital = holdings * strategy.BasePrice,
                            MarketFresh = evaluationFresh,
                            HasPendingOrder = activeExecution != null
                        });
                    YouPinGridExecutionOutcome execution = BuildExecutionOutcome(
                        activeExecution ?? _executionJournal?.FindLatest(strategy.Id));
                    if (_executionModule != null
                        && (activeExecution != null
                            || (strategy.Enabled
                                && !strategy.ObserveOnly
                                && plan.Action != YouPinGridAction.None)))
                    {
                        execution = await _executionModule.ExecuteOrReconcileAsync(
                            settings,
                            strategy,
                            plan,
                            cancellationToken).ConfigureAwait(false);
                        if (execution.CompletedBasePrice is decimal completedPrice && completedPrice > 0m)
                        {
                            if (TryAdvanceBasePrice(
                                    strategy,
                                    completedPrice,
                                    out YouPinGridStrategy advanced,
                                    out long advancedVersion))
                            {
                                strategy = advanced;
                                stateVersion = advancedVersion;
                                plan = YouPinGridTradingPlanner.Plan(
                                    strategy,
                                    new YouPinGridEvaluationInput
                                    {
                                        ObservationPrice = quote.LowestPrice,
                                        AvailableHoldings = holdings,
                                        ReservedCapital = holdings * strategy.BasePrice,
                                        MarketFresh = evaluationFresh
                                    });
                            }
                            else
                            {
                                execution = MarkBasePricePersistenceFailure(strategy, execution);
                            }
                        }
                    }
                    rows.Add(new YouPinGridStrategySnapshot
                    {
                        Strategy = CloneStrategy(strategy),
                        MarketQuote = quote,
                        Plan = plan,
                        Execution = execution,
                        Holdings = holdings,
                        Status = BuildRowStatus(strategy, quote, plan, execution, inventoryFresh)
                    });
                }

                DateTime refreshedAt = _now();
                var snapshot = new YouPinGridRuntimeSnapshot
                {
                    Strategies = rows.ToArray(),
                    LastRefreshAt = refreshedAt,
                    EnabledCount = rows.Count(row => row.Strategy.Enabled),
                    TriggeredCount = rows.Count(row => row.Plan.Action != YouPinGridAction.None),
                    UnavailableCount = rows.Count(row => !row.MarketQuote.Available),
                    Status = rows.Count == 0
                        ? "还没有网格策略"
                        : $"已刷新 {rows.Count} 条策略"
                };
                bool published;
                YouPinGridRuntimeSnapshot result;
                lock (_stateLock)
                {
                    published = stateVersion == _stateVersion;
                    if (published)
                    {
                        _snapshot = snapshot;
                        UpdateInventoryRefreshRegistrationLocked();
                    }
                    result = _snapshot;
                }
                if (published)
                    DataUpdated?.Invoke();
                return result;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public async Task<YouPinGridMutationResult> UpsertStrategyAsync(
            YouPinGridStrategy strategy,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            cancellationToken.ThrowIfCancellationRequested();
            string validation = Validate(strategy);
            if (validation.Length > 0)
                return YouPinGridMutationResult.Failure(validation);

            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            YouPinGridMutationResult result;
            try
            {
                lock (_stateLock)
                    result = UpsertStrategyLocked(strategy);
            }
            finally
            {
                _refreshGate.Release();
            }

            if (result.Succeeded)
                DataUpdated?.Invoke();
            return result;
        }

        public async Task<YouPinGridMutationResult> DeleteStrategyAsync(
            string strategyId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = (strategyId ?? string.Empty).Trim();
            if (id.Length == 0)
                return YouPinGridMutationResult.Failure("未指定要删除的策略。");

            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            YouPinGridMutationResult result;
            try
            {
                lock (_stateLock)
                    result = DeleteStrategyLocked(id);
            }
            finally
            {
                _refreshGate.Release();
            }

            if (result.Succeeded)
                DataUpdated?.Invoke();
            return result;
        }

        private YouPinGridMutationResult UpsertStrategyLocked(YouPinGridStrategy strategy)
        {
            YouPinGridState candidate = CloneState(_state);
            YouPinGridStrategy copy = CloneStrategy(strategy);
            if (string.IsNullOrWhiteSpace(copy.Id))
                copy.Id = Guid.NewGuid().ToString("N");
            copy.ItemName = copy.ItemName.Trim();
            copy.TemplateId = copy.TemplateId.Trim();
            bool duplicateTarget = candidate.Strategies.Any(item =>
                !string.Equals(item.Id, copy.Id, StringComparison.Ordinal)
                && string.Equals(item.TemplateId.Trim(), copy.TemplateId.Trim(), StringComparison.Ordinal)
                && string.Equals(item.ItemName.Trim(), copy.ItemName.Trim(), StringComparison.Ordinal));
            if (duplicateTarget)
                return YouPinGridMutationResult.Failure("同款饰品只能创建一条交易网格策略。");

            int index = candidate.Strategies.FindIndex(item =>
                string.Equals(item.Id, copy.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                YouPinGridExecutionRecord? activeExecution = _executionJournal?.FindActive(copy.Id);
                if (activeExecution != null
                    && !HasSameExecutionDefinition(candidate.Strategies[index], copy))
                {
                    return YouPinGridMutationResult.Failure(
                        "该策略仍有悠悠订单等待回读或需要人工核对，暂不能修改交易参数。");
                }
                candidate.Strategies[index] = copy;
            }
            else
            {
                if (_executionJournal?.FindActive(copy.Id) != null)
                {
                    return YouPinGridMutationResult.Failure(
                        "该策略仍有悠悠订单等待回读或需要人工核对，暂不能重新创建。");
                }
                if (candidate.Strategies.Count >= MaximumStrategyCount)
                {
                    return YouPinGridMutationResult.Failure(
                        $"交易网格最多保留 {MaximumStrategyCount} 条策略。");
                }
                candidate.Strategies.Add(copy);
            }

            if (!_store.Save(candidate))
                return YouPinGridMutationResult.Failure("交易网格保存失败，请查看日志。");
            _state = candidate;
            _stateVersion++;
            _snapshot = BuildUnrefreshedSnapshot(_state.Strategies);
            UpdateInventoryRefreshRegistrationLocked();
            return YouPinGridMutationResult.Success("交易网格策略已保存。");
        }

        private YouPinGridMutationResult DeleteStrategyLocked(string id)
        {
            YouPinGridState candidate = CloneState(_state);
            int removed = candidate.Strategies.RemoveAll(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
            YouPinGridExecutionRecord? activeExecution = _executionJournal?.FindActive(id);
            if (activeExecution != null)
            {
                return YouPinGridMutationResult.Failure(
                    "该策略仍有悠悠订单等待回读或需要人工核对，暂不能删除。");
            }
            if (removed == 0)
                return YouPinGridMutationResult.Failure("没有找到该交易网格策略。");
            if (!_store.Save(candidate))
                return YouPinGridMutationResult.Failure("删除后保存失败，请查看日志。");
            _state = candidate;
            _stateVersion++;
            _snapshot = BuildUnrefreshedSnapshot(_state.Strategies);
            UpdateInventoryRefreshRegistrationLocked();
            return YouPinGridMutationResult.Success("交易网格策略已删除。");
        }

        private static string Validate(YouPinGridStrategy strategy)
        {
            if (string.IsNullOrWhiteSpace(strategy.ItemName))
                return "请填写完整饰品名称。";
            if (string.IsNullOrWhiteSpace(strategy.TemplateId))
                return "请填写悠悠模板 ID。";
            if (strategy.BasePrice <= 0m)
                return "基准价必须大于 0。";
            if (strategy.GridPercent <= 0m || strategy.GridPercent >= 100m)
                return "网格比例必须大于 0 且小于 100%。";
            if (strategy.QuantityPerGrid <= 0)
                return "每格数量必须至少为 1 件。";
            if (strategy.MinimumPrice < 0m || strategy.MaximumPrice < 0m || strategy.MaxCapital < 0m)
                return "价格和资金限制不能为负数。";
            if (strategy.MaximumPrice > 0m
                && strategy.MinimumPrice > 0m
                && strategy.MaximumPrice < strategy.MinimumPrice)
                return "有效最高价不能低于有效最低价。";
            if (strategy.MinimumHoldings < 0 || strategy.MaxHoldings < 1)
                return "持有件数设置超出允许范围。";
            if (strategy.MaxHoldings < strategy.MinimumHoldings)
                return "最大持有件数不能小于最低持有件数。";
            if (strategy.MaxBatchQuantity < 1)
                return "跨格最大批量必须至少为 1 件。";
            return "";
        }

        private static string BuildRowStatus(
            YouPinGridStrategy strategy,
            YouPinGridMarketQuote quote,
            YouPinGridPlan plan,
            YouPinGridExecutionOutcome execution,
            bool inventoryFresh)
        {
            if (execution.Stage != YouPinGridExecutionStage.None
                && !string.IsNullOrWhiteSpace(execution.Message))
                return execution.Message;
            if (!quote.Available)
                return quote.Message;
            if (!strategy.Enabled)
                return "策略未启用，仅展示观察结果";
            if (!strategy.ObserveOnly && !inventoryFresh)
                return "悠悠库存尚未完成最新回读，本轮不会执行真实买卖";
            if (strategy.ObserveOnly && plan.Action != YouPinGridAction.None)
                return "已触发预演，当前不会提交真实订单";
            return plan.Message;
        }

        private static bool IsInventoryFresh(YouPinInventoryState inventory, DateTime now)
        {
            TimeSpan age = now - inventory.LastFetch;
            return inventory.LastFetch != DateTime.MinValue
                && string.IsNullOrWhiteSpace(inventory.LastError)
                && age >= -MaximumFutureClockSkew
                && age <= InventoryFreshness;
        }

        private bool TryAdvanceBasePrice(
            YouPinGridStrategy strategy,
            decimal completedPrice,
            out YouPinGridStrategy advanced,
            out long stateVersion)
        {
            lock (_stateLock)
            {
                YouPinGridStrategy? current = _state.Strategies.FirstOrDefault(item =>
                    string.Equals(item.Id, strategy.Id, StringComparison.Ordinal));
                if (current == null || current.BasePrice != strategy.BasePrice)
                {
                    advanced = CloneStrategy(strategy);
                    stateVersion = _stateVersion;
                    return false;
                }

                YouPinGridState candidate = CloneState(_state);
                YouPinGridStrategy target = candidate.Strategies.First(item =>
                    string.Equals(item.Id, strategy.Id, StringComparison.Ordinal));
                target.BasePrice = completedPrice;
                if (!_store.Save(candidate))
                {
                    advanced = CloneStrategy(strategy);
                    stateVersion = _stateVersion;
                    return false;
                }

                _state = candidate;
                _stateVersion++;
                advanced = CloneStrategy(target);
                stateVersion = _stateVersion;
                return true;
            }
        }

        private YouPinGridExecutionOutcome MarkBasePricePersistenceFailure(
            YouPinGridStrategy strategy,
            YouPinGridExecutionOutcome execution)
        {
            YouPinGridExecutionRecord? record = _executionJournal?.FindLatest(strategy.Id);
            if (record != null)
            {
                record.Stage = YouPinGridExecutionStage.RequiresManualReview;
                record.Message = "悠悠订单已完成，但新基准价保存失败，策略已停止自动执行";
                record.UpdatedAt = _now();
                _executionJournal?.Save(record);
            }
            return new YouPinGridExecutionOutcome
            {
                Stage = YouPinGridExecutionStage.RequiresManualReview,
                Action = execution.Action,
                Quantity = execution.Quantity,
                UnitPrice = execution.UnitPrice,
                Message = "悠悠订单已完成，但新基准价保存失败，策略已停止自动执行"
            };
        }

        private static YouPinGridExecutionOutcome BuildExecutionOutcome(YouPinGridExecutionRecord? record)
        {
            if (record == null)
                return new YouPinGridExecutionOutcome();
            return new YouPinGridExecutionOutcome
            {
                Stage = record.Stage,
                Action = record.Action,
                Quantity = record.Quantity,
                UnitPrice = record.UnitPrice,
                Message = record.Message
            };
        }

        private void UpdateInventoryRefreshRegistrationLocked()
        {
            bool shouldRun = _executionModule != null
                && _settings != null
                && HasBackgroundWorkLocked();
            _inventoryService.SetBackgroundRefreshConsumer(
                InventoryRefreshConsumerKey,
                shouldRun ? AutomaticRefreshInterval : null);
        }

        private void OnInventoryDataUpdated()
        {
            StartBackgroundRefresh();
        }

        private void StartBackgroundRefresh()
        {
            Settings? settings;
            bool hasBackgroundWork;
            lock (_stateLock)
            {
                settings = _settings;
                hasBackgroundWork = HasBackgroundWorkLocked();
            }
            if (settings == null || !hasBackgroundWork
                || Interlocked.Exchange(ref _backgroundRefreshActive, 1) != 0)
                return;

            _ = RunBackgroundRefreshAsync(settings);
        }

        private bool HasBackgroundWorkLocked()
        {
            return _state.Strategies.Any(strategy =>
                (strategy.Enabled && !strategy.ObserveOnly)
                || _executionJournal?.FindActive(strategy.Id)?.Stage is
                    YouPinGridExecutionStage.Prepared or YouPinGridExecutionStage.AwaitingSettlement);
        }

        private static bool HasSameExecutionDefinition(
            YouPinGridStrategy current,
            YouPinGridStrategy candidate)
        {
            return string.Equals(current.ItemName.Trim(), candidate.ItemName.Trim(), StringComparison.Ordinal)
                && string.Equals(current.TemplateId.Trim(), candidate.TemplateId.Trim(), StringComparison.Ordinal)
                && current.BasePrice == candidate.BasePrice
                && current.GridPercent == candidate.GridPercent
                && current.QuantityPerGrid == candidate.QuantityPerGrid
                && current.MinimumPrice == candidate.MinimumPrice
                && current.MaximumPrice == candidate.MaximumPrice
                && current.MinimumHoldings == candidate.MinimumHoldings
                && current.MaxHoldings == candidate.MaxHoldings
                && current.MaxCapital == candidate.MaxCapital
                && current.CrossGridMultiplierEnabled == candidate.CrossGridMultiplierEnabled
                && current.MaxBatchQuantity == candidate.MaxBatchQuantity;
        }

        private async Task RunBackgroundRefreshAsync(Settings settings)
        {
            try
            {
                await RefreshAsync(settings).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error(
                    "YouPinGrid",
                    "悠悠交易网格后台检查失败：" + YouPinMobileApiClient.Sanitize(ex.Message));
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundRefreshActive, 0);
            }
        }

        private static YouPinGridRuntimeSnapshot BuildUnrefreshedSnapshot(
            IEnumerable<YouPinGridStrategy> strategies)
        {
            YouPinGridStrategySnapshot[] rows = strategies
                .Select(strategy => new YouPinGridStrategySnapshot
                {
                    Strategy = CloneStrategy(strategy),
                    Status = "等待刷新悠悠同款在售价"
                })
                .ToArray();
            return new YouPinGridRuntimeSnapshot
            {
                Strategies = rows,
                EnabledCount = rows.Count(row => row.Strategy.Enabled),
                Status = rows.Length == 0 ? "还没有网格策略" : "等待刷新"
            };
        }

        private static YouPinGridState CloneState(YouPinGridState? state)
        {
            return new YouPinGridState
            {
                SchemaVersion = state?.SchemaVersion ?? 1,
                Strategies = (state?.Strategies ?? new List<YouPinGridStrategy>())
                    .Select(CloneStrategy)
                    .ToList()
            };
        }

        private static YouPinGridStrategy CloneStrategy(YouPinGridStrategy strategy)
        {
            return new YouPinGridStrategy
            {
                Id = strategy.Id,
                ItemName = strategy.ItemName,
                TemplateId = strategy.TemplateId,
                Enabled = strategy.Enabled,
                ObserveOnly = strategy.ObserveOnly,
                BasePrice = strategy.BasePrice,
                GridPercent = strategy.GridPercent,
                QuantityPerGrid = strategy.QuantityPerGrid,
                MinimumPrice = strategy.MinimumPrice,
                MaximumPrice = strategy.MaximumPrice,
                MinimumHoldings = strategy.MinimumHoldings,
                MaxHoldings = strategy.MaxHoldings,
                MaxCapital = strategy.MaxCapital,
                CrossGridMultiplierEnabled = strategy.CrossGridMultiplierEnabled,
                MaxBatchQuantity = strategy.MaxBatchQuantity
            };
        }

        public void Dispose()
        {
            _inventoryService.DataUpdated -= OnInventoryDataUpdated;
            lock (_stateLock)
                _inventoryService.SetBackgroundRefreshConsumer(InventoryRefreshConsumerKey, null);
            _refreshGate.Dispose();
        }
    }
}
