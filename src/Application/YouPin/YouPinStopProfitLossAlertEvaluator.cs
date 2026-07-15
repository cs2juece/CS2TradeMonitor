using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;
using static CS2TradeMonitor.Application.YouPin.YouPinInventoryComputationHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinStopProfitLossAlertEvaluator
    {
        public static List<YouPinStopProfitLossAlert> CreateAlerts(
            YouPinInventoryHistory history,
            YouPinInventorySnapshot current,
            Settings settings)
        {
            var alerts = new List<YouPinStopProfitLossAlert>();
            if (!settings.YouPinStopProfitLossEnabled) return alerts;
            if (current.Items.Count == 0) return alerts;

            int windowMinutes = Math.Clamp(settings.YouPinStopProfitLossWindowMinutes <= 0 ? 180 : settings.YouPinStopProfitLossWindowMinutes, 5, 10080);
            double profitThreshold = Math.Max(0.01, settings.YouPinStopProfitPercentThreshold <= 0 ? 30 : settings.YouPinStopProfitPercentThreshold);
            double lossThreshold = Math.Max(0.01, settings.YouPinStopLossPercentThreshold <= 0 ? 30 : settings.YouPinStopLossPercentThreshold);
            int cooldown = Math.Clamp(settings.YouPinStopProfitLossCooldownMinutes <= 0 ? 30 : settings.YouPinStopProfitLossCooldownMinutes, 1, 1440);
            var specifiedKeywords = ParseSpecifiedItems(settings.YouPinStopProfitLossSpecifiedItems);
            var excludedKeywords = ParseSpecifiedItems(settings.YouPinStopProfitLossExcludedItems);
            var itemRules = YouPinStopProfitLossRuleStore.LoadRules(settings.YouPinStopProfitLossItemRulesJson);
            bool onlySpecified = settings.YouPinStopProfitLossOnlySpecifiedItems;
            if (onlySpecified && specifiedKeywords.Count == 0 && itemRules.Count == 0) return alerts;

            var currentGroups = YouPinStopProfitLossRuleStore.BuildCostBasisGroups(current.Items);
            var baseline = FindBaselineSnapshot(history, current.Time, windowMinutes);
            var baselineGroups = baseline == null
                ? new Dictionary<string, YouPinStopProfitLossMonitorGroup>(StringComparer.OrdinalIgnoreCase)
                : YouPinStopProfitLossRuleStore.BuildCostBasisGroups(baseline.Items)
                    .GroupBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var lastAlertTimes = history.LastStopProfitLossAlertTimes ?? new Dictionary<string, DateTime>();
            foreach (var group in currentGroups)
            {
                var anyItemRule = YouPinStopProfitLossRuleStore.FindAnyRule(group, itemRules);
                if (anyItemRule?.Enabled == false)
                    continue;

                var itemRule = anyItemRule;
                bool matchesSpecifiedKeyword = YouPinStopProfitLossRuleStore.MatchesKeywords(group, specifiedKeywords);
                if (onlySpecified && itemRule == null && !matchesSpecifiedKeyword) continue;
                if (!onlySpecified && YouPinStopProfitLossRuleStore.MatchesKeywords(group, excludedKeywords)) continue;

                double delta = group.CurrentUnitPrice - group.CostUnitPrice;
                if (Math.Abs(delta) < 0.01) continue;

                double percent = delta / group.CostUnitPrice * 100.0;
                double itemProfitThreshold = itemRule?.ProfitPercent ?? profitThreshold;
                double itemLossThreshold = itemRule?.LossPercent ?? lossThreshold;
                string direction;
                if ((itemRule?.ProfitEnabled ?? true) && percent >= itemProfitThreshold)
                    direction = "止盈";
                else if ((itemRule?.LossEnabled ?? true) && percent <= -itemLossThreshold)
                    direction = "止损";
                else
                    continue;

                double activeThreshold = direction == "止盈" ? itemProfitThreshold : itemLossThreshold;
                if (!WasConditionSustainedAtBaseline(baselineGroups, group, direction, activeThreshold))
                    continue;

                string dedupeKey = $"{direction}:{group.Key}";
                if (lastAlertTimes.TryGetValue(dedupeKey, out var last)
                    && current.Time - last < TimeSpan.FromMinutes(cooldown))
                {
                    continue;
                }

                alerts.Add(new YouPinStopProfitLossAlert
                {
                    Time = current.Time,
                    BaselineTime = baseline?.Time ?? current.Time,
                    Direction = direction,
                    Name = group.Name,
                    Quantity = group.Quantity,
                    OldUnitPrice = group.CostUnitPrice,
                    NewUnitPrice = group.CurrentUnitPrice,
                    Delta = delta,
                    Percent = percent,
                    WindowMinutes = windowMinutes,
                    DedupeKey = dedupeKey,
                    Message = $"{direction} {group.Name} {FormatSignedPercent(percent)}（成本 ¥{group.CostUnitPrice:F2} -> 现价 ¥{group.CurrentUnitPrice:F2}）"
                });
            }

            return alerts
                .OrderByDescending(x => Math.Abs(x.Percent))
                .Take(20)
                .ToList();
        }

        private static YouPinInventorySnapshot? FindBaselineSnapshot(
            YouPinInventoryHistory history,
            DateTime currentTime,
            int windowMinutes)
        {
            var target = currentTime - TimeSpan.FromMinutes(windowMinutes);
            return history.Snapshots
                .Where(snapshot => snapshot.Time <= target)
                .OrderByDescending(snapshot => snapshot.Time)
                .FirstOrDefault();
        }

        private static bool WasConditionSustainedAtBaseline(
            IReadOnlyDictionary<string, YouPinStopProfitLossMonitorGroup> baselineGroups,
            YouPinStopProfitLossMonitorGroup currentGroup,
            string direction,
            double threshold)
        {
            if (!baselineGroups.TryGetValue(currentGroup.Key, out var baselineGroup))
                return false;
            if (baselineGroup.CostUnitPrice <= 0 || baselineGroup.CurrentUnitPrice <= 0)
                return false;

            double baselinePercent = (baselineGroup.CurrentUnitPrice - baselineGroup.CostUnitPrice) / baselineGroup.CostUnitPrice * 100.0;
            return direction == "止盈"
                ? baselinePercent >= threshold
                : baselinePercent <= -threshold;
        }

        internal static List<string> ParseSpecifiedItems(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();

            return value
                .Replace('，', ',')
                .Replace('；', ',')
                .Replace('、', ',')
                .Replace(';', ',')
                .Split(new[] { ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

    }
}
