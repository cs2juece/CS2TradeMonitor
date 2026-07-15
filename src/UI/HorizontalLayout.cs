using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    public enum LayoutMode
    {
        Horizontal,
        Taskbar
    }

    public class HorizontalLayout
    {
        private readonly Theme _t;
        private readonly LayoutMode _mode;
        private readonly Settings _settings;

        private readonly int _padding;
        private readonly int _requestedWidth;
        private int _rowH;
        private const int TextSafetyPadding = 8;

        // DPI
        private readonly float _dpiScale;

        public int PanelWidth { get; private set; }

        private int ScaledTextSafetyPadding => Math.Max(TextSafetyPadding, (int)Math.Ceiling(TextSafetyPadding * _dpiScale));

        public HorizontalLayout(Theme t, int initialWidth, LayoutMode mode, Settings? settings = null)
        {
            _t = t;
            _mode = mode;
            // 预览/测试调用可显式传 settings；旧调用方不传时保留一次配置兜底。
            _settings = settings ?? Settings.Load();

            if (mode == LayoutMode.Taskbar)
            {
                _dpiScale = CS2TradeMonitor.src.UI.Helpers.TaskbarWinHelper.GetTaskbarDpi() / 96f;
            }
            else
            {
                _dpiScale = UIUtils.ScaleFactor;
            }

            _padding = t.Layout.Padding;
            _requestedWidth = Math.Max(1, initialWidth);
            if (mode == LayoutMode.Horizontal)
                _rowH = Math.Max(t.FontItem.Height, t.FontValue.Height);
            else
                _rowH = 0; // 任务栏模式稍后根据 taskbarHeight 决定

            PanelWidth = initialWidth;
        }

        /// <summary>
        /// Build：横屏/任务栏共用布局
        /// </summary>
        public int Build(List<Column> cols, int taskbarHeight = 32)
        {
            if (cols == null || cols.Count == 0) return 0;

            var s = _settings.GetStyle();
            if (_mode == LayoutMode.Horizontal && IsMarketDisplayOnlyHorizontal(cols))
            {
                int padX = 8;
                int padY = 2;
                int marketGap = (int)Math.Round(_settings.HorizontalItemSpacing * _dpiScale);
                int marketTotalWidth = padX * 2;

                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    foreach (var col in cols)
                    {
                        int itemWidth = MeasureMetricItem(g, col.Top, s, null);
                        col.ColumnWidth = Math.Max(1, itemWidth);
                        marketTotalWidth += col.ColumnWidth;
                    }
                }

                if (cols.Count > 1) marketTotalWidth += (cols.Count - 1) * marketGap;

                _rowH = Math.Max(_t.FontItem.Height, _t.FontValue.Height);
                PanelWidth = Math.Max(1, marketTotalWidth);

                int marketX = padX;
                for (int i = 0; i < cols.Count; i++)
                {
                    var col = cols[i];
                    int rowY = padY;
                    col.Bounds = new Rectangle(marketX, rowY, col.ColumnWidth, _rowH);
                    col.BoundsTop = col.Bounds;
                    col.BoundsBottom = Rectangle.Empty;
                    marketX += col.ColumnWidth + marketGap;
                }

                return _rowH + padY * 2;
            }

            if (IsSingleMarketDisplayColumn(cols))
            {
                int padX = 8;
                int padY = 2;
                var item = cols[0].Top!;
                Font labelFont;
                Font valueFont;

                if (_mode == LayoutMode.Taskbar)
                {
                    var marketFont = UIUtils.GetFont(s.Font, s.Size * _dpiScale, s.Bold);
                    labelFont = marketFont;
                    valueFont = marketFont;
                }
                else
                {
                    labelFont = _t.FontItem;
                    valueFont = _t.FontValue;
                }

                int textW;
                int textH;
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    textW = MeasureMarketDisplayItem(g, item.Key, _settings, labelFont, valueFont);
                    textH = Math.Max(labelFont.Height, valueFont.Height);
                }

                int contentW = Math.Max(1, textW + TextSafetyPadding);
                PanelWidth = _mode == LayoutMode.Horizontal
                    ? Math.Max(1, contentW + padX * 2)
                    : Math.Max(_requestedWidth, contentW + padX * 2);
                _rowH = Math.Max(1, textH);
                cols[0].ColumnWidth = contentW;
                cols[0].Bounds = new Rectangle(padX, padY, contentW, _rowH);
                cols[0].BoundsTop = cols[0].Bounds;
                cols[0].BoundsBottom = Rectangle.Empty;

                return _rowH + padY * 2;
            }

            int pad = _padding;
            int padV = _padding / 2;
            bool isTaskbarSingle = (_mode == LayoutMode.Taskbar && _settings.TaskbarSingleLine);
            bool isHorizontalSingle = (_mode == LayoutMode.Horizontal && _settings.HorizontalSingleLine);

            if (_mode == LayoutMode.Taskbar)
            {
                padV = 0;
                _rowH = isTaskbarSingle ? taskbarHeight : taskbarHeight / 2;
            }

            int totalWidth = pad * 2;
            float dpi = _dpiScale;
            Font? taskbarMeasureFont = _mode == LayoutMode.Taskbar
                ? UIUtils.GetFont(s.Font, s.Size * _dpiScale, s.Bold)
                : null;

            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                foreach (var col in cols)
                {
                    // 分别计算 Top 和 Bottom 的所需宽度，然后取最大值
                    int widthTop = 0;
                    int widthBottom = 0;



                    // 执行测量
                    widthTop = MeasureMetricItem(g, col.Top, s, taskbarMeasureFont);
                    widthBottom = MeasureMetricItem(g, col.Bottom, s, taskbarMeasureFont);

                    // ★★★ 核心修复：列宽取上下两者的最大值 ★★★
                    // 这样即使 IP 在下面，列宽也会被 IP 撑大；
                    // 同时上面的普通项也能利用这个宽度正常显示（虽然左右会有空余，但不会重叠）
                    col.ColumnWidth = Math.Max(widthTop, widthBottom);

                    totalWidth += col.ColumnWidth;
                }
            }


            // 组间距逻辑
            int gapBase = (_mode == LayoutMode.Taskbar) ? s.Gap : _settings.HorizontalItemSpacing;
            int gap = (int)Math.Round(gapBase * dpi);

            if (cols.Count > 1) totalWidth += (cols.Count - 1) * gap;
            PanelWidth = totalWidth;

            // ===== 设置列 Bounds =====
            int x = pad;

            foreach (var col in cols)
            {
                int colHeight;
                if (isTaskbarSingle || isHorizontalSingle)
                    colHeight = _rowH;
                else
                    colHeight = _rowH * 2;

                col.Bounds = new Rectangle(x, padV, col.ColumnWidth, colHeight);

                if (_mode == LayoutMode.Taskbar)
                {
                    int fixOffset = 1;

                    if (isTaskbarSingle)
                    {
                        col.BoundsTop = new Rectangle(x, col.Bounds.Y + fixOffset, col.ColumnWidth, colHeight);
                        col.BoundsBottom = Rectangle.Empty;
                    }
                    else
                    {
                        // 双行模式
                        col.BoundsTop = new Rectangle(x, col.Bounds.Y + s.VOff + fixOffset, col.ColumnWidth, _rowH - s.VOff);
                        col.BoundsBottom = new Rectangle(x, col.Bounds.Y + _rowH - s.VOff + fixOffset, col.ColumnWidth, _rowH);
                    }
                }
                else
                {
                    // 横屏模式
                    if (isHorizontalSingle)
                    {
                        // 单行模式：Top 居中显示，隐藏 Bottom
                        col.BoundsTop = new Rectangle(col.Bounds.X, col.Bounds.Y, col.Bounds.Width, _rowH);
                        col.BoundsBottom = Rectangle.Empty;
                    }
                    else
                    {
                        // 默认双行模式
                        col.BoundsTop = new Rectangle(col.Bounds.X, col.Bounds.Y, col.Bounds.Width, _rowH);
                        col.BoundsBottom = new Rectangle(col.Bounds.X, col.Bounds.Y + _rowH, col.Bounds.Width, _rowH);
                    }
                }

                // [补充修正] 如果是 NET.IP 混合列，我们需要告诉 Renderer 不要画 Label 区域，而是全宽显示
                // 但由于 Renderer 是根据 (LabelRect, ValueRect) 绘图的，而 HorizontalLayout 不负责计算具体的 LabelRect
                // 所以我们依赖 TaskbarRenderer 的逻辑：它会看 Label 是否为空。
                // 只要列宽足够（ColumnWidth 够大），TaskbarRenderer 右对齐 Value 时就不会出问题。

                x += col.ColumnWidth + gap;
            }

            return padV * 2 + ((isTaskbarSingle || isHorizontalSingle) ? _rowH : _rowH * 2);
        }

        private static bool IsMarketDisplayOnlyHorizontal(List<Column> cols)
        {
            return cols.Count > 0
                && cols.All(col => col.Top != null
                    && col.Bottom == null
                    && IsMarketDisplayKey(col.Top.Key));
        }

        private static bool IsSingleMarketDisplayColumn(List<Column> cols)
        {
            if (cols.Count != 1) return false;
            var top = cols[0].Top;

            return cols.Count == 1
                && top != null
                && cols[0].Bottom == null
                && IsMarketDisplayKey(top.Key);
        }

        private static bool IsMarketDisplayKey(string key)
        {
            return MarketDisplayFormatter.IsMarketDisplayKey(key);
        }

        private int MeasureMetricItem(Graphics g, MetricItem? item, Settings.TBStyle s, Font? taskbarMeasureFont)
        {
            if (item == null) return 0;

            float dpi = _dpiScale;

            if (MarketDisplayFormatter.IsMarketDisplayKey(item.Key))
            {
                Font labelFont;
                Font valueFont;

                if (_mode == LayoutMode.Taskbar)
                {
                    var f = taskbarMeasureFont ?? UIUtils.GetFont(s.Font, s.Size * _dpiScale, s.Bold);
                    labelFont = f;
                    valueFont = f;
                }
                else
                {
                    labelFont = _t.FontItem;
                    valueFont = _t.FontValue;
                }

                return MeasureMarketDisplayItem(g, item.Key, _settings, labelFont, valueFont) + ScaledTextSafetyPadding;
            }

            if (_mode == LayoutMode.Taskbar && YouPinInventoryTrendDisplayMetric.IsKey(item.Key))
            {
                Font font = taskbarMeasureFont ?? UIUtils.GetFont(s.Font, s.Size * _dpiScale, s.Bold);
                string label = YouPinInventoryTrendDisplayMetric.TaskbarDisplayLabel;
                string value = YouPinInventoryTrendDisplayMetric.FormatValue(_settings);
                int labelWidth = TextRenderer.MeasureText(
                    g,
                    label,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                int valueWidth = TextRenderer.MeasureText(
                    g,
                    value,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                int gap = Math.Max(2, (int)Math.Round(4 * dpi));
                int safety = Math.Max(2, (int)Math.Round(3 * dpi));

                return labelWidth + gap + valueWidth + safety;
            }

            // [通用逻辑] 如果隐藏标签 (ShortLabel 为空 或 " ")，则只计算文本宽
            if (string.IsNullOrEmpty(item.ShortLabel) || item.ShortLabel == " ")
            {
                // 对于 Dashboard/IP 类，直接使用当前文本作为测量依据
                string valText = item.TextValue ?? item.GetFormattedText(true);
                if (string.IsNullOrEmpty(valText)) return 0;

                Font valFont;
                bool disposeFont = false;

                if (_mode == LayoutMode.Taskbar)
                {
                    valFont = taskbarMeasureFont ?? UIUtils.GetFont(s.Font, s.Size * _dpiScale, s.Bold);
                    disposeFont = false;
                }
                else
                {
                    valFont = _t.FontItem;
                }

                try
                {
                    int w = TextRenderer.MeasureText(g, valText, valFont,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding).Width;

                    // 纯文本项建议稍微加一点点左右 padding，防止紧贴
                    return w + ScaledTextSafetyPadding;
                }
                finally
                {
                    if (disposeFont) valFont.Dispose();
                }
            }
            else
            {
                // [普通逻辑] 标签 + 数值 + 间距
                // 1. Label
                string label = item.ShortLabel;
                Font labelFont, valueFont;
                bool disposeFont = false;

                if (_mode == LayoutMode.Taskbar)
                {
                    var f = taskbarMeasureFont ?? UIUtils.GetFont(s.Font, s.Size * _dpiScale, s.Bold);
                    labelFont = f; valueFont = f;
                    disposeFont = false;
                }
                else
                {
                    labelFont = _t.FontItem;
                    valueFont = _t.FontValue;
                }

                try
                {
                    int wLabel = TextRenderer.MeasureText(g, label, labelFont,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

                    // 2. Value (使用样本值估算 或 真实值)
                    string sample = GenerateSampleText(item);

                    int wValue = TextRenderer.MeasureText(g, sample, valueFont,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

                    // 3. Padding
                    int paddingX;
                    if (_mode == LayoutMode.Taskbar || _settings.HorizontalFollowsTaskbar)
                        paddingX = (int)Math.Round(s.Inner * dpi);
                    else
                        paddingX = (int)Math.Round(_settings.HorizontalInnerSpacing * dpi);

                    return wLabel + wValue + paddingX + ScaledTextSafetyPadding;
                }
                finally
                {
                    if (disposeFont)
                    {
                        labelFont.Dispose();
                        // valueFont is same reference as labelFont in Taskbar mode
                    }
                }
            }
        }

        private static int MeasureMarketDisplayItem(Graphics g, string key, Settings settings, Font labelFont, Font valueFont)
        {
            return MarketDisplayRenderMetrics.Measure(g, settings, labelFont, valueFont).TotalWidth;
        }

        private string GenerateSampleText(MetricItem item)
        {
            // 1. 优先获取 TextValue
            string val = item.TextValue ?? "";

            // 统一获取默认单位，用于任务栏宽度估算。
            string rawUnit = MetricUtils.GetUnitStr(item.Key, 0, MetricUtils.UnitContext.Taskbar);

            // 2. 如果没有实时文本，则使用样本文本估算宽度。
            if (string.IsNullOrEmpty(val) && !item.Key.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase))
            {
                val = MetricUtils.GetSampleValueStr(item.Key);

                // 数据大小类样本文本固定按 MB 估算，避免宽度抖动。
                var type = MetricUtils.GetType(item.Key);
                if (type == MetricType.DataSpeed || type == MetricType.DataSize)
                {
                    rawUnit = "MB";
                }
            }

            // 2. 处理显示单位 (叠加用户配置)
            string? userFmt = item.BoundConfig?.UnitTaskbar;
            string unit = MetricUtils.GetDisplayUnit(item.Key, rawUnit, userFmt);

            // 3. 拼接并生成样本 (将所有数字替换为 '0')
            // [Optimization] 使用 string.Create 避免中间数组分配 (Net 8.0+)
            bool appendUnit = !string.IsNullOrEmpty(unit) && !val.EndsWith(unit);
            int totalLen = val.Length + (appendUnit ? unit.Length : 0);

            return string.Create(totalLen, (val, unit, appendUnit), (span, state) =>
            {
                var (v, u, append) = state;
                int pos = 0;

                // 写入数值部分 (数字转0)
                foreach (char c in v)
                {
                    span[pos++] = char.IsDigit(c) ? '0' : c;
                }

                // 写入单位部分
                if (append)
                {
                    foreach (char c in u)
                    {
                        span[pos++] = char.IsDigit(c) ? '0' : c;
                    }
                }
            });
        }

        // [通用方案] 获取当前布局的签名是否变化 (用于检测是否需要重绘)
        public string GetLayoutSignature(List<Column> cols)
        {
            if (cols == null || cols.Count == 0) return "";

            unchecked
            {
                int hash = 17;
                foreach (var col in cols)
                {
                    // 直接计算 Top 和 Bottom 的特征哈希，不再生成中间样本字符串
                    void AddItemToHash(MetricItem? item)
                    {
                        if (item == null) return;
                        string text = item.TextValue ?? item.GetFormattedText(true);
                        hash = hash * 31 + text.Length;

                        bool fullHash = MarketDisplayFormatter.IsMarketDisplayKey(item.Key);
                        foreach (char c in text)
                        {
                            if (fullHash || !char.IsDigit(c))
                                hash = (hash << 5) - hash + c;
                        }
                    }

                    AddItemToHash(col.Top);
                    AddItemToHash(col.Bottom);
                }
                return hash.ToString();
            }
        }
    }

    public class Column
    {
        public MetricItem? Top;
        public MetricItem? Bottom;

        public int ColumnWidth;
        public Rectangle Bounds = Rectangle.Empty;

        // ★★ B 方案新增：上下行布局由 Layout 计算，不再由 Renderer 处理
        public Rectangle BoundsTop = Rectangle.Empty;
        public Rectangle BoundsBottom = Rectangle.Empty;
    }
}
