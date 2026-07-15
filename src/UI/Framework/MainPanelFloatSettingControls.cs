using System;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelFloatSettingControls
    {
        public static LiteNumberInput AddFloat(
            LiteSettingsGroup group,
            string title,
            string unit,
            Func<float> get,
            Action<float> set,
            Func<float, float> normalize,
            Action? markCustomLayout,
            Action afterChanged,
            Func<bool> isUpdatingControls,
            Action<Action> registerRefresh,
            Action<Action> registerSave)
        {
            var input = new LiteNumberInput(FormatValue(get(), normalize), unit, "", 70)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (isUpdatingControls())
                    return;

                if (TryNormalizeText(input.Inner.Text, normalize, out float value))
                {
                    markCustomLayout?.Invoke();
                    set(value);
                    afterChanged();
                }
            };
            registerRefresh(() => input.Inner.Text = FormatValue(get(), normalize));
            registerSave(() =>
            {
                if (TryNormalizeText(input.Inner.Text, normalize, out float value))
                    set(value);
            });
            group.AddItem(new LiteSettingsItem(FrameworkSettingsPageBase.TitleText(title), input));
            return input;
        }

        internal static string FormatValue(float value, Func<float, float> normalize)
        {
            return normalize(value).ToString("0.##");
        }

        internal static bool TryNormalizeText(string text, Func<float, float> normalize, out float value)
        {
            if (float.TryParse(text, out float parsed))
            {
                value = normalize(parsed);
                return true;
            }

            value = 0;
            return false;
        }
    }
}
