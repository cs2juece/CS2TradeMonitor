using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using System;
using System.Drawing;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;
namespace CS2TradeMonitor.src.Core
{
    public sealed class MarketDisplaySegments
    {
        public string Label { get; init; } = "";
        public bool HasData { get; init; }
        public string IndexText { get; init; } = "";
        public string PercentText { get; init; } = "";
        public string PlaceholderText { get; init; } = "";

        public string PrimaryText => HasData ? IndexText : PlaceholderText;
        public string SecondaryText => HasData ? PercentText : "";
        public string ValueText => HasData ? MarketDisplayFormatter.FormatValueText(IndexText, PercentText) : PlaceholderText;
    }

    public static class MarketDisplayFormatter
    {
        public const int LabelGap = 8;
        public const int ValueGap = 10;
        private static MarketDataSourceRuntimeServices? _services;
        private static MarketDataSourceRuntimeServices Services => _services ??= MarketDataSourceRuntimeServices.Resolve();
        private static ISteamDtItemService SteamDtItems => Services.SteamDtItems;

        public static bool IsMarketDisplayKey(string? key)
        {
            return MarketDataSourceManager.IsDisplayKey(key);
        }

        public static bool IsMarketKey(string? key)
        {
            return MarketDataSourceManager.IsMarketKey(key);
        }

        public static int GetMarketDisplayOrder(string key)
        {
            return MarketDataSourceManager.GetDisplayOrder(key);
        }

        public static string GetLabel(string key)
        {
            return MarketDataSourceManager.GetDisplaySnapshot(key).Label;
        }

