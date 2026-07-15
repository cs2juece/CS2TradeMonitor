using CS2TradeMonitor.src.Core;
using System.Linq;
using System.Drawing.Drawing2D;

namespace CS2TradeMonitor
{
    /// <summary>
    /// 横版渲染器（基于列结构绘制）
    /// 完全保留原版布局，不做任何功能添加。
    /// 修复内容：
    /// 1. Render 方法签名修复（支持 panelWidth）
    /// 2. value/颜色 使用 UIUtils 统一入口 -> 升级为 MetricItem 缓存入口
    /// 3. 删除文件内重复工具函数
    /// </summary>
    public static class HorizontalRenderer
    {
        private const double InteractiveTransparentBackgroundOpacity = 1.0 / 255.0;

        public static void Render(Graphics g, Theme t, List<Column> cols, int panelWidth, Settings? cfg = null)
        {
            int panelHeight = (int)g.VisibleClipBounds.Height;
            double backgroundOpacity = GetBackgroundOpacity(cfg);
            double textOpacity = Math.Clamp(cfg?.TextOpacity ?? 1.0, 0.1, 1.0);
            bool isMarketDisplayOnly = IsMarketDisplayOnly(cols);
            string bgColor = GetPanelBackgroundColor(t, cfg, isMarketDisplayOnly);

            var oldCompositingMode = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (var bg = new SolidBrush(UIUtils.WithOpacity(ThemeManager.ParseColor(bgColor), backgroundOpacity)))
                g.FillRectangle(bg, new Rectangle(0, 0, panelWidth, panelHeight));
            g.CompositingMode = oldCompositingMode;

            foreach (var col in cols)
                DrawColumn(g, col, t, textOpacity);
        }

        private static double GetBackgroundOpacity(Settings? cfg)
        {
            double opacity = Math.Clamp(cfg?.PanelBackgroundOpacity ?? cfg?.Opacity ?? 1.0, 0.0, 1.0);
            if (opacity <= 0.0 && cfg?.ClickThrough != true)
                return InteractiveTransparentBackgroundOpacity;

            return opacity;
        }

        private static void DrawColumn(Graphics g, Column col, Theme t, double textOpacity)
        {
            if (col.Bounds == Rectangle.Empty) return;

            // ★★★ 优化：优先使用 Layout 预计算好的 Bounds，不再重复计算 ★★★
            // 这样可以同时兼容双行模式、任务栏单行模式、以及横条单行模式

            // 1. 绘制 Top
            if (col.BoundsTop != Rectangle.Empty && col.Top != null)
            {
                DrawItem(g, col.Top, col.BoundsTop, t, textOpacity);
            }

            // 2. 绘制 Bottom
            if (col.BoundsBottom != Rectangle.Empty && col.Bottom != null)
            {
                DrawItem(g, col.Bottom, col.BoundsBottom, t, textOpacity);
            }
        }

        private static bool IsMarketDisplayOnly(List<Column> cols)
        {
            var items = cols
                .SelectMany(c => new[] { c.Top, c.Bottom })
                .Where(i => i != null)
                .ToList();

            return items.Count > 0 && items.All(i => MarketDisplayFormatter.IsMarketDisplayKey(i!.Key));
        }

        private static string GetPanelBackgroundColor(Theme t, Settings? cfg, bool isMarketDisplayOnly)
        {
            if (!string.IsNullOrWhiteSpace(cfg?.PanelBackgroundColor))
                return cfg.PanelBackgroundColor;

            if (isMarketDisplayOnly && !string.IsNullOrWhiteSpace(cfg?.SteamDtBackgroundColor))
                return cfg.SteamDtBackgroundColor;

            return t.Color.Background;
        }

        private static void DrawItem(Graphics g, MetricItem it, Rectangle rc, Theme t, double textOpacity)
        {
            if (MarketDisplayFormatter.IsMarketDisplayKey(it.Key))
            {
                MarketDisplayRenderer.Draw(g, it, rc, t, textOpacity, drawSeparator: false);
                return;
            }

            // 使用 MetricItem 统一格式化 (横屏模式=true)
            string value = it.GetFormattedText(true);
            Font labelFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: false);
            Font valueFont = MetricRenderAppearance.GetFont(t, it.RuntimeSettings, valueFont: true);
            MetricRenderAppearance.GetColors(it, t, out var lblColor, out var valColor);

            // ★★★ 策略 A: 纯文本模式 (隐藏标签) ★★★
            // 适用于 IP、Dashboard 文本，直接居左显示
            // 逻辑：如果 ShortLabel 被显式设为空格或空，则视为隐藏标签
            bool hideLabel = string.IsNullOrEmpty(it.ShortLabel) || it.ShortLabel == " ";

            if (hideLabel)
            {
                // 可以根据偏好选择 Left 或 Center，这里选用 Left 比较稳妥
                UIUtils.DrawText(
                    g,
                    value,
                    valueFont,
                    rc,
                    valColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                    textOpacity
                );
                return;
            }

            // ★★★ 策略 B: 标准标签模式 ★★★
            // Label (左对齐)
            // 优化：直接使用缓存的 ShortLabel
            string label = !string.IsNullOrEmpty(it.ShortLabel) ? it.ShortLabel : it.Label;
            if (string.IsNullOrEmpty(label)) label = it.Key;

            UIUtils.DrawText(
                g,
                label,
                labelFont,
                rc,
                lblColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                textOpacity
            );

            // Value (右对齐)
            // ★★★ 修复：统一使用 Item 字体 (即标签字体)，与任务栏保持一致 ★★★
            UIUtils.DrawText(
                g,
                value,
                valueFont,
                rc,
                valColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding,
                textOpacity
            );
        }

    }
}
