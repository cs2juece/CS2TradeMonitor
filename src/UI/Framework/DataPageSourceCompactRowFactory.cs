using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class DataPageSourceCompactRowFactory
    {
        public static Control Create(string titleText, Label statusValue, Label sourceValue, Control intervalValue, Label detailValue)
        {
            var row = new Panel
            {
                Height = UIUtils.S(42),
                BackColor = Color.Transparent
            };
            var title = new Label
            {
                Text = titleText,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var sourceCaption = CreateInlineCaption("来源");
            var intervalCaption = CreateInlineCaption("间隔");

            statusValue.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            sourceValue.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            if (intervalValue is Label intervalLabel)
                intervalLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            detailValue.Font = new Font("Microsoft YaHei UI", 8.5F);
            detailValue.ForeColor = UIColors.TextSub;
            detailValue.TextAlign = ContentAlignment.MiddleLeft;
            detailValue.AutoEllipsis = false;

            row.Controls.AddRange(new Control[] { title, statusValue, sourceCaption, sourceValue, intervalCaption, intervalValue, detailValue });
            row.Layout += (_, __) =>
            {
                var layout = DataPageSourceCompactRowModel.BuildLayout(row.ClientSize, intervalValue is LiteNumberInput);
                title.Bounds = layout.TitleBounds;
                statusValue.Bounds = layout.StatusBounds;
                sourceCaption.Bounds = layout.SourceCaptionBounds;
                sourceValue.Bounds = layout.SourceBounds;
                intervalCaption.Bounds = layout.IntervalCaptionBounds;
                intervalValue.Bounds = layout.IntervalBounds;
                detailValue.Bounds = layout.DetailBounds;
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private static Label CreateInlineCaption(string text) => new Label
        {
            Text = text,
            AutoSize = false,
            Font = new Font("Microsoft YaHei UI", 8F),
            ForeColor = UIColors.TextSub,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
