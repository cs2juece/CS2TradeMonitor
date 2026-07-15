using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelComboHelper
    {
        public static IEnumerable<string> GetFontOptions(string currentFont)
        {
            var fonts = new[]
            {
                Settings.DEFAULT_TB_FONT,
                currentFont,
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "Segoe UI",
                "Arial",
                "Tahoma",
                "Consolas"
            };
            return fonts
                .Where(font => !string.IsNullOrWhiteSpace(font))
                .Distinct(StringComparer.CurrentCultureIgnoreCase);
        }

        public static void ConfigureMappedCombo(LiteComboBox combo)
        {
            combo.Width = UIUtils.S(170);
            combo.MinimumSize = new Size(UIUtils.S(150), UIUtils.S(28));
            combo.Inner.DropDown += (_, __) =>
            {
                int maxWidth = Math.Max(combo.Inner.Width, combo.Width);
                int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
                foreach (object? item in combo.Inner.Items)
                {
                    if (item == null)
                        continue;

                    string text = combo.Inner.GetItemText(item) ?? string.Empty;
                    int width = TextRenderer.MeasureText(text, combo.Inner.Font).Width + scrollBarWidth + UIUtils.S(16);
                    if (width > maxWidth)
                        maxWidth = width;
                }

                combo.Inner.DropDownWidth = maxWidth;
            };
        }
    }
}
