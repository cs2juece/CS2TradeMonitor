using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.Core;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal static class SteamOfferPageControls
    {
        public static Label MakeLabel(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = UIColors.TextMain,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        public static Label CreateHeaderHint(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = UIColors.TextSub,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        public static Label CreateValueLabel()
        {
            return new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Panel CreateStatusPill(string caption, Label value)
        {
            var block = new Panel
            {
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(0, 0, 0, 0))
            };
            var label = new Label
            {
                Text = caption,
                AutoSize = false,
                Dock = DockStyle.Left,
                Width = UIUtils.S(caption.Length > 2 ? 58 : 34),
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            value.Dock = DockStyle.Fill;
            value.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            block.Controls.Add(value);
            block.Controls.Add(label);
            return block;
        }

        public static void SetStatusValue(Label? label, string text, Color color)
        {
            if (label == null) return;
            label.Text = text;
            label.ForeColor = color;
        }

        public static Color StatusToneColor(SteamOfferStatusTone tone) => tone switch
        {
            SteamOfferStatusTone.Success => Color.FromArgb(0, 170, 90),
            SteamOfferStatusTone.Warning => UIColors.TextWarn,
            _ => UIColors.TextSub
        };

        public static Color ConnectionToneColor(SteamConnectionStatusTone tone) => tone switch
        {
            SteamConnectionStatusTone.Warning => UIColors.TextWarn,
            SteamConnectionStatusTone.Muted => UIColors.TextSub,
            _ => Color.FromArgb(0, 170, 90)
        };

        public static Color TokenSessionToneColor(SteamTokenSessionStatusTone tone) => tone switch
        {
            SteamTokenSessionStatusTone.Success => Color.FromArgb(0, 170, 90),
            SteamTokenSessionStatusTone.Warning => UIColors.TextWarn,
            _ => UIColors.TextSub
        };

        public static Color OperationStatusToneColor(SteamOfferOperationStatusTone tone) => tone switch
        {
            SteamOfferOperationStatusTone.Success => Color.FromArgb(0, 170, 90),
            SteamOfferOperationStatusTone.Warning => UIColors.TextWarn,
            _ => UIColors.TextSub
        };

        public static void PaintTotpCountdownBar(Graphics graphics, Rectangle clientRectangle, int secondsLeft)
        {
            Rectangle bounds = new Rectangle(
                0,
                0,
                Math.Max(0, clientRectangle.Width - 1),
                Math.Max(0, clientRectangle.Height - 1));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            using (var track = new SolidBrush(UIColors.ControlBg))
                graphics.FillRectangle(track, bounds);
            using (var border = new Pen(Color.FromArgb(150, UIColors.Border)))
                graphics.DrawRectangle(border, bounds);

            double percent = SteamOfferPageModel.BuildTotpCountdownPercent(secondsLeft);
            int fillWidth = Math.Max(0, (int)Math.Round((clientRectangle.Width - 2) * percent));
            if (fillWidth <= 0)
                return;

            Rectangle fill = new Rectangle(
                1,
                1,
                Math.Min(fillWidth, Math.Max(0, clientRectangle.Width - 2)),
                Math.Max(1, clientRectangle.Height - 2));
            using var brush = new SolidBrush(secondsLeft <= 5 ? UIColors.TextWarn : UIColors.Primary);
            graphics.FillRectangle(brush, fill);
        }

        public static void ClearAndDispose(Control.ControlCollection controls)
        {
            while (controls.Count > 0)
            {
                Control control = controls[0];
                controls.RemoveAt(0);
                control.Dispose();
            }
        }

        public static void RefreshTheme(Control root)
        {
            foreach (Control child in root.Controls)
            {
                switch (child)
                {
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
