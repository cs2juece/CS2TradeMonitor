using System;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelDirectSettingControls
    {
        public static LiteCheck AddToggle(
            LiteSettingsGroup group,
            string title,
            Func<bool> get,
            Action<bool> set,
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh,
            Action<Action> registerSave)
        {
            var check = new LiteCheck(get(), "启用");
            check.CheckedChanged += (_, __) =>
            {
                if (isUpdatingControls())
                    return;
                set(check.Checked);
            };
            registerRefresh(() => check.Checked = get());
            registerSave(() => set(check.Checked));
            group.AddItem(new LiteSettingsItem(title, check));
            return check;
        }

        public static LiteNumberInput AddInt(
            LiteSettingsGroup group,
            string title,
            string unit,
            Func<int> get,
            Action<int> set,
            int width,
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh,
            Action<Action> registerSave)
        {
            var input = new LiteNumberInput(get().ToString(), unit, "", width)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (isUpdatingControls())
                    return;
                if (int.TryParse(input.Inner.Text, out int value))
                    set(value);
            };
            registerRefresh(() => input.Inner.Text = get().ToString());
            registerSave(() =>
            {
                if (int.TryParse(input.Inner.Text, out int value))
                    set(value);
            });
            group.AddItem(new LiteSettingsItem(FrameworkSettingsPageBase.TitleText(title), input));
            return input;
        }
    }
}
