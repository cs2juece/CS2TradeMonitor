using System.Text.Json;
using CS2QuantWeb.Core;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.Shared.Market;

namespace CS2TradeMonitor.Shared.QuantResearch;

public sealed class QuantResearchFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "QueryQuantResearchStatus";
    public const string StartBinding = "StartQuantResearchCore";
    public const string OpenBinding = "OpenQuantResearchWorkbench";
    public const string SearchBinding = "SearchQuantResearchItems";
    public const string RunBinding = "RunQuantResearch";
    public const string ServiceAddress = "app://cs2-trade-monitor/quant-research";

    private readonly ISettingsSnapshotStore _settings;
    private readonly IQuantResearchSeriesProvider _series;
    private readonly IQuantResearchModule _research;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MarketItemCandidate> _candidates = [];
    private QuantResearchRunProjection? _lastRun;
    private string _searchKeyword = string.Empty;
    private string _status = "研究工具已就绪";
    private string _statusDetail = "量化算法已在应用内运行，可直接开始研究。";
    private long _version;

    public QuantResearchFeatureModule(
        ISettingsSnapshotStore settings,
        IQuantResearchSeriesProvider series,
        IQuantResearchModule research)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _series = series ?? throw new ArgumentNullException(nameof(series));
        _research = research ?? throw new ArgumentNullException(nameof(research));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or StartBinding or OpenBinding or SearchBinding or RunBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var projection = new QuantResearchFeatureProjection(
                true,
                !string.IsNullOrWhiteSpace(snapshot.Settings.SteamDtApiKey),
                "应用内研究工具",
                ServiceAddress,
                _status,
                _statusDetail,
                _searchKeyword,
                _candidates,
                IndicatorCatalog.All.Select(item => new QuantResearchCatalogItemProjection(
                    item.Code,
                    item.Name,
                    item.Description)).ToArray(),
                StrategyCatalog.BuiltIns.Select(item => new QuantResearchCatalogItemProjection(
                    item.Id,
                    item.Name,
                    item.Description)).ToArray(),
                _lastRun);
            return new FeatureStateProjection(
                query.SemanticId,
                FeatureAvailability.Available,
                "ok",
                "量化研究状态已更新。",
                JsonSerializer.Serialize(projection),
                Math.Max(snapshot.Version, _version),
                DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.Equals(command.BindingName, QueryBinding, StringComparison.Ordinal))
        {
            return CoreCommandResult.Disabled(
                "quant-research.query-read-only",
                "量化研究查询不能作为写入命令执行。",
                command.CorrelationId,
                Volatile.Read(ref _version));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return command.BindingName switch
            {
                StartBinding => MarkReady(command),
                OpenBinding => MarkReady(command),
                SearchBinding => await SearchAsync(command, cancellationToken).ConfigureAwait(false),
                RunBinding => await RunAsync(command, cancellationToken).ConfigureAwait(false),
                _ => CoreCommandResult.Disabled(
                    "quant-research.binding-unavailable",
                    "该量化研究命令尚未注册。",
                    command.CorrelationId,
                    _version)
            };
        }
        catch (QuantResearchSeriesException exception)
        {
            _status = "数据读取失败";
            _statusDetail = exception.Message;
            return CoreCommandResult.NeedsUserAction(
                "quant-research.series-unavailable",
                exception.Message,
                command.CorrelationId,
                _version);
        }
        catch (ArgumentException exception)
        {
            _status = "输入需要调整";
            _statusDetail = exception.Message;
            return CoreCommandResult.NeedsUserAction(
                "quant-research.input-invalid",
                exception.Message,
                command.CorrelationId,
                _version);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);

    private CoreCommandResult MarkReady(FeatureCommand command)
    {
        _status = "研究工具已就绪";
        _statusDetail = "量化算法已加载，可直接开始研究。";
        long version = Interlocked.Increment(ref _version);
        return CoreCommandResult.Success(_statusDetail, command.CorrelationId, version);
    }

    private async Task<CoreCommandResult> SearchAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SearchQuantResearchItemsCommand request = Deserialize<SearchQuantResearchItemsCommand>(command.PayloadJson);
        string keyword = (request.Keyword ?? string.Empty).Trim();
        if (keyword.Length < 2)
            throw new ArgumentException("请输入至少 2 个字符搜索单品。", nameof(command));

        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MarketItemCandidate> candidates = await _series.SearchAsync(
            keyword,
            snapshot.Settings.SteamDtApiKey ?? string.Empty,
            cancellationToken).ConfigureAwait(false);
        _searchKeyword = keyword;
        _candidates = candidates.Take(30).ToArray();
        _status = _candidates.Count == 0 ? "没有找到单品" : "单品搜索完成";
        _statusDetail = _candidates.Count == 0
            ? "请尝试更完整的中文名或英文市场名。"
            : $"找到 {_candidates.Count} 个候选，请选择后运行研究。";
        long version = Interlocked.Increment(ref _version);
        return CoreCommandResult.Success(_statusDetail, command.CorrelationId, Math.Max(version, snapshot.Version));
    }

    private async Task<CoreCommandResult> RunAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        RunQuantResearchCommand request = Deserialize<RunQuantResearchCommand>(command.PayloadJson);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.Settings.SteamDtApiKey))
            throw new QuantResearchSeriesException("未配置 SteamDT API Key。请先到“大盘数据源”页面填写并保存。");

        _status = "正在运行研究";
        _statusDetail = "正在读取 K 线并执行桌面同源指标、缠论结构与回测。";
        QuantResearchSeries loaded = await _series.LoadItemAsync(
            request.MarketHashName,
            request.DisplayName,
            request.Range,
            snapshot.Settings.SteamDtApiKey,
            cancellationToken).ConfigureAwait(false);
        StrategyDefinition? strategy = string.IsNullOrWhiteSpace(request.StrategyId)
            ? null
            : StrategyCatalog.Get(request.StrategyId);
        var options = new ResearchAnalysisOptions(
            Indicators: IndicatorCatalog.DefaultSelections,
            Strategy: strategy,
            LockMode: request.TPlusSevenEnabled
                ? ExecutionLockMode.TPlusSeven
                : ExecutionLockMode.None);
        QuantResearchResult result = _research.Analyze(
            loaded.Symbol,
            loaded.Source,
            loaded.Candles,
            options) with
        { Interval = loaded.Interval };
        _lastRun = Project(result, request.TPlusSevenEnabled);
        _status = "研究已完成";
        _statusDetail = $"{result.Symbol} · {result.Summary.CandleCount} 根 K 线 · {result.ResultSummary.AvailableStrategyCount} 个可用策略";
        long version = Interlocked.Increment(ref _version);
        return CoreCommandResult.Success(_statusDetail, command.CorrelationId, Math.Max(version, snapshot.Version));
    }

    private static QuantResearchRunProjection Project(QuantResearchResult result, bool tPlusSevenEnabled)
    {
        QuantResearchSignalProjection[] signals = result.StrategySignals
            .Concat(result.Chan.Signals)
            .OrderByDescending(signal => signal.Date)
            .Take(40)
            .Select(signal => new QuantResearchSignalProjection(
                signal.Date.ToString("yyyy-MM-dd"),
                signal.Strategy,
                signal.Side switch
                {
                    SignalSide.Buy => "买入",
                    SignalSide.Sell => "卖出",
                    _ => "风险"
                },
                signal.Price,
                signal.Reason,
                signal.Level))
            .ToArray();
        return new QuantResearchRunProjection(
            result.Symbol,
            result.Source,
            result.Interval == CandleInterval.Week ? "周线" : "日线",
            result.ActiveStrategy?.Name ?? "默认研究策略",
            tPlusSevenEnabled,
            result.Summary.CandleCount,
            result.Summary.StartDate.ToString("yyyy-MM-dd"),
            result.Summary.EndDate.ToString("yyyy-MM-dd"),
            result.Summary.LatestClose,
            result.Summary.PeriodReturnPercent,
            result.Summary.MaxDrawdownPercent,
            result.ResultSummary.Status,
            result.ResultSummary.BestStrategy,
            result.ResultSummary.BestNetReturnPercent,
            result.ResultSummary.WeightedWinRatePercent,
            result.ResultSummary.WorstMaxDrawdownPercent,
            result.ResultSummary.AverageHoldingDays,
            result.ResultSummary.StableStrategyCount,
            result.Quality.IsUsable,
            result.Quality.Warnings.Select(warning => warning.Message).ToArray(),
            result.Backtests.Select(backtest => new QuantResearchBacktestProjection(
                backtest.Strategy,
                backtest.IsAvailable,
                backtest.Status,
                backtest.TradeCount,
                backtest.TotalReturnPercent,
                backtest.WinRatePercent,
                backtest.MaxDrawdownPercent,
                backtest.AverageHoldingDays,
                backtest.CostPercent,
                backtest.UnrealizedReturnPercent,
                backtest.OpenPositionCount,
                backtest.BlockedExitCount)).ToArray(),
            signals,
            result.Candles.TakeLast(180).Select(candle => new QuantResearchCandleProjection(
                candle.Date.ToString("yyyy-MM-dd"),
                candle.Open,
                candle.High,
                candle.Low,
                candle.Close,
                candle.Volume)).ToArray(),
            result.MethodNote,
            DateTimeOffset.UtcNow);
    }

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("请求内容为空或格式无效。", nameof(json));
}
