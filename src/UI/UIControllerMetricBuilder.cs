using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.SystemServices.InfoService;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor
{
    internal static class UIControllerMetricBuilder
    {
        public static List<GroupLayoutInfo> BuildGroups(Settings cfg, IInfoService infoService)
        {
            var groups = new List<GroupLayoutInfo>();

            var activeItems = YouPinInventoryTrendDisplayMetric.IncludeConfigured(cfg, cfg.MonitorItems)
                .Where(x => x.VisibleInPanel)
                .GroupBy(x => x.UIGroup)
                .OrderBy(g => g.Min(x => x.SortIndex))
                .SelectMany(g => g.OrderBy(x => x.SortIndex))
                .ToList();

            if (activeItems.Count == 0)
                return groups;

            if (UIControllerMarketDisplayConfig.IsMarketDisplayOnly(activeItems))
            {
                var marketItems = activeItems
                    .OrderBy(x => UIControllerMarketDisplayConfig.GetMarketDisplayOrder(x.Key))
                    .ThenBy(x => x.SortIndex)
                    .Select(item => CreateMetric(item, cfg, infoService))
                    .ToList();

                groups.Add(new GroupLayoutInfo("MARKET", marketItems));
                return groups;
            }

            string currentGroupKey = "";
            List<MetricItem> currentGroupList = new();

            foreach (var cfgItem in activeItems)
            {
                UIControllerMarketDisplayConfig.NormalizeMarketDisplayItem(cfgItem);
                string groupKey = cfgItem.UIGroup;

                if (groupKey != currentGroupKey && currentGroupList.Count > 0)
                {
                    groups.Add(CreateGroup(currentGroupKey, currentGroupList, cfg));
                    currentGroupList = new List<MetricItem>();
                }

                currentGroupKey = groupKey;
                currentGroupList.Add(CreateMetric(cfgItem, cfg, infoService));
            }

            if (currentGroupList.Count > 0)
                groups.Add(CreateGroup(currentGroupKey, currentGroupList, cfg));

            return groups;
        }

        public static UIControllerMetricColumns BuildColumns(Settings cfg, IInfoService infoService, int formWidth)
        {
            return new UIControllerMetricColumns(
                BuildColumnsCore(cfg, infoService, formWidth, forTaskbar: false),
                BuildColumnsCore(cfg, infoService, formWidth, forTaskbar: true));
        }

        private static GroupLayoutInfo CreateGroup(string groupKey, List<MetricItem> items, Settings cfg)
        {
            var group = new GroupLayoutInfo(groupKey, items);
            string groupName = LanguageManager.T(UIUtils.Intern("Groups." + groupKey));
            if (cfg.GroupAliases.ContainsKey(groupKey))
                groupName = cfg.GroupAliases[groupKey];
            group.Label = groupName;
            return group;
        }

        private static List<Column> BuildColumnsCore(Settings cfg, IInfoService infoService, int formWidth, bool forTaskbar)
        {
            var cols = new List<Column>();
            var query = YouPinInventoryTrendDisplayMetric.IncludeConfigured(cfg, cfg.MonitorItems)
                .Where(x => forTaskbar ? x.VisibleInTaskbar : x.VisibleInPanel);

            bool useTaskbarSort = forTaskbar || cfg.HorizontalFollowsTaskbar;
            List<MonitorItemConfig> items = query.ToList();
            bool isMarketDisplayOnly = UIControllerMarketDisplayConfig.IsMarketDisplayOnly(items);

            if (isMarketDisplayOnly)
            {
                items = items
                    .OrderBy(item => UIControllerMarketDisplayConfig.GetMarketDisplayOrder(item.Key))
                    .ThenBy(item => useTaskbarSort ? item.TaskbarSortIndex : item.SortIndex)
                    .ToList();
            }
            else if (useTaskbarSort)
            {
                items = query
                    .OrderBy(item => item.TaskbarSortIndex)
                    .ToList();
            }
            else
            {
                items = query
                    .GroupBy(x => x.UIGroup)
                    .OrderBy(g => g.Min(item => item.SortIndex))
                    .SelectMany(g => g.OrderBy(item => item.SortIndex))
                    .ToList();
            }

            items = ApplyItemMonitorRotation(items, forTaskbar, formWidth);

            bool singleLine = (forTaskbar && cfg.TaskbarSingleLine) ||
                              (!forTaskbar && cfg.HorizontalMode && cfg.HorizontalSingleLine);
            bool keepHorizontalMarketItemsSeparate = !forTaskbar && cfg.HorizontalMode && isMarketDisplayOnly;
            int step = (singleLine || keepHorizontalMarketItemsSeparate) ? 1 : 2;

            for (int i = 0; i < items.Count; i += step)
            {
                var col = new Column
                {
                    Top = CreateMetric(items[i], cfg, infoService)
                };

                if (!singleLine && i + 1 < items.Count)
                    col.Bottom = CreateMetric(items[i + 1], cfg, infoService);

                cols.Add(col);
            }

            return cols;
        }

        private static List<MonitorItemConfig> ApplyItemMonitorRotation(List<MonitorItemConfig> items, bool forTaskbar, int formWidth)
        {
            var itemMonitors = items
                .Where(x => x.Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (itemMonitors.Count <= 1)
                return items;

            int availableWidth = forTaskbar
                ? Math.Max(UIUtils.S(180), formWidth)
                : Math.Max(UIUtils.S(220), formWidth);
            int estimatedItemWidth = UIUtils.S(forTaskbar ? 150 : 190);
            int maxVisible = Math.Max(1, availableWidth / Math.Max(1, estimatedItemWidth));

            if (itemMonitors.Count <= maxVisible)
                return items;

            long slot = DateTimeOffset.Now.ToUnixTimeSeconds() / 8;
            int start = (int)(slot % itemMonitors.Count);
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < maxVisible; i++)
                selected.Add(itemMonitors[(start + i) % itemMonitors.Count].Key);

            return items
                .Where(x => !x.Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase) || selected.Contains(x.Key))
                .ToList();
        }

        private static MetricItem CreateMetric(MonitorItemConfig cfgItem, Settings cfg, IInfoService infoService)
        {
            UIControllerMarketDisplayConfig.NormalizeMarketDisplayItem(cfgItem);
            var item = new MetricItem
            {
                Key = cfgItem.Key,
                BoundConfig = cfgItem,
                RuntimeSettings = cfg,
                InfoService = infoService,
                Label = LanguageManager.T(UIUtils.Intern("Items." + cfgItem.Key)),
                ShortLabel = LanguageManager.T(UIUtils.Intern("Short." + cfgItem.Key))
            };

            if (TryUpdateTextSource(item, cfg, infoService))
            {
                item.Value = null;
            }
            else
            {
                item.Value = null;
            }

            return item;
        }

        private static bool TryUpdateTextSource(MetricItem item, Settings cfg, IInfoService infoService)
        {
            string? text = null;

            if (item.DashValueKey != null)
                text = infoService.GetValue(item.DashValueKey);
            else if (MarketDisplayFormatter.IsMarketDisplayKey(item.Key))
                text = MarketDisplayFormatter.GetValueText(item.Key, cfg);

            if (text == null)
                return false;
            if (string.Equals(item.TextValue, text, StringComparison.Ordinal))
                return false;

            item.TextValue = text;
            return true;
        }
    }

    internal sealed record UIControllerMetricColumns(List<Column> Horizontal, List<Column> Taskbar);
}
