using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinInventoryTrendUiFactory
    {
        public static Panel CreateStatCard(string title, out Label valueLabel, out Label subLabel)
        {
            var card = new Panel
            {
                BackColor = UIColors.ControlBg,
                Padding = UIUtils.S(new Padding(14, 10, 14, 10))
            };
            card.Paint += PaintBorder;

            var titleLabel = new Label
            {
                Text = title,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var value = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var sub = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            valueLabel = value;
            subLabel = sub;
            card.Controls.Add(titleLabel);
            card.Controls.Add(value);
            card.Controls.Add(sub);
            card.Layout += (_, __) =>
            {
                int width = Math.Max(1, card.ClientSize.Width - card.Padding.Horizontal);
                titleLabel.SetBounds(card.Padding.Left, card.Padding.Top, width, UIUtils.S(22));
                value.SetBounds(card.Padding.Left, titleLabel.Bottom + UIUtils.S(4), width, UIUtils.S(36));
                sub.SetBounds(card.Padding.Left, value.Bottom + UIUtils.S(4), width, UIUtils.S(24));
            };

            return card;
        }

        public static LiteButton CreateHeaderButton(string text)
        {
            var button = new LiteButton(text, false)
            {
                Width = UIUtils.S(76),
                Height = UIUtils.S(28),
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            button.BackColor = UIColors.CardBg;
            button.ForeColor = UIColors.TextMain;
            button.FlatAppearance.BorderColor = UIColors.Border;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        public static Label CreateHeaderLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        public static void PaintBorder(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control)
                return;

            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
        }
    }
}
