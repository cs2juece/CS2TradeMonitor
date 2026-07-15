using System;
using System.Drawing;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class DataPageSourceCompactRowModel
    {
        public static DataPageSourceCompactRowLayout BuildLayout(Size rowSize, bool intervalIsInput)
        {
            int rowWidth = Math.Max(1, rowSize.Width);
            int rowHeight = Math.Max(1, rowSize.Height);
            int gap = UIUtils.S(12);
            int y = 0;
            int h = Math.Max(UIUtils.S(28), Math.Min(UIUtils.S(34), rowHeight - UIUtils.S(38)));
            int titleW = UIUtils.S(145);
            int statusW = UIUtils.S(72);
            int capW = UIUtils.S(36);
            int sourceW = UIUtils.S(170);
            int intervalW = UIUtils.S(94);

            if (rowWidth < UIUtils.S(700))
            {
                int topH = UIUtils.S(26);
                int secondY = UIUtils.S(28);
                var title = new Rectangle(0, 0, Math.Min(UIUtils.S(170), rowWidth / 2), topH);
                var status = new Rectangle(title.Right + UIUtils.S(8), 0, Math.Max(UIUtils.S(64), rowWidth - title.Right - UIUtils.S(8)), topH);

                int narrowX = 0;
                var sourceCaption = new Rectangle(narrowX, secondY, capW, UIUtils.S(24));
                narrowX = sourceCaption.Right + UIUtils.S(4);
                var source = new Rectangle(narrowX, secondY, Math.Min(UIUtils.S(126), Math.Max(UIUtils.S(90), rowWidth / 4)), UIUtils.S(24));
                narrowX = source.Right + gap;
                var intervalCaption = new Rectangle(narrowX, secondY, capW, UIUtils.S(24));
                narrowX = intervalCaption.Right + UIUtils.S(4);
                var interval = intervalIsInput
                    ? new Rectangle(narrowX, secondY - UIUtils.S(2), Math.Min(intervalW, Math.Max(UIUtils.S(78), rowWidth - narrowX)), UIUtils.S(28))
                    : new Rectangle(narrowX, secondY, Math.Min(intervalW, Math.Max(UIUtils.S(78), rowWidth - narrowX)), UIUtils.S(24));
                var detail = new Rectangle(0, UIUtils.S(54), rowWidth, UIUtils.S(18));

                return new DataPageSourceCompactRowLayout(title, status, sourceCaption, source, intervalCaption, interval, detail);
            }

            if (rowWidth < UIUtils.S(840))
            {
                titleW = UIUtils.S(126);
                sourceW = UIUtils.S(138);
                intervalW = UIUtils.S(88);
                gap = UIUtils.S(8);
            }

            int x = 0;
            var wideTitle = new Rectangle(x, y, titleW, h);
            x = wideTitle.Right + gap;
            var wideStatus = new Rectangle(x, y, statusW, h);
            x = wideStatus.Right + gap;
            var wideSourceCaption = new Rectangle(x, y, capW, h);
            x = wideSourceCaption.Right + UIUtils.S(4);
            var wideSource = new Rectangle(x, y, sourceW, h);
            x = wideSource.Right + gap;
            var wideIntervalCaption = new Rectangle(x, y, capW, h);
            x = wideIntervalCaption.Right + UIUtils.S(4);
            Rectangle wideInterval;
            if (intervalIsInput)
            {
                int inputH = UIUtils.S(28);
                wideInterval = new Rectangle(x, y + (h - inputH) / 2, intervalW, inputH);
            }
            else
            {
                wideInterval = new Rectangle(x, y, intervalW, h);
            }

            x = wideInterval.Right + gap;
            int detailTop = Math.Max(UIUtils.S(34), wideTitle.Bottom + UIUtils.S(4));
            int detailH = Math.Max(UIUtils.S(24), rowHeight - detailTop - UIUtils.S(2));
            var wideDetail = new Rectangle(0, detailTop, rowWidth, detailH);

            return new DataPageSourceCompactRowLayout(
                wideTitle,
                wideStatus,
                wideSourceCaption,
                wideSource,
                wideIntervalCaption,
                wideInterval,
                wideDetail);
        }
    }

    internal readonly record struct DataPageSourceCompactRowLayout(
        Rectangle TitleBounds,
        Rectangle StatusBounds,
        Rectangle SourceCaptionBounds,
        Rectangle SourceBounds,
        Rectangle IntervalCaptionBounds,
        Rectangle IntervalBounds,
        Rectangle DetailBounds);
}
