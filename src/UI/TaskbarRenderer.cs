using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.State;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    /// <summary>
    /// 任务栏渲染器（仅负责绘制，不再负责布局）
    /// </summary>
    public static class TaskbarRenderer
    {
        private static Font? _cachedFont;

        private static readonly Color LABEL_LIGHT = Color.FromArgb(20, 20, 20);
        private static readonly Color SAFE_LIGHT = Color.FromArgb(0x00, 0x80, 0x40);
        private static readonly Color WARN_LIGHT = Color.FromArgb(0xB5, 0x75, 0x00);
        private static readonly Color CRIT_LIGHT = Color.FromArgb(0xC0, 0x30, 0x30);

        private static readonly Color LABEL_DARK = Color.White;
        private static readonly Color SAFE_DARK = Color.FromArgb(0x66, 0xFF, 0x99);
        private static readonly Color WARN_DARK = Color.FromArgb(0xFF, 0xD6, 0x66);
        private static readonly Color CRIT_DARK = Color.FromArgb(0xFF, 0x66, 0x66);

        private static bool _useCustom;
        private static Color _cLabel;
        private static Color _cSafe;
        private static Color _cWarn;
        private static Color _cCrit;

        public static void ReloadStyle(Settings cfg)
        {
            ReloadStyle(new TaskbarStyleConfigSnapshot(
                cfg.TaskbarFontFamily ?? "",
                cfg.TaskbarFontSize,
                cfg.TaskbarFontBold,
                cfg.TaskbarColorLabel ?? "",
                cfg.TaskbarColorSafe ?? "",
                cfg.TaskbarColorWarn ?? "",
                cfg.TaskbarColorCrit ?? ""));
        }

        private static void ReloadStyle(TaskbarStyleConfigSnapshot style)
        {
            float taskbarDpiScale = CS2TradeMonitor.src.UI.Helpers.TaskbarWinHelper.GetTaskbarDpi() / 96f;
            string fontFamily = string.IsNullOrWhiteSpace(style.FontFamily) ? "Segoe UI" : style.FontFamily;
            float fontSize = style.FontSize <= 0 ? 9f : style.FontSize;
            _cachedFont = UIUtils.GetFont(fontFamily, fontSize * taskbarDpiScale, style.FontBold);

            _useCustom = true;
            try
            {
                _cLabel = ColorTranslator.FromHtml(style.ColorLabel);
                _cSafe = ColorTranslator.FromHtml(style.ColorSafe);
                _cWarn = ColorTranslator.FromHtml(style.ColorWarn);
                _cCrit = ColorTranslator.FromHtml(style.ColorCrit);
            }
            catch
            {
                _useCustom = false;
            }
        }

        public static void Render(Graphics g, List<Column> cols, bool light)
        {
            EnsureFont();

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            foreach (var col in cols)
            {
                if (col.Top != null && col.Bottom == null && col.Bounds != Rectangle.Empty)
                {
                    DrawItem(g, col.Top, col.Bounds, light);
                    continue;
                }

                if (col.BoundsTop != Rectangle.Empty && col.Top != null)
                {
                    DrawItem(g, col.Top, col.BoundsTop, light);
                }

                if (col.BoundsBottom != Rectangle.Empty && col.Bottom != null)
                {
                    DrawItem(g, col.Bottom, col.BoundsBottom, light);
                }
            }
        }

        public static void RenderStaticPreview(Graphics g, Settings cfg, Rectangle bounds, bool light, bool singleLine)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            ReloadStyle(cfg);
            EnsureFont();

            Color labelColor = _useCustom ? _cLabel : light ? LABEL_LIGHT : LABEL_DARK;
            Color critColor = _useCustom ? _cCrit : GetStateColor(MetricUtils.STATE_CRIT, light);
            Color safeColor = _useCustom ? _cSafe : GetStateColor(MetricUtils.STATE_SAFE, light);
            Font font = _cachedFont!;

            var state = g.Save();
            try
            {
                g.SetClip(bounds);
                int lineHeight = Math.Max(UIUtils.S(20), font.Height + UIUtils.S(4));
                if (singleLine)
                {
                    int gap = UIUtils.S(18);
                    int itemWidth = Math.Max(1, (bounds.Width - gap) / 2);
                    int y = bounds.Top + Math.Max(0, (bounds.Height - lineHeight) / 2);
                    DrawStaticPreviewLine(
                        g,
                        "DT指数",
                        "888.00  -1.00%",
                        font,
                        new Rectangle(bounds.Left, y, itemWidth, lineHeight),
                        labelColor,
                        safeColor);
                    DrawStaticPreviewLine(
                        g,
                        "QAQ指数",
                        "1888.00  +1.00%",
                        font,
                        new Rectangle(bounds.Left + itemWidth + gap, y, Math.Max(1, bounds.Width - itemWidth - gap), lineHeight),
                        labelColor,
                        critColor);
                }
                else
                {
                    int rowGap = UIUtils.S(4);
                    int totalHeight = lineHeight * 2 + rowGap;
                    int y = bounds.Top + Math.Max(0, (bounds.Height - totalHeight) / 2);
                    DrawStaticPreviewLine(
                        g,
                        "DT指数",
                        "888.00  -1.00%",
                        font,
                        new Rectangle(bounds.Left, y, bounds.Width, lineHeight),
                        labelColor,
                        safeColor);
                    DrawStaticPreviewLine(
                        g,
                        "QAQ指数",
                        "1888.00  +1.00%",
                        font,
                        new Rectangle(bounds.Left, y + lineHeight + rowGap, bounds.Width, lineHeight),
                        labelColor,
                        critColor);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static void DrawStaticPreviewLine(
            Graphics g,
            string label,
            string value,
            Font font,
            Rectangle rc,
            Color labelColor,
            Color valueColor)
        {
            int gap = UIUtils.S(10);
            int minLabelWidth = UIUtils.S(58);
            int desiredLabelWidth = MeasureTextWidth(g, label, font) + UIUtils.S(2);
            int labelWidth = Math.Min(
                Math.Max(minLabelWidth, desiredLabelWidth),
                Math.Max(minLabelWidth, (rc.Width - gap) / 2));
            int valueX = rc.Left + labelWidth + gap;
            int valueWidth = Math.Max(1, rc.Right - valueX);
            TextFormatFlags flags =
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix;

            TextRenderer.DrawText(
                g,
                label,
                font,
                new Rectangle(rc.Left, rc.Top, Math.Max(1, labelWidth), rc.Height),
                labelColor,
                flags);
            TextRenderer.DrawText(
                g,
                value,
                font,
                new Rectangle(valueX, rc.Top, valueWidth, rc.Height),
                valueColor,
                flags);
        }

        private static void EnsureFont()
        {
            if (IsFontUsable(_cachedFont)) return;

            _cachedFont = null;
            try
            {
                ReloadStyle(MetricRuntimeServices.Resolve().AppConfigState.TaskbarStyle);
            }
            catch
            {
                // 样式加载失败时使用默认字体兜底，任务栏渲染不能因此中断。
            }

            _cachedFont ??= UIUtils.GetFont("Segoe UI", 9f, false);
        }

        private static bool IsFontUsable(Font? font)
        {
            if (font == null) return false;

            try
            {
                _ = font.FontFamily.Name;
                _ = font.Size;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void DrawPreviewPair(Graphics g, string label, string value, Font font, Rectangle rc, Color labelColor, Color valueColor)
        {
            TextRenderer.DrawText(
                g,
                label,
                font,
                rc,
                labelColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                g,
                value,
                font,
                rc,
                valueColor,
                TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private static void DrawItem(Graphics g, MetricItem item, Rectangle rc, bool light)
        {
            if (MarketDisplayFormatter.IsMarketDisplayKey(item.Key))
            {
                DrawMarketDisplayItem(g, item, rc, light);
                return;
            }

            if (YouPinInventoryTrendDisplayMetric.IsKey(item.Key))
            {
                DrawCompactInventoryTrendItem(g, item, rc, light);
                return;
            }

            string label = item.ShortLabel;
            bool hideLabel = string.IsNullOrEmpty(label) || label == " ";

            if (!hideLabel)
            {
                if (string.IsNullOrEmpty(label)) label = item.Label;
                if (string.IsNullOrEmpty(label)) label = item.Key;
            }

            string value = item.GetFormattedText(true);
            Font font = _cachedFont!;
            GetColors(item, light, out var labelColor, out var valueColor);

            if (hideLabel)
            {
                TextRenderer.DrawText(
                    g, value, font, rc, valueColor,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.NoClipping);
                return;
            }

            TextRenderer.DrawText(
                g, label, font, rc, labelColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping);

            TextRenderer.DrawText(
                g, value, font, rc, valueColor,
                TextFormatFlags.Right |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping);
        }

        private static void DrawCompactInventoryTrendItem(Graphics g, MetricItem item, Rectangle rc, bool light)
        {
            Font font = _cachedFont!;
            GetColors(item, light, out var labelColor, out var valueColor);

            string label = YouPinInventoryTrendDisplayMetric.TaskbarDisplayLabel;
            string value = item.GetFormattedText(true);
            int gap = UIUtils.S(4);
            int labelWidth = MeasureTextWidth(g, label, font);
            int valueX = rc.Left + labelWidth + gap;

            TextFormatFlags flags =
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix;

            TextRenderer.DrawText(
                g,
                label,
                font,
                new Rectangle(rc.Left, rc.Top, Math.Max(1, labelWidth), rc.Height),
                labelColor,
                flags);

            TextRenderer.DrawText(
                g,
                value,
                font,
                new Rectangle(valueX, rc.Top, Math.Max(1, rc.Right - valueX), rc.Height),
                valueColor,
                flags | TextFormatFlags.EndEllipsis);
        }

        private static void DrawMarketDisplayItem(Graphics g, MetricItem item, Rectangle rc, bool light)
        {
            Font font = _cachedFont!;
            GetColors(item, light, out _, out var textColor);

            var segments = MarketDisplayFormatter.GetSegments(item.Key, item.RuntimeSettings);
            var metrics = MarketDisplayRenderMetrics.Measure(g, item.RuntimeSettings, font, font);
            int x = rc.Left;

            UIUtils.DrawTextGrayAA(
                g, segments.Label, font,
                new Rectangle(x, rc.Top, Math.Max(1, metrics.LabelWidth), rc.Height),
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping);

            x += metrics.LabelWidth + MarketDisplayFormatter.LabelGap;

            string primaryText = segments.PrimaryText;
            UIUtils.DrawTextGrayAA(
                g, primaryText, font,
                new Rectangle(x, rc.Top, Math.Max(1, metrics.PrimaryWidth), rc.Height),
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping);

            if (!segments.HasData || string.IsNullOrWhiteSpace(segments.SecondaryText))
            {
                return;
            }

            x += metrics.PrimaryWidth + MarketDisplayFormatter.ValueGap;
            UIUtils.DrawTextGrayAA(
                g, segments.SecondaryText, font,
                new Rectangle(x, rc.Top, Math.Max(1, rc.Right - x), rc.Height),
                textColor,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoClipping);
        }

        private static void GetColors(MetricItem item, bool light, out Color labelColor, out Color valueColor)
        {
            if (_useCustom)
            {
                labelColor = _cLabel;
                valueColor = GetCustomStateColor(item.CachedColorState);
            }
            else
            {
                labelColor = light ? LABEL_LIGHT : LABEL_DARK;
                valueColor = GetStateColor(item.CachedColorState, light);
            }

            if (MarketDisplayFormatter.IsMarketDisplayKey(item.Key))
            {
                labelColor = valueColor;
            }
        }

        private static int MeasureTextWidth(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(
                g, text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }

        private static Color GetStateColor(int state, bool light)
        {
            if (state < 0) return light ? LABEL_LIGHT : LABEL_DARK;
            if (state == 2) return light ? CRIT_LIGHT : CRIT_DARK;
            if (state == 1) return light ? WARN_LIGHT : WARN_DARK;
            return light ? SAFE_LIGHT : SAFE_DARK;
        }

        private static Color GetCustomStateColor(int state)
        {
            if (state < 0) return _cLabel;
            if (state == 2) return _cCrit;
            if (state == 1) return _cWarn;
            return _cSafe;
        }
    }
}
