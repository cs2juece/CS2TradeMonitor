using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelSettingsPageModel
    {
        public static ItemMonitorConfig? GetPrimaryItem(IEnumerable<ItemMonitorConfig> items)
        {
            ArgumentNullException.ThrowIfNull(items);

            return items
                .OrderBy(item => item.SortIndex <= 0 ? int.MaxValue : item.SortIndex)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public static string BuildItemTitle(ItemMonitorConfig? item)
        {
            if (item is null)
                return "暂无监控单品";

            if (!string.IsNullOrWhiteSpace(item.Name))
                return item.Name;

            if (!string.IsNullOrWhiteSpace(item.MarketHashName))
                return item.MarketHashName;

            return string.IsNullOrWhiteSpace(item.ItemId) ? "未命名单品" : item.ItemId;
        }

        public static string BuildItemStatus(ItemMonitorConfig? item)
        {
            if (item is null)
                return "请到左侧“单品监控”页面先添加饰品";

            if (item.LastPrice <= 0)
                return string.IsNullOrWhiteSpace(item.LastStatus) ? "暂无价格数据" : item.LastStatus;

            string text = $"当前 ¥{item.LastPrice:0.##}";
            if (Math.Abs(item.LastChange) > 0.000001)
                text += $"  {item.LastChange:+0.##;-0.##;0}";
            if (item.HasChangeData)
                text += $"  {item.LastChangeRatio:+0.##;-0.##;0}%";
            if (item.LastUpdateTime > 0)
                text += "  " + DateTimeOffset.FromUnixTimeMilliseconds(item.LastUpdateTime)
                    .LocalDateTime
                    .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return text;
        }

        public static Color ResolveItemStatusColor(ItemMonitorConfig? item)
        {
            if (item is null || item.LastPrice <= 0)
                return UIColors.TextWarn;

            if (!item.HasChangeData)
                return UIColors.TextSub;

            return item.LastChangeRatio >= 0 ? Color.FromArgb(220, 70, 90) : Color.FromArgb(80, 220, 180);
        }

        public static bool IsItemFieldEnabled(ItemMonitorConfig? item, int flag)
        {
            if (item is null)
                return false;

            int flags = item.DisplayFieldFlags == 0 ? ItemMonitorDisplayFields.Default : item.DisplayFieldFlags;
            return (flags & flag) != 0;
        }

        public static int ToggleItemFieldFlags(int currentFlags, int flag)
        {
            int flags = currentFlags == 0 ? ItemMonitorDisplayFields.Default : currentFlags;
            if ((flags & flag) != 0)
                flags &= ~flag;
            else
                flags |= flag;

            return ItemMonitorDisplayFields.Normalize(flags);
        }

        public static string BuildInventoryPreviewText(int displayMode, int signMode)
        {
            bool showSign = signMode == 0;
            string amount = showSign ? "+¥128.40" : "¥128.40";
            string percent = showSign ? "+2.36%" : "2.36%";
            return displayMode switch
            {
                1 => amount,
                2 => percent,
                _ => amount + " " + percent
            };
        }

        public static string FormatColorHtml(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static string NormalizeColorHtml(string? value, string fallback)
        {
            string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value;
            if (string.IsNullOrWhiteSpace(candidate))
                return string.Empty;

            try
            {
                return FormatColorHtml(ColorTranslator.FromHtml(candidate));
            }
            catch
            {
                if (string.Equals(candidate, fallback, StringComparison.Ordinal))
                    return string.Empty;

                return NormalizeColorHtml(fallback, string.Empty);
            }
        }

        public static int ResolveTaskbarPresetSelection(
            int previousSelection,
            float fontSize,
            bool fontBold,
            int itemSpacing,
            int innerSpacing,
            int verticalPadding,
            bool singleLine,
            bool customStyle,
            string backgroundColor,
            string labelColor,
            string safeColor,
            string warnColor,
            string criticalColor)
        {
            if (MatchesPreset(fontSize, fontBold, itemSpacing, innerSpacing, verticalPadding, 9f, false, 4, 4, Settings.DEFAULT_TB_VOFF)
                && singleLine)
            {
                return 1;
            }

            if (MatchesPreset(fontSize, fontBold, itemSpacing, innerSpacing, verticalPadding, 12f, true, 6, 8, 2)
                && customStyle
                && ColorEquals(backgroundColor, "#001E3D")
                && ColorEquals(labelColor, "#FFFFFF")
                && ColorEquals(safeColor, "#00CC66")
                && ColorEquals(warnColor, "#FFFF00")
                && ColorEquals(criticalColor, "#FF4444"))
            {
                return 2;
            }

            if (MatchesPreset(fontSize, fontBold, itemSpacing, innerSpacing, verticalPadding, 11f, true, 6, 8, 2)
                && customStyle
                && ColorEquals(backgroundColor, "#001E3D")
                && ColorEquals(labelColor, "#FFD700")
                && ColorEquals(safeColor, "#00FFCC")
                && ColorEquals(warnColor, "#FFFF00")
                && ColorEquals(criticalColor, "#FF4444"))
            {
                return 3;
            }

            if (MatchesPreset(
                fontSize,
                fontBold,
                itemSpacing,
                innerSpacing,
                verticalPadding,
                Settings.DEFAULT_TB_SIZE_BOLD,
                true,
                Settings.DEFAULT_TB_GAP,
                Settings.DEFAULT_TB_INNER_BOLD,
                Settings.DEFAULT_TB_VOFF))
            {
                return 0;
            }

            return Math.Clamp(previousSelection, 0, 3);
        }

        public static object NormalizeCompactValue(string key, string text, string fallback)
        {
            if (key is nameof(Settings.DefaultItemPriceAlertRisePercent) or nameof(Settings.DefaultItemPriceAlertFallPercent))
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    value = TryParseDouble(fallback, 0d);

                return ItemMonitorListCardModel.NormalizeDefaultPercent(value);
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                intValue = TryParseInt(fallback, 0);

            return key switch
            {
                nameof(Settings.DefaultItemRefreshIntervalSec) => ItemMonitorListCardModel.NormalizeDefaultInterval(intValue),
                nameof(Settings.DefaultItemPriceAlertWindowMinutes) => ItemMonitorListCardModel.NormalizeDefaultWindowMinutes(intValue),
                nameof(Settings.DefaultItemPriceAlertCooldownMinutes) => ItemMonitorListCardModel.NormalizeDefaultCooldownMinutes(intValue),
                _ => intValue
            };
        }

        private static int TryParseInt(string text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static double TryParseDouble(string text, double fallback)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
        }

        private static bool MatchesPreset(
            float fontSize,
            bool fontBold,
            int itemSpacing,
            int innerSpacing,
            int verticalPadding,
            float expectedFontSize,
            bool expectedFontBold,
            int expectedItemSpacing,
            int expectedInnerSpacing,
            int expectedVerticalPadding)
        {
            return Math.Abs(fontSize - expectedFontSize) < 0.05f
                && fontBold == expectedFontBold
                && itemSpacing == expectedItemSpacing
                && innerSpacing == expectedInnerSpacing
                && verticalPadding == expectedVerticalPadding;
        }

        private static bool ColorEquals(string value, string expected)
        {
            return string.Equals(
                NormalizeColorHtml(value, expected),
                NormalizeColorHtml(expected, string.Empty),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
