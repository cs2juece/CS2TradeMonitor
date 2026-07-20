using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.Core
{
    /// <summary>
    /// Owns the built-in market-alert rule identities and compatibility normalization.
    /// </summary>
    internal static class MarketAlertRuleCatalog
    {
        public static bool EnsureBuiltinRules(List<MarketAlertRule> rules)
        {
            ArgumentNullException.ThrowIfNull(rules);

            bool changed = false;
            foreach (MarketAlertRule defaultRule in Settings.CreateDefaultMarketAlertRules())
            {
                MarketAlertRule? existing = rules.FirstOrDefault(
                    rule => string.Equals(rule.Id, defaultRule.Id, StringComparison.OrdinalIgnoreCase));
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

                if (string.IsNullOrWhiteSpace(existing.SourceId))
                {
                    existing.SourceId = defaultRule.SourceId;
                    changed = true;
                }
            }

            return changed;
        }

        public static bool IsLegacyBuiltinRuleName(MarketAlertRule rule)
        {
            ArgumentNullException.ThrowIfNull(rule);

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
    }
}
