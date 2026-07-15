using System;
using System.Collections.Generic;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelFloatGroupsBuilder
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

        private readonly Func<string, bool, bool> _getBool;
        private readonly Action<string, bool> _setBool;
        private readonly AddToggleControl _addToggle;
        private readonly AddIndexComboControl _addMappedCombo;
        private readonly Action<LiteSettingsGroup> _addGroupToPage;
        private readonly Action<LiteSettingsGroup> _invalidateGroupLayout;
        private readonly Action<LiteCheck?, LiteCheck?> _ensureSafeVisibility;
        private readonly Func<MainPanelSafeVisibilityResult> _resolveSafeVisibility;
        private readonly Action _hideMainWindow;
        private readonly Action _showMainWindow;
        private readonly Action _refreshMonitorDisplay;
        private readonly Func<bool> _isUpdatingControls;
        private readonly Action<Action> _registerRefresh;
        private readonly Action<Action> _registerSave;
        private LiteSettingsGroup? _floatAppearanceModeGroup;

        public MainPanelFloatGroupsBuilder(
            Func<string, bool, bool> getBool,
            Action<string, bool> setBool,
            AddToggleControl addToggle,
            AddIndexComboControl addMappedCombo,
            Action<LiteSettingsGroup> addGroupToPage,
            Action<LiteSettingsGroup> invalidateGroupLayout,
            Action<LiteCheck?, LiteCheck?> ensureSafeVisibility,
            Func<MainPanelSafeVisibilityResult> resolveSafeVisibility,
            Action hideMainWindow,
            Action showMainWindow,
            Action refreshMonitorDisplay,
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh,
            Action<Action> registerSave)
        {
            _getBool = getBool ?? throw new ArgumentNullException(nameof(getBool));
            _setBool = setBool ?? throw new ArgumentNullException(nameof(setBool));
            _addToggle = addToggle ?? throw new ArgumentNullException(nameof(addToggle));
            _addMappedCombo = addMappedCombo ?? throw new ArgumentNullException(nameof(addMappedCombo));
            _addGroupToPage = addGroupToPage ?? throw new ArgumentNullException(nameof(addGroupToPage));
            _invalidateGroupLayout = invalidateGroupLayout ?? throw new ArgumentNullException(nameof(invalidateGroupLayout));
            _ensureSafeVisibility = ensureSafeVisibility ?? throw new ArgumentNullException(nameof(ensureSafeVisibility));
            _resolveSafeVisibility = resolveSafeVisibility ?? throw new ArgumentNullException(nameof(resolveSafeVisibility));
            _hideMainWindow = hideMainWindow ?? throw new ArgumentNullException(nameof(hideMainWindow));
            _showMainWindow = showMainWindow ?? throw new ArgumentNullException(nameof(showMainWindow));
            _refreshMonitorDisplay = refreshMonitorDisplay ?? throw new ArgumentNullException(nameof(refreshMonitorDisplay));
            _isUpdatingControls = isUpdatingControls ?? throw new ArgumentNullException(nameof(isUpdatingControls));
            _registerRefresh = registerRefresh ?? throw new ArgumentNullException(nameof(registerRefresh));
            _registerSave = registerSave ?? throw new ArgumentNullException(nameof(registerSave));
        }

        public void Reset()
        {
            _floatAppearanceModeGroup = null;
        }

        public void CreateBehaviorGroup()
        {
            var group = new LiteSettingsGroup("悬浮窗行为");
            AddFloatingWindowEnabledToggle(group);
            _addToggle(group, "Menu.TopMost", nameof(Settings.TopMost), false, _ => _refreshMonitorDisplay());
            _addToggle(group, "Menu.ClickThrough", nameof(Settings.ClickThrough), false, _ =>
            {
                _ensureSafeVisibility(null, null);
                _refreshMonitorDisplay();
            });
            _addToggle(group, "Menu.LockPosition", nameof(Settings.LockPosition), false, null);
            _addToggle(group, "Menu.ClampToScreen", nameof(Settings.ClampToScreen), true, null);
            _addToggle(group, "Menu.AutoHide", nameof(Settings.AutoHide), false, null);
            _addGroupToPage(group);
        }

        public void CreateAppearanceModeGroupShell()
        {
            var group = new LiteSettingsGroup("悬浮窗方向");
            _floatAppearanceModeGroup = group;
            _addGroupToPage(group);
        }

        public void CreateAppearanceModeEditor()
        {
            LiteSettingsGroup group = _floatAppearanceModeGroup ?? new LiteSettingsGroup("悬浮窗方向");
            if (_floatAppearanceModeGroup == null)
            {
                _floatAppearanceModeGroup = group;
                _addGroupToPage(group);
            }

            group.SuspendLayout();
            try
            {
                _addMappedCombo(group, "Menu.DisplayMode",
                    new[] { LanguageManager.T("Menu.Vertical") ?? "竖向", LanguageManager.T("Menu.HorizontalSingleLineMode") ?? "横向单行" },
                    () => _getBool(nameof(Settings.HorizontalMode), false) ? 1 : 0,
                    index =>
                    {
                        bool horizontal = index == 1;
                        _setBool(nameof(Settings.HorizontalMode), horizontal);
                        _setBool(nameof(Settings.HorizontalSingleLine), horizontal);
                        _refreshMonitorDisplay();
                    },
                    fullWidth: true);
            }
            finally
            {
                group.ResumeLayout(true);
            }

            _invalidateGroupLayout(group);
        }

        private LiteCheck AddFloatingWindowEnabledToggle(LiteSettingsGroup group)
        {
            var check = new LiteCheck(!_getBool(nameof(Settings.HideMainForm), false), LanguageManager.T("Menu.Enable"));
            check.CheckedChanged += (_, __) =>
            {
                if (_isUpdatingControls())
                    return;

                bool hideMainForm = !check.Checked;
                _setBool(nameof(Settings.HideMainForm), hideMainForm);
                _ensureSafeVisibility(null, null);
                MainPanelSafeVisibilityResult visibility = _resolveSafeVisibility();
                if (visibility.RequiresCorrection)
                {
                    _setBool(nameof(Settings.HideMainForm), visibility.HideMainForm);
                    check.Checked = true;
                    hideMainForm = visibility.HideMainForm;
                }
                if (hideMainForm)
                    _hideMainWindow();
                else
                    _showMainWindow();
                _refreshMonitorDisplay();
            };
            _registerRefresh(() => check.Checked = !_getBool(nameof(Settings.HideMainForm), false));
            _registerSave(() => _setBool(nameof(Settings.HideMainForm), !check.Checked));
            group.AddItem(new LiteSettingsItem("开启悬浮窗", check));
            return check;
        }
    }
}
