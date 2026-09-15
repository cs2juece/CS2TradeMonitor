using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Shared.Market;

public sealed record ItemPriceAlertDefaults(
    int WindowMinutes,
    int CooldownMinutes,
    double RisePercent,
    double FallPercent);

public sealed record ItemPriceAlertDecision(
    string Title,
    string Message,
    bool DesktopEnabled,
    bool PhoneEnabled);

/// <summary>
/// Portable form of the desktop SteamDtItemService price-alert policy.
/// It is the only implementation allowed to mutate item alert baselines and cooldown state.
/// </summary>
public static class ItemPriceAlertEvaluator
{
    public static ItemPriceAlertDecision? Evaluate(
        ItemMonitorConfig item,
        double price,
        DateTimeOffset now,
        ItemPriceAlertDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (price <= 0)
            return null;

        DateTime localNow = now.DateTime;
        long nowMs = now.ToUnixTimeMilliseconds();
        int defaultWindowMinutes = defaults.WindowMinutes > 0 ? defaults.WindowMinutes : 10;
        int defaultCooldownMinutes = defaults.CooldownMinutes > 0 ? defaults.CooldownMinutes : 10;
        int windowMinutes = Math.Clamp(
            item.PriceAlertWindowMinutes > 0 ? item.PriceAlertWindowMinutes : defaultWindowMinutes,
            1,
            10080);
        int cooldownMinutes = Math.Clamp(
            item.PriceAlertCooldownMinutes > 0 ? item.PriceAlertCooldownMinutes : defaultCooldownMinutes,
            1,
            1440);
        double risePercentThreshold = item.PriceAlertRisePercent > 0
            ? item.PriceAlertRisePercent
            : Math.Max(0, defaults.RisePercent);
        double fallPercentThreshold = item.PriceAlertFallPercent > 0
            ? item.PriceAlertFallPercent
            : Math.Max(0, defaults.FallPercent);

        DateTimeOffset? baselineTime = UnixMsToInstant(item.PriceAlertBaselineTime);
        bool baselineMissing = item.PriceAlertBaselinePrice <= 0 || baselineTime is null;
        bool baselineExpired = !baselineMissing && now - baselineTime!.Value > TimeSpan.FromMinutes(windowMinutes);
        if (baselineMissing || baselineExpired)
        {
            item.PriceAlertBaselinePrice = price;
            item.PriceAlertBaselineTime = nowMs;
        }

        if (!item.PriceAlertDesktopEnabled && !item.PriceAlertPhoneEnabled)
            return null;

        var reasons = new List<string>();
        ItemPriceAlertTriggerMode triggerMode = ResolveTriggerMode(item);
        if (triggerMode == ItemPriceAlertTriggerMode.Breakthrough)
        {
            if (item.PriceAlertAbove > 0 && price >= item.PriceAlertAbove)
                reasons.Add($"高于 ¥{item.PriceAlertAbove:F2}");
            if (item.PriceAlertBelow > 0 && price <= item.PriceAlertBelow)
                reasons.Add($"低于 ¥{item.PriceAlertBelow:F2}");
        }

        if (triggerMode == ItemPriceAlertTriggerMode.Percent && item.PriceAlertBaselinePrice > 0)
        {
            double percent = (price - item.PriceAlertBaselinePrice) / item.PriceAlertBaselinePrice * 100.0;
            if (risePercentThreshold > 0 && percent >= risePercentThreshold)
                reasons.Add($"{windowMinutes} 分钟内上涨 {percent:F2}%");
            if (fallPercentThreshold > 0 && percent <= -fallPercentThreshold)
                reasons.Add($"{windowMinutes} 分钟内下跌 {Math.Abs(percent):F2}%");
        }

        if (reasons.Count == 0)
            return null;

        DateTimeOffset? lastTrigger = UnixMsToInstant(item.PriceAlertLastTriggerTime);
        if (lastTrigger is not null && now - lastTrigger.Value < TimeSpan.FromMinutes(cooldownMinutes))
            return null;

        string reasonText = string.Join("；", reasons);
        item.PriceAlertLastTriggerTime = nowMs;
        item.PriceAlertLastMessage = $"{localNow:MM-dd HH:mm:ss} {reasonText}";
        item.PriceAlertBaselinePrice = price;
        item.PriceAlertBaselineTime = nowMs;

        string name = string.IsNullOrWhiteSpace(item.Name) ? item.ItemId : item.Name;
        return new ItemPriceAlertDecision(
            "单品价格提醒",
            $"{name}\n当前 ¥{price:F2}，{reasonText}",
            item.PriceAlertDesktopEnabled,
            item.PriceAlertPhoneEnabled);
    }

