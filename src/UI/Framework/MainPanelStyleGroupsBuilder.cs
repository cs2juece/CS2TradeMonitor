using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelStyleGroupsBuilder
    {
        internal delegate LiteCheck AddToggleControl(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            bool fallback,
            Action<bool>? afterChanged);

        internal delegate LiteNumberInput AddDirectIntControl(
            LiteSettingsGroup group,
            string title,
            string unit,
            Func<int> get,
            Action<int> set,
            int width);

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

        internal delegate LiteComboBox AddMappedStringComboControl(
            LiteSettingsGroup group,
            string title,
            IEnumerable<string> items,
            Func<string> getValue,
            Action<string> setValue,
            bool fullWidth);

        private readonly Func<string, double, double> _getDouble;
        private readonly Func<string, int, int> _getInt;
        private readonly Func<string, string, string> _getString;
        private readonly Action<string, object> _set;
        private readonly AddToggleControl _addToggle;
        private readonly AddDirectIntControl _addDirectInt;
        private readonly AddIntControl _addInt;
        private readonly AddFloatControl _addFloat;
        private readonly AddMappedStringComboControl _addMappedCombo;
        private readonly Action<LiteSettingsGroup, string, string, string, Action<string>?> _addColor;
        private readonly Action<LiteSettingsGroup, string> _addHint;
        private readonly Action<LiteSettingsGroup> _addGroupToPage;
        private readonly Action<int> _applyPreset;
        private readonly Action _refreshMonitorDisplay;

        public MainPanelStyleGroupsBuilder(
            Func<string, double, double> getDouble,
            Func<string, int, int> getInt,
            Func<string, string, string> getString,
            Action<string, object> set,
            AddToggleControl addToggle,
            AddDirectIntControl addDirectInt,
            AddIntControl addInt,
            AddFloatControl addFloat,
            AddMappedStringComboControl addMappedCombo,
            Action<LiteSettingsGroup, string, string, string, Action<string>?> addColor,
            Action<LiteSettingsGroup, string> addHint,
            Action<LiteSettingsGroup> addGroupToPage,
            Action<int> applyPreset,
            Action refreshMonitorDisplay)
        {
            _getDouble = getDouble ?? throw new ArgumentNullException(nameof(getDouble));
            _getInt = getInt ?? throw new ArgumentNullException(nameof(getInt));
            _getString = getString ?? throw new ArgumentNullException(nameof(getString));
            _set = set ?? throw new ArgumentNullException(nameof(set));
            _addToggle = addToggle ?? throw new ArgumentNullException(nameof(addToggle));
            _addDirectInt = addDirectInt ?? throw new ArgumentNullException(nameof(addDirectInt));
            _addInt = addInt ?? throw new ArgumentNullException(nameof(addInt));
            _addFloat = addFloat ?? throw new ArgumentNullException(nameof(addFloat));
            _addMappedCombo = addMappedCombo ?? throw new ArgumentNullException(nameof(addMappedCombo));
            _addColor = addColor ?? throw new ArgumentNullException(nameof(addColor));
            _addHint = addHint ?? throw new ArgumentNullException(nameof(addHint));
            _addGroupToPage = addGroupToPage ?? throw new ArgumentNullException(nameof(addGroupToPage));
            _applyPreset = applyPreset ?? throw new ArgumentNullException(nameof(applyPreset));
            _refreshMonitorDisplay = refreshMonitorDisplay ?? throw new ArgumentNullException(nameof(refreshMonitorDisplay));
        }

        public void CreateFloatAppearanceWidthGroup()
        {
            var group = new LiteSettingsGroup("悬浮窗宽度");
            _addDirectInt(group, "界面宽度", "px",
                () => Math.Clamp(_getInt(nameof(Settings.PanelWidth), 220), 180, 1200),
                value =>
                {
                    _set(nameof(Settings.PanelWidth), Math.Clamp(value, 180, 1200));
                    _refreshMonitorDisplay();
                },
                width: 70);
            _addGroupToPage(group);
        }

        public void CreateFloatAppearanceOpacityGroup()
        {
            var group = new LiteSettingsGroup("悬浮窗透明度");

            Step("PanelBackgroundOpacity", () =>
                _addDirectInt(group, "Menu.PanelBackgroundOpacity", "%",
                    () => (int)Math.Round((1.0 - _getDouble(nameof(Settings.PanelBackgroundOpacity), 1.0)) * 100),
                    value =>
                    {
                        double opacity = 1.0 - Math.Clamp(value, 0, 100) / 100.0;
                        _set(nameof(Settings.PanelBackgroundOpacity), Math.Clamp(opacity, 0.0, 1.0));
                        _set(nameof(Settings.Opacity), Math.Clamp(opacity, 0.0, 1.0));
                        _refreshMonitorDisplay();
                    },
                    width: 70));
            Step("TextOpacity", () =>
                _addDirectInt(group, "Menu.TextOpacity", "%",
                    () => (int)Math.Round((1.0 - _getDouble(nameof(Settings.TextOpacity), 1.0)) * 100),
                    value =>
                    {
                        _set(nameof(Settings.TextOpacity), Math.Clamp(1.0 - Math.Clamp(value, 0, 70) / 100.0, 0.0, 1.0));
                        _refreshMonitorDisplay();
                    },
                    width: 70));
            Step("AddGroupToPage", () => _addGroupToPage(group));
        }

        public void CreateFloatAppearanceAdvancedGroup()
        {
            var group = new LiteSettingsGroup("悬浮窗高级外观");

            Step("PanelBackgroundColor", () =>
                _addColor(group, "Menu.PanelBackgroundColor", nameof(Settings.PanelBackgroundColor), "", _ => _refreshMonitorDisplay()));
            Step("Hint", () =>
                _addHint(group, "提示：悬浮窗背景色为空时使用当前皮肤默认背景色。"));
            Step("Scale", () =>
                _addDirectInt(group, "Menu.Scale", "%",
                    () => (int)Math.Round(_getDouble(nameof(Settings.UIScale), 1.0) * 100),
                    value => _set(nameof(Settings.UIScale), Math.Clamp(Math.Clamp(value, 50, 200) / 100.0, 0.5, 2.0)),
                    width: 70));
            Step("AddGroupToPage", () => _addGroupToPage(group));
        }

        public void CreateFloatLayoutGroup()
        {
            var group = new LiteSettingsGroup("悬浮窗布局");
            _addInt(group, "Menu.TaskbarItemSpacing", nameof(Settings.HorizontalItemSpacing), 12, "px", 70, value => value, _ => _refreshMonitorDisplay());
            _addInt(group, "Menu.TaskbarInnerSpacing", nameof(Settings.HorizontalInnerSpacing), 8, "px", 70, value => value, _ => _refreshMonitorDisplay());
            _addHint(group, "横向模式固定为单行显示；间距可使用正负数。");
            _addGroupToPage(group);
        }

        public void CreateTaskbarFontGroup()
        {
            var group = new LiteSettingsGroup("字号");
            _addFloat(group, "Menu.TaskbarFontSize", nameof(Settings.TaskbarFontSize), Settings.DEFAULT_TB_SIZE_REGULAR, "pt",
                value => Math.Clamp(value, 6f, 32f), markCustomLayout: true);
            _addToggle(group, "Menu.TaskbarFontBold", nameof(Settings.TaskbarFontBold), false, _ =>
            {
                _set(nameof(Settings.TaskbarCustomLayout), true);
                _refreshMonitorDisplay();
            });
            _addGroupToPage(group);
        }

        public void CreateTaskbarFontFamilyGroup()
        {
            var group = new LiteSettingsGroup("字体");
            _addMappedCombo(group, "Menu.TaskbarFont", MainPanelComboHelper.GetFontOptions(_getString(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT)),
                () => _getString(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT),
                text =>
                {
                    _set(nameof(Settings.TaskbarCustomLayout), true);
                    _set(nameof(Settings.TaskbarFontFamily), text);
                    _refreshMonitorDisplay();
                },
                fullWidth: false);
            _addGroupToPage(group);
        }

        public void CreateTaskbarSpacingGroup()
        {
            var group = new LiteSettingsGroup("间距");
            _addHint(group, "这里是悬浮窗横向显示和任务栏共用的间距设置；任务栏上下边距只影响任务栏贴边显示。");
            AddTaskbarLayoutInt(group, "Menu.TaskbarItemSpacing", nameof(Settings.TaskbarItemSpacing), Settings.DEFAULT_TB_GAP);
            AddTaskbarLayoutInt(group, "Menu.TaskbarInnerSpacing", nameof(Settings.TaskbarInnerSpacing), Settings.DEFAULT_TB_INNER_REGULAR);
            AddTaskbarLayoutInt(group, "Menu.TaskbarVerticalPadding", nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF);
            _addGroupToPage(group);
        }

        public void CreateTaskbarColorGroup()
        {
            var group = new LiteSettingsGroup("颜色");
            _addHint(group, "这里是悬浮窗横向显示和任务栏共用的文字颜色；透明键色用于贴合任务栏底色和消除文字边缘杂色。");
            AddTaskbarStyleColor(group, "Menu.BackgroundColor", nameof(Settings.TaskbarColorBg), "#000000");
            AddTaskbarStyleColor(group, "标签颜色", nameof(Settings.TaskbarColorLabel), "#FFFFFF");
            AddTaskbarStyleColor(group, "Menu.ValueSafeColor", nameof(Settings.TaskbarColorSafe), "#00FF00");
            AddTaskbarStyleColor(group, "Menu.ValueWarnColor", nameof(Settings.TaskbarColorWarn), "#FFFF00");
            AddTaskbarStyleColor(group, "Menu.ValueCritColor", nameof(Settings.TaskbarColorCrit), "#FF0000");
            _addGroupToPage(group);
        }

        public void CreateTaskbarPresetGroup()
        {
            var group = new LiteSettingsGroup("预设");
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = UIUtils.S(new Padding(0, 5, 0, 5)),
                BackColor = Color.Transparent
            };
            AddPresetButton(flow, LanguageManager.T("Menu.PresetDefault"), 0);
            AddPresetButton(flow, LanguageManager.T("Menu.PresetCompact"), 1);
            AddPresetButton(flow, LanguageManager.T("Menu.PresetHighContrast"), 2);
            AddPresetButton(flow, LanguageManager.T("Menu.PresetEsports"), 3);
            group.AddFullItem(flow);
            _addGroupToPage(group);
        }

        private void AddTaskbarLayoutInt(LiteSettingsGroup group, string title, string settingKey, int fallback)
        {
            _addInt(group, title, settingKey, fallback, "px", 70, value => value, _ =>
            {
                _set(nameof(Settings.TaskbarCustomLayout), true);
                _refreshMonitorDisplay();
            });
        }

        private void AddTaskbarStyleColor(LiteSettingsGroup group, string title, string settingKey, string fallback)
        {
            _addColor(group, title, settingKey, fallback, _ =>
            {
                _set(nameof(Settings.TaskbarCustomStyle), true);
                _refreshMonitorDisplay();
            });
        }

        private void AddPresetButton(Control parent, string text, int preset)
        {
            var button = new LiteButton(text)
            {
                Width = UIUtils.S(96),
                Height = UIUtils.S(28),
                Margin = UIUtils.S(new Padding(0, 0, 10, 0))
            };
            button.Click += (_, __) => _applyPreset(preset);
            parent.Controls.Add(button);
        }

        private static void Step(string name, Action action)
        {
            using (UiJankProfiler.Measure("MainPanel.FloatAppearanceStep", name, thresholdMs: 1))
                action();
        }
    }
}
