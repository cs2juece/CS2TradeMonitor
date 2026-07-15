using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using System.Globalization;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinLandlordPagePresenter
    {
        private readonly IYouPinLandlordAutomation _automation;

        public YouPinLandlordPagePresenter()
            : this(YouPinPageRuntimeServices.Resolve().LandlordAutomation)
        {
        }

        internal YouPinLandlordPagePresenter(IYouPinLandlordAutomation automation)
        {
            _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        }

        public event Action? SnapshotChanged
        {
            add => _automation.SnapshotChanged += value;
            remove => _automation.SnapshotChanged -= value;
        }

        public void Configure(Settings settings) => _automation.Configure(settings);

        public YouPinLandlordPolicy ApplyPolicy(YouPinLandlordPolicy policy)
            => _automation.ApplyPolicy(policy);

        public YouPinLandlordPolicy GetPolicy() => _automation.GetPolicy();

        public YouPinLandlordPageStateViewModel GetPageState(
            YouPinRentalScanScope scope = YouPinRentalScanScope.All)
        {
            YouPinLandlordPageStateViewModel state = BuildPageState(
                _automation.GetSnapshot(),
                _automation.GetPolicy(),
                scope);
            YouPinLandlordAuditHealth health = _automation.GetAuditHealth();
            return health.IsHealthy
                ? state
                : state with { StatusText = state.StatusText + " · 审计日志写入异常" };
        }

        public Task<YouPinLandlordRunResult> RunNowAsync(
            YouPinRentalShelfType rentalType,
            string trigger,
            CancellationToken cancellationToken)
        {
            return _automation.RunRentalTypeNowAsync(
                rentalType,
                trigger,
                cancellationToken);
        }

        public Task<YouPinLandlordRunResult> RunInventoryNowAsync(
            string trigger,
            CancellationToken cancellationToken)
        {
            return _automation.RunInventoryNowAsync(trigger, cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ScanRentalNowAsync(
            YouPinRentalScanScope scope,
            string trigger,
            CancellationToken cancellationToken)
        {
            return _automation.ScanRentalNowAsync(scope, trigger, cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ExecuteRentalNowAsync(
            YouPinRentalScanScope scope,
            string trigger,
            CancellationToken cancellationToken)
        {
            return _automation.ExecuteRentalNowAsync(scope, trigger, cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ScanInventoryNowAsync(
            string trigger,
            CancellationToken cancellationToken)
        {
            return _automation.ScanInventoryNowAsync(trigger, cancellationToken);
        }

        public Task<YouPinLandlordRunResult> ExecuteInventoryNowAsync(
            string trigger,
            CancellationToken cancellationToken)
        {
            return _automation.ExecuteInventoryNowAsync(trigger, cancellationToken);
        }

        public async Task<string> RefreshPricingPreferenceAsync(CancellationToken cancellationToken)
        {
            await _automation.RefreshPricingPreferenceAsync(cancellationToken);
            return BuildPricingPreferenceText();
        }

        public YouPinLandlordInventoryPageStateViewModel GetInventoryPageState()
        {
            YouPinLandlordInventoryPageStateViewModel state = BuildInventoryPageState(
                _automation.GetSnapshot(),
                _automation.GetPolicy().InventoryAutoRent);
            YouPinLandlordAuditHealth health = _automation.GetAuditHealth();
            return health.IsHealthy
                ? state
                : state with { StatusText = state.StatusText + " · 审计日志写入异常" };
        }

        public string BuildInventoryOperationText()
        {
            YouPinLandlordOperationRecord[] records = _automation
                .GetSnapshot()
                .RecentOperations
                .Where(record => record.Workflow == YouPinLandlordWorkflow.InventoryAutoRent)
                .TakeLast(30)
                .Reverse()
                .ToArray();
            if (records.Length == 0)
                return "暂无库存自动出租扫描记录。";

            return string.Join(
                "\r\n\r\n",
                records.Select(record =>
                {
                    string runId = record.RunId.Length > 8 ? record.RunId[..8] : record.RunId;
                    string actionId = record.ActionId.Length > 8 ? record.ActionId[..8] : record.ActionId;
                    string item = string.IsNullOrWhiteSpace(record.ItemName) ? "整体任务" : record.ItemName;
                    string runMode = record.RunMode == YouPinLandlordRunMode.ScanOnly
                        ? "只读扫描"
                        : "执行";
                    return $"{record.Time:MM-dd HH:mm:ss}  {runMode}  Run={runId}  Action={actionId}\r\n"
                        + $"{item} · {record.Result} · {record.Message}";
                }));
        }

        public string BuildInventoryExecutionQueueText()
        {
            YouPinLandlordPlannedAction[] actions = _automation
                .GetSnapshot()
                .CurrentPlan
                .Actions
                .Where(action => action.Workflow == YouPinLandlordWorkflow.InventoryAutoRent)
                .ToArray();
            if (actions.Length == 0)
                return "当前没有库存自动出租队列。完成一次扫描后，这里会显示逐件处理状态。";

            return string.Join(
                "\r\n\r\n",
                actions.Reverse().Select(action =>
                {
                    string actionId = action.ActionId.Length > 8 ? action.ActionId[..8] : action.ActionId;
                    string price = action.TargetShortRent.HasValue
                        ? $" · 目标短租 ¥{action.TargetShortRent:0.##}"
                        : string.Empty;
                    return $"{FormatActionState(action.State)} · {action.ItemName}{price}\r\n"
                        + $"Action={actionId} · {action.Reason}";
                }));
        }

        public Task<IReadOnlyList<YouPinLandlordOperationRecord>> QueryHistoryAsync(
            YouPinLandlordAuditQuery query,
            CancellationToken cancellationToken)
        {
            return _automation.QueryHistoryAsync(query, cancellationToken);
        }

        public YouPinLandlordAuditHealth GetAuditHealth() => _automation.GetAuditHealth();

        public static string FormatHistoryRecords(IReadOnlyList<YouPinLandlordOperationRecord> records)
        {
            if (records.Count == 0)
                return "没有符合筛选条件的包租公操作记录。";
            return string.Join(
                "\r\n\r\n",
                records.Select(record =>
                {
                    string runId = record.RunId.Length > 12 ? record.RunId[..12] : record.RunId;
                    string actionId = record.ActionId.Length > 12 ? record.ActionId[..12] : record.ActionId;
                    string workflow = record.Workflow == YouPinLandlordWorkflow.RentalReprice
                        ? "租赁自动改价"
                        : "库存自动出租";
                    string runMode = record.RunMode == YouPinLandlordRunMode.ScanOnly
                        ? "只读扫描"
                        : "执行";
                    string rentalType = record.RentalType switch
                    {
                        YouPinRentalShelfType.ZeroCd => "0CD",
                        YouPinRentalShelfType.InventoryRental => "库存出租",
                        _ => "整体"
                    };
                    string item = string.IsNullOrWhiteSpace(record.ItemName) ? "整体任务" : record.ItemName;
                    return $"{record.Time:yyyy-MM-dd HH:mm:ss} · {workflow} · {rentalType} · {runMode} · {record.Stage}\r\n"
                        + $"{item}\r\n{record.Result} · {record.Message}\r\n"
                        + $"Run={runId}  Action={actionId}  任务累计耗时={record.ElapsedMilliseconds} ms";
                }));
        }

        public Task<YouPinLandlordRunResult> RunNowAsync(
            YouPinRentalScanScope scope,
            string trigger,
            CancellationToken cancellationToken)
        {
            return scope == YouPinRentalScanScope.All
                ? _automation.RunNowAsync(
                    YouPinLandlordWorkflow.RentalReprice,
                    trigger,
                    cancellationToken)
                : RunNowAsync(
                    scope == YouPinRentalScanScope.ZeroCd
                        ? YouPinRentalShelfType.ZeroCd
                        : YouPinRentalShelfType.InventoryRental,
                    trigger,
                    cancellationToken);
        }

        public string BuildRecentOperationText(YouPinRentalScanScope scope = YouPinRentalScanScope.All)
        {
            YouPinLandlordSnapshot snapshot = _automation.GetSnapshot();
            YouPinLandlordOperationRecord[] records = snapshot.RecentOperations
                .Where(record => !record.RentalType.HasValue || ScopeContains(scope, record.RentalType.Value))
                .TakeLast(20)
                .Reverse()
                .ToArray();
            if (records.Length == 0)
                return "暂无包租公运行记录。完成一次检查后，这里会显示关联的运行与判断结果。";

            var lines = new List<string>();
            foreach (YouPinLandlordOperationRecord record in records)
            {
                string runId = record.RunId.Length > 8 ? record.RunId[..8] : record.RunId;
                string item = string.IsNullOrWhiteSpace(record.ItemName) ? "整体任务" : record.ItemName;
                string runMode = record.RunMode == YouPinLandlordRunMode.ScanOnly
                    ? "只读扫描"
                    : "执行";
                lines.Add(
                    $"{record.Time:MM-dd HH:mm:ss}  {runMode}  Run={runId}  {record.Stage}  {item}\r\n"
                    + $"{record.Result} · {record.Message} · {record.ElapsedMilliseconds} ms");
            }

            return string.Join("\r\n\r\n", lines);
        }

        public string BuildPricingPreferenceText()
        {
            YouPinLandlordPricingPreference preference = _automation.GetSnapshot().PricingPreference;
            if (preference.PricingType == 0 && preference.LeaseDays.Count == 0)
                return "尚未读取悠悠云端的一键定价偏好。请先执行一次检查。";

            return FormatPricingPreferenceDetails(preference);
        }

        internal static string FormatPricingPreferenceDetails(
            YouPinLandlordPricingPreference preference)
        {
            string days = preference.LeaseDays.Count == 0
                ? "未设置"
                : string.Join("、", preference.LeaseDays) + " 天";
            return "以下内容来自悠悠云端，本软件只读展示，不在本地修改。\r\n\r\n"
                + $"交易方式：{FormatTransactionMode(preference.TransactionMode)}\r\n"
                + $"租送活动：{FormatSwitch(preference.DefaultRentalActivityEnabled)}\r\n"
                + $"赔付方式：{FormatDepositCompensationType(preference.DepositCompensationType)}\r\n"
                + $"租赁天数：{days}\r\n"
                + $"填充长租租金：{FormatSwitch(preference.FillRentEnabled)}\r\n"
                + $"长租租金系数：{preference.LongRentCoefficient:0.##} × 短租租金";
        }

        public string BuildExecutionQueueText(YouPinRentalScanScope scope = YouPinRentalScanScope.All)
        {
            YouPinLandlordPlannedAction[] actions = _automation
                .GetSnapshot()
                .CurrentPlan
                .Actions
                .Where(action => ScopeContains(scope, action.RentalType))
                .ToArray();
            if (actions.Length == 0)
                return "当前没有执行队列记录。完成一次检查后，这里会显示逐件处理状态。";

            return string.Join(
                "\r\n\r\n",
                actions.TakeLast(50).Reverse().Select(action =>
                {
                    string runId = action.RunId.Length > 8 ? action.RunId[..8] : action.RunId;
                    string actionId = action.ActionId.Length > 8 ? action.ActionId[..8] : action.ActionId;
                    return $"{FormatActionState(action.State)} · {action.ItemName}\r\n"
                        + $"Run={runId}  Action={actionId}  {action.Reason}";
                }));
        }

        internal static YouPinLandlordPageStateViewModel BuildPageState(
            YouPinLandlordSnapshot snapshot,
            YouPinRentalScanScope scope = YouPinRentalScanScope.All)
        {
            return BuildPageState(snapshot, YouPinLandlordPolicy.Default, scope);
        }

        internal static YouPinLandlordPageStateViewModel BuildPageState(
            YouPinLandlordSnapshot snapshot,
            YouPinLandlordPolicy policy,
            YouPinRentalScanScope scope = YouPinRentalScanScope.All)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(policy);

            YouPinLandlordShelfItem[] shelf = snapshot.Shelf
                .Where(item => ScopeContains(scope, item.RentalType))
                .ToArray();
            YouPinLandlordSelectionRule selection = ResolveSelection(policy, scope);
            YouPinLandlordShelfRowViewModel[] rows = selection.Scope
                == YouPinLandlordSelectionScope.SameItemName
                    ? BuildSameNameShelfRows(shelf, selection)
                    : shelf.Select(item => BuildPerAssetShelfRow(item, selection)).ToArray();
            int zeroCdCount = shelf.Count(item => item.RentalType == YouPinRentalShelfType.ZeroCd);
            int inventoryRentalCount = shelf.Length - zeroCdCount;

            return new YouPinLandlordPageStateViewModel(
                string.IsNullOrWhiteSpace(snapshot.LastError)
                    ? snapshot.Status
                    : "检查失败：" + snapshot.LastError,
                ResolveRentalLastCheckedAt(snapshot, scope) == DateTime.MinValue
                    ? "暂无"
                    : ResolveRentalLastCheckedAt(snapshot, scope).ToString(
                        "MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture),
                FormatPreference(snapshot.PricingPreference),
                snapshot.IsRunning,
                shelf.Length,
                zeroCdCount,
                inventoryRentalCount,
                rows)
            {
                LastExecutedText = FormatRentalExecutionTime(snapshot, scope)
            };
        }

        private static YouPinLandlordSelectionRule ResolveSelection(
            YouPinLandlordPolicy policy,
            YouPinRentalScanScope scope)
        {
            if (policy.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified)
                return policy.UnifiedRental.Selection;

            return scope == YouPinRentalScanScope.InventoryRental
                ? policy.InventoryRental.Selection
                : policy.ZeroCd.Selection;
        }

        private static YouPinLandlordShelfRowViewModel BuildPerAssetShelfRow(
            YouPinLandlordShelfItem item,
            YouPinLandlordSelectionRule selection)
        {
            return new YouPinLandlordShelfRowViewModel(
                item.ActionId,
                item.AssetId,
                selection.IsSelected(item.AssetId, item.ItemName),
                1,
                item.ItemName,
                item.RentalType,
                FormatRentalType(item.RentalType),
                "¥" + item.CurrentRent.ToString("0.00", CultureInfo.InvariantCulture),
                item.CurrentRank.HasValue ? $"第{item.CurrentRank}位" : "未找到",
                $"前{item.TargetRank}位",
                FormatDecision(item.DecisionCode, item.DecisionText),
                item.DecisionText,
                item.CheckedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }

        private static YouPinLandlordShelfRowViewModel[] BuildSameNameShelfRows(
            IReadOnlyList<YouPinLandlordShelfItem> shelf,
            YouPinLandlordSelectionRule selection)
        {
            return shelf
                .GroupBy(item => item.ItemName, StringComparer.Ordinal)
                .Select(group =>
                {
                    YouPinLandlordShelfItem[] items = group.ToArray();
                    YouPinRentalShelfType[] rentalTypes = items
                        .Select(item => item.RentalType)
                        .Distinct()
                        .ToArray();
                    decimal[] rents = items.Select(item => item.CurrentRent).Distinct().ToArray();
                    int[] targetRanks = items.Select(item => item.TargetRank).Distinct().ToArray();
                    string decision = items.Any(item => item.DecisionCode == YouPinLandlordDecisionCode.OutsideTargetRank)
                        ? "需要改价"
                        : items.All(item => item.DecisionCode == YouPinLandlordDecisionCode.WithinTargetRank)
                            ? "无需处理"
                            : items.All(item => item.DecisionText.Contains("名单", StringComparison.Ordinal))
                                ? "名单跳过"
                                : "检查异常";
                    string decisionDetail = string.Join(
                        "；",
                        items.Select(item => item.DecisionText)
                            .Where(text => !string.IsNullOrWhiteSpace(text))
                            .Distinct(StringComparer.Ordinal));
                    return new YouPinLandlordShelfRowViewModel(
                        items[0].ActionId,
                        group.Key,
                        selection.IsSelected(string.Empty, group.Key),
                        items.Length,
                        group.Key,
                        items[0].RentalType,
                        rentalTypes.Length == 1 ? FormatRentalType(rentalTypes[0]) : "0CD + 库存出租",
                        rents.Length == 1
                            ? "¥" + rents[0].ToString("0.00", CultureInfo.InvariantCulture)
                            : $"共 {items.Length} 件",
                        items.Length == 1 && items[0].CurrentRank.HasValue
                            ? $"第{items[0].CurrentRank}位"
                            : $"共 {items.Length} 件",
                        targetRanks.Length == 1 ? $"前{targetRanks[0]}位" : "分别设置",
                        decision,
                        decisionDetail,
                        items.Max(item => item.CheckedAt).ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                })
                .OrderBy(item => item.ItemName, StringComparer.CurrentCulture)
                .ToArray();
        }

        private static string FormatRentalType(YouPinRentalShelfType rentalType)
        {
            return rentalType == YouPinRentalShelfType.ZeroCd ? "0CD" : "库存出租";
        }

        internal static YouPinLandlordInventoryPageStateViewModel BuildInventoryPageState(
            YouPinLandlordSnapshot snapshot,
            YouPinLandlordInventoryPolicy policy)
        {
            YouPinLandlordInventoryRowViewModel[] rows = policy.SelectionScope
                == YouPinLandlordSelectionScope.SameItemName
                    ? BuildSameNameInventoryRows(snapshot.Inventory, policy)
                    : BuildPerAssetInventoryRows(snapshot.Inventory, policy);
            rows = rows
                .OrderByDescending(item => item.IsEligible)
                .ThenBy(item => item.ItemName, StringComparer.CurrentCulture)
                .ToArray();
            return new YouPinLandlordInventoryPageStateViewModel(
                snapshot.IsRunning,
                snapshot.InventoryStatus,
                snapshot.InventoryLastCheckedAt == DateTime.MinValue
                    ? "暂无"
                    : snapshot.InventoryLastCheckedAt.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                snapshot.Inventory.Count,
                snapshot.Inventory.Count(item => item.IsEligible),
                rows.Count(item => item.IsSelected),
                rows)
            {
                LastExecutedText = FormatExecutionTime(snapshot.InventoryAutoRentExecution)
            };
        }

        private static string FormatRentalExecutionTime(
            YouPinLandlordSnapshot snapshot,
            YouPinRentalScanScope scope)
        {
            if (scope == YouPinRentalScanScope.ZeroCd)
                return FormatExecutionTime(snapshot.ZeroCdExecution);
            if (scope == YouPinRentalScanScope.InventoryRental)
                return FormatExecutionTime(snapshot.InventoryRentalExecution);

            string zeroCd = FormatExecutionTime(snapshot.ZeroCdExecution);
            string inventoryRental = FormatExecutionTime(snapshot.InventoryRentalExecution);
            if (zeroCd == "暂无" && inventoryRental == "暂无")
                return "暂无";
            return $"0CD {zeroCd} · 普通 {inventoryRental}";
        }

        private static DateTime ResolveRentalLastCheckedAt(
            YouPinLandlordSnapshot snapshot,
            YouPinRentalScanScope scope)
        {
            if (scope == YouPinRentalScanScope.ZeroCd)
                return snapshot.ZeroCdLastCheckedAt;
            if (scope == YouPinRentalScanScope.InventoryRental)
                return snapshot.InventoryRentalLastCheckedAt;

            DateTime latest = snapshot.ZeroCdLastCheckedAt > snapshot.InventoryRentalLastCheckedAt
                ? snapshot.ZeroCdLastCheckedAt
                : snapshot.InventoryRentalLastCheckedAt;
            return latest == DateTime.MinValue ? snapshot.LastCheckedAt : latest;
        }

        private static string FormatExecutionTime(YouPinLandlordExecutionState state)
        {
            return state.LastStartedAtUtc == DateTime.MinValue
                ? "暂无"
                : state.LastStartedAtUtc.ToLocalTime()
                    .ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static YouPinLandlordInventoryRowViewModel[] BuildPerAssetInventoryRows(
            IReadOnlyList<YouPinLandlordInventoryItem> inventory,
            YouPinLandlordInventoryPolicy policy)
        {
            return inventory
                .Select(item => new YouPinLandlordInventoryRowViewModel(
                    item.AssetId,
                    item.ItemName,
                    policy.IsSelected(item.AssetId, item.ItemName),
                    item.IsEligible,
                    item.IsEligible ? "✓ 可出租" : "✕ 暂不可出租",
                    item.EligibilityReason,
                    1,
                    item.IsEligible ? 1 : 0))
                .ToArray();
        }

        private static YouPinLandlordInventoryRowViewModel[] BuildSameNameInventoryRows(
            IReadOnlyList<YouPinLandlordInventoryItem> inventory,
            YouPinLandlordInventoryPolicy policy)
        {
            return inventory
                .GroupBy(item => item.ItemName, StringComparer.Ordinal)
                .Select(group =>
                {
                    int itemCount = group.Count();
                    int eligibleItemCount = group.Count(item => item.IsEligible);
                    bool anyEligible = eligibleItemCount > 0;
                    string eligibilityReason = eligibleItemCount switch
                    {
                        0 => "当前没有符合出租资格的饰品",
                        _ when eligibleItemCount == itemCount => "全部符合出租资格",
                        _ => "每件饰品仍分别判断出租资格"
                    };
                    return new YouPinLandlordInventoryRowViewModel(
                        group.Key,
                        group.Key,
                        policy.IsSelected(string.Empty, group.Key),
                        anyEligible,
                        $"共 {itemCount} 件 · 可出租 {eligibleItemCount} 件",
                        eligibilityReason,
                        itemCount,
                        eligibleItemCount);
                })
                .ToArray();
        }

        private static bool ScopeContains(YouPinRentalScanScope scope, YouPinRentalShelfType type)
        {
            YouPinRentalScanScope flag = type == YouPinRentalShelfType.ZeroCd
                ? YouPinRentalScanScope.ZeroCd
                : YouPinRentalScanScope.InventoryRental;
            return scope.HasFlag(flag);
        }

        private static string FormatPreference(YouPinLandlordPricingPreference preference)
        {
            if (preference.PricingType == 0 && preference.LeaseDays.Count == 0)
                return "尚未读取悠悠云端偏好";

            string leaseDays = preference.LeaseDays.Count == 0
                ? "未设置租期"
                : "租期 " + string.Join('/', preference.LeaseDays) + " 天";
            return "悠悠云端偏好已同步 · " + leaseDays;
        }

        private static string FormatDecision(YouPinLandlordDecisionCode code)
        {
            return code switch
            {
                YouPinLandlordDecisionCode.WithinTargetRank => "无需处理",
                YouPinLandlordDecisionCode.OutsideTargetRank => "需要改价",
                _ => "检查异常"
            };
        }

        private static string FormatDecision(
            YouPinLandlordDecisionCode code,
            string decisionDetail)
        {
            return decisionDetail.Contains("名单", StringComparison.Ordinal)
                ? "名单跳过"
                : FormatDecision(code);
        }

        private static string FormatSwitch(bool enabled) => enabled ? "已开启" : "已关闭";

        private static string FormatTransactionMode(int mode)
        {
            return mode switch
            {
                0 => "可租可售",
                1 => "租赁",
                _ => $"未知（{mode}）"
            };
        }

        private static string FormatDepositCompensationType(int type)
        {
            return type switch
            {
                0 => "优先使用押金赔付",
                1 => "优先使用安心涨赔付",
                _ => $"未知（{type}）"
            };
        }

        private static string FormatActionState(YouPinLandlordActionState state)
        {
            return state switch
            {
                YouPinLandlordActionState.Evaluating => "等待判断",
                YouPinLandlordActionState.PricingReady => "已取得定价",
                YouPinLandlordActionState.Planned => "待处理",
                YouPinLandlordActionState.Executing => "执行中",
                YouPinLandlordActionState.AwaitingSynchronization => "等待同步",
                YouPinLandlordActionState.Rechecking => "回查中",
                YouPinLandlordActionState.Succeeded => "成功",
                YouPinLandlordActionState.Failed => "异常",
                YouPinLandlordActionState.Skipped => "已跳过",
                _ => "仅观察"
            };
        }
    }

    internal sealed record YouPinLandlordShelfRowViewModel(
        string ActionId,
        string SelectionKey,
        bool IsSelected,
        int ItemCount,
        string ItemName,
        YouPinRentalShelfType RentalType,
        string RentalTypeText,
        string CurrentRentText,
        string CurrentRankText,
        string TargetRankText,
        string DecisionText,
        string DecisionDetail,
        string CheckedAtText);

    internal sealed record YouPinLandlordPageStateViewModel(
        string StatusText,
        string LastCheckedText,
        string PreferenceText,
        bool IsRunning,
        int TotalShelfCount,
        int ZeroCdCount,
        int InventoryRentalCount,
        IReadOnlyList<YouPinLandlordShelfRowViewModel> ShelfRows)
    {
        public string LastExecutedText { get; init; } = "暂无";
    }

    internal sealed record YouPinLandlordInventoryRowViewModel(
        string SelectionKey,
        string ItemName,
        bool IsSelected,
        bool IsEligible,
        string EligibilityText,
        string EligibilityReason,
        int ItemCount,
        int EligibleItemCount);

    internal sealed record YouPinLandlordInventoryPageStateViewModel(
        bool IsRunning,
        string StatusText,
        string LastCheckedText,
        int TotalCount,
        int EligibleCount,
        int SelectedCount,
        IReadOnlyList<YouPinLandlordInventoryRowViewModel> Rows)
    {
        public string LastExecutedText { get; init; } = "暂无";
    }
}
