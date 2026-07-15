using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelMonitorGroupsBuilder
    {
        internal delegate LiteNumberInput AddDirectIntControl(
            LiteSettingsGroup group,
            string title,
            string unit,
            Func<int> get,
            Action<int> set,
            int width);

        internal delegate LiteCheck AddDirectToggleControl(
            LiteSettingsGroup group,
            string title,
            Func<bool> get,
            Action<bool> set);

        internal delegate LiteNumberInput AddIntControl(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            int fallback,
            string unit,
            int width,
            Func<int, int>? normalize,
            Action<int>? afterChanged);

        internal delegate void AddFloatControl(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            float fallback,
            string unit,
            Func<float, float> normalize,
            bool markCustomLayout);

        private readonly Func<List<ItemMonitorConfig>> _getItemConfigs;
        private readonly Func<string, int, int> _getInt;
        private readonly Action<string, object> _set;
        private readonly AddDirectIntControl _addDirectInt;
        private readonly AddDirectToggleControl _addDirectToggle;
        private readonly Action<LiteSettingsGroup, string, string, string> _addDirectColor;
        private readonly AddIntControl _addInt;
        private readonly AddFloatControl _addFloat;
        private readonly Action<LiteSettingsGroup, string> _addHint;
        private readonly Action<LiteSettingsGroup> _addGroupToPage;
        private readonly Action<Action> _registerSave;
        private readonly Action _refreshMonitorDisplay;

        public MainPanelMonitorGroupsBuilder(
            Func<List<ItemMonitorConfig>> getItemConfigs,
            Func<string, int, int> getInt,
            Action<string, object> set,
            AddDirectIntControl addDirectInt,
            AddDirectToggleControl addDirectToggle,
            Action<LiteSettingsGroup, string, string, string> addDirectColor,
            AddIntControl addInt,
            AddFloatControl addFloat,
            Action<LiteSettingsGroup, string> addHint,
            Action<LiteSettingsGroup> addGroupToPage,
            Action<Action> registerSave,
            Action refreshMonitorDisplay)
        {
            _getItemConfigs = getItemConfigs ?? throw new ArgumentNullException(nameof(getItemConfigs));
            _getInt = getInt ?? throw new ArgumentNullException(nameof(getInt));
            _set = set ?? throw new ArgumentNullException(nameof(set));
            _addDirectInt = addDirectInt ?? throw new ArgumentNullException(nameof(addDirectInt));
            _addDirectToggle = addDirectToggle ?? throw new ArgumentNullException(nameof(addDirectToggle));
            _addDirectColor = addDirectColor ?? throw new ArgumentNullException(nameof(addDirectColor));
            _addInt = addInt ?? throw new ArgumentNullException(nameof(addInt));
            _addFloat = addFloat ?? throw new ArgumentNullException(nameof(addFloat));
            _addHint = addHint ?? throw new ArgumentNullException(nameof(addHint));
            _addGroupToPage = addGroupToPage ?? throw new ArgumentNullException(nameof(addGroupToPage));
            _registerSave = registerSave ?? throw new ArgumentNullException(nameof(registerSave));
            _refreshMonitorDisplay = refreshMonitorDisplay ?? throw new ArgumentNullException(nameof(refreshMonitorDisplay));
        }

        public void CreateItemMonitorDisplayGroup()
        {
            var group = new LiteSettingsGroup("单品监控外观");
            List<ItemMonitorConfig> items = _getItemConfigs();

            _addDirectInt(group, "全局抓取间隔", "秒",
                () => _getInt(nameof(Settings.DefaultItemRefreshIntervalSec), 300),
                value =>
                {
                    int normalized = NormalizeItemRefreshInterval(value);
                    _set(nameof(Settings.DefaultItemRefreshIntervalSec), normalized);
                    ApplyItemRefreshInterval(items, normalized);
                },
                width: 70);

            _addDirectToggle(group, "默认显示悬浮窗",
                () => AreAllVisibleInPanel(items),
                value =>
                {
                    SetPanelVisibility(items, value);
                    _set(nameof(Settings.ItemConfigs), items);
                    _refreshMonitorDisplay();
                });

            _addDirectToggle(group, "默认显示任务栏",
                () => AreAllVisibleInTaskbar(items),
                value =>
                {
                    SetTaskbarVisibility(items, value);
                    _set(nameof(Settings.ItemConfigs), items);
                    _refreshMonitorDisplay();
                });

            if (items.Count == 0)
            {
                _addHint(group, "还没有监控单品。请到左侧“单品监控”页面先添加饰品。");
            }
            else
            {
                _addHint(group, "这里控制单品是否进入悬浮窗/任务栏，以及显示名称、价格、涨跌、来源和刷新时间；抓取间隔为全局设置，所有单品共用。");
                foreach (ItemMonitorConfig item in items.OrderBy(item => item.SortIndex).ThenBy(item => item.Name))
                    group.AddFullItem(CreateItemMonitorDisplayRow(item, items));
            }

            _registerSave(() => _set(nameof(Settings.ItemConfigs), items));
            _addGroupToPage(group);
        }

        public void CreateItemMonitorColorGroup()
        {
            var group = new LiteSettingsGroup("单品颜色");
            _addHint(group, "单品价格、涨跌颜色与大盘/任务栏显示管线共用；修改后悬浮窗和任务栏会一起生效。");
            _addDirectColor(group, "上涨颜色", nameof(Settings.SteamDtPositiveColor), "#DC465A");
            _addDirectColor(group, "下跌颜色", nameof(Settings.SteamDtNegativeColor), "#50A087");
            _addDirectColor(group, "异常/过期颜色", nameof(Settings.SteamDtWarningColor), "#F0A000");
            _addDirectColor(group, "普通文字颜色", nameof(Settings.SteamDtNeutralColor), "#202020");
            _addGroupToPage(group);
        }

        public void CreateInventoryTrendDisplayGroup()
        {
            var group = new LiteSettingsGroup("库存涨跌外观");
            _addInt(group, "页面刷新间隔", nameof(Settings.YouPinTrendPageRefreshSec), 300, "秒", 70,
                value => Math.Clamp(value, 30, 3600), afterChanged: null);
            _addFloat(group, "列表字号", nameof(Settings.YouPinTrendFontSize), 9f, "pt",
                value => Math.Clamp(value, 7f, 16f), markCustomLayout: false);
            _addHint(group, "库存涨跌页会按这里的字号和颜色渲染统计卡、明细列表和当日盈亏曲线。");
            _addGroupToPage(group);
        }

        public void CreateInventoryTrendColorGroup()
        {
            var group = new LiteSettingsGroup("库存颜色");
            _addDirectColor(group, "上涨颜色", nameof(Settings.YouPinTrendRiseColor), "#DC465A");
            _addDirectColor(group, "下跌颜色", nameof(Settings.YouPinTrendFallColor), "#50A087");
            _addDirectColor(group, "普通文字颜色", nameof(Settings.YouPinTrendTextColor), "#202020");
            _addDirectColor(group, "辅助文字颜色", nameof(Settings.YouPinTrendSubTextColor), "#5A5A5A");
            _addDirectColor(group, "盈亏曲线颜色", nameof(Settings.YouPinTrendCurveColor), "#0078D7");
            _addGroupToPage(group);
        }

        internal static int NormalizeItemRefreshInterval(int value)
        {
            return Math.Max(60, value);
        }

        internal static void ApplyItemRefreshInterval(IEnumerable<ItemMonitorConfig> items, int intervalSec)
        {
            int normalized = NormalizeItemRefreshInterval(intervalSec);
            foreach (ItemMonitorConfig item in items)
                item.RefreshIntervalSec = normalized;
        }

        internal static bool AreAllVisibleInPanel(IReadOnlyCollection<ItemMonitorConfig> items)
        {
            return items.Count > 0 && items.All(item => item.VisibleInPanel);
        }

        internal static bool AreAllVisibleInTaskbar(IReadOnlyCollection<ItemMonitorConfig> items)
        {
            return items.Count > 0 && items.All(item => item.VisibleInTaskbar);
        }

        internal static void SetPanelVisibility(IEnumerable<ItemMonitorConfig> items, bool visible)
        {
            foreach (ItemMonitorConfig item in items)
                item.VisibleInPanel = visible;
        }

        internal static void SetTaskbarVisibility(IEnumerable<ItemMonitorConfig> items, bool visible)
        {
            foreach (ItemMonitorConfig item in items)
                item.VisibleInTaskbar = visible;
        }

        private Panel CreateItemMonitorDisplayRow(ItemMonitorConfig item, List<ItemMonitorConfig> items)
        {
            return MainPanelItemMonitorRowFactory.Create(
                item,
                items,
                () => _set(nameof(Settings.ItemConfigs), items),
                _refreshMonitorDisplay);
        }
    }
}
