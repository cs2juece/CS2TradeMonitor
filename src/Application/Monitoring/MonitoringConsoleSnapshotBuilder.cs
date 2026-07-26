using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;

namespace CS2TradeMonitor.Application.Monitoring
{
    public sealed class MonitoringConsoleSnapshotBuilder
    {
        private readonly IMonitorModuleHost _moduleHost;
        private readonly IMarketAlertService _marketAlerts;
        private readonly IYouPinInventoryService _youPinInventory;
        private readonly ICs2UpdateReminderService _cs2Updates;
        private readonly IPhoneAlertDispatchService _phoneAlerts;
        private readonly IAlertHistoryStore _alertHistory;

        public MonitoringConsoleSnapshotBuilder(
            IMonitorModuleHost moduleHost,
            IMarketAlertService marketAlerts,
            IYouPinInventoryService youPinInventory,
            ICs2UpdateReminderService cs2Updates,
            IPhoneAlertDispatchService phoneAlerts,
            IAlertHistoryStore alertHistory)
        {
            _moduleHost = moduleHost ?? throw new ArgumentNullException(nameof(moduleHost));
            _marketAlerts = marketAlerts ?? throw new ArgumentNullException(nameof(marketAlerts));
            _youPinInventory = youPinInventory ?? throw new ArgumentNullException(nameof(youPinInventory));
            _cs2Updates = cs2Updates ?? throw new ArgumentNullException(nameof(cs2Updates));
            _phoneAlerts = phoneAlerts ?? throw new ArgumentNullException(nameof(phoneAlerts));
            _alertHistory = alertHistory ?? throw new ArgumentNullException(nameof(alertHistory));
        }

