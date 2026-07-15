using System;
using System.Collections.Generic;
using System.Linq;
using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.src.Core
{
    public sealed class MarketAlertNotificationEventArgs : EventArgs
    {
        public MarketAlertNotificationEventArgs(string title, string message)
        {
            Title = title;
            Message = message;
        }

        public string Title { get; }
        public string Message { get; }
    }

    public sealed class MarketAlertService : IMarketAlertService
    {
        private static readonly Lazy<MarketAlertService> _instance = new(() => new MarketAlertService());
        public static MarketAlertService Instance => _instance.Value;

        private readonly Dictionary<string, DateTime> _lastProcessedSamples = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<MarketAlertSample>> _history = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastAlertTimes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<MarketAlertMessage> _pendingFullscreenAlerts = new();
        private DateTime _lastSuppressedAt = DateTime.MinValue;

        private MarketAlertService() { }

        public event EventHandler<MarketAlertNotificationEventArgs>? AlertRequested;

        public void ApplySettings(Settings cfg)
        {
            EnsureRuleIds(cfg);
            if (!cfg.MarketAlertsEnabled)
            {
                _pendingFullscreenAlerts.Clear();
            }
        }

        public void Evaluate(Settings cfg)
        {
            EnsureRuleIds(cfg);

            if (!cfg.MarketAlertsEnabled)
            {
                _pendingFullscreenAlerts.Clear();
                return;
            }

            var now = DateTime.Now;
            bool suppress = cfg.MarketAlertDeferWhenFullscreen && FullscreenActivityDetector.ShouldSuppressNotifications();
            if (suppress)
            {
                _lastSuppressedAt = now;
            }

            EvaluateSource(cfg, MarketDataSourceManager.QaqId, MarketDataSourceManager.QaqDisplayKey, suppress, now);
            EvaluateSource(cfg, MarketDataSourceManager.SteamDtId, MarketDataSourceManager.SteamDtDisplayKey, suppress, now);

            FlushPendingIfReady(cfg, suppress, now);
        }

        private void EvaluateSource(Settings cfg, string sourceId, string displayKey, bool suppress, DateTime now)
        {
            var rules = cfg.MarketAlertRules
                .Where(r => r.Enabled
                    && r.Threshold > 0
                    && string.Equals(r.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (rules.Count == 0)
                return;

            var snapshot = MarketDataSourceManager.GetDisplaySnapshot(displayKey);
            if (!snapshot.HasData || snapshot.IsStale || snapshot.RetrievedAt == default)
                return;

            if (_lastProcessedSamples.TryGetValue(sourceId, out var processedAt)
                && snapshot.RetrievedAt <= processedAt)
            {
                return;
            }

            _lastProcessedSamples[sourceId] = snapshot.RetrievedAt;
            var history = GetHistory(sourceId);
            bool hasBaseline = history.Count > 0;

            var current = new MarketAlertSample(snapshot.RetrievedAt, snapshot.Index);
            history.Add(current);
            TrimHistory(history, rules);

            if (!hasBaseline)
                return;

            foreach (var rule in rules)
            {
                if (!TryBuildAlert(rule, sourceId, history, current, now, out var message))
                    continue;

                _lastAlertTimes[rule.Id] = now;
                Dispatch(cfg, message, suppress, now);
            }
        }

        private bool TryBuildAlert(
            MarketAlertRule rule,
            string sourceId,
            List<MarketAlertSample> history,
            MarketAlertSample current,
            DateTime now,
            out MarketAlertMessage message)
        {
            message = default;

            int cooldownMinutes = Math.Clamp(rule.CooldownMinutes, 1, 1440);
            if (_lastAlertTimes.TryGetValue(rule.Id, out var lastAlert)
                && now - lastAlert < TimeSpan.FromMinutes(cooldownMinutes))
            {
                return false;
            }

            string sourceLabel = GetSourceLabel(sourceId);
            string ruleName = string.IsNullOrWhiteSpace(rule.Name) ? sourceLabel : rule.Name.Trim();

            switch (rule.RuleType)
            {
                case MarketAlertRuleType.CrossAbove:
                    if (!TryGetPreviousSample(history, current, out var previousAbove)
                        || previousAbove.Index >= rule.Threshold
                        || current.Index < rule.Threshold)
                    {
                        return false;
                    }

                    message = new MarketAlertMessage(
                        $"大盘预警 - {sourceLabel}",
                        $"{ruleName} 突破 {rule.Threshold:F2}：当前 {current.Index:F2}");
                    return true;

                case MarketAlertRuleType.CrossBelow:
                    if (!TryGetPreviousSample(history, current, out var previousBelow)
                        || previousBelow.Index <= rule.Threshold
                        || current.Index > rule.Threshold)
                    {
                        return false;
                    }

                    message = new MarketAlertMessage(
                        $"大盘预警 - {sourceLabel}",
                        $"{ruleName} 跌破 {rule.Threshold:F2}：当前 {current.Index:F2}");
                    return true;

                case MarketAlertRuleType.RiseByPercent:
                    if (!TryGetWindowPercent(rule, history, current, out double risePercent) || risePercent < rule.Threshold)
                        return false;
                    message = new MarketAlertMessage(
                        $"大盘预警 - {sourceLabel}",
                        $"{ruleName} {rule.WindowMinutes}分钟上涨 +{risePercent:F2}%：当前 {current.Index:F2}");
                    return true;

                case MarketAlertRuleType.FallByPercent:
                    if (!TryGetWindowPercent(rule, history, current, out double fallPercent) || fallPercent > -rule.Threshold)
                        return false;
                    message = new MarketAlertMessage(
                        $"大盘预警 - {sourceLabel}",
                        $"{ruleName} {rule.WindowMinutes}分钟下跌 {fallPercent:F2}%：当前 {current.Index:F2}");
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryGetWindowPercent(
            MarketAlertRule rule,
            List<MarketAlertSample> history,
            MarketAlertSample current,
            out double percent)
        {
            percent = 0;
            int windowMinutes = Math.Clamp(rule.WindowMinutes, 1, 1440);
            DateTime earliestAllowed = current.Time - TimeSpan.FromMinutes(windowMinutes);
            var baseline = history
                .Where(s => s.Time < current.Time && s.Time >= earliestAllowed)
                .OrderBy(s => s.Time)
                .FirstOrDefault();

            if (baseline.Time == default || Math.Abs(baseline.Index) < 0.000001)
                return false;

            percent = (current.Index - baseline.Index) / baseline.Index * 100.0;
            return true;
        }

        private static bool TryGetPreviousSample(
            List<MarketAlertSample> history,
            MarketAlertSample current,
            out MarketAlertSample previous)
        {
            previous = history
                .Where(s => s.Time < current.Time)
                .OrderByDescending(s => s.Time)
                .FirstOrDefault();

            return previous.Time != default;
        }

        private void Dispatch(Settings cfg, MarketAlertMessage message, bool suppress, DateTime now)
        {
            if (suppress)
            {
                EnqueuePending(message, now);
                return;
            }

            if (_pendingFullscreenAlerts.Count > 0)
            {
                EnqueuePending(message, now);
                FlushPendingIfReady(cfg, suppress: false, now);
                return;
            }

            AlertRequested?.Invoke(this, new MarketAlertNotificationEventArgs(message.Title, message.Text));
        }

        private void FlushPendingIfReady(Settings cfg, bool suppress, DateTime now)
        {
            if (_pendingFullscreenAlerts.Count == 0 || suppress)
                return;

            if (cfg.MarketAlertDeferWhenFullscreen
                && _lastSuppressedAt != DateTime.MinValue
                && now - _lastSuppressedAt < TimeSpan.FromSeconds(3))
            {
                return;
            }

            var pending = _pendingFullscreenAlerts
                .GroupBy(x => x.Text)
                .Select(g => g.OrderBy(x => x.CreatedAt).Last())
                .OrderBy(x => x.CreatedAt)
                .TakeLast(20)
                .ToList();

            _pendingFullscreenAlerts.Clear();
            if (pending.Count == 0)
                return;

            var lines = pending.Take(3).Select(x => x.Text).ToList();
            if (pending.Count > 3)
                lines.Add($"另有 {pending.Count - 3} 条预警");

            AlertRequested?.Invoke(this, new MarketAlertNotificationEventArgs(
                "大盘预警汇总",
                string.Join(Environment.NewLine, lines)));
        }

        private void EnqueuePending(MarketAlertMessage message, DateTime now)
        {
            _pendingFullscreenAlerts.Add(message with { CreatedAt = now });
            if (_pendingFullscreenAlerts.Count > 50)
                _pendingFullscreenAlerts.RemoveRange(0, _pendingFullscreenAlerts.Count - 50);
        }

        private List<MarketAlertSample> GetHistory(string sourceId)
        {
            if (!_history.TryGetValue(sourceId, out var history))
            {
                history = new List<MarketAlertSample>();
                _history[sourceId] = history;
            }

            return history;
        }

        private static void TrimHistory(List<MarketAlertSample> history, IReadOnlyList<MarketAlertRule> rules)
        {
            int maxWindow = rules
                .Where(r => r.RuleType == MarketAlertRuleType.RiseByPercent || r.RuleType == MarketAlertRuleType.FallByPercent)
                .Select(r => Math.Clamp(r.WindowMinutes, 1, 1440))
                .DefaultIfEmpty(1)
                .Max();

            DateTime cutoff = history[^1].Time - TimeSpan.FromMinutes(maxWindow + 1);
            history.RemoveAll(s => s.Time < cutoff);
        }

        private static void EnsureRuleIds(Settings cfg)
        {
            foreach (var rule in cfg.MarketAlertRules)
            {
                if (string.IsNullOrWhiteSpace(rule.Id))
                    rule.Id = Guid.NewGuid().ToString("N");
            }
        }

        private static string GetSourceLabel(string sourceId)
        {
            if (string.Equals(sourceId, MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase))
                return "SteamDT";

            return "QAQ";
        }

        private readonly struct MarketAlertSample
        {
            public MarketAlertSample(DateTime time, double index)
            {
                Time = time;
                Index = index;
            }

            public DateTime Time { get; }
            public double Index { get; }
        }

        private readonly record struct MarketAlertMessage(string Title, string Text)
        {
            public DateTime CreatedAt { get; init; }
        }
    }
}
