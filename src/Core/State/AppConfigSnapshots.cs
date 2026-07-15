using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.Core.State
{
    public sealed record ThemeConfigSnapshot(
        bool SettingsPanelDarkMode,
        string Skin,
        double UiScale,
        bool TopMost,
        int PanelWidth,
        double Opacity,
        double PanelBackgroundOpacity,
        double TextOpacity)
    {
        public static ThemeConfigSnapshot Empty { get; } = new(false, "", 1, false, 0, 1, 1, 1);
    }

    public sealed record MarketDisplayConfigSnapshot(
        int MarketFormat,
        bool ShowPercent,
        int SteamDtRefreshSec,
        int CsqaqRefreshSec,
        bool HasSteamDtApiKey,
        bool HasCsqaqApiToken,
        string TaskbarFontFamily,
        float TaskbarFontSize,
        bool TaskbarFontBold,
        int TaskbarItemSpacing,
        int TaskbarInnerSpacing,
        int TaskbarVerticalPadding)
    {
        public static MarketDisplayConfigSnapshot Empty { get; } = new(0, true, Settings.DefaultMarketRefreshSec, Settings.DefaultMarketRefreshSec, false, false, "", 0, false, 0, 0, 0);
    }

    public sealed record TaskbarStyleConfigSnapshot(
        string FontFamily,
        float FontSize,
        bool FontBold,
        string ColorLabel,
        string ColorSafe,
        string ColorWarn,
        string ColorCrit)
    {
        public static TaskbarStyleConfigSnapshot Empty { get; } = new(
            "Segoe UI",
            9f,
            false,
            "#FFFFFF",
            "#00CC66",
            "#FFFF00",
            "#FF4444");
    }

    public sealed record ItemMonitorConfigSnapshot(
        int DefaultRefreshIntervalSec,
        double DefaultAlertRisePercent,
        double DefaultAlertFallPercent,
        int DefaultAlertWindowMinutes,
        int DefaultAlertCooldownMinutes,
        int Count,
        IReadOnlyList<ItemConfigSummary> Items)
    {
        public static ItemMonitorConfigSnapshot Empty { get; } = new(600, 0, 0, 10, 10, 0, Array.Empty<ItemConfigSummary>());
    }

    public sealed record ItemConfigSummary(
        string ItemKey,
        string ItemId,
        string Name,
        string ShortName,
        string MarketHashName,
        string PlatformItemId,
        int DisplayFieldFlags,
        bool Enabled,
        bool VisibleInPanel,
        bool VisibleInTaskbar,
        int RefreshIntervalSec,
        double LastPrice,
        double LastChange,
        double LastChangeRatio,
        long LastUpdateTime,
        string LastStatus,
        bool HasChangeData);

    public sealed record NotificationConfigSnapshot(
        bool DoNotDisturbEnabled,
        bool MarketAlertsEnabled,
        string MarketAlertNotificationMode,
        bool PhoneAlertEnabled,
        string PhoneAlertProvider,
        string PhoneAlertDispatchMode,
        bool Cs2UpdateReminderEnabled,
        bool Cs2UpdateReminderWechatEnabled,
        bool Cs2UpdateReminderSoundEnabled,
        bool YouPinSaleReminderEnabled,
        bool YouPinMsgCenterEnabled,
        bool YouPinStopProfitLossEnabled,
        bool SteamOfferEnabled)
    {
        public static NotificationConfigSnapshot Empty { get; } = new(
            false,
            false,
            "",
            false,
            "",
            "",
            false,
            false,
            false,
            false,
            false,
            false,
            false);
    }

    public sealed record MetricConfigSnapshot(
        int MemoryDisplayMode,
        MetricThresholdSnapshot Thresholds,
        float RecordedMaxCpuClock,
        float RecordedMaxCpuPower,
        float RecordedMaxGpuClock,
        float RecordedMaxGpuPower,
        float RecordedMaxCpuFan,
        float RecordedMaxCpuPump,
        float RecordedMaxChassisFan,
        float RecordedMaxGpuFan,
        float RecordedMaxFps)
    {
        public static MetricConfigSnapshot Empty { get; } = new(
            1,
            MetricThresholdSnapshot.Empty,
            4200.0f,
            65.0f,
            1800.0f,
            100.0f,
            4000,
            5000,
            3000,
            3500,
            144.0f);
    }

    public sealed record MetricThresholdSnapshot(
        MetricValueRangeSnapshot Load,
        MetricValueRangeSnapshot Temp,
        MetricValueRangeSnapshot DiskIOMB,
        MetricValueRangeSnapshot NetUpMB,
        MetricValueRangeSnapshot NetDownMB,
        MetricValueRangeSnapshot DataUpMB,
        MetricValueRangeSnapshot DataDownMB)
    {
        public static MetricThresholdSnapshot Empty { get; } = new(
            new MetricValueRangeSnapshot(60, 85),
            new MetricValueRangeSnapshot(50, 70),
            new MetricValueRangeSnapshot(2, 8),
            new MetricValueRangeSnapshot(1, 2),
            new MetricValueRangeSnapshot(2, 8),
            new MetricValueRangeSnapshot(512, 1024),
            new MetricValueRangeSnapshot(2048, 5096));
    }

    public sealed record MetricValueRangeSnapshot(double Warn, double Crit);

    public sealed record YouPinTrendIndicatorConfigSnapshot(
        int DisplayMode,
        int SignMode,
        string ProfitColor,
        string LossColor,
        string ZeroColor)
    {
        public static YouPinTrendIndicatorConfigSnapshot Empty { get; } = new(
            0,
            0,
            "#DC465A",
            "#50A087",
            "#FFFFFF");
    }
}
