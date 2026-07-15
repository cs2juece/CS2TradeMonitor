namespace CS2TradeMonitor.src.Core.State
{
    public interface IAppConfigState
    {
        ThemeConfigSnapshot Theme { get; }

        MarketDisplayConfigSnapshot MarketDisplay { get; }

        TaskbarStyleConfigSnapshot TaskbarStyle { get; }

        ItemMonitorConfigSnapshot ItemMonitor { get; }

        NotificationConfigSnapshot Notifications { get; }

        MetricConfigSnapshot Metrics { get; }

        YouPinTrendIndicatorConfigSnapshot YouPinTrendIndicator { get; }

        event EventHandler<ConfigChangedEventArgs>? Changed;

        void PublishFrom(Settings settings, string reason = "SettingsSaved");
    }
}
