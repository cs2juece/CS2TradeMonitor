using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.Shared.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class GroupLayoutCache
    {
        public GroupLayoutCache(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }
    }

    internal sealed class DeferredTabGroupBuild
    {
        public DeferredTabGroupBuild(string key, string tab, string scope, Action build, Action? afterBuild)
        {
            Key = key;
            Tab = tab;
            Scope = scope;
            Build = build;
            AfterBuild = afterBuild;
        }

        public string Key { get; }

        public string Tab { get; }

        public string Scope { get; }

        public Action Build { get; }

        public Action? AfterBuild { get; }
    }

    internal static class MainPanelTabKeys
    {
        public const string Float = "Float";
        public const string Taskbar = "Taskbar";
        public const string Appearance = "Appearance";
        public const string Style = "Style";
        public const string ItemMonitor = "ItemMonitor";
        public const string InventoryTrend = "InventoryTrend";

        public static IReadOnlyList<MainPanelTabOption> Options { get; } = new[]
        {
            new MainPanelTabOption(Float, "悬浮窗"),
            new MainPanelTabOption(Taskbar, "任务栏"),
            new MainPanelTabOption(Style, "字体与颜色"),
            new MainPanelTabOption(ItemMonitor, "单品监控"),
            new MainPanelTabOption(InventoryTrend, "库存涨跌")
        };

        public static string Normalize(string key)
        {
            if (string.Equals(key, Taskbar, StringComparison.OrdinalIgnoreCase)) return Taskbar;
            if (string.Equals(key, Appearance, StringComparison.OrdinalIgnoreCase)) return Style;
            if (string.Equals(key, Style, StringComparison.OrdinalIgnoreCase)) return Style;
            if (string.Equals(key, ItemMonitor, StringComparison.OrdinalIgnoreCase)) return ItemMonitor;
            if (string.Equals(key, InventoryTrend, StringComparison.OrdinalIgnoreCase)) return InventoryTrend;
            return Float;
        }

        public static int GetLogicalButtonWidth(string text)
        {
            return text.Length >= 4 ? 108 : 92;
        }
    }

    internal sealed record MainPanelTabOption(string Key, string Text);

    internal sealed record MainPanelDeferredGroupPlan(
        string Key,
        string Tab,
        string Scope);

    internal sealed record MainPanelInitialTabGroupPlan(
        string Key,
        string Scope);

    internal sealed record MainPanelInitialTabBuildPlan(
        string Tab,
        IReadOnlyList<MainPanelInitialTabGroupPlan> Groups,
        MainPanelInitialTabFollowUp FollowUp);

    internal enum MainPanelInitialTabFollowUp
    {
        None,
        FloatAppearance,
        TaskbarAdvanced
    }

    internal static class MainPanelInitialTabGroupPlanner
    {
        public static MainPanelInitialTabBuildPlan Build(string tab)
        {
            string normalized = MainPanelTabKeys.Normalize(tab);
            return normalized switch
            {
                MainPanelTabKeys.Taskbar => new MainPanelInitialTabBuildPlan(
                    normalized,
                    new[] { new MainPanelInitialTabGroupPlan("Taskbar.General", "Taskbar.General") },
                    MainPanelInitialTabFollowUp.TaskbarAdvanced),
                MainPanelTabKeys.Style => new MainPanelInitialTabBuildPlan(
                    normalized,
                    new[] { new MainPanelInitialTabGroupPlan("Style.Font", "Style.Font") },
                    MainPanelInitialTabFollowUp.None),
                MainPanelTabKeys.ItemMonitor => new MainPanelInitialTabBuildPlan(
                    normalized,
                    new[] { new MainPanelInitialTabGroupPlan("ItemMonitor.Display", "ItemMonitor.Display") },
                    MainPanelInitialTabFollowUp.None),
                MainPanelTabKeys.InventoryTrend => new MainPanelInitialTabBuildPlan(
                    normalized,
                    new[] { new MainPanelInitialTabGroupPlan("InventoryTrend.Display", "InventoryTrend.Display") },
                    MainPanelInitialTabFollowUp.None),
                _ => new MainPanelInitialTabBuildPlan(
                    MainPanelTabKeys.Float,
                    new[]
                    {
                        new MainPanelInitialTabGroupPlan("Float.Behavior", "Float.Behavior"),
                        new MainPanelInitialTabGroupPlan("Float.Layout", "Float.Layout")
                    },
                    MainPanelInitialTabFollowUp.FloatAppearance)
            };
        }
    }

    internal static class MainPanelDeferredGroupPlanner
    {
        private static readonly IReadOnlyList<MainPanelDeferredGroupPlan> StyleGroups = new[]
        {
            new MainPanelDeferredGroupPlan("Style.FontFamily", MainPanelTabKeys.Style, "Style.FontFamily"),
            new MainPanelDeferredGroupPlan("Style.Spacing", MainPanelTabKeys.Style, "Style.Spacing"),
            new MainPanelDeferredGroupPlan("Style.Color", MainPanelTabKeys.Style, "Style.Color"),
            new MainPanelDeferredGroupPlan("Style.Preset", MainPanelTabKeys.Style, "Style.Preset")
        };

        private static readonly IReadOnlyList<MainPanelDeferredGroupPlan> ItemMonitorGroups = new[]
        {
            new MainPanelDeferredGroupPlan("ItemMonitor.Color", MainPanelTabKeys.ItemMonitor, "ItemMonitor.Color")
        };

        private static readonly IReadOnlyList<MainPanelDeferredGroupPlan> InventoryTrendGroups = new[]
        {
            new MainPanelDeferredGroupPlan("InventoryTrend.Color", MainPanelTabKeys.InventoryTrend, "InventoryTrend.Color")
        };

        public static IReadOnlyList<MainPanelDeferredGroupPlan> BuildSupplementalGroups(
            string activeTab,
            IReadOnlyCollection<string> builtTabs)
        {
            string normalized = MainPanelTabKeys.Normalize(activeTab);
            if (!builtTabs.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                return Array.Empty<MainPanelDeferredGroupPlan>();

            return normalized switch
            {
                MainPanelTabKeys.Style => StyleGroups,
                MainPanelTabKeys.ItemMonitor => ItemMonitorGroups,
                MainPanelTabKeys.InventoryTrend => InventoryTrendGroups,
                _ => Array.Empty<MainPanelDeferredGroupPlan>()
            };
        }
    }

    internal static class ItemMonitorDisplayFields
    {
        public const int Name = 1 << 0;
        public const int Price = 1 << 1;
        public const int Change = 1 << 2;
        public const int Percent = 1 << 3;
        public const int Source = 1 << 4;
        public const int RefreshTime = 1 << 5;
        public const int YouPinBid = 1 << 6;
        public const int Default = Name | Price;
        public const int All = Name | Price | Change | Percent | Source | RefreshTime | YouPinBid;

        public static IReadOnlyList<ItemMonitorDisplayFieldOption> Options { get; } = new[]
        {
            new ItemMonitorDisplayFieldOption("名称", Name),
            new ItemMonitorDisplayFieldOption("价格", Price),
            new ItemMonitorDisplayFieldOption("悠悠求购", YouPinBid),
            new ItemMonitorDisplayFieldOption("涨跌", Change),
            new ItemMonitorDisplayFieldOption("涨跌幅", Percent),
            new ItemMonitorDisplayFieldOption("来源", Source),
            new ItemMonitorDisplayFieldOption("时间", RefreshTime)
        };

        public static int Normalize(int flags)
        {
            int normalized = flags == 0 ? Default : flags;
            return (normalized & All) == 0 ? Price : normalized;
        }
    }

    internal sealed record ItemMonitorDisplayFieldOption(string Text, int Flag);

    internal sealed record MainPanelSettingAssignment(string Key, object Value);

    internal sealed record MainPanelSafeVisibilityResult(
        bool RequiresCorrection,
        bool HideMainForm,
        bool ShowTaskbar);

    internal static class MainPanelSettingsRules
    {
        public static IReadOnlyList<MainPanelSettingAssignment> BuildTaskbarStylePreset(bool bold)
            => InterfaceSettingsRules.BuildTaskbarStylePreset(bold)
                .Select(assignment => new MainPanelSettingAssignment(assignment.Key, assignment.Value))
                .ToArray();

        public static IReadOnlyList<MainPanelSettingAssignment> BuildTaskbarPreset(int type)
            => InterfaceSettingsRules.BuildTaskbarPreset(type)
                .Select(assignment => new MainPanelSettingAssignment(assignment.Key, assignment.Value))
                .ToArray();

        public static MainPanelSafeVisibilityResult ResolveSafeVisibility(
            bool hideMainForm,
            bool hideTrayIcon,
            bool showTaskbar,
            bool clickThrough,
            bool taskbarClickThrough)
        {
            InterfaceSafeVisibilityResult result = InterfaceSettingsRules.ResolveSafeVisibility(
                hideMainForm,
                hideTrayIcon,
                showTaskbar,
                clickThrough,
                taskbarClickThrough);
            return new MainPanelSafeVisibilityResult(
                result.RequiresCorrection,
                result.HideMainForm,
                result.ShowTaskbar);
        }
    }
}
