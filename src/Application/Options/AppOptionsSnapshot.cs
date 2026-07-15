namespace CS2TradeMonitor.Application.Options
{
    public sealed record AppOptionsSnapshot(
        UiOptions Ui,
        SteamOptions Steam,
        YouPinOptions YouPin,
        MarketOptions Market,
        NotificationOptions Notification,
        StartupOptions Startup);

    public sealed record UiOptions(
        string Skin,
        string Language,
        bool SettingsPanelDarkMode,
        bool TopMost,
        bool ShowTaskbar,
        bool ShowMainWindowInTaskbar,
        bool HideMainForm,
        bool HideTrayIcon);

    public sealed record SteamOptions(
        bool OfferEnabled,
        int OfferRefreshSec,
        bool OfferAutoCheck,
        bool OfferAutoAccept,
        int OfferAutoCheckSec,
        bool OfferAllowYouPinVerifiedAccept);

    public sealed record YouPinOptions(
        bool InventoryEnabled,
        int InventoryRefreshSec,
        bool SaleReminderEnabled,
        int SaleReminderRefreshSec,
        bool SaleReminderIncludeAllTodos,
        bool QuoteAutoRefreshEnabled,
        int QuoteAutoRefreshSec,
        bool MessageCenterEnabled,
        int MessageCenterRefreshSec);

    public sealed record MarketOptions(
        int SteamDtRefreshSec,
        int CsqaqRefreshSec,
        bool AlertsEnabled,
        bool DoNotDisturbEnabled,
        int DefaultAlertWindowMinutes,
        int DefaultAlertCooldownMinutes);

    public sealed record NotificationOptions(
        bool PhoneAlertEnabled,
        string PhoneAlertProvider,
        PhoneAlertDispatchMode PhoneAlertDispatchMode,
        bool Cs2UpdateReminderEnabled,
        int Cs2UpdateReminderRefreshSec);

    public sealed record StartupOptions(
        bool AutoStart,
        bool ShowMainWindowInTaskbar,
        bool HideTrayIcon);
}
