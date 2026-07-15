using CS2TradeMonitor.src.Core;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    internal static class MarketDisplayRenderer
    {
        public static void Draw(
            Graphics graphics,
            MetricItem item,
            Rectangle bounds,
            Theme theme,
            double textOpacity,
            bool drawSeparator)
        {
            MarketDisplaySegments segments = MarketDisplayFormatter.GetSegments(item.Key, item.RuntimeSettings);
            Font labelFont = MetricRenderAppearance.GetFont(theme, item.RuntimeSettings, valueFont: false);
            Font valueFont = MetricRenderAppearance.GetFont(theme, item.RuntimeSettings, valueFont: true);
            MarketDisplayColumnMetrics metrics = MarketDisplayRenderMetrics.Measure(
                graphics,
                item.RuntimeSettings,
                labelFont,
                valueFont);
            Color textColor = MetricRenderAppearance.GetValueColor(item, theme);
            int x = bounds.Left;

            DrawSegment(graphics, segments.Label, labelFont, textColor,
                new Rectangle(x, bounds.Top, Math.Max(1, metrics.LabelWidth), bounds.Height), textOpacity);

            x += metrics.LabelWidth + MarketDisplayFormatter.LabelGap;
            DrawSegment(graphics, segments.PrimaryText, valueFont, textColor,
                new Rectangle(x, bounds.Top, Math.Max(1, metrics.PrimaryWidth), bounds.Height), textOpacity);

            if (segments.HasData && !string.IsNullOrWhiteSpace(segments.SecondaryText))
            {
                x += metrics.PrimaryWidth + MarketDisplayFormatter.ValueGap;
                DrawSegment(graphics, segments.SecondaryText, valueFont, textColor,
                    new Rectangle(x, bounds.Top, Math.Max(1, bounds.Right - x), bounds.Height), textOpacity);
            }

            if (drawSeparator)
            {
                Color dividerColor = Color.FromArgb(100, ThemeManager.ParseColor(theme.Color.BarBackground));
                Pen dividerPen = UIUtils.GetPen(dividerColor, 1);
                int lineY = bounds.Bottom - 1;
                graphics.DrawLine(dividerPen, bounds.Left + 5, lineY, bounds.Right - 5, lineY);
            }
        }

        private static void DrawSegment(
            Graphics graphics,
            string text,
            Font font,
            Color color,
            Rectangle bounds,
            double opacity)
        {
            UIUtils.DrawTextGrayAA(
                graphics,
                text,
                font,
                bounds,
                color,
                TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoClipping,
                opacity);
        }
    }
}