        public async Task<MonitoringConsoleSnapshot> BuildAsync(
            Settings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            DateTimeOffset now = DateTimeOffset.Now;
            IReadOnlyList<MonitorModuleHealth> modules = _moduleHost.GetHealthSnapshot();
            IReadOnlyList<AlertReadinessSnapshot> readiness = new[]
            {
                _marketAlerts.GetReadiness(settings),
                BuildItemReadiness(settings, now),
                BuildInventoryReadiness(settings, _youPinInventory.GetStopProfitLossState(), now),
                BuildUpdateReadiness(settings, _cs2Updates, now),
                BuildPhoneReadiness(settings, _phoneAlerts, now)
            };
            IReadOnlyList<AlertHistoryEntry> recentAlerts = await _alertHistory
                .ReadLatestAsync(8, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<ConsoleUpdateSnapshot> recentUpdates = _cs2Updates.RecentItems
                .OrderByDescending(item => item.PublishedAt)
                .Take(3)
                .Select(item => new ConsoleUpdateSnapshot(
                    item.Source,
                    item.Title,
                    item.Summary,
                    item.PublishedAt))
                .ToArray();

            int moduleProblems = modules.Count(module => module.State is MonitorModuleState.Faulted
                or MonitorModuleState.Stopped
                or MonitorModuleState.NotStarted);
            int readinessProblems = readiness.Count(item => item.State == AlertReadinessState.Faulted);
            bool healthy = moduleProblems == 0 && readinessProblems == 0;
            string overallText = healthy
                ? $"运行正常 · {modules.Count} 个模块"
                : $"需要注意 · 模块异常 {moduleProblems} 项，提醒异常 {readinessProblems} 项";

            return new MonitoringConsoleSnapshot(
                overallText,
                healthy,
                modules,
                readiness,
                recentAlerts,
                recentUpdates,
                now);
        }

        internal static AlertReadinessSnapshot BuildItemReadiness(Settings settings, DateTimeOffset now)
        {
            var enabled = (settings.ItemConfigs ?? new List<ItemMonitorConfig>())
                .Where(item => item.Enabled && ItemPriceAlertPolicy.IsDeliveryEnabled(item))
                .ToArray();
            if (enabled.Length == 0)
            {
                return Snapshot(
                    "item-alert",
                    "单品价格提醒",
                    AlertReadinessState.Disabled,
                    "没有已启用的单品提醒",
                    "ItemMonitor",
                    now);
            }

            var configured = enabled
                .Where(item => ItemPriceAlertPolicy.HasConfiguredThreshold(item, settings))
                .ToArray();
            if (configured.Length == 0)
            {
                return Snapshot(
                    "item-alert",
                    "单品价格提醒",
                    AlertReadinessState.Waiting,
                    $"已启用 {enabled.Length} 项，但尚未填写有效阈值",
                    "ItemMonitor",
                    now);
            }

            int waitingForData = configured.Count(item => item.LastPrice <= 0);
            int coolingDown = configured.Count(item => IsItemCoolingDown(item, now));
            int ready = configured.Length - waitingForData - coolingDown;
            if (ready > 0)
            {
                return Snapshot(
                    "item-alert",
                    "单品价格提醒",
                    AlertReadinessState.Ready,
                    $"{ready} 项已准备 · 等待数据 {waitingForData} 项 · 冷却 {coolingDown} 项",
                    "ItemMonitor",
                    now);
            }

            AlertReadinessState state = coolingDown > 0 && waitingForData == 0
                ? AlertReadinessState.CoolingDown
                : AlertReadinessState.Waiting;
            string message = state == AlertReadinessState.CoolingDown
                ? $"{coolingDown} 项处于冷却期"
                : $"等待价格数据 · {waitingForData} 项";
            return Snapshot("item-alert", "单品价格提醒", state, message, "ItemMonitor", now);
        }

        internal static AlertReadinessSnapshot BuildInventoryReadiness(
            Settings settings,
            YouPinStopProfitLossState state,
            DateTimeOffset now)
        {
            if (!settings.YouPinStopProfitLossEnabled)
            {
                return Snapshot(
                    "inventory-stop-alert",
                    "库存止盈止损",
                    AlertReadinessState.Disabled,
                    "监控已关闭",
                    "YouPinStopProfitLoss",
                    now);
            }

            if (!string.IsNullOrWhiteSpace(state.LastError))
            {
                return Snapshot(
                    "inventory-stop-alert",
                    "库存止盈止损",
                    AlertReadinessState.Faulted,
                    "最近扫描失败：" + state.LastError.Trim(),
                    "YouPinStopProfitLoss",
                    now);
            }

            bool lacksScope = settings.YouPinStopProfitLossOnlySpecifiedItems
                && YouPinStopProfitLossAlertEvaluator.ParseSpecifiedItems(settings.YouPinStopProfitLossSpecifiedItems).Count == 0
                && YouPinStopProfitLossRuleStore.LoadRules(settings.YouPinStopProfitLossItemRulesJson).Count == 0;
            if (lacksScope)
            {
                return Snapshot(
                    "inventory-stop-alert",
                    "库存止盈止损",
                    AlertReadinessState.Waiting,
                    "仅监控指定饰品，但尚未选择饰品",
                    "YouPinStopProfitLoss",
                    now);
            }

            if (state.LastFetch == DateTime.MinValue)
            {
                return Snapshot(
                    "inventory-stop-alert",
                    "库存止盈止损",
                    AlertReadinessState.Waiting,
                    "等待首次库存扫描和持续时间基线",
                    "YouPinStopProfitLoss",
                    now);
            }

            return Snapshot(
                "inventory-stop-alert",
                "库存止盈止损",
                AlertReadinessState.Ready,
                "扫描链路已准备 · 最近 " + FormatRelativeTime(state.LastFetch, now),
                "YouPinStopProfitLoss",
                now);
        }

        internal static AlertReadinessSnapshot BuildUpdateReadiness(
            Settings settings,
            ICs2UpdateReminderService updates,
            DateTimeOffset now)
        {
            if (!settings.Cs2UpdateReminderEnabled)
            {
                return Snapshot(
                    "cs2-update-alert",
                    "CS2 更新提醒",
                    AlertReadinessState.Disabled,
                    "更新检查已关闭",
                    "Cs2UpdatePhoneReminder",
                    now);
            }

            var result = updates.LastResult;
            if (result.CheckedAt == DateTime.MinValue)
            {
                return Snapshot(
                    "cs2-update-alert",
                    "CS2 更新提醒",
                    AlertReadinessState.Waiting,
                    "等待首次检查",
                    "Cs2UpdatePhoneReminder",
                    now);
            }

            return Snapshot(
                "cs2-update-alert",
                "CS2 更新提醒",
                result.Success ? AlertReadinessState.Ready : AlertReadinessState.Faulted,
                result.Success ? result.Message : "最近检查失败：" + result.Message,
                "Cs2UpdatePhoneReminder",
                now);
        }

        internal static AlertReadinessSnapshot BuildPhoneReadiness(
            Settings settings,
            IPhoneAlertDispatchService phoneAlerts,
            DateTimeOffset now)
        {
            if (!settings.PhoneAlertEnabled)
            {
                return Snapshot(
                    "phone-alert",
                    "手机提醒渠道",
                    AlertReadinessState.Disabled,
                    "手机提醒已关闭",
                    "Cs2UpdatePhoneReminder",
                    now);
            }

            bool configured = phoneAlerts.IsConfigured(settings);
            return Snapshot(
                "phone-alert",
                "手机提醒渠道",
                configured ? AlertReadinessState.Ready : AlertReadinessState.Waiting,
                configured ? "至少一个渠道已启用且配置完整" : "没有已启用且配置完整的渠道",
                "Cs2UpdatePhoneReminder",
                now);
        }

        private static bool IsItemCoolingDown(ItemMonitorConfig item, DateTimeOffset now)
        {
            if (item.PriceAlertLastTriggerTime <= 0)
                return false;

            try
            {
                DateTimeOffset lastTrigger = DateTimeOffset.FromUnixTimeMilliseconds(item.PriceAlertLastTriggerTime);
                int minutes = Math.Clamp(item.PriceAlertCooldownMinutes <= 0 ? 10 : item.PriceAlertCooldownMinutes, 1, 1440);
                return now - lastTrigger < TimeSpan.FromMinutes(minutes);
            }
            catch
            {
                return false;
            }
        }

        private static AlertReadinessSnapshot Snapshot(
            string id,
            string displayName,
            AlertReadinessState state,
            string message,
            string pageKey,
            DateTimeOffset observedAt)
        {
            return new AlertReadinessSnapshot(id, displayName, state, message, pageKey, observedAt);
        }

        private static string FormatRelativeTime(DateTime time, DateTimeOffset now)
        {
            TimeSpan elapsed = now.LocalDateTime - time;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
            if (elapsed < TimeSpan.FromMinutes(1))
                return "刚刚";
            if (elapsed < TimeSpan.FromHours(1))
                return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
            return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        }
    }
}
