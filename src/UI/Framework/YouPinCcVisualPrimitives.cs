using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinCcRoundedPanel : Panel
    {
        public YouPinCcRoundedPanel()
        {
            BackColor = Color.Transparent;
            Radius = UIUtils.S(8);
            DrawBorder = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Radius { get; set; }

        public bool DrawBorder { get; set; }

        public Color? FillOverride { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var path = RoundedRect(rect, Math.Max(1, Radius));
            using var fill = new SolidBrush(FillOverride ?? UIColors.CardBg);
            e.Graphics.FillPath(fill, path);
            if (DrawBorder)
            {
                using var border = new Pen(UIColors.Border);
                e.Graphics.DrawPath(border, path);
            }

            base.OnPaint(e);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class YouPinCcUi
    {
        public static Label Label(string text, float size = 9F, FontStyle style = FontStyle.Regular, Color? color = null, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color ?? UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = align
            };
        }

        public static Panel AddTopCard(ScrollableControl container, Control card, int bottomGap = 14, int maxContentWidth = 0)
        {
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                Height = card.Height + UIUtils.S(bottomGap),
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, UIUtils.S(bottomGap))
            };
            card.Dock = DockStyle.None;
            wrapper.Controls.Add(card);
            wrapper.Layout += (_, __) =>
            {
                Control? parent = wrapper.Parent;
                int viewportWidth = parent is null
                    ? wrapper.ClientSize.Width
                    : FrameworkSettingsPageLayoutHelper.CalculateVisibleWidthWithinForm(parent);
                Rectangle bounds = parent is ScrollableControl
                    ? FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(
                        viewportWidth,
                        parent.Padding)
                    : FrameworkSettingsPageLayoutHelper.CalculateCenteredContentBounds(
                        viewportWidth,
                        Math.Max(1, viewportWidth - SystemInformation.VerticalScrollBarWidth));
                int left = Math.Max(0, bounds.Left - wrapper.Left);
                int visibleWrapperWidth = FrameworkSettingsPageLayoutHelper.CalculateVisibleWidthWithinForm(wrapper);
                int cardWidth = Math.Min(bounds.Width, Math.Max(1, visibleWrapperWidth - left));
                if (maxContentWidth > 0)
                    cardWidth = Math.Min(cardWidth, UIUtils.S(maxContentWidth));

                int height = Math.Max(1, wrapper.ClientSize.Height - wrapper.Padding.Bottom);
                card.SetBounds(left, 0, cardWidth, height);
            };
            container.Controls.Add(wrapper);
            return wrapper;
        }
    }
}
