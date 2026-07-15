using System;
using System.Collections;
using System.Collections.Generic;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelMappedComboControls
    {
        public static LiteComboBox AddByIndex(
            LiteSettingsGroup group,
            string title,
            IEnumerable<string> items,
            Func<int> getIndex,
            Action<int> setIndex,
            bool fullWidth,
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh)
        {
            var combo = new LiteComboBox();
            MainPanelComboHelper.ConfigureMappedCombo(combo);
            foreach (string item in items)
                combo.Items.Add(item);

            SelectIndex(combo, getIndex());
            registerRefresh(() => SelectIndex(combo, getIndex()));
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (!isUpdatingControls() && combo.SelectedIndex >= 0)
                    setIndex(combo.SelectedIndex);
            };

            AddItem(group, title, combo, fullWidth);
            return combo;
        }

        public static LiteComboBox AddByValue(
            LiteSettingsGroup group,
            string title,
            IEnumerable<string> items,
            Func<string> getValue,
            Action<string> setValue,
            bool fullWidth,
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh)
        {
            var combo = new LiteComboBox();
            MainPanelComboHelper.ConfigureMappedCombo(combo);
            foreach (string item in items)
                combo.Items.Add(item);

            SelectValue(combo, getValue());
            registerRefresh(() => SelectValue(combo, getValue()));
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (!isUpdatingControls() && combo.SelectedIndex >= 0)
                    setValue(combo.Text);
            };

            AddItem(group, title, combo, fullWidth);
            return combo;
        }

        internal static bool TryResolveValueIndex(IEnumerable items, string value, out int index)
        {
            bool hasItems = false;
            int current = 0;
            foreach (object? item in items)
            {
                hasItems = true;
                if (string.Equals(item?.ToString(), value, StringComparison.Ordinal))
                {
                    index = current;
                    return true;
                }

                current++;
            }

            index = hasItems ? 0 : -1;
            return false;
        }

        private static void SelectIndex(LiteComboBox combo, int index)
        {
            if (index >= 0 && index < combo.Items.Count)
                combo.SelectedIndex = index;
        }

        private static void SelectValue(LiteComboBox combo, string value)
        {
            TryResolveValueIndex(combo.Items, value, out int index);
            if (index >= 0)
                combo.SelectedIndex = index;
        }

        private static void AddItem(LiteSettingsGroup group, string title, LiteComboBox combo, bool fullWidth)
        {
            var item = new LiteSettingsItem(FrameworkSettingsPageBase.TitleText(title), combo);
            if (fullWidth)
                group.AddFullItem(item);
            else
                group.AddItem(item);
        }
    }
}
