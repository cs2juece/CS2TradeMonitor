using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelSettingControlBinder
    {
        private readonly Func<bool> _isUpdatingControls;
        private readonly Action<Action> _registerRefresh;
        private readonly Action<Action> _registerSave;
        private readonly Func<LiteSettingsGroup, string, string, string, Action<string>?, LiteColorInput> _addColor;
        private readonly Func<string, float, float> _getFloat;
        private readonly Action<string, object> _set;
        private readonly Action _afterMonitorChanged;

        public MainPanelSettingControlBinder(
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh,
            Action<Action> registerSave,
            Func<LiteSettingsGroup, string, string, string, Action<string>?, LiteColorInput> addColor,
            Func<string, float, float> getFloat,
            Action<string, object> set,
            Action afterMonitorChanged)
        {
            _isUpdatingControls = isUpdatingControls ?? throw new ArgumentNullException(nameof(isUpdatingControls));
            _registerRefresh = registerRefresh ?? throw new ArgumentNullException(nameof(registerRefresh));
            _registerSave = registerSave ?? throw new ArgumentNullException(nameof(registerSave));
            _addColor = addColor ?? throw new ArgumentNullException(nameof(addColor));
            _getFloat = getFloat ?? throw new ArgumentNullException(nameof(getFloat));
            _set = set ?? throw new ArgumentNullException(nameof(set));
            _afterMonitorChanged = afterMonitorChanged ?? throw new ArgumentNullException(nameof(afterMonitorChanged));
        }

        public LiteCheck AddDirectToggle(LiteSettingsGroup group, string title, Func<bool> get, Action<bool> set)
        {
            return MainPanelDirectSettingControls.AddToggle(
                group,
                title,
                get,
                set,
                _isUpdatingControls,
                _registerRefresh,
                _registerSave);
        }

        public LiteNumberInput AddDirectInt(
            LiteSettingsGroup group,
            string title,
            string unit,
            Func<int> get,
            Action<int> set,
            int width = 60)
        {
            return MainPanelDirectSettingControls.AddInt(
                group,
                title,
                unit,
                get,
                set,
                width,
                _isUpdatingControls,
                _registerRefresh,
                _registerSave);
        }

        public void AddDirectColor(LiteSettingsGroup group, string title, string settingKey, string fallback)
        {
            AddColor(group, title, settingKey, fallback, _ => _afterMonitorChanged());
        }

        public LiteColorInput AddColor(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            string fallback,
            Action<string>? afterChanged)
        {
            return _addColor(group, title, settingKey, fallback, afterChanged);
        }

        public LiteComboBox AddMappedCombo(
            LiteSettingsGroup group,
            string title,
            IEnumerable<string> items,
            Func<int> getIndex,
            Action<int> setIndex,
            bool fullWidth = false)
        {
            return MainPanelMappedComboControls.AddByIndex(
                group,
                title,
                items,
                getIndex,
                setIndex,
                fullWidth,
                _isUpdatingControls,
                _registerRefresh);
        }

        public LiteComboBox AddMappedCombo(
            LiteSettingsGroup group,
            string title,
            IEnumerable<string> items,
            Func<string> getValue,
            Action<string> setValue,
            bool fullWidth = false)
        {
            return MainPanelMappedComboControls.AddByValue(
                group,
                title,
                items,
                getValue,
                setValue,
                fullWidth,
                _isUpdatingControls,
                _registerRefresh);
        }

        public void AddFloat(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            float fallback,
            string unit,
            Func<float, float> normalize,
            bool markCustomLayout = false)
        {
            MainPanelFloatSettingControls.AddFloat(
                group,
                title,
                unit,
                () => _getFloat(settingKey, fallback),
                value => _set(settingKey, value),
                normalize,
                markCustomLayout ? () => _set(nameof(Settings.TaskbarCustomLayout), true) : null,
                _afterMonitorChanged,
                _isUpdatingControls,
                _registerRefresh,
                _registerSave);
        }
    }
}