        public static MarketDisplaySegments GetSegments(string key, Settings? settings = null, bool triggerFetch = false)
        {
            var snapshot = MarketDataSourceManager.GetDisplaySnapshot(key, triggerFetch);
            if (key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
            {
                return GetItemSegments(key, snapshot, settings);
            }

            var displayConfig = Services.AppConfigState.MarketDisplay;
            bool showPercent = settings?.SteamDtShowPercent ?? displayConfig.ShowPercent;
            int format = settings?.MarketFormat ?? displayConfig.MarketFormat;

            string idxText = snapshot.HasData ? FormatIndex(snapshot.Index) : "";
            string changeText = snapshot.HasData ? FormatSignedChange(snapshot.Change) : "";
            string pctText = (snapshot.HasData && showPercent) ? FormatSignedPercent(snapshot.Percent) : "";
            string label = snapshot.Label;

            string primary = idxText;
            string secondary = pctText;

            if (snapshot.HasData)
            {
                switch (format)
                {
                    case 1:
                        primary = idxText;
                        secondary = changeText;
                        break;
                    case 2:
                        primary = showPercent ? pctText : idxText;
                        secondary = "";
                        break;
                    case 3:
                        primary = idxText;
                        secondary = "";
                        break;
                    case 4:
                        primary = idxText;
                        secondary = showPercent ? $"{changeText}  {pctText}" : changeText;
                        break;
                    case 5:
                        label = GetShortLabel(snapshot.Key, snapshot.Label);
                        primary = idxText;
                        secondary = showPercent ? pctText : "";
                        break;
                    case 0:
                    default:
                        primary = idxText;
                        secondary = showPercent ? pctText : "";
                        break;
                }
            }

            return snapshot.HasData
                ? new MarketDisplaySegments
                {
                    Label = label,
                    HasData = true,
                    IndexText = primary,
                    PercentText = secondary
                }
                : new MarketDisplaySegments
                {
                    Label = label,
                    HasData = false,
                    PlaceholderText = snapshot.PlaceholderText
                };
        }

        private static MarketDisplaySegments GetItemSegments(string key, MarketDisplaySnapshot snapshot, Settings? settings)
        {
            string itemId = key.Length > 5 ? key.Substring(5) : "";
            string itemLookupId = itemId;
            string shortName = "";
            int flags = 0;

            if (settings != null)
            {
                var item = settings.ItemConfigs?.FirstOrDefault(x =>
                    x.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    x.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                itemLookupId = item?.ItemId ?? itemId;
                shortName = item?.ShortName ?? "";
                flags = item?.DisplayFieldFlags ?? 0;
            }
            else
            {
                ItemConfigSummary? item = Services.AppConfigState.ItemMonitor.Items.FirstOrDefault(x =>
                    x.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    x.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                itemLookupId = item?.ItemId ?? itemId;
                shortName = item?.ShortName ?? "";
                flags = item?.DisplayFieldFlags ?? 0;
            }

            if (flags == 0) flags = (1 << 0) | (1 << 1);

            string label = (flags & (1 << 0)) != 0
                ? (!string.IsNullOrWhiteSpace(shortName) ? shortName : snapshot.Label)
                : "";

            if (!snapshot.HasData)
            {
                return new MarketDisplaySegments
                {
                    Label = label,
                    HasData = false,
                    PlaceholderText = snapshot.PlaceholderText
                };
            }

            var latest = SteamDtItems.GetItemData(itemLookupId);
            var secondaryParts = new List<string>();
            string primary = "";

            if ((flags & (1 << 1)) != 0)
                primary = "¥" + FormatIndex(snapshot.Index);
            if ((flags & (1 << 2)) != 0 && snapshot.HasChangeData)
                secondaryParts.Add(FormatSignedChange(snapshot.Change));
            if ((flags & (1 << 3)) != 0 && snapshot.HasChangeData)
                secondaryParts.Add(FormatSignedPercent(snapshot.Percent));
            if ((flags & (1 << 4)) != 0 && !string.IsNullOrWhiteSpace(latest?.Source))
                secondaryParts.Add(latest.Source);
            if ((flags & (1 << 5)) != 0 && snapshot.RetrievedAt != default)
                secondaryParts.Add(snapshot.RetrievedAt.ToString("HH:mm"));

            if (string.IsNullOrWhiteSpace(primary))
            {
                primary = secondaryParts.Count > 0 ? secondaryParts[0] : "¥" + FormatIndex(snapshot.Index);
                if (secondaryParts.Count > 0) secondaryParts.RemoveAt(0);
            }

            return new MarketDisplaySegments
            {
                Label = label,
                HasData = true,
                IndexText = primary,
                PercentText = string.Join("  ", secondaryParts)
            };
        }

        public static string GetValueText(string key, Settings? settings = null, bool triggerFetch = false)
        {
            return GetSegments(key, settings, triggerFetch).ValueText;
        }

        public static string GetFullText(string key, Settings? settings = null, bool triggerFetch = false)
        {
            string label = GetLabel(key);
            string value = GetValueText(key, settings, triggerFetch);
            return string.IsNullOrEmpty(label) ? value : $"{label} {value}";
        }

        public static string GetCompactFullText(string key, Settings? settings = null, bool triggerFetch = false)
        {
            string label = GetLabel(key);
            string value = GetValueText(key, settings, triggerFetch);
            return string.IsNullOrEmpty(label) ? value : $"{label}{value}";
        }

        public static string FormatIndex(double index)
        {
            return index.ToString("F2");
        }

        public static string FormatValueText(double index, double percent)
        {
            return $"{FormatIndex(index)}  {FormatSignedPercent(percent)}";
        }

        public static string FormatValueText(string indexText, string percentText)
        {
            if (string.IsNullOrWhiteSpace(indexText)) return percentText ?? "";
            if (string.IsNullOrWhiteSpace(percentText)) return indexText ?? "";
            return $"{indexText}  {percentText}";
        }

        public static string FormatFullText(string key, double index, double percent)
        {
            string label = GetLabel(key);
            string value = FormatValueText(index, percent);
            return string.IsNullOrEmpty(label) ? value : $"{label} {value}";
        }

        public static string FormatCompactFullText(string key, double index, double percent)
        {
            string label = GetLabel(key);
            string value = FormatValueText(index, percent);
            return string.IsNullOrEmpty(label) ? value : $"{label}{value}";
        }

        public static string FormatSignedPercent(double value)
        {
            return $"{(value >= 0 ? "+" : "-")}{Math.Abs(value):F2}%";
        }

        public static string FormatSignedChange(double value)
        {
            return $"{(value >= 0 ? "+" : "-")}{Math.Abs(value):F2}";
        }

        private static string GetShortLabel(string key, string fallback)
        {
            if (key.Equals(MarketDataSourceManager.SteamDtDisplayKey, StringComparison.OrdinalIgnoreCase)) return "DT";
            if (key.Equals(MarketDataSourceManager.QaqDisplayKey, StringComparison.OrdinalIgnoreCase)) return "QAQ";
            if (key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
            {
                string itemId = key.Length > 5 ? key.Substring(5) : "";
                var itemConfig = Services.AppConfigState.ItemMonitor.Items.FirstOrDefault(x =>
                    x.ItemKey.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    x.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
                if (itemConfig != null && !string.IsNullOrEmpty(itemConfig.ShortName))
                {
                    return itemConfig.ShortName;
                }
            }
            return fallback;
        }

        public static int GetColorState(string key)
        {
            if (IsMarketDisplayKey(key))
            {
                var snapshot = MarketDataSourceManager.GetDisplaySnapshot(key);
                if (!snapshot.HasData) return MetricUtils.STATE_NEUTRAL;
                return snapshot.IsStale
                    ? MetricUtils.STATE_WARN
                    : snapshot.Percent >= 0
                        ? MetricUtils.STATE_CRIT
                        : MetricUtils.STATE_SAFE;
            }

            return MetricUtils.STATE_SAFE;
        }

        public static Color GetTextColor(string key, int colorState, Settings? settings, Theme theme)
        {
            if (!IsMarketKey(key))
            {
                return UIUtils.GetStateColor(colorState, theme, true);
            }

            Color fallback = UIUtils.GetStateColor(colorState, theme, true);
            if (settings == null) return fallback;

            string hex = colorState switch
            {
                MetricUtils.STATE_CRIT => settings.SteamDtPositiveColor,
                MetricUtils.STATE_SAFE => settings.SteamDtNegativeColor,
                MetricUtils.STATE_WARN => settings.SteamDtWarningColor,
                _ => settings.SteamDtNeutralColor
            };

            return ParseColor(hex, fallback);
        }

        private static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var color = ThemeManager.ParseColor(hex);
            return color == Color.Transparent ? fallback : color;
        }
    }
}
