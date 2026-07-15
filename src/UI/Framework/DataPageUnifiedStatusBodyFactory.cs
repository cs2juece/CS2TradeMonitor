using CS2TradeMonitor.src.Core;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct DataPageUnifiedStatusBodyLayout(
        int BodyHeight,
        Rectangle SteamDtBounds,
        Rectangle CsqaqBounds);

    internal static class DataPageUnifiedStatusBodyModel
    {
        public static DataPageUnifiedStatusBodyLayout BuildLayout(Size bodySize, Padding padding)
        {
            int x = padding.Left;
            int y = padding.Top;
            int available = Math.Max(1, bodySize.Width - padding.Horizontal);
            int gap = UIUtils.S(12);
            bool twoColumns = available >= UIUtils.S(1280);
            if (twoColumns)
            {
                int rowHeight = UIUtils.S(82);
                int columnWidth = Math.Max(UIUtils.S(360), (available - gap) / 2);
                int secondWidth = Math.Max(UIUtils.S(360), available - columnWidth - gap);
                var steamDtBounds = new Rectangle(x, y, columnWidth, rowHeight);
                return new DataPageUnifiedStatusBodyLayout(
                    padding.Vertical + rowHeight,
                    steamDtBounds,
                    new Rectangle(steamDtBounds.Right + gap, y, secondWidth, rowHeight));
            }

            int stackedRowHeight = UIUtils.S(74);
            var firstRow = new Rectangle(x, y, available, stackedRowHeight);
            return new DataPageUnifiedStatusBodyLayout(
                padding.Vertical + stackedRowHeight * 2 + UIUtils.S(6),
                firstRow,
                new Rectangle(x, firstRow.Bottom + UIUtils.S(6), available, stackedRowHeight));
        }
    }

    internal static class DataPageUnifiedStatusBodyFactory
    {
        public static Control Create(Control steamDtRow, Control csqaqRow)
        {
            ArgumentNullException.ThrowIfNull(steamDtRow);
            ArgumentNullException.ThrowIfNull(csqaqRow);

            var body = new Panel
            {
                Height = UIUtils.S(112),
                BackColor = Color.Transparent,
                Padding = UIUtils.S(new Padding(20, 10, 20, 12))
            };

            body.Controls.AddRange(new[] { steamDtRow, csqaqRow });
            body.Layout += (_, __) =>
            {
                DataPageUnifiedStatusBodyLayout layout = DataPageUnifiedStatusBodyModel.BuildLayout(
                    body.Size,
                    body.Padding);
                if (body.Height != layout.BodyHeight)
                    body.Height = layout.BodyHeight;
                steamDtRow.Bounds = layout.SteamDtBounds;
                csqaqRow.Bounds = layout.CsqaqBounds;
            };

            return body;
        }
    }
}
