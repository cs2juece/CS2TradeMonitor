using CS2TradeMonitor.src.Core;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    public readonly struct MarketDisplayColumnMetrics
    {
        public MarketDisplayColumnMetrics(int labelWidth, int primaryWidth, int secondaryWidth, bool hasSecondary)
        {
            LabelWidth = labelWidth;
            PrimaryWidth = primaryWidth;
            SecondaryWidth = secondaryWidth;
            HasSecondary = hasSecondary;
        }

        public int LabelWidth { get; }
        public int PrimaryWidth { get; }
        public int SecondaryWidth { get; }
        public bool HasSecondary { get; }

        public int TotalWidth =>
            LabelWidth
            + MarketDisplayFormatter.LabelGap
            + PrimaryWidth
            + (HasSecondary ? MarketDisplayFormatter.ValueGap + SecondaryWidth : 0);
    }

    public static class MarketDisplayRenderMetrics
    {
        private static readonly string[] MarketKeys =
        {
            MarketDataSourceManager.QaqDisplayKey,
            MarketDataSourceManager.SteamDtDisplayKey
        };

        public static MarketDisplayColumnMetrics Measure(Graphics g, Settings? settings, Font labelFont, Font valueFont, bool triggerFetch = false)
        {
            int labelWidth = 0;
            int primaryWidth = 0;
            int secondaryWidth = 0;
            bool hasSecondary = false;

            var keys = new System.Collections.Generic.List<string>(MarketKeys);
            if (settings?.MonitorItems != null)
            {
                foreach (var item in settings.MonitorItems)
                {
                    if (item.Key != null && item.Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
                    {
                        if (item.VisibleInPanel || item.VisibleInTaskbar)
                        {
                            keys.Add(item.Key);
                        }
                    }
                }
            }

            foreach (string key in keys)
            {
                var segments = MarketDisplayFormatter.GetSegments(key, settings, triggerFetch);
                labelWidth = Math.Max(labelWidth, MeasureTextWidth(g, segments.Label, labelFont));
                primaryWidth = Math.Max(primaryWidth, MeasureTextWidth(g, segments.PrimaryText, valueFont));

                if (segments.HasData && !string.IsNullOrWhiteSpace(segments.SecondaryText))
                {
                    hasSecondary = true;
                    secondaryWidth = Math.Max(secondaryWidth, MeasureTextWidth(g, segments.SecondaryText, valueFont));
                }
            }

            return new MarketDisplayColumnMetrics(labelWidth, primaryWidth, secondaryWidth, hasSecondary);
        }

        private static int MeasureTextWidth(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            return TextRenderer.MeasureText(
                g,
                text,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        }
    }
}
