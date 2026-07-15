using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class ItemMonitorPageControls
    {
        public static Label CreateRowLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        public static Label CreateLabel(string text, bool strong = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        public static Label CreateTinyLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        public static Label CreateStatusLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Height = UIUtils.S(24),
                Dock = DockStyle.Top,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        public static Color SearchStatusColor(bool warn)
        {
            return warn ? UIColors.TextWarn : Color.FromArgb(0, 150, 80);
        }

        public static void ClampGroupWidth(Control wrapper, Control group)
        {
            int targetWidth = Math.Max(1, wrapper.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - UIUtils.S(6));
            if (group.Width != targetWidth)
                group.Width = targetWidth;
        }

        public static void PaintBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control)
                return;

            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }

        public static void RefreshTheme(Control root)
        {
            foreach (Control child in root.Controls)
            {
                switch (child)
                {
                    case LiteSettingsGroup group:
                        group.ApplySystemTheme();
                        break;
                    case LiteButton button:
                        button.RefreshTheme();
                        break;
                    case LiteComboBox combo:
                        combo.RefreshTheme();
                        break;
                    case LiteUnderlineInput input:
                        input.RefreshTheme();
                        break;
                    case LiteColorInput colorInput:
                        colorInput.Input.RefreshTheme();
                        break;
                }

                if (child.HasChildren)
                    RefreshTheme(child);
            }
        }
    }
}
