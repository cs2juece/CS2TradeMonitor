using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinInventoryComputationHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinStopProfitLossItemRule
    {
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool ProfitEnabled { get; set; } = true;
        public bool LossEnabled { get; set; } = true;
        public double? ProfitPercent { get; set; }
        public double? LossPercent { get; set; }
    }

    internal sealed class YouPinStopProfitLossMonitorGroup
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public int Quantity { get; set; }
        public int MissingPurchaseCount { get; set; }
        public double CostUnitPrice { get; set; }
        public double CurrentUnitPrice { get; set; }
    }

    internal static class YouPinStopProfitLossRuleStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static List<YouPinStopProfitLossItemRule> LoadRules(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<YouPinStopProfitLossItemRule>();

            try
            {
                var rules = JsonSerializer.Deserialize<List<YouPinStopProfitLossItemRule>>(json, JsonOptions);
                if (rules == null)
                    return new List<YouPinStopProfitLossItemRule>();

                return rules
                    .Select(NormalizeRule)
                    .Where(rule => !string.IsNullOrWhiteSpace(rule.Name) || !string.IsNullOrWhiteSpace(rule.TemplateId))
                    .GroupBy(rule => BuildRuleIdentity(rule), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch
            {
                return new List<YouPinStopProfitLossItemRule>();
            }
        }

        public static string SaveRules(IEnumerable<YouPinStopProfitLossItemRule> rules)
        {
            var normalized = (rules ?? Enumerable.Empty<YouPinStopProfitLossItemRule>())
                .Select(NormalizeRule)
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Name) || !string.IsNullOrWhiteSpace(rule.TemplateId))
                .GroupBy(rule => BuildRuleIdentity(rule), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            return normalized.Count == 0
                ? ""
                : JsonSerializer.Serialize(normalized, JsonOptions);
        }

        public static List<YouPinStopProfitLossMonitorGroup> BuildCostBasisGroups(IEnumerable<YouPinInventoryItem> items)
        {
            if (items == null)
                return new List<YouPinStopProfitLossMonitorGroup>();

            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                    || !string.IsNullOrWhiteSpace(item.TemplateId)
                    || !string.IsNullOrWhiteSpace(item.AssetId))
                .GroupBy(TrendKey, StringComparer.OrdinalIgnoreCase)
                .Select(BuildCostBasisGroup)
                .Where(group => group.Quantity > 0 && group.CostUnitPrice > 0 && group.CurrentUnitPrice > 0)
                .ToList();
        }

        public static bool MatchesKeywords(
            YouPinStopProfitLossMonitorGroup group,
            IReadOnlyList<string> keywords)
        {
            if (group == null || keywords == null || keywords.Count == 0)
                return false;

            return keywords.Any(keyword => MatchesKeyword(group, keyword));
        }

        public static YouPinStopProfitLossItemRule? FindRule(
            YouPinStopProfitLossMonitorGroup group,
            IReadOnlyList<YouPinStopProfitLossItemRule> rules)
        {
            var rule = FindAnyRule(group, rules);
            return rule?.Enabled == true ? rule : null;
        }

        public static YouPinStopProfitLossItemRule? FindAnyRule(
            YouPinStopProfitLossMonitorGroup group,
            IReadOnlyList<YouPinStopProfitLossItemRule> rules)
        {
            if (group == null || rules == null || rules.Count == 0)
                return null;

            return rules.FirstOrDefault(rule => MatchesRule(group, rule));
        }

        private static YouPinStopProfitLossMonitorGroup BuildCostBasisGroup(IGrouping<string, YouPinInventoryItem> group)
        {
            var allItems = group.ToList();
            var pricedWithCost = allItems
                .Where(item => item.Price > 0 && item.PurchasePrice > 0)
                .ToList();
            int quantity = pricedWithCost.Sum(item => Math.Max(1, item.Quantity));
            int missingPurchase = allItems
                .Where(item => item.PurchasePrice <= 0)
                .Sum(item => Math.Max(1, item.Quantity));
            var first = allItems.FirstOrDefault();

            return new YouPinStopProfitLossMonitorGroup
            {
                Key = group.Key,
                Name = first?.Name ?? group.Key,
                TemplateId = first?.TemplateId ?? "",
                Quantity = quantity,
                MissingPurchaseCount = missingPurchase,
                CostUnitPrice = WeightedAverage(pricedWithCost, item => item.PurchasePrice),
                CurrentUnitPrice = WeightedAverage(pricedWithCost, item => item.Price)
            };
        }

        private static double WeightedAverage(List<YouPinInventoryItem> items, Func<YouPinInventoryItem, double> selector)
        {
            int quantity = items.Sum(item => Math.Max(1, item.Quantity));
            return quantity > 0
                ? items.Sum(item => selector(item) * Math.Max(1, item.Quantity)) / quantity
                : 0;
        }

        private static YouPinStopProfitLossItemRule NormalizeRule(YouPinStopProfitLossItemRule? rule)
        {
            rule ??= new YouPinStopProfitLossItemRule();
            rule.Name = (rule.Name ?? string.Empty).Trim();
            rule.TemplateId = (rule.TemplateId ?? string.Empty).Trim();
            rule.ProfitPercent = NormalizePercentOverride(rule.ProfitPercent);
            rule.LossPercent = NormalizePercentOverride(rule.LossPercent);
            return rule;
        }

        private static double? NormalizePercentOverride(double? value)
        {
            if (!value.HasValue || value.Value <= 0)
                return null;

            return Math.Clamp(value.Value, 0.01, 1000);
        }

        private static string BuildRuleIdentity(YouPinStopProfitLossItemRule rule)
        {
            return !string.IsNullOrWhiteSpace(rule.TemplateId)
                ? "T:" + rule.TemplateId
                : "N:" + rule.Name;
        }

        private static bool MatchesRule(YouPinStopProfitLossMonitorGroup group, YouPinStopProfitLossItemRule rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.TemplateId)
                && string.Equals(group.TemplateId, rule.TemplateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(rule.Name) && MatchesKeyword(group, rule.Name);
        }

        private static bool MatchesKeyword(YouPinStopProfitLossMonitorGroup group, string keyword)
        {
            keyword = (keyword ?? string.Empty).Trim();
            if (keyword.Length == 0)
                return false;

            return group.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || group.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || group.TemplateId.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }
}
