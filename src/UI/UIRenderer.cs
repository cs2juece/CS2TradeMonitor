using CS2TradeMonitor.src.Core;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    public static class UIRenderer
    {
        private const double InteractiveTransparentBackgroundOpacity = 1.0 / 255.0;

        public static void ClearCache()
        {
            UIUtils.ClearBrushCache();
        }

        public static void Render(Graphics g, List<GroupLayoutInfo> groups, Theme t, Settings? cfg = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool isMarketDisplayOnly = IsMarketDisplayOnly(groups);
            double backgroundOpacity = GetBackgroundOpacity(cfg);
            double textOpacity = GetTextOpacity(cfg);

            // 1. 绘制背景
            // ★★★ [核心修复] 扩大绘制区域，解决左侧和上侧漏黑边的问题 ★★★
            // 原理：从 (-5, -5) 开始画，确保绝对覆盖掉 (0,0) 处的物理像素死角。
            // 多余的部分会被系统自动裁剪，不会有副作用。
            string bgColor = GetPanelBackgroundColor(t, cfg, isMarketDisplayOnly);
            var oldCompositingMode = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var bgBrush = new SolidBrush(UIUtils.WithOpacity(ThemeManager.ParseColor(bgColor), backgroundOpacity)))
            {
                g.FillRectangle(bgBrush,
                    new Rectangle(-5, -5, (int)g.VisibleClipBounds.Width + 10, (int)g.VisibleClipBounds.Height + 10));
            }
            g.CompositingMode = oldCompositingMode;

            // 2. 绘制各分组
            foreach (var gr in groups)
            {
                if (!isMarketDisplayOnly)
                    DrawGroupHeader(g, gr, t, textOpacity);

                // 遍历子项绘制 (不再区分 NET/DISK，统一由 Item.Style 决定)
                for (int i = 0; i < gr.Items.Count; i++)
                {
                    var it = gr.Items[i];
                    bool isLast = isMarketDisplayOnly || (i == gr.Items.Count - 1);

                    if (it.Style == MetricRenderStyle.TwoColumn)
                        DrawTwoColumnItem(g, it, t, textOpacity);
                    else if (it.Style == MetricRenderStyle.TextOnly) // [新增]
                        DrawTextItem(g, it, t, isLast, textOpacity);
                    else
                        DrawStandardItem(g, it, t, isLast, textOpacity);
                }
            }
        }

        private static double GetBackgroundOpacity(Settings? cfg)
        {
            double opacity = Math.Clamp(cfg?.PanelBackgroundOpacity ?? cfg?.Opacity ?? 1.0, 0.0, 1.0);
            if (opacity <= 0.0 && cfg?.ClickThrough != true)
                return InteractiveTransparentBackgroundOpacity;

            return opacity;
        }

        private static double GetTextOpacity(Settings? cfg)
        {
            return Math.Clamp(cfg?.TextOpacity ?? 1.0, 0.1, 1.0);
        }

        private static string GetPanelBackgroundColor(Theme t, Settings? cfg, bool isMarketDisplayOnly)
        {
            if (!string.IsNullOrWhiteSpace(cfg?.PanelBackgroundColor))
                return cfg.PanelBackgroundColor;

            if (isMarketDisplayOnly && !string.IsNullOrWhiteSpace(cfg?.SteamDtBackgroundColor))
                return cfg.SteamDtBackgroundColor;

            return t.Color.Background;
        }

        private static bool IsMarketDisplayOnly(List<GroupLayoutInfo> groups)
        {
            var items = groups.SelectMany(g => g.Items).ToList();
            return items.Count > 0 && items.All(x => IsMarketDisplayKey(x.Key));
        }

        private static bool IsMarketDisplayKey(string key)
        {
            return MarketDisplayFormatter.IsMarketDisplayKey(key);
        }

        private static void DrawGroupHeader(Graphics g, GroupLayoutInfo gr, Theme t, double textOpacity)
        {
            int gp = t.Layout.GroupPadding;

            // 绘制组标题 (CPU, GPU...)
            // ★★★ 优化：直接使用缓存的 gr.Label，不再每帧调用 LanguageManager.T ★★★
            string label = string.IsNullOrEmpty(gr.Label) ? gr.GroupName : gr.Label;

            int titleH = t.FontGroup.Height;
            int extraH = (int)(4 * t.Layout.LayoutScale);
            int titleY = gr.Bounds.Y + Math.Max(2, gp / 2);
            var rectTitle = new Rectangle(
                gr.Bounds.X + gp,
                titleY,
                gr.Bounds.Width - gp * 2,
                titleH + extraH);

            UIUtils.DrawText(g, label, t.FontGroup, rectTitle,
                ThemeManager.ParseColor(t.Color.TextGroup),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                textOpacity);
        }

        /// <summary>
        /// 绘制标准项 (标签 + 数值 + 进度条)
        /// </summary>
        private static void DrawStandardItem(Graphics g, MetricItem it, Theme t, bool isLast, double textOpacity)
        {
            if (it.Bounds == Rectangle.Empty) return;

            if (MarketDisplayFormatter.IsMarketDisplayKey(it.Key))
            {
                MarketDisplayRenderer.Draw(g, it, it.Bounds, t, textOpacity, drawSeparator: !isLast);
                return;
            }

            if (YouPinInventoryTrendDisplayMetric.IsKey(it.Key))
            {
                DrawInlineInventoryTrendItem(g, it, t, isLast, textOpacity);
                return;
            }

            // Label (左对齐)
            // ★★★ 优化：直接使用缓存的 it.Label ★★★
            string label = string.IsNullOrEmpty(it.Label) ? it.Key : it.Label;

            string valText = it.GetFormattedText(false);
            Font labelFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: false);
            Font valueFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: true);
            MetricRenderAppearance.GetColors(it, t, out var lblColor, out var valColor);

            UIUtils.DrawText(g, label, labelFont, it.LabelRect,
                lblColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                textOpacity);

            // Value (右对齐)  传入 false 表示竖屏/普通模式
            UIUtils.DrawText(g, valText, valueFont, it.ValueRect,
                valColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                textOpacity);

            // Bar - 注意：这里调用的是 UIUtils.DrawBar，它现在已经使用了优化的画刷逻辑
            UIUtils.DrawBar(g, it, t);
        }

        private static void DrawInlineInventoryTrendItem(Graphics g, MetricItem it, Theme t, bool isLast, double textOpacity)
        {
            Font labelFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: false);
            Font valueFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: true);
            MetricRenderAppearance.GetColors(it, t, out var labelColor, out var valueColor);

            Rectangle rc = it.Bounds;
            string label = YouPinInventoryTrendDisplayMetric.TaskbarDisplayLabel;
            string value = it.GetFormattedText(false);
            int labelWidth = MeasureTextWidth(g, label, labelFont);
            int gap = UIUtils.S(4);
            int valueX = rc.Left + labelWidth + gap;

            UIUtils.DrawText(g, label, labelFont,
                new Rectangle(rc.Left, rc.Top, Math.Max(1, labelWidth), rc.Height),
                labelColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping,
                textOpacity);

            UIUtils.DrawText(g, value, valueFont,
                new Rectangle(valueX, rc.Top, Math.Max(1, rc.Right - valueX), rc.Height),
                valueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoClipping | TextFormatFlags.EndEllipsis,
                textOpacity);

            if (!isLast)
            {
                Color divColor = Color.FromArgb(100, ThemeManager.ParseColor(t.Color.BarBackground));
                var pen = UIUtils.GetPen(divColor, 1);
                int lineY = it.Bounds.Bottom - 1;
                g.DrawLine(pen, it.Bounds.Left + 5, lineY, it.Bounds.Right - 5, lineY);
            }
        }

        /// <summary>
        /// 绘制双列项 (居中标签 + 居中数值)
        /// </summary>
        private static void DrawTwoColumnItem(Graphics g, MetricItem it, Theme t, double textOpacity)
        {
            if (it.Bounds == Rectangle.Empty) return;

            // Label (居中顶部)
            string label = string.IsNullOrEmpty(it.Label) ? it.Key : it.Label;

            // Value (居中底部)
            // ★★★ [优化]：如果用户使用了自定义单位 (HasCustomUnit)，则跳过窄屏自动精简逻辑 ★★★
            // 原逻辑：if (narrow) valText = FormatHorizontalValue(...)
            bool narrow = t.Layout.Width < 240 * t.Layout.LayoutScale;
            string valText = it.GetFormattedText(narrow);
            Font labelFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: false);
            Font valueFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: true);
            MetricRenderAppearance.GetColors(it, t, out var lblColor, out var valColor);

            UIUtils.DrawText(g, label, labelFont, it.LabelRect,
                lblColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding,
                textOpacity);

            UIUtils.DrawText(g, valText, valueFont, it.ValueRect,
                valColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.NoPadding,
                textOpacity);
        }

        // [新增] 绘制纯文本项方法
        private static void DrawTextItem(Graphics g, MetricItem it, Theme t, bool isLast, double textOpacity)
        {
            if (it.Bounds == Rectangle.Empty) return;

            if (MarketDisplayFormatter.IsMarketDisplayKey(it.Key))
            {
                MarketDisplayRenderer.Draw(g, it, it.Bounds, t, textOpacity, drawSeparator: !isLast);
                return;
            }

            // 2. 绘制右侧数值 (192.168.x.x)
            // ★★★ 修复：直接使用布局计算好的 ValueRect (已修正为全宽) ★★★
            // 优先调用 GetFormattedText 以支持单位显示
            string text = it.GetFormattedText(false);
            Font labelFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: false);
            Font valueFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: true);
            MetricRenderAppearance.GetColors(it, t, out var lblColor, out var valColor);

            // 1. 绘制左侧标签 (IP)
            string label = string.IsNullOrEmpty(it.Label) ? it.Key : it.Label;
            bool hideLabel = string.IsNullOrWhiteSpace(label);
            if (!hideLabel)
            {
                UIUtils.DrawText(g, label, labelFont, it.LabelRect,
                    lblColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                    textOpacity);
            }

            UIUtils.DrawText(g, text, valueFont, it.ValueRect,
                valColor,
                (hideLabel ? TextFormatFlags.Left : TextFormatFlags.Right)
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoClipping,
                textOpacity);

            // ★★★ 修复：补充分割线 (最后一行不画) ★★★
            if (!isLast)
            {
                // 使用半透明的 BarBackground 作为分割线
                Color divColor = Color.FromArgb(100, ThemeManager.ParseColor(t.Color.BarBackground));

                // [优化] 使用缓存的 Pen
                var pen = UIUtils.GetPen(divColor, 1);

                // 线条稍微缩进一点，更美观
                int lineY = it.Bounds.Bottom - 1;
                g.DrawLine(pen, it.Bounds.Left + 5, lineY, it.Bounds.Right - 5, lineY);
            }
        }

        private static int MeasureTextWidth(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(g, text, font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }


    }
}
