using System;
using System.Collections.Generic;
using System.Linq;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Framework.SteamOffers;
using CS2TradeMonitor.src.UI.SettingsPage;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class SettingsPageRoute
    {
        public SettingsPageRoute(
            string key,
            Func<string> titleFactory,
            Type pageType,
            Func<SettingsPageBase> factory,
            bool placeInSystemContainer = false)
        {
            Key = key;
            TitleFactory = titleFactory;
            PageType = pageType;
            Factory = factory;
            PlaceInSystemContainer = placeInSystemContainer;
        }

        public string Key { get; }
        public Func<string> TitleFactory { get; }
        public Type PageType { get; }
        public Func<SettingsPageBase> Factory { get; }
        public bool PlaceInSystemContainer { get; }
        public string Title => TitleFactory();

        public SettingsPageBase CreatePage() => Factory();
    }

    public static class SettingsPageRegistry
    {
        private static readonly SettingsPageRoute[] Routes =
        {
            // SettingsForm is the production shell. Pages below must be Framework hosts.
            new("MainPanel", () => "🖥️ " + LanguageManager.T("Menu.MainFormSettings"), typeof(MainPanelHostPage), () => new MainPanelHostPage()),
            new("ItemMonitor", () => "📦 单品监控", typeof(ItemMonitorHostPage), () => new ItemMonitorHostPage()),
            new("YouPin", () => "🔔 悠悠有品", typeof(YouPinCcHostPage), () => new YouPinCcHostPage()),
            new("YouPinStopProfitLoss", () => "🎯 库存止损/盈", typeof(YouPinStopProfitLossRedesignHostPage), () => new YouPinStopProfitLossRedesignHostPage()),
            new("SteamOffers", () => "🧾 Steam报价", typeof(SteamOfferRedesignHostPage), () => new SteamOfferRedesignHostPage()),
            new("Data", () => "📈 大盘数据源", typeof(DataRedesignHostPage), () => new DataRedesignHostPage()),
            new("MarketAlerts", () => "🔔 大盘预警", typeof(MarketAlertRedesignHostPage), () => new MarketAlertRedesignHostPage()),
            new("Cs2UpdatePhoneReminder", () => "🆕 更新与手机提醒", typeof(Cs2UpdatePhoneReminderHostPage), () => new Cs2UpdatePhoneReminderHostPage()),
            new("YouPinProfitLoss", () => "💰 吃米/亏米统计", typeof(YouPinProfitLossRedesignHostPage), () => new YouPinProfitLossRedesignHostPage()),
            new("System", () => "⚙️ " + LanguageManager.T("Menu.SystemSettings"), typeof(SystemSettingsHostPage), () => new SystemSettingsHostPage(), placeInSystemContainer: true)
        };

        private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MainPanelTest"] = "MainPanel",
            ["ItemMonitorTest"] = "ItemMonitor",
            ["YouPinInventory"] = "YouPin",
            ["YouPinInventoryTrend"] = "YouPin:Inventory",
            ["YouPinSaleReminder"] = "YouPin:Quote",
            ["YouPinQuote"] = "YouPin:Quote",
            ["YouPinAutoQuote"] = "YouPin:AutoQuote",
            ["YouPinCc"] = "YouPin:Cc",
            ["YouPinLandlord"] = "YouPin:Cc",
            ["YouPinSettings"] = "YouPin:Settings",
            ["YouPinStopProfitLossRedesign"] = "YouPinStopProfitLoss",
            ["SteamOffersRedesign"] = "SteamOffers",
            ["SteamOffersList"] = "SteamOffers:List",
            ["SteamOffersAuto"] = "SteamOffers:Auto",
            ["SteamOffersSettings"] = "SteamOffers:Settings",
            ["DataRedesign"] = "Data",
            ["MarketDataSourceRedesign"] = "Data",
            ["MarketAlertsRedesign"] = "MarketAlerts",
            ["YouPinProfitLossRedesign"] = "YouPinProfitLoss",
            ["PhoneAlerts"] = "Cs2UpdatePhoneReminder",
            ["Cs2UpdateReminder"] = "Cs2UpdatePhoneReminder"
        };

        private static readonly Dictionary<string, SettingsPageRoute> RouteMap =
            Routes.ToDictionary(route => route.Key, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<SettingsPageRoute> NavigationRoutes => Routes;

        public static string NormalizeKey(string key)
        {
            string normalized = (key ?? string.Empty).Trim();
            if (Aliases.TryGetValue(normalized, out var alias))
                normalized = alias;

            if (TrySplitRoute(normalized, out string baseKey, out string subRoute))
            {
                string normalizedBase = NormalizeBaseKey(baseKey);
                return string.IsNullOrWhiteSpace(subRoute)
                    ? normalizedBase
                    : normalizedBase + ":" + subRoute.Trim();
            }

            return NormalizeBaseKey(normalized);
        }

        public static string GetBaseKey(string key)
        {
            string normalized = NormalizeKey(key);
            return TrySplitRoute(normalized, out string baseKey, out _)
                ? baseKey
                : normalized;
        }

        public static string? GetSubRoute(string key)
        {
            string normalized = NormalizeKey(key);
            return TrySplitRoute(normalized, out _, out string subRoute) && !string.IsNullOrWhiteSpace(subRoute)
                ? subRoute.Trim()
                : null;
        }

        public static bool IsKnownRoute(string key)
        {
            return RouteMap.ContainsKey(GetBaseKey(key));
        }

        public static SettingsPageBase CreatePage(string key)
        {
            var normalized = GetBaseKey(key);
            if (!RouteMap.TryGetValue(normalized, out var route))
                throw new InvalidOperationException($"未知设置页: {normalized}");

            return route.CreatePage();
        }

        public static void ValidateProductionRoutes()
        {
            var duplicates = Routes
                .GroupBy(route => route.Key, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (duplicates.Length > 0)
                throw new InvalidOperationException("设置页生产路由重复: " + string.Join(", ", duplicates));

            foreach (var alias in Aliases)
            {
                if (!RouteMap.ContainsKey(GetBaseKey(alias.Value)))
                    throw new InvalidOperationException($"设置页别名 {alias.Key} 指向未知路由 {alias.Value}");
            }

            foreach (var route in Routes)
            {
                if (!typeof(SettingsPageBase).IsAssignableFrom(route.PageType))
                    throw new InvalidOperationException($"设置页路由 {route.Key} 的类型不是 SettingsPageBase: {route.PageType.FullName}");

                if (route.PageType.Name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException($"设置页生产路由不得注册占位页: {route.Key} -> {route.PageType.FullName}");

                if (route.PageType.Name.Contains("TestHostPage", StringComparison.OrdinalIgnoreCase)
                    || route.PageType.Name.Contains("RedesignTest", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"设置页生产路由不得注册测试命名页面: {route.Key} -> {route.PageType.FullName}");
                }

                var ns = route.PageType.Namespace ?? string.Empty;
                if (ns.StartsWith("CS2TradeMonitor.src.UI.SettingsPage", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"设置页生产路由不得直接注册旧 SettingsPage 页面: {route.Key} -> {route.PageType.FullName}");
                }
            }
        }

        private static string NormalizeBaseKey(string key)
        {
            string normalized = (key ?? string.Empty).Trim();
            if (!Aliases.TryGetValue(normalized, out var alias))
                return normalized;

            return TrySplitRoute(alias, out string aliasBase, out _)
                ? NormalizeBaseKey(aliasBase)
                : alias;
        }

        private static bool TrySplitRoute(string key, out string baseKey, out string subRoute)
        {
            string value = key ?? string.Empty;
            int index = value.IndexOf(':');
            if (index < 0)
            {
                baseKey = value;
                subRoute = string.Empty;
                return false;
            }

            baseKey = value[..index].Trim();
            subRoute = value[(index + 1)..].Trim();
            return true;
        }
    }
}
