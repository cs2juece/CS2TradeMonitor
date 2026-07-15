using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelSettingsApplier
    {
        private readonly Func<string, bool, bool> _getBool;
        private readonly Action<string, object> _set;

        public MainPanelSettingsApplier(Func<string, bool, bool> getBool, Action<string, object> set)
        {
            _getBool = getBool ?? throw new ArgumentNullException(nameof(getBool));
            _set = set ?? throw new ArgumentNullException(nameof(set));
        }

        public void ApplyTaskbarStylePreset(bool bold)
        {
            ApplyAssignments(MainPanelSettingsRules.BuildTaskbarStylePreset(bold));
        }

        public void ApplyTaskbarPreset(int type)
        {
            ApplyAssignments(MainPanelSettingsRules.BuildTaskbarPreset(type));
        }

        public MainPanelSafeVisibilityResult ResolveSafeVisibility()
        {
            return ResolveSafeVisibility(
                _getBool(nameof(Settings.HideMainForm), false),
                _getBool(nameof(Settings.ShowTaskbar), true));
        }

        public MainPanelSafeVisibilityResult ResolveSafeVisibility(bool hideMainForm, bool showTaskbar)
        {
            return MainPanelSettingsRules.ResolveSafeVisibility(
                hideMainForm,
                _getBool(nameof(Settings.HideTrayIcon), false),
                showTaskbar,
                _getBool(nameof(Settings.ClickThrough), false),
                _getBool(nameof(Settings.TaskbarClickThrough), false));
        }

        private void ApplyAssignments(IEnumerable<MainPanelSettingAssignment> assignments)
        {
            foreach (MainPanelSettingAssignment assignment in assignments)
                _set(assignment.Key, assignment.Value);
        }
    }
}
