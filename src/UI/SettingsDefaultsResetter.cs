using System;
using System.Collections.Generic;
using System.Text.Json;
using CS2TradeMonitor.src.UI.Framework;

namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsDefaultsResetter
    {
        public static Settings CreateNormalizedDefaults()
        {
            var defaults = new Settings();
            defaults.InitDefaultItems();
            if (defaults.TaskbarPresetStyle == -1) defaults.TaskbarPresetStyle = 1;
            return defaults;
        }

        public static string GetSectionLabel(string pageKey, string? mainTab)
        {
            pageKey = SettingsPageRegistry.GetBaseKey(pageKey);
            return pageKey switch
            {
                "MainPanel" when mainTab == "Taskbar" => "任务栏显示",
                "MainPanel" when mainTab == "Style" => "字体与颜色",
                "MainPanel" when mainTab == "ItemMonitor" => "单品监控外观",
                "MainPanel" when mainTab == "InventoryTrend" => "库存涨跌外观",
                "MainPanel" => "悬浮窗",
                "Data" => "大盘数据源",
                "ItemMonitor" => "单品监控",
                "YouPin" => "悠悠有品",
                "YouPinStopProfitLoss" => "库存止损/盈",
                "YouPinProfitLoss" => "吃米/亏米统计",
                "SteamOffers" => "Steam报价",
                "MarketAlerts" => "大盘预警",
                "Cs2UpdatePhoneReminder" => "CS2更新与手机提醒",
                "System" => "系统",
                _ => "当前页面"
            };
        }

        public static bool TryResetSection(
            Settings draft,
            Settings defaults,
            string pageKey,
            string? mainTab,
            Action? resetCs2Schedule = null,
            Action<Settings>? configureYouPinInventory = null)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(defaults);

            switch (SettingsPageRegistry.GetBaseKey(pageKey))
            {
                case "ItemMonitor":
                    ResetItemMonitorDefaults(draft, defaults);
                    return true;
                case "YouPin":
                    ResetYouPinSaleReminderDefaults(draft, defaults);
                    return true;
                case "SteamOffers":
                    ResetSteamOfferDefaults(draft, defaults);
                    return true;
                case "YouPinStopProfitLoss":
                    ResetYouPinStopProfitLossDefaults(draft, defaults);
                    configureYouPinInventory?.Invoke(draft);
                    return true;
                case "MainPanel" when mainTab == "Taskbar":
                    ResetTaskbarDefaults(draft, defaults);
                    return true;
                case "MainPanel" when mainTab == "Style":
                    ResetStyleDefaults(draft, defaults);
                    return true;
                case "MainPanel" when mainTab == "ItemMonitor":
                    ResetItemMonitorAppearanceDefaults(draft, defaults);
                    return true;
                case "MainPanel" when mainTab == "InventoryTrend":
                    ResetInventoryTrendAppearanceDefaults(draft, defaults);
                    return true;
                case "MainPanel":
                    ResetFloatDefaults(draft, defaults);
                    return true;
                case "Data":
                    ResetDataDefaults(draft, defaults);
                    return true;
                case "MarketAlerts":
                    ResetMarketAlertDefaults(draft, defaults);
                    return true;
                case "Cs2UpdatePhoneReminder":
                    ResetCs2UpdateReminderDefaults(draft, defaults);
                    ResetPhoneAlertDefaults(draft, defaults);
                    resetCs2Schedule?.Invoke();
                    return true;
                case "System":
                    ResetSystemDefaults(draft, defaults);
                    return true;
                default:
                    return false;
            }
        }

        private static void ResetFloatDefaults(Settings draft, Settings defaults)
        {
            draft.Skin = defaults.Skin;
            draft.HorizontalMode = defaults.HorizontalMode;
            draft.Opacity = defaults.Opacity;
            draft.PanelBackgroundOpacity = defaults.PanelBackgroundOpacity;
            draft.TextOpacity = defaults.TextOpacity;
            draft.PanelWidth = defaults.PanelWidth;
            draft.UIScale = defaults.UIScale;
            draft.PanelBackgroundColor = defaults.PanelBackgroundColor;
            draft.TopMost = defaults.TopMost;
            draft.ClickThrough = defaults.ClickThrough;
            draft.HideMainForm = defaults.HideMainForm;
            draft.LockPosition = defaults.LockPosition;
            draft.ClampToScreen = defaults.ClampToScreen;
            draft.AutoHide = defaults.AutoHide;
            draft.HorizontalFollowsTaskbar = defaults.HorizontalFollowsTaskbar;
            draft.HorizontalSingleLine = defaults.HorizontalSingleLine;
            draft.HorizontalItemSpacing = defaults.HorizontalItemSpacing;
            draft.HorizontalInnerSpacing = defaults.HorizontalInnerSpacing;
        }

        private static void ResetTaskbarDefaults(Settings draft, Settings defaults)
        {
            draft.ShowTaskbar = defaults.ShowTaskbar;
            draft.TaskbarPresetStyle = defaults.TaskbarPresetStyle;
            draft.TaskbarSingleLine = defaults.TaskbarSingleLine;
            draft.TaskbarHoverShowAll = defaults.TaskbarHoverShowAll;
            draft.TaskbarClickThrough = defaults.TaskbarClickThrough;
            draft.TaskbarAlignLeft = defaults.TaskbarAlignLeft;
            draft.TaskbarManualOffset = defaults.TaskbarManualOffset;
        }

        private static void ResetStyleDefaults(Settings draft, Settings defaults)
        {
            draft.TaskbarCustomLayout = defaults.TaskbarCustomLayout;
            draft.TaskbarFontFamily = defaults.TaskbarFontFamily;
            draft.TaskbarFontSize = defaults.TaskbarFontSize;
            draft.TaskbarFontBold = defaults.TaskbarFontBold;
            draft.TaskbarItemSpacing = defaults.TaskbarItemSpacing;
            draft.TaskbarInnerSpacing = defaults.TaskbarInnerSpacing;
            draft.TaskbarVerticalPadding = defaults.TaskbarVerticalPadding;
            draft.TaskbarCustomStyle = defaults.TaskbarCustomStyle;
            draft.TaskbarColorBg = defaults.TaskbarColorBg;
            draft.TaskbarColorLabel = defaults.TaskbarColorLabel;
            draft.TaskbarColorSafe = defaults.TaskbarColorSafe;
            draft.TaskbarColorWarn = defaults.TaskbarColorWarn;
            draft.TaskbarColorCrit = defaults.TaskbarColorCrit;
        }

        private static void ResetDataDefaults(Settings draft, Settings defaults)
        {
            // 保留原有 SteamDtApiKey 和刷新设置，不恢复为默认值，除非完全恢复默认。
            draft.SteamDtCompactMode = defaults.SteamDtCompactMode;
            draft.SteamDtShowPercent = defaults.SteamDtShowPercent;
            draft.SteamDtPositiveColor = defaults.SteamDtPositiveColor;
            draft.SteamDtNegativeColor = defaults.SteamDtNegativeColor;
            draft.SteamDtWarningColor = defaults.SteamDtWarningColor;
            draft.SteamDtNeutralColor = defaults.SteamDtNeutralColor;
            draft.SteamDtBackgroundColor = defaults.SteamDtBackgroundColor;
        }

        private static void ResetMarketAlertDefaults(Settings draft, Settings defaults)
        {
            draft.MarketAlertsEnabled = defaults.MarketAlertsEnabled;
            draft.MarketAlertDeferWhenFullscreen = defaults.MarketAlertDeferWhenFullscreen;
            draft.MarketAlertNotificationMode = defaults.MarketAlertNotificationMode;
            draft.MarketAlertDefaultWindowMinutes = defaults.MarketAlertDefaultWindowMinutes;
            draft.MarketAlertDefaultCooldownMinutes = defaults.MarketAlertDefaultCooldownMinutes;
            draft.MarketAlertRules = Settings.CreateDefaultMarketAlertRules();
        }

        private static void ResetPhoneAlertDefaults(Settings draft, Settings defaults)
        {
            draft.PhoneAlertEnabled = defaults.PhoneAlertEnabled;
            draft.PhoneAlertProvider = defaults.PhoneAlertProvider;
            draft.ServerChanSendKey = defaults.ServerChanSendKey;
            draft.WxPusherSpt = defaults.WxPusherSpt;
            draft.PhoneAlertDispatchMode = defaults.PhoneAlertDispatchMode;
            draft.PhoneAlertChannels = JsonSerializer.Deserialize<List<PhoneAlertChannelConfig>>(
                JsonSerializer.Serialize(defaults.PhoneAlertChannels)) ?? new List<PhoneAlertChannelConfig>();
        }

        private static void ResetCs2UpdateReminderDefaults(Settings draft, Settings defaults)
        {
            draft.Cs2UpdateReminderEnabled = defaults.Cs2UpdateReminderEnabled;
            draft.Cs2UpdateReminderRefreshSec = defaults.Cs2UpdateReminderRefreshSec;
            draft.Cs2UpdateReminderWechatEnabled = defaults.Cs2UpdateReminderWechatEnabled;
            draft.Cs2UpdateReminderSoundEnabled = defaults.Cs2UpdateReminderSoundEnabled;
            draft.Cs2UpdateBaselineKey = defaults.Cs2UpdateBaselineKey;
            draft.Cs2UpdateBaselineTitle = defaults.Cs2UpdateBaselineTitle;
            draft.Cs2UpdateBaselinePublishedAt = defaults.Cs2UpdateBaselinePublishedAt;
            draft.Cs2UpdateLastCheckTime = defaults.Cs2UpdateLastCheckTime;
            draft.Cs2UpdateLastStatus = defaults.Cs2UpdateLastStatus;
        }

        private static void ResetSystemDefaults(Settings draft, Settings defaults)
        {
            draft.AutoStart = defaults.AutoStart;
            draft.ShowMainWindowInTaskbar = defaults.ShowMainWindowInTaskbar;
            draft.HideTrayIcon = defaults.HideTrayIcon;
            draft.Language = "zh";
        }

        private static void ResetItemMonitorDefaults(Settings draft, Settings defaults)
        {
            draft.DefaultItemRefreshIntervalSec = defaults.DefaultItemRefreshIntervalSec;
            draft.DefaultItemPriceAlertRisePercent = defaults.DefaultItemPriceAlertRisePercent;
            draft.DefaultItemPriceAlertFallPercent = defaults.DefaultItemPriceAlertFallPercent;
            draft.DefaultItemPriceAlertWindowMinutes = defaults.DefaultItemPriceAlertWindowMinutes;
            draft.DefaultItemPriceAlertCooldownMinutes = defaults.DefaultItemPriceAlertCooldownMinutes;
            draft.ItemConfigs = defaults.ItemConfigs != null ? new List<ItemMonitorConfig>(defaults.ItemConfigs) : new List<ItemMonitorConfig>();
        }

        private static void ResetItemMonitorAppearanceDefaults(Settings draft, Settings defaults)
        {
            draft.ItemMonitorDefaultVisibleInPanel = defaults.ItemMonitorDefaultVisibleInPanel;
            draft.ItemMonitorDefaultVisibleInTaskbar = defaults.ItemMonitorDefaultVisibleInTaskbar;
            draft.SteamDtPositiveColor = defaults.SteamDtPositiveColor;
            draft.SteamDtNegativeColor = defaults.SteamDtNegativeColor;
            draft.SteamDtWarningColor = defaults.SteamDtWarningColor;
            draft.SteamDtNeutralColor = defaults.SteamDtNeutralColor;
        }

        private static void ResetInventoryTrendAppearanceDefaults(Settings draft, Settings defaults)
        {
            draft.YouPinTrendPageRefreshSec = defaults.YouPinTrendPageRefreshSec;
            draft.YouPinTrendFontSize = defaults.YouPinTrendFontSize;
            draft.YouPinTrendRiseColor = defaults.YouPinTrendRiseColor;
            draft.YouPinTrendFallColor = defaults.YouPinTrendFallColor;
            draft.YouPinTrendTextColor = defaults.YouPinTrendTextColor;
            draft.YouPinTrendSubTextColor = defaults.YouPinTrendSubTextColor;
            draft.YouPinTrendCurveColor = defaults.YouPinTrendCurveColor;
            draft.YouPinTrendIndicatorVisibleInPanel = defaults.YouPinTrendIndicatorVisibleInPanel;
            draft.YouPinTrendIndicatorVisibleInTaskbar = defaults.YouPinTrendIndicatorVisibleInTaskbar;
            draft.YouPinTrendIndicatorDisplayMode = defaults.YouPinTrendIndicatorDisplayMode;
            draft.YouPinTrendIndicatorSignMode = defaults.YouPinTrendIndicatorSignMode;
            draft.YouPinTrendIndicatorFontSize = defaults.YouPinTrendIndicatorFontSize;
            draft.YouPinTrendIndicatorFontBold = defaults.YouPinTrendIndicatorFontBold;
            draft.YouPinTrendIndicatorProfitColor = defaults.YouPinTrendIndicatorProfitColor;
            draft.YouPinTrendIndicatorLossColor = defaults.YouPinTrendIndicatorLossColor;
            draft.YouPinTrendIndicatorZeroColor = defaults.YouPinTrendIndicatorZeroColor;
            draft.YouPinTrendIndicatorSubTextColor = defaults.YouPinTrendIndicatorSubTextColor;
        }

        private static void ResetYouPinSaleReminderDefaults(Settings draft, Settings defaults)
        {
            draft.YouPinSaleReminderEnabled = defaults.YouPinSaleReminderEnabled;
            draft.YouPinSaleReminderRefreshSec = defaults.YouPinSaleReminderRefreshSec;
            draft.YouPinSaleReminderIncludeAllTodos = defaults.YouPinSaleReminderIncludeAllTodos;
            draft.YouPinSaleReminderNotificationMode = defaults.YouPinSaleReminderNotificationMode;
            draft.YouPinMsgCenterEnabled = defaults.YouPinMsgCenterEnabled;
            draft.YouPinMsgCenterRefreshSec = defaults.YouPinMsgCenterRefreshSec;
            draft.YouPinMsgCenterNotificationMode = defaults.YouPinMsgCenterNotificationMode;
        }

        private static void ResetYouPinStopProfitLossDefaults(Settings draft, Settings defaults)
        {
            draft.YouPinStopProfitLossEnabled = defaults.YouPinStopProfitLossEnabled;
            draft.YouPinStopProfitLossWindowMinutes = defaults.YouPinStopProfitLossWindowMinutes;
            draft.YouPinStopProfitPercentThreshold = defaults.YouPinStopProfitPercentThreshold;
            draft.YouPinStopLossPercentThreshold = defaults.YouPinStopLossPercentThreshold;
            draft.YouPinStopProfitLossCooldownMinutes = defaults.YouPinStopProfitLossCooldownMinutes;
            draft.YouPinStopProfitLossNotificationMode = defaults.YouPinStopProfitLossNotificationMode;
            draft.YouPinStopProfitLossOnlySpecifiedItems = defaults.YouPinStopProfitLossOnlySpecifiedItems;
            draft.YouPinStopProfitLossSpecifiedItems = defaults.YouPinStopProfitLossSpecifiedItems;
            draft.YouPinStopProfitLossExcludedItems = defaults.YouPinStopProfitLossExcludedItems;
            draft.YouPinStopProfitLossItemRulesJson = defaults.YouPinStopProfitLossItemRulesJson;
        }

        private static void ResetSteamOfferDefaults(Settings draft, Settings defaults)
        {
            draft.SteamOfferEnabled = defaults.SteamOfferEnabled;
            draft.SteamOfferRefreshSec = defaults.SteamOfferRefreshSec;
            draft.SteamOfferAllowYouPinVerifiedAccept = defaults.SteamOfferAllowYouPinVerifiedAccept;
            draft.SteamOfferAutoCheck = defaults.SteamOfferAutoCheck;
            draft.SteamOfferAutoAccept = defaults.SteamOfferAutoAccept;
            draft.SteamOfferAutoCheckSec = defaults.SteamOfferAutoCheckSec;
            draft.SteamOfferRedesignRule = defaults.SteamOfferRedesignRule;
            draft.SteamOfferRedesignAutoRefresh = defaults.SteamOfferRedesignAutoRefresh;
            draft.SteamOfferRedesignRefreshMinutes = defaults.SteamOfferRedesignRefreshMinutes;
            draft.SteamOfferRedesignSkipSingleConfirm = defaults.SteamOfferRedesignSkipSingleConfirm;
            draft.SteamOfferRedesignSkipBatchConfirm = defaults.SteamOfferRedesignSkipBatchConfirm;
        }
    }
}
