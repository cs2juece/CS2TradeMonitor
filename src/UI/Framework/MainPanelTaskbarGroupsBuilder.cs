using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelTaskbarGroupsBuilder
    {
        internal delegate LiteCheck AddToggleControl(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            bool fallback,
            Action<bool>? afterChanged);

        internal delegate LiteComboBox AddIndexComboControl(
            LiteSettingsGroup group,
            string title,
            IEnumerable<string> items,
            Func<int> getIndex,
            Action<int> setIndex,
            bool fullWidth);

        internal delegate LiteNumberInput AddIntControl(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            int fallback,
            string unit,
            int width,
            Func<int, int>? normalize,
            Action<int>? afterChanged);

        private readonly Func<string, bool, bool> _getBool;
        private readonly Func<string, int, int> _getInt;
        private readonly Action<string, bool> _setBool;
        private readonly AddToggleControl _addToggle;
        private readonly AddIndexComboControl _addMappedCombo;
        private readonly AddIntControl _addInt;
        private readonly Action<LiteSettingsGroup, string> _addHint;
        private readonly Action<LiteSettingsGroup> _addGroupToPage;
        private readonly Action<LiteSettingsGroup> _invalidateGroupLayout;
        private readonly Action<bool> _applyTaskbarStylePreset;
        private readonly Action<LiteCheck?, LiteCheck?> _ensureSafeVisibility;
        private readonly Action _refreshMonitorDisplay;
        private readonly List<Control> _taskbarControls = new();
        private LiteCheck? _taskbarShowCheck;
        private LiteSettingsGroup? _taskbarAlignGroup;

        public MainPanelTaskbarGroupsBuilder(
            Func<string, bool, bool> getBool,
            Func<string, int, int> getInt,
            Action<string, bool> setBool,
            AddToggleControl addToggle,
            AddIndexComboControl addMappedCombo,
            AddIntControl addInt,
            Action<LiteSettingsGroup, string> addHint,
            Action<LiteSettingsGroup> addGroupToPage,
            Action<LiteSettingsGroup> invalidateGroupLayout,
            Action<bool> applyTaskbarStylePreset,
            Action<LiteCheck?, LiteCheck?> ensureSafeVisibility,
            Action refreshMonitorDisplay)
        {
            _getBool = getBool ?? throw new ArgumentNullException(nameof(getBool));
            _getInt = getInt ?? throw new ArgumentNullException(nameof(getInt));
            _setBool = setBool ?? throw new ArgumentNullException(nameof(setBool));
            _addToggle = addToggle ?? throw new ArgumentNullException(nameof(addToggle));
            _addMappedCombo = addMappedCombo ?? throw new ArgumentNullException(nameof(addMappedCombo));
            _addInt = addInt ?? throw new ArgumentNullException(nameof(addInt));
            _addHint = addHint ?? throw new ArgumentNullException(nameof(addHint));
            _addGroupToPage = addGroupToPage ?? throw new ArgumentNullException(nameof(addGroupToPage));
            _invalidateGroupLayout = invalidateGroupLayout ?? throw new ArgumentNullException(nameof(invalidateGroupLayout));
            _applyTaskbarStylePreset = applyTaskbarStylePreset ?? throw new ArgumentNullException(nameof(applyTaskbarStylePreset));
            _ensureSafeVisibility = ensureSafeVisibility ?? throw new ArgumentNullException(nameof(ensureSafeVisibility));
            _refreshMonitorDisplay = refreshMonitorDisplay ?? throw new ArgumentNullException(nameof(refreshMonitorDisplay));
        }

        public void Reset()
        {
            _taskbarControls.Clear();
            _taskbarShowCheck = null;
            _taskbarAlignGroup = null;
        }

        public void CreateGeneralGroup()
        {
            var group = new LiteSettingsGroup(LanguageManager.T("Menu.TaskbarShow"));
            _taskbarControls.Clear();
            _taskbarShowCheck = _addToggle(group, "Menu.TaskbarShow", nameof(Settings.ShowTaskbar), true, _ =>
            {
                _ensureSafeVisibility(null, _taskbarShowCheck);
                SetTaskbarControlsEnabled(_getBool(nameof(Settings.ShowTaskbar), true));
                _refreshMonitorDisplay();
            });
            _taskbarControls.Add(_addMappedCombo(group, "Menu.TaskbarStyle",
                new[] { LanguageManager.T("Menu.TaskbarStyleBold"), LanguageManager.T("Menu.TaskbarStyleRegular") },
                () => _getInt(nameof(Settings.TaskbarPresetStyle), 1) == 1 ? 0 : 1,
                index => _applyTaskbarStylePreset(index == 0),
                fullWidth: false));
            _taskbarControls.Add(_addToggle(group, "Menu.TaskbarSingleLine", nameof(Settings.TaskbarSingleLine), false, _ => _refreshMonitorDisplay()));
            SetTaskbarControlsEnabled(_getBool(nameof(Settings.ShowTaskbar), true));
            _addGroupToPage(group);
        }

        public void CreateAdvancedBehaviorGroup()
        {
            var group = new LiteSettingsGroup("任务栏高级");
            _taskbarControls.Add(_addToggle(group, "Menu.TaskbarHoverShowAll", nameof(Settings.TaskbarHoverShowAll), true, null));
            SetTaskbarControlsEnabled(_getBool(nameof(Settings.ShowTaskbar), true));
            _addGroupToPage(group);
        }

        public void CreateAlignGroupShell()
        {
            var group = new LiteSettingsGroup("任务栏对齐");
            _taskbarAlignGroup = group;
            _addGroupToPage(group);
        }

        public void CreateAlignEditor()
        {
            LiteSettingsGroup group = _taskbarAlignGroup ?? new LiteSettingsGroup("任务栏对齐");
            if (_taskbarAlignGroup == null)
            {
                _taskbarAlignGroup = group;
                _addGroupToPage(group);
            }

            group.SuspendLayout();
            try
            {
                LiteComboBox alignControl = _addMappedCombo(group, "Menu.TaskbarAlign",
                    new[] { LanguageManager.T("Menu.TaskbarAlignRight"), LanguageManager.T("Menu.TaskbarAlignLeft") },
                    () => _getBool(nameof(Settings.TaskbarAlignLeft), false) ? 1 : 0,
                    index =>
                    {
                        _setBool(nameof(Settings.TaskbarAlignLeft), index == 1);
                        _refreshMonitorDisplay();
                    },
                    fullWidth: true);
                _taskbarControls.Add(alignControl);
                alignControl.Enabled = _getBool(nameof(Settings.ShowTaskbar), true);
            }
            finally
            {
                group.ResumeLayout(true);
            }

            _invalidateGroupLayout(group);
        }

        public void CreateOffsetGroup()
        {
            var group = new LiteSettingsGroup("任务栏偏移");
            _taskbarControls.Add(_addInt(group, "Menu.TaskbarOffset", nameof(Settings.TaskbarManualOffset), 0, "px", 70,
                value => Math.Clamp(value, -1200, 1200), _ => _refreshMonitorDisplay()));
            _addHint(group, LanguageManager.T("Menu.TaskbarAlignTip"));
            SetTaskbarControlsEnabled(_getBool(nameof(Settings.ShowTaskbar), true));
            _addGroupToPage(group);
        }

        private void SetTaskbarControlsEnabled(bool enabled)
        {
            foreach (Control control in _taskbarControls)
                control.Enabled = enabled;
        }
    }
}
