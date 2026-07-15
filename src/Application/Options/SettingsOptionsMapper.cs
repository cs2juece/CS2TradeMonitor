namespace CS2TradeMonitor.Application.Options
{
    public static class SettingsOptionsMapper
    {
        public static AppOptionsSnapshot ToSnapshot(Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            return new AppOptionsSnapshot(
                Ui: new UiOptions(
                    Skin: settings.Skin,
                    Language: settings.Language,
                    SettingsPanelDarkMode: settings.SettingsPanelDarkMode,
                    TopMost: settings.TopMost,
                    ShowTaskbar: settings.ShowTaskbar,
                    ShowMainWindowInTaskbar: settings.ShowMainWindowInTaskbar,
                    HideMainForm: settings.HideMainForm,
                    HideTrayIcon: settings.HideTrayIcon),
                Steam: new SteamOptions(
                    OfferEnabled: settings.SteamOfferEnabled,
                    OfferRefreshSec: settings.SteamOfferRefreshSec,
                    OfferAutoCheck: settings.SteamOfferAutoCheck,
                    OfferAutoAccept: settings.SteamOfferAutoAccept,
                    OfferAutoCheckSec: settings.SteamOfferAutoCheckSec,
                    OfferAllowYouPinVerifiedAccept: settings.SteamOfferAllowYouPinVerifiedAccept),
                YouPin: new YouPinOptions(
                    InventoryEnabled: settings.YouPinInventoryEnabled,
                    InventoryRefreshSec: settings.YouPinInventoryRefreshSec,
                    SaleReminderEnabled: settings.YouPinSaleReminderEnabled,
                    SaleReminderRefreshSec: settings.YouPinSaleReminderRefreshSec,
                    SaleReminderIncludeAllTodos: settings.YouPinSaleReminderIncludeAllTodos,
                    QuoteAutoRefreshEnabled: settings.YouPinQuoteAutoRefreshEnabled,
                    QuoteAutoRefreshSec: settings.YouPinQuoteAutoRefreshSec,
                    MessageCenterEnabled: settings.YouPinMsgCenterEnabled,
                    MessageCenterRefreshSec: settings.YouPinMsgCenterRefreshSec),
                Market: new MarketOptions(
                    SteamDtRefreshSec: settings.SteamDtRefreshSec,
                    CsqaqRefreshSec: settings.CsqaqRefreshSec,
                    AlertsEnabled: settings.MarketAlertsEnabled,
                    DoNotDisturbEnabled: settings.DoNotDisturbEnabled,
                    DefaultAlertWindowMinutes: settings.MarketAlertDefaultWindowMinutes,
                    DefaultAlertCooldownMinutes: settings.MarketAlertDefaultCooldownMinutes),
                Notification: new NotificationOptions(
                    PhoneAlertEnabled: settings.PhoneAlertEnabled,
                    PhoneAlertProvider: settings.PhoneAlertProvider,
                    PhoneAlertDispatchMode: settings.PhoneAlertDispatchMode,
                    Cs2UpdateReminderEnabled: settings.Cs2UpdateReminderEnabled,
                    Cs2UpdateReminderRefreshSec: settings.Cs2UpdateReminderRefreshSec),
                Startup: new StartupOptions(
                    AutoStart: settings.AutoStart,
                    ShowMainWindowInTaskbar: settings.ShowMainWindowInTaskbar,
                    HideTrayIcon: settings.HideTrayIcon));
        }
    }
}
