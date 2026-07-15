using CS2TradeMonitor.src.Core;
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
        public const int Default = Name | Price;
        public const int All = Name | Price | Change | Percent | Source | RefreshTime;

        public static IReadOnlyList<ItemMonitorDisplayFieldOption> Options { get; } = new[]
        {
            new ItemMonitorDisplayFieldOption("名称", Name),
            new ItemMonitorDisplayFieldOption("价格", Price),
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
        {
            return new[]
            {
                Assign(nameof(Settings.TaskbarPresetStyle), bold ? 1 : 0),
                Assign(nameof(Settings.TaskbarCustomLayout), true),
                Assign(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT),
                Assign(nameof(Settings.TaskbarFontSize), bold ? Settings.DEFAULT_TB_SIZE_BOLD : Settings.DEFAULT_TB_SIZE_REGULAR),
                Assign(nameof(Settings.TaskbarFontBold), bold),
                Assign(nameof(Settings.TaskbarInnerSpacing), bold ? Settings.DEFAULT_TB_INNER_BOLD : Settings.DEFAULT_TB_INNER_REGULAR),
                Assign(nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF)
            };
        }

        public static IReadOnlyList<MainPanelSettingAssignment> BuildTaskbarPreset(int type)
        {
            return type switch
            {
                0 => new[]
                {
                    Assign(nameof(Settings.TaskbarPresetStyle), 1),
                    Assign(nameof(Settings.TaskbarCustomLayout), true),
                    Assign(nameof(Settings.TaskbarCustomStyle), true),
                    Assign(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT),
                    Assign(nameof(Settings.TaskbarFontSize), Settings.DEFAULT_TB_SIZE_BOLD),
                    Assign(nameof(Settings.TaskbarFontBold), true),
                    Assign(nameof(Settings.TaskbarItemSpacing), Settings.DEFAULT_TB_GAP),
                    Assign(nameof(Settings.TaskbarInnerSpacing), Settings.DEFAULT_TB_INNER_BOLD),
                    Assign(nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF),
                    Assign(nameof(Settings.Skin), "DarkFlat_Classic")
                },
                1 => new[]
                {
                    Assign(nameof(Settings.TaskbarPresetStyle), 0),
                    Assign(nameof(Settings.TaskbarCustomLayout), true),
                    Assign(nameof(Settings.TaskbarFontSize), 9f),
                    Assign(nameof(Settings.TaskbarItemSpacing), 4),
                    Assign(nameof(Settings.TaskbarInnerSpacing), 4),
                    Assign(nameof(Settings.TaskbarVerticalPadding), 2),
                    Assign(nameof(Settings.TaskbarSingleLine), true),
                    Assign(nameof(Settings.TaskbarFontBold), false)
                },
                2 => new[]
                {
                    Assign(nameof(Settings.TaskbarCustomLayout), true),
                    Assign(nameof(Settings.TaskbarFontSize), 12f),
                    Assign(nameof(Settings.TaskbarFontBold), true),
                    Assign(nameof(Settings.TaskbarItemSpacing), 6),
                    Assign(nameof(Settings.TaskbarInnerSpacing), 8),
                    Assign(nameof(Settings.TaskbarVerticalPadding), 2),
                    Assign(nameof(Settings.TaskbarCustomStyle), true),
                    Assign(nameof(Settings.TaskbarColorBg), "#001E3D"),
                    Assign(nameof(Settings.TaskbarColorLabel), "#FFFFFF"),
                    Assign(nameof(Settings.TaskbarColorCrit), "#FF4444"),
                    Assign(nameof(Settings.TaskbarColorSafe), "#00CC66"),
                    Assign(nameof(Settings.TaskbarColorWarn), "#FFFF00")
                },
                3 => new[]
                {
                    Assign(nameof(Settings.TaskbarCustomLayout), true),
                    Assign(nameof(Settings.TaskbarFontSize), 11f),
                    Assign(nameof(Settings.TaskbarFontBold), true),
                    Assign(nameof(Settings.TaskbarItemSpacing), 6),
                    Assign(nameof(Settings.TaskbarInnerSpacing), 8),
                    Assign(nameof(Settings.TaskbarVerticalPadding), 2),
                    Assign(nameof(Settings.TaskbarCustomStyle), true),
                    Assign(nameof(Settings.TaskbarColorBg), "#001E3D"),
                    Assign(nameof(Settings.TaskbarColorLabel), "#FFD700"),
                    Assign(nameof(Settings.TaskbarColorCrit), "#FF4444"),
                    Assign(nameof(Settings.TaskbarColorSafe), "#00FFCC"),
                    Assign(nameof(Settings.TaskbarColorWarn), "#FFFF00")
                },
                _ => Array.Empty<MainPanelSettingAssignment>()
            };
        }

        public static MainPanelSafeVisibilityResult ResolveSafeVisibility(
            bool hideMainForm,
            bool hideTrayIcon,
            bool showTaskbar,
            bool clickThrough,
            bool taskbarClickThrough)
        {
            bool noInteractiveEntry = (hideMainForm || clickThrough)
                && (!showTaskbar || taskbarClickThrough)
                && hideTrayIcon;
            return noInteractiveEntry
                ? new MainPanelSafeVisibilityResult(true, HideMainForm: false, ShowTaskbar: true)
                : new MainPanelSafeVisibilityResult(false, hideMainForm, showTaskbar);
        }

        private static MainPanelSettingAssignment Assign(string key, object value)
        {
            return new MainPanelSettingAssignment(key, value);
        }
    }
}
