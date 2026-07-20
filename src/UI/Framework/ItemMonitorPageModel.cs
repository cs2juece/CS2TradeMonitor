using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class ItemMonitorPageModel
    {
        public const int FieldName = 1 << 0;
        public const int FieldPrice = 1 << 1;
        public const int FieldChange = 1 << 2;
        public const int FieldPercent = 1 << 3;
        public const int FieldSource = 1 << 4;
        public const int FieldRefreshTime = 1 << 5;
        public const int DefaultFields = FieldName | FieldPrice;
        public const int AllFields = FieldName | FieldPrice | FieldChange | FieldPercent | FieldSource | FieldRefreshTime;

        public static string BuildItemListSignature(
            IEnumerable<ItemMonitorConfig> items,
            int defaultRefreshSec,
            double defaultRisePercent,
            double defaultFallPercent,
            int defaultWindowMinutes,
            int defaultCooldownMinutes)
        {
            string settings = string.Join("|",
                defaultRefreshSec.ToString(CultureInfo.InvariantCulture),
                defaultRisePercent.ToString("0.####", CultureInfo.InvariantCulture),
                defaultFallPercent.ToString("0.####", CultureInfo.InvariantCulture),
                defaultWindowMinutes.ToString(CultureInfo.InvariantCulture),
                defaultCooldownMinutes.ToString(CultureInfo.InvariantCulture));

            var itemParts = items
                .OrderBy(item => item.SortIndex <= 0 ? int.MaxValue : item.SortIndex)
                .ThenBy(item => item.Name)
                .Select(item => string.Join("|",
                    item.ItemId,
                    item.ItemKey,
                    item.Name,
                    item.ShortName,
                    item.Enabled,
                    item.VisibleInPanel,
                    item.VisibleInTaskbar,
                    item.RefreshIntervalSec,
                    item.DisplayFieldFlags,
                    item.PriceAlertDesktopEnabled,
                    item.PriceAlertPhoneEnabled,
                    item.PriceAlertTriggerMode,
                    item.PriceAlertAbove.ToString("0.####", CultureInfo.InvariantCulture),
                    item.PriceAlertBelow.ToString("0.####", CultureInfo.InvariantCulture),
                    item.PriceAlertRisePercent.ToString("0.####", CultureInfo.InvariantCulture),
                    item.PriceAlertFallPercent.ToString("0.####", CultureInfo.InvariantCulture),
                    item.PriceAlertWindowMinutes,
                    item.PriceAlertCooldownMinutes,
                    item.SortIndex,
                    item.TaskbarSortIndex,
                    item.LastPrice.ToString("0.####", CultureInfo.InvariantCulture),
                    item.LastChange.ToString("0.####", CultureInfo.InvariantCulture),
                    item.LastChangeRatio.ToString("0.####", CultureInfo.InvariantCulture),
                    item.HasChangeData,
                    item.LastStatus,
                    item.MarketHashName,
                    item.PlatformItemId,
                    item.LastUpdateTime));

            return settings + "\n" + string.Join("\n", itemParts);
        }

        public static List<ItemMonitorConfig> OrderItemsForDisplay(IEnumerable<ItemMonitorConfig> items)
        {
            return items
                .OrderBy(item => item.SortIndex <= 0 ? int.MaxValue : item.SortIndex)
                .ThenBy(item => item.Name)
                .ToList();
        }

        public static bool IsDuplicate(IEnumerable<ItemMonitorConfig> items, SteamDtSearchCandidate candidate)
        {
            return items.Any(item =>
                (!string.IsNullOrEmpty(candidate.MarketHashName) && string.Equals(item.MarketHashName, candidate.MarketHashName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(candidate.MarketHashName) && string.Equals(item.ItemId, candidate.MarketHashName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(candidate.PlatformItemId) && string.Equals(item.PlatformItemId, candidate.PlatformItemId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(candidate.ItemId) && string.Equals(item.ItemId, candidate.ItemId, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(item.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));
        }

        public static ItemMonitorConfig CreateCandidateConfig(
            SteamDtSearchCandidate candidate,
            int defaultRefreshSec,
            int sortIndex,
            int taskbarSortIndex,
            bool defaultVisibleInPanel = false,
            bool defaultVisibleInTaskbar = false,
            double defaultRisePercent = 0,
            double defaultFallPercent = 0,
            int defaultWindowMinutes = 10)
        {
            string id = ResolveCandidateId(candidate);
            double risePercent = ItemMonitorListCardModel.NormalizeDefaultPercent(defaultRisePercent);
            double fallPercent = ItemMonitorListCardModel.NormalizeDefaultPercent(defaultFallPercent);
            return new ItemMonitorConfig
            {
                ItemId = id,
                ItemKey = "ITEM." + id,
                Name = candidate.Name.Trim(),
                ShortName = MakeShortName(candidate.Name),
                Enabled = true,
                VisibleInPanel = defaultVisibleInPanel,
                VisibleInTaskbar = defaultVisibleInTaskbar,
                RefreshIntervalSec = defaultRefreshSec,
                DisplayFieldFlags = DefaultFields,
                SortIndex = sortIndex,
                TaskbarSortIndex = taskbarSortIndex,
                LastPrice = candidate.Price,
                LastStatus = candidate.Price > 0 ? "缓存" : "后台读取中",
                MarketHashName = candidate.MarketHashName,
                PlatformItemId = candidate.PlatformItemId,
                PriceAlertEnabled = risePercent > 0 || fallPercent > 0,
                PriceAlertDesktopEnabled = true,
                PriceAlertPhoneEnabled = false,
                PriceAlertDeliverySchemaVersion = ItemMonitorConfig.CurrentPriceAlertDeliverySchemaVersion,
                PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Percent,
                PriceAlertRisePercent = risePercent,
                PriceAlertFallPercent = fallPercent,
                PriceAlertWindowMinutes = ItemMonitorListCardModel.NormalizeDefaultWindowMinutes(defaultWindowMinutes <= 0 ? 10 : defaultWindowMinutes),
                PriceAlertCooldownMinutes = 10
            };
        }

        public static int NextSortIndex(IEnumerable<ItemMonitorConfig> items, bool taskbar)
        {
            var itemList = items as ICollection<ItemMonitorConfig> ?? items.ToList();
            if (itemList.Count == 0)
                return taskbar ? 6001 : 1;

            return taskbar
                ? itemList.Max(item => item.TaskbarSortIndex <= 0 ? 6000 : item.TaskbarSortIndex) + 1
                : itemList.Max(item => item.SortIndex) + 1;
        }

        public static List<ItemMonitorConfig> NormalizeItemIndexes(
            IEnumerable<ItemMonitorConfig> items,
            int defaultRefreshSec)
        {
            int refreshSec = Math.Max(60, defaultRefreshSec <= 0 ? 600 : defaultRefreshSec);
            List<ItemMonitorConfig> ordered = OrderItemsForDisplay(items);
            for (int i = 0; i < ordered.Count; i++)
            {
                ItemMonitorConfig item = ordered[i];
                SyncItemKey(item);
                item.RefreshIntervalSec = Math.Max(60, item.RefreshIntervalSec <= 0 ? refreshSec : item.RefreshIntervalSec);
                if (item.DisplayFieldFlags == 0)
                    item.DisplayFieldFlags = DefaultFields;
                if (item.PriceAlertWindowMinutes <= 0)
                    item.PriceAlertWindowMinutes = 10;
                if (item.PriceAlertCooldownMinutes <= 0)
                    item.PriceAlertCooldownMinutes = 10;
                if (!Enum.IsDefined(typeof(ItemPriceAlertTriggerMode), item.PriceAlertTriggerMode))
                    item.PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Auto;
                item.SortIndex = i + 1;
                if (item.TaskbarSortIndex <= 0)
                    item.TaskbarSortIndex = 6000 + i + 1;
            }

            return ordered;
        }

        public static List<ItemMonitorConfig>? MoveItem(
            IEnumerable<ItemMonitorConfig> items,
            ItemMonitorConfig item,
            int direction)
        {
            List<ItemMonitorConfig> ordered = items
                .OrderBy(x => x.SortIndex)
                .ThenBy(x => x.Name)
                .ToList();
            int index = ordered.IndexOf(item);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= ordered.Count)
                return null;

            ordered.RemoveAt(index);
            ordered.Insert(target, item);
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].SortIndex = i + 1;
                ordered[i].TaskbarSortIndex = 6000 + i + 1;
            }

            return ordered;
        }

        public static string GetCandidateDisplay(SteamDtSearchCandidate candidate)
        {
            string name = string.IsNullOrWhiteSpace(candidate.Name) ? "未命名单品" : candidate.Name.Trim();
            string price = candidate.Price > 0 ? "¥" + candidate.Price.ToString("0.##", CultureInfo.InvariantCulture) : "暂无价格";
            string source = string.IsNullOrWhiteSpace(candidate.Source) ? "来源未知" : candidate.Source.Trim();
            return $"{name}    {price} / {source}";
        }

        public static string BuildItemStatusText(ItemMonitorConfig item)
        {
            if (item.LastPrice <= 0)
            {
                return string.IsNullOrWhiteSpace(item.LastStatus)
                    ? "价格：未读取"
                    : "价格：未读取  状态：" + item.LastStatus;
            }

            var parts = new List<string>
            {
                "当前 ¥" + item.LastPrice.ToString("F2")
            };
            if (item.HasChangeData)
            {
                parts.Add(MarketDisplayFormatter.FormatSignedChange(item.LastChange));
                parts.Add(MarketDisplayFormatter.FormatSignedPercent(item.LastChangeRatio));
            }
            if (!string.IsNullOrWhiteSpace(item.LastStatus) && !item.LastStatus.Equals("成功", StringComparison.OrdinalIgnoreCase))
                parts.Add(item.LastStatus);
            if (item.LastUpdateTime > 0)
            {
                try
                {
                    parts.Add(DateTimeOffset.FromUnixTimeMilliseconds(item.LastUpdateTime).LocalDateTime.ToString("HH:mm:ss"));
                }
                catch
                {
                    // Ignore old malformed timestamps and keep the row usable.
                }
            }

            return string.Join("  ", parts);
        }

        public static string BuildCompactPriceText(ItemMonitorConfig item)
        {
            if (item.LastPrice <= 0)
                return "暂无价格 · 稍后自动重试";

            var parts = new List<string>
            {
                (IsCacheStatus(item.LastStatus) ? "缓存 ¥" : "当前 ¥") + item.LastPrice.ToString("F2", CultureInfo.InvariantCulture)
            };

            if (item.HasChangeData)
            {
                parts.Add(MarketDisplayFormatter.FormatSignedChange(item.LastChange));
                parts.Add(MarketDisplayFormatter.FormatSignedPercent(item.LastChangeRatio));
            }

            if (IsBackgroundReadingStatus(item.LastStatus))
                parts.Add("后台读取中");
            else if (!item.HasChangeData && IsCacheStatus(item.LastStatus))
                parts.Add("后台补数据");

            return string.Join("  ", parts);
        }

        public static string BuildLastRefreshShortText(ItemMonitorConfig item, DateTime? now = null)
        {
            if (IsBackgroundReadingStatus(item.LastStatus))
                return "刚刚";

            if (item.LastUpdateTime > 0)
            {
                try
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(item.LastUpdateTime).LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
                catch
                {
                    // Malformed old timestamps fall through to a safe compact placeholder.
                }
            }

            if (item.LastPrice > 0)
                return "缓存";

            return "--";
        }

        public static string BuildCompactConfigSummary(ItemMonitorConfig item)
        {
            int interval = NormalizeItemRefreshInterval(item.RefreshIntervalSec, 600);
            return $"{interval}秒 · 涨{FormatAlertNumber(item.PriceAlertRisePercent)}% · 跌{FormatAlertNumber(item.PriceAlertFallPercent)}%";
        }

        public static string BuildCompactConfigDetail(ItemMonitorConfig item)
        {
            if (item.PriceAlertDesktopEnabled && item.PriceAlertPhoneEnabled)
                return "电脑+手机提醒";
            if (item.PriceAlertDesktopEnabled)
                return "电脑提醒";
            if (item.PriceAlertPhoneEnabled)
                return "手机提醒";
            return "提醒关闭";
        }

        public static ItemPriceAlertTriggerMode ResolveTriggerMode(ItemMonitorConfig item)
        {
            if (item.PriceAlertTriggerMode == ItemPriceAlertTriggerMode.Breakthrough ||
                item.PriceAlertTriggerMode == ItemPriceAlertTriggerMode.Percent)
            {
                return item.PriceAlertTriggerMode;
            }

            return item.PriceAlertAbove > 0 || item.PriceAlertBelow > 0
                ? ItemPriceAlertTriggerMode.Breakthrough
                : ItemPriceAlertTriggerMode.Percent;
        }

        public static int NormalizeItemRefreshInterval(int value, int fallback)
        {
            int resolved = value <= 0 ? fallback : value;
            return Math.Max(60, resolved <= 0 ? 600 : resolved);
        }

        public static Color GetItemStatusColor(ItemMonitorConfig item)
        {
            if (item.LastPrice <= 0)
                return UIColors.TextSub;
            if (!item.HasChangeData)
                return UIColors.TextMain;

            return item.LastChangeRatio >= 0 ? Color.FromArgb(220, 70, 90) : Color.FromArgb(60, 150, 125);
        }

        private static bool IsCacheStatus(string? status)
        {
            return !string.IsNullOrWhiteSpace(status) &&
                   status.Contains("缓存", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBackgroundReadingStatus(string? status)
        {
            return !string.IsNullOrWhiteSpace(status) &&
                   status.Contains("后台读取", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseNonNegativeDouble(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return true;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            value = Math.Max(0, value);
            return true;
        }

        public static string FormatAlertNumber(double value)
        {
            if (value <= 0)
                return "0";

            return Math.Abs(value - Math.Round(value)) < 0.0001
                ? value.ToString("0")
                : value.ToString("0.##");
        }

        public static string MakeShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "单品";

            string text = name.Trim();
            int pipe = text.IndexOf('|');
            if (pipe >= 0 && pipe < text.Length - 1)
                text = text[(pipe + 1)..].Trim();
            int wear = text.IndexOf('(');
            if (wear > 0)
                text = text[..wear].Trim();

            return text.Length > 10 ? text[..10] : text;
        }

        public static int NormalizeDisplayFields(int flags)
        {
            int current = flags == 0 ? DefaultFields : flags;
            return (current & AllFields) == 0 ? FieldPrice : current;
        }

        private static string ResolveCandidateId(SteamDtSearchCandidate candidate)
        {
            return !string.IsNullOrWhiteSpace(candidate.MarketHashName)
                ? candidate.MarketHashName.Trim()
                : (!string.IsNullOrWhiteSpace(candidate.ItemId) ? candidate.ItemId.Trim() : candidate.Name.Trim());
        }

        public static void SyncItemKey(ItemMonitorConfig item)
        {
            if (string.IsNullOrWhiteSpace(item.ItemId))
                item.ItemId = item.Name.Trim();
            item.ItemKey = "ITEM." + item.ItemId.Trim();
            if (string.IsNullOrWhiteSpace(item.Name))
                item.Name = item.ItemId;
            if (string.IsNullOrWhiteSpace(item.ShortName))
                item.ShortName = MakeShortName(item.Name);
        }
    }

    internal sealed class CandidateListItem
    {
        private readonly string _displayText;

        public CandidateListItem(SteamDtSearchCandidate candidate, string displayText)
        {
            Candidate = candidate;
            _displayText = displayText;
        }

        public SteamDtSearchCandidate Candidate { get; }

        public override string ToString()
        {
            return _displayText;
        }
    }
}
