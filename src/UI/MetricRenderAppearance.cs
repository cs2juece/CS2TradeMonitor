using CS2TradeMonitor.src.Core;
using System.Drawing;

namespace CS2TradeMonitor
{
    internal static class MetricRenderAppearance
    {
        public static Font GetFont(Theme theme, Settings? settings, bool valueFont)
        {
            if (settings == null)
                return valueFont ? theme.FontValue : theme.FontItem;

            try
            {
                return UIUtils.GetFont(
                    string.IsNullOrWhiteSpace(settings.TaskbarFontFamily) ? Settings.DEFAULT_TB_FONT : settings.TaskbarFontFamily,
                    settings.TaskbarFontSize <= 0 ? Settings.DEFAULT_TB_SIZE_BOLD : settings.TaskbarFontSize,
                    settings.TaskbarFontBold);
            }
            catch
            {
                return valueFont ? theme.FontValue : theme.FontItem;
            }
        }

        public static void GetColors(MetricItem item, Theme theme, out Color labelColor, out Color valueColor)
        {
            Settings? settings = item.RuntimeSettings;
            if (settings != null && TryParseColor(settings.TaskbarColorLabel, out labelColor))
            {
                valueColor = GetValueColor(settings, item.CachedColorState, theme);
                return;
            }

            labelColor = ThemeManager.ParseColor(theme.Color.TextPrimary);
            valueColor = item.GetTextColor(theme);
        }

        public static Color GetValueColor(MetricItem item, Theme theme)
        {
            Settings? settings = item.RuntimeSettings;
            if (settings != null && TryParseColor(settings.TaskbarColorLabel, out _))
                return GetValueColor(settings, item.CachedColorState, theme);

            return item.GetTextColor(theme);
        }

        private static Color GetValueColor(Settings settings, int state, Theme theme)
        {
            string hex = state switch
            {
                < 0 => settings.TaskbarColorLabel,
                2 => settings.TaskbarColorCrit,
                1 => settings.TaskbarColorWarn,
                _ => settings.TaskbarColorSafe
            };

            return TryParseColor(hex, out Color color) ? color : UIUtils.GetStateColor(state, theme, true);
        }

        private static bool TryParseColor(string? hex, out Color color)
        {
            try
            {
                color = ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(hex) ? "#FFFFFF" : hex);
                return true;
            }
            catch
            {
                color = Color.White;
                return false;
            }
        }
    }
}
