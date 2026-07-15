using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly struct RuleRowSpec
    {
        public RuleRowSpec(string sourceId, MarketAlertRuleType ruleType, string title, string unit, string placeholder, string hint)
        {
            SourceId = sourceId;
            RuleType = ruleType;
            Title = title;
            Unit = unit;
            Placeholder = placeholder;
            Hint = hint;
        }

        public string SourceId { get; }
        public MarketAlertRuleType RuleType { get; }
        public string Title { get; }
        public string Unit { get; }
        public string Placeholder { get; }
        public string Hint { get; }
    }

    internal static class MarketAlertPageModel
    {
        public static RuleRowSpec[] CreateSourceRuleSpecs(string sourceId)
        {
            return new[]
            {
                new RuleRowSpec(sourceId, MarketAlertRuleType.CrossAbove, "突破点位", "点", "输入点位", "指数从低于该点位上涨并穿过时提醒。"),
                new RuleRowSpec(sourceId, MarketAlertRuleType.CrossBelow, "跌破点位", "点", "输入点位", "指数从高于该点位下跌并穿过时提醒。"),
                new RuleRowSpec(sourceId, MarketAlertRuleType.RiseByPercent, "规定时间内上涨百分比报警", "%", "3", "在右侧分钟数内，上涨达到右侧百分比时报警。"),
                new RuleRowSpec(sourceId, MarketAlertRuleType.FallByPercent, "规定时间内下跌百分比报警", "%", "3", "在右侧分钟数内，下跌达到右侧百分比时报警。")
            };
        }

        public static string GetRuleHint(MarketAlertRuleType ruleType, int windowMinutes)
        {
            int minutes = Math.Clamp(windowMinutes, 1, 1440);
            return ruleType switch
            {
                MarketAlertRuleType.RiseByPercent => $"在 {minutes} 分钟内，上涨达到右侧百分比时报警。",
                MarketAlertRuleType.FallByPercent => $"在 {minutes} 分钟内，下跌达到右侧百分比时报警。",
                MarketAlertRuleType.CrossAbove => "指数从低于该点位上涨并穿过时提醒。",
                MarketAlertRuleType.CrossBelow => "指数从高于该点位下跌并穿过时提醒。",
                _ => ""
            };
        }

        public static bool IsBuiltinRule(MarketAlertRule rule)
        {
            return rule.Id.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase);
        }

        public static bool EnsureBuiltinRules(List<MarketAlertRule> rules, IEnumerable<MarketAlertRule> defaultRules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            ArgumentNullException.ThrowIfNull(defaultRules);

            bool changed = false;
            foreach (MarketAlertRule defaultRule in defaultRules)
            {
                MarketAlertRule? existing = rules.FirstOrDefault(rule => string.Equals(rule.Id, defaultRule.Id, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    rules.Add(defaultRule);
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.Name) || IsLegacyBuiltinRuleName(existing))
                {
                    existing.Name = defaultRule.Name;
                    changed = true;
                }
            }

            return changed;
        }

        public static bool IsLegacyBuiltinRuleName(MarketAlertRule rule)
        {
            if (rule.RuleType != MarketAlertRuleType.RiseByPercent
                && rule.RuleType != MarketAlertRuleType.FallByPercent)
            {
                return false;
            }

            string source = string.Equals(rule.SourceId, MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase)
                ? "SteamDT"
                : "QAQ";
            string legacyName = rule.RuleType == MarketAlertRuleType.RiseByPercent
                ? $"{source} 指定时间内上涨"
                : $"{source} 指定时间内下跌";

            return string.Equals(rule.Name?.Trim(), legacyName, StringComparison.Ordinal);
        }

        public static string FormatThreshold(MarketAlertRule rule)
        {
            return rule.Threshold <= 0 ? "" : rule.Threshold.ToString("0.##");
        }
    }
}
