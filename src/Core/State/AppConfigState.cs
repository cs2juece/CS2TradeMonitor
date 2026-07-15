using System;
using System.Linq;

namespace CS2TradeMonitor.src.Core.State
{
    public sealed class AppConfigState : IAppConfigState
    {
        private readonly object _gate = new();

        private AppConfigState()
        {
        }

        public static AppConfigState Instance { get; } = new();

        public ThemeConfigSnapshot Theme { get; private set; } = ThemeConfigSnapshot.Empty;

        public MarketDisplayConfigSnapshot MarketDisplay { get; private set; } = MarketDisplayConfigSnapshot.Empty;

        public TaskbarStyleConfigSnapshot TaskbarStyle { get; private set; } = TaskbarStyleConfigSnapshot.Empty;

        public ItemMonitorConfigSnapshot ItemMonitor { get; private set; } = ItemMonitorConfigSnapshot.Empty;

        public NotificationConfigSnapshot Notifications { get; private set; } = NotificationConfigSnapshot.Empty;

        public MetricConfigSnapshot Metrics { get; private set; } = MetricConfigSnapshot.Empty;

        public YouPinTrendIndicatorConfigSnapshot YouPinTrendIndicator { get; private set; } = YouPinTrendIndicatorConfigSnapshot.Empty;

        public event EventHandler<ConfigChangedEventArgs>? Changed;

        public void PublishFrom(Settings settings, string reason = "SettingsSaved")
        {
            if (settings == null) return;

            lock (_gate)
            {
                Theme = new ThemeConfigSnapshot(
                    settings.SettingsPanelDarkMode,
                    settings.Skin ?? "",
                    settings.UIScale,
                    settings.TopMost,
                    settings.PanelWidth,
                    settings.Opacity,
                    settings.PanelBackgroundOpacity,
                    settings.TextOpacity);

                MarketDisplay = new MarketDisplayConfigSnapshot(
                    settings.MarketFormat,
                    settings.SteamDtShowPercent,
                    settings.SteamDtRefreshSec,
                    settings.CsqaqRefreshSec,
                    !string.IsNullOrWhiteSpace(settings.SteamDtApiKey),
                    !string.IsNullOrWhiteSpace(settings.CsqaqApiToken),
                    settings.TaskbarFontFamily ?? "",
                    settings.TaskbarFontSize,
                    settings.TaskbarFontBold,
                    settings.TaskbarItemSpacing,
                    settings.TaskbarInnerSpacing,
                    settings.TaskbarVerticalPadding);

                TaskbarStyle = new TaskbarStyleConfigSnapshot(
                    settings.TaskbarFontFamily ?? "",
                    settings.TaskbarFontSize,
                    settings.TaskbarFontBold,
                    settings.TaskbarColorLabel ?? "",
                    settings.TaskbarColorSafe ?? "",
                    settings.TaskbarColorWarn ?? "",
                    settings.TaskbarColorCrit ?? "");

                ItemMonitor = new ItemMonitorConfigSnapshot(
                    settings.DefaultItemRefreshIntervalSec,
                    settings.DefaultItemPriceAlertRisePercent,
                    settings.DefaultItemPriceAlertFallPercent,
                    settings.DefaultItemPriceAlertWindowMinutes,
                    settings.DefaultItemPriceAlertCooldownMinutes,
                    settings.ItemConfigs?.Count ?? 0,
                    settings.ItemConfigs?
                        .Select(x => new ItemConfigSummary(
                            x.ItemKey ?? "",
                            x.ItemId ?? "",
                            x.Name ?? "",
                            x.ShortName ?? "",
                            x.MarketHashName ?? "",
                            x.PlatformItemId ?? "",
                            x.DisplayFieldFlags,
                            x.Enabled,
                            x.VisibleInPanel,
                            x.VisibleInTaskbar,
                            x.RefreshIntervalSec,
                            x.LastPrice,
                            x.LastChange,
                            x.LastChangeRatio,
                            x.LastUpdateTime,
                            x.LastStatus ?? "",
                            x.HasChangeData))
                        .ToArray() ?? Array.Empty<ItemConfigSummary>());

                Notifications = new NotificationConfigSnapshot(
                    settings.DoNotDisturbEnabled,
                    settings.MarketAlertsEnabled,
                    settings.MarketAlertNotificationMode.ToString(),
                    settings.PhoneAlertEnabled,
                    settings.PhoneAlertProvider ?? "",
                    settings.PhoneAlertDispatchMode.ToString(),
                    settings.Cs2UpdateReminderEnabled,
                    settings.Cs2UpdateReminderWechatEnabled,
                    settings.Cs2UpdateReminderSoundEnabled,
                    settings.YouPinSaleReminderEnabled,
                    settings.YouPinMsgCenterEnabled,
                    settings.YouPinStopProfitLossEnabled,
                    settings.SteamOfferEnabled);

                var thresholds = settings.Thresholds ?? new ThresholdsSet();
                Metrics = new MetricConfigSnapshot(
                    settings.MemoryDisplayMode,
                    new MetricThresholdSnapshot(
                        ToRange(thresholds.Load),
                        ToRange(thresholds.Temp),
                        ToRange(thresholds.DiskIOMB),
                        ToRange(thresholds.NetUpMB),
                        ToRange(thresholds.NetDownMB),
                        ToRange(thresholds.DataUpMB),
                        ToRange(thresholds.DataDownMB)),
                    settings.RecordedMaxCpuClock,
                    settings.RecordedMaxCpuPower,
                    settings.RecordedMaxGpuClock,
                    settings.RecordedMaxGpuPower,
                    settings.RecordedMaxCpuFan,
                    settings.RecordedMaxCpuPump,
                    settings.RecordedMaxChassisFan,
                    settings.RecordedMaxGpuFan,
                    settings.RecordedMaxFps);

                YouPinTrendIndicator = new YouPinTrendIndicatorConfigSnapshot(
                    settings.YouPinTrendIndicatorDisplayMode,
                    settings.YouPinTrendIndicatorSignMode,
                    settings.YouPinTrendIndicatorProfitColor ?? "",
                    settings.YouPinTrendIndicatorLossColor ?? "",
                    settings.YouPinTrendIndicatorZeroColor ?? "");
            }

            Changed?.Invoke(this, new ConfigChangedEventArgs(reason, DateTime.Now));
        }

        private static MetricValueRangeSnapshot ToRange(ValueRange? range)
        {
            return new MetricValueRangeSnapshot(range?.Warn ?? 0, range?.Crit ?? 0);
        }
    }

}