    public static ItemPriceAlertTriggerMode ResolveTriggerMode(ItemMonitorConfig item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.PriceAlertTriggerMode is ItemPriceAlertTriggerMode.Breakthrough or ItemPriceAlertTriggerMode.Percent)
            return item.PriceAlertTriggerMode;
        return item.PriceAlertAbove > 0 || item.PriceAlertBelow > 0
            ? ItemPriceAlertTriggerMode.Breakthrough
            : ItemPriceAlertTriggerMode.Percent;
    }

    private static DateTimeOffset? UnixMsToInstant(long unixMs)
    {
        if (unixMs <= 0)
            return null;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

public sealed record MarketAlertDispatch(string Title, string Message);

/// <summary>
/// Portable form of the desktop MarketAlertService state machine.
/// Source history, crossing rules, percentage windows, cooldowns and deferred summaries live here.
/// </summary>
public sealed class MarketAlertEvaluator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastProcessedSamples = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MarketAlertSample>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastAlertTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingMarketAlert> _pendingFullscreenAlerts = [];
    private DateTime _lastSuppressedAt = DateTime.MinValue;

    public void ApplySettings(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            EnsureRuleIds(settings);
            if (!settings.MarketAlertsEnabled)
                _pendingFullscreenAlerts.Clear();
        }
    }

    public IReadOnlyDictionary<string, DateTime> GetLastAlertTimes()
    {
        lock (_sync)
            return new Dictionary<string, DateTime>(_lastAlertTimes, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<MarketAlertDispatch> Evaluate(
        Settings settings,
        MarketSourceSnapshot steamDt,
        MarketSourceSnapshot qaq,
        bool suppress,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(steamDt);
        ArgumentNullException.ThrowIfNull(qaq);
        lock (_sync)
        {
            EnsureRuleIds(settings);
            if (!settings.MarketAlertsEnabled)
            {
                _pendingFullscreenAlerts.Clear();
                return [];
            }

            if (suppress)
                _lastSuppressedAt = now;

            var dispatches = new List<MarketAlertDispatch>();
            EvaluateSource(settings, steamDt, suppress, now, dispatches);
            EvaluateSource(settings, qaq, suppress, now, dispatches);
            FlushPendingIfReady(settings, suppress, now, dispatches);
            return dispatches;
        }
    }

    private void EvaluateSource(
        Settings settings,
        MarketSourceSnapshot snapshot,
        bool suppress,
        DateTime now,
        List<MarketAlertDispatch> dispatches)
    {
        MarketAlertRule[] rules = settings.MarketAlertRules
            .Where(rule => rule.Enabled
                && rule.Threshold > 0
                && string.Equals(rule.SourceId, snapshot.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rules.Length == 0 || !snapshot.HasData || snapshot.IsStale || snapshot.RetrievedAt == default)
            return;
        if (_lastProcessedSamples.TryGetValue(snapshot.Id, out DateTime processedAt)
            && snapshot.RetrievedAt <= processedAt)
        {
            return;
        }

        _lastProcessedSamples[snapshot.Id] = snapshot.RetrievedAt;
        List<MarketAlertSample> history = GetHistory(snapshot.Id);
        bool hasBaseline = history.Count > 0;
        var current = new MarketAlertSample(snapshot.RetrievedAt, snapshot.Index);
        history.Add(current);
        TrimHistory(history, rules);
        if (!hasBaseline)
            return;

        foreach (MarketAlertRule rule in rules)
        {
            if (!TryBuildAlert(rule, snapshot.Id, history, current, now, out MarketAlertDispatch? dispatch))
                continue;
            _lastAlertTimes[rule.Id] = now;
            Dispatch(settings, dispatch, suppress, now, dispatches);
        }
    }

    private bool TryBuildAlert(
        MarketAlertRule rule,
        string sourceId,
        List<MarketAlertSample> history,
        MarketAlertSample current,
        DateTime now,
        out MarketAlertDispatch dispatch)
    {
        dispatch = new MarketAlertDispatch("", "");
        int cooldownMinutes = Math.Clamp(rule.CooldownMinutes, 1, 1440);
        if (_lastAlertTimes.TryGetValue(rule.Id, out DateTime lastAlert)
            && now - lastAlert < TimeSpan.FromMinutes(cooldownMinutes))
        {
            return false;
        }

        string sourceLabel = string.Equals(sourceId, MarketDataSourceIds.SteamDt, StringComparison.OrdinalIgnoreCase)
            ? "SteamDT"
            : "QAQ";
        string ruleName = string.IsNullOrWhiteSpace(rule.Name) ? sourceLabel : rule.Name.Trim();
        switch (rule.RuleType)
        {
            case MarketAlertRuleType.CrossAbove:
                if (!TryGetPreviousSample(history, current, out MarketAlertSample previousAbove)
                    || previousAbove.Index >= rule.Threshold
                    || current.Index < rule.Threshold)
                    return false;
                dispatch = new MarketAlertDispatch(
                    $"大盘预警 - {sourceLabel}",
                    $"{ruleName} 突破 {rule.Threshold:F2}：当前 {current.Index:F2}");
                return true;

            case MarketAlertRuleType.CrossBelow:
                if (!TryGetPreviousSample(history, current, out MarketAlertSample previousBelow)
                    || previousBelow.Index <= rule.Threshold
                    || current.Index > rule.Threshold)
                    return false;
                dispatch = new MarketAlertDispatch(
                    $"大盘预警 - {sourceLabel}",
                    $"{ruleName} 跌破 {rule.Threshold:F2}：当前 {current.Index:F2}");
                return true;

            case MarketAlertRuleType.RiseByPercent:
                if (!TryGetWindowPercent(rule, history, current, out double risePercent)
                    || risePercent < rule.Threshold)
                    return false;
                dispatch = new MarketAlertDispatch(
                    $"大盘预警 - {sourceLabel}",
                    $"{ruleName} {rule.WindowMinutes}分钟上涨 +{risePercent:F2}%：当前 {current.Index:F2}");
                return true;

            case MarketAlertRuleType.FallByPercent:
                if (!TryGetWindowPercent(rule, history, current, out double fallPercent)
                    || fallPercent > -rule.Threshold)
                    return false;
                dispatch = new MarketAlertDispatch(
                    $"大盘预警 - {sourceLabel}",
                    $"{ruleName} {rule.WindowMinutes}分钟下跌 {fallPercent:F2}%：当前 {current.Index:F2}");
                return true;

            default:
                return false;
        }
    }

    private void Dispatch(
        Settings settings,
        MarketAlertDispatch dispatch,
        bool suppress,
        DateTime now,
        List<MarketAlertDispatch> dispatches)
    {
        if (suppress)
        {
            EnqueuePending(dispatch, now);
            return;
        }
        if (_pendingFullscreenAlerts.Count > 0)
        {
            EnqueuePending(dispatch, now);
            FlushPendingIfReady(settings, false, now, dispatches);
            return;
        }
        dispatches.Add(dispatch);
    }

    private void FlushPendingIfReady(
        Settings settings,
        bool suppress,
        DateTime now,
        List<MarketAlertDispatch> dispatches)
    {
        if (_pendingFullscreenAlerts.Count == 0 || suppress)
            return;
        if (settings.MarketAlertDeferWhenFullscreen
            && _lastSuppressedAt != DateTime.MinValue
            && now - _lastSuppressedAt < TimeSpan.FromSeconds(3))
        {
            return;
        }

        List<PendingMarketAlert> pending = _pendingFullscreenAlerts
            .GroupBy(alert => alert.Message, StringComparer.Ordinal)
            .Select(group => group.OrderBy(alert => alert.CreatedAt).Last())
            .OrderBy(alert => alert.CreatedAt)
            .TakeLast(20)
            .ToList();
        _pendingFullscreenAlerts.Clear();
        if (pending.Count == 0)
            return;

        List<string> lines = pending.Take(3).Select(alert => alert.Message).ToList();
        if (pending.Count > 3)
            lines.Add($"另有 {pending.Count - 3} 条预警");
        dispatches.Add(new MarketAlertDispatch("大盘预警汇总", string.Join(Environment.NewLine, lines)));
    }

    private void EnqueuePending(MarketAlertDispatch dispatch, DateTime now)
    {
        _pendingFullscreenAlerts.Add(new PendingMarketAlert(dispatch.Title, dispatch.Message, now));
        if (_pendingFullscreenAlerts.Count > 50)
            _pendingFullscreenAlerts.RemoveRange(0, _pendingFullscreenAlerts.Count - 50);
    }

    private List<MarketAlertSample> GetHistory(string sourceId)
    {
        if (!_history.TryGetValue(sourceId, out List<MarketAlertSample>? history))
        {
            history = [];
            _history[sourceId] = history;
        }
        return history;
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
        MarketAlertSample baseline = history
            .Where(sample => sample.Time < current.Time && sample.Time >= earliestAllowed)
            .OrderBy(sample => sample.Time)
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
            .Where(sample => sample.Time < current.Time)
            .OrderByDescending(sample => sample.Time)
            .FirstOrDefault();
        return previous.Time != default;
    }

    private static void TrimHistory(List<MarketAlertSample> history, IReadOnlyList<MarketAlertRule> rules)
    {
        int maxWindow = rules
            .Where(rule => rule.RuleType is MarketAlertRuleType.RiseByPercent or MarketAlertRuleType.FallByPercent)
            .Select(rule => Math.Clamp(rule.WindowMinutes, 1, 1440))
            .DefaultIfEmpty(1)
            .Max();
        DateTime cutoff = history[^1].Time - TimeSpan.FromMinutes(maxWindow + 1);
        DateTime latestPreviousTime = history.Count > 1 ? history[^2].Time : DateTime.MinValue;
        history.RemoveAll(sample => sample.Time < cutoff && sample.Time != latestPreviousTime);
    }

    private static void EnsureRuleIds(Settings settings)
    {
        foreach (MarketAlertRule rule in settings.MarketAlertRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                rule.Id = Guid.NewGuid().ToString("N");
        }
    }

    private readonly record struct MarketAlertSample(DateTime Time, double Index);
    private sealed record PendingMarketAlert(string Title, string Message, DateTime CreatedAt);
}
