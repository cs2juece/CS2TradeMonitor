using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.Shared.Market;

namespace CS2TradeMonitor.src.Core;

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

/// <summary>
/// Desktop adapter around the portable market-alert state machine.
/// Windows owns fullscreen detection and UI delivery; shared code owns every trading rule.
/// </summary>
public sealed class MarketAlertService : IMarketAlertService
{
    private static readonly Lazy<MarketAlertService> SharedInstance = new(() => new MarketAlertService());
    private readonly MarketAlertEvaluator _evaluator = new();

    private MarketAlertService()
    {
    }

    public static MarketAlertService Instance => SharedInstance.Value;

    public event EventHandler<MarketAlertNotificationEventArgs>? AlertRequested;

    public void ApplySettings(Settings cfg)
        => _evaluator.ApplySettings(cfg);

    public void Evaluate(Settings cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        DateTime now = DateTime.Now;
        bool suppress = cfg.MarketAlertDeferWhenFullscreen
            && FullscreenActivityDetector.ShouldSuppressNotifications();
        IReadOnlyList<MarketAlertDispatch> dispatches = _evaluator.Evaluate(
            cfg,
            ToPortableSnapshot(
                MarketDataSourceManager.SteamDtId,
                "SteamDT",
                MarketDataSourceManager.GetDisplaySnapshot(MarketDataSourceManager.SteamDtDisplayKey)),
            ToPortableSnapshot(
                MarketDataSourceManager.QaqId,
                "QAQ",
                MarketDataSourceManager.GetDisplaySnapshot(MarketDataSourceManager.QaqDisplayKey)),
            suppress,
            now);

        foreach (MarketAlertDispatch dispatch in dispatches)
            AlertRequested?.Invoke(this, new MarketAlertNotificationEventArgs(dispatch.Title, dispatch.Message));
    }

    public AlertReadinessSnapshot GetReadiness(Settings cfg)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        _evaluator.ApplySettings(cfg);
        DateTimeOffset observedAt = DateTimeOffset.Now;
        if (!cfg.MarketAlertsEnabled)
        {
            return new AlertReadinessSnapshot(
                "market-alert",
                "大盘预警",
                AlertReadinessState.Disabled,
                "总开关已关闭",
                "MarketAlerts",
                observedAt);
        }

        MarketAlertRule[] rules = cfg.MarketAlertRules
            .Where(rule => rule.Enabled && rule.Threshold > 0)
            .ToArray();
        if (rules.Length == 0)
        {
            return new AlertReadinessSnapshot(
                "market-alert",
                "大盘预警",
                AlertReadinessState.Waiting,
                "没有已启用且阈值有效的规则",
                "MarketAlerts",
                observedAt);
        }

        int waitingForData = rules
            .Select(rule => rule.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(sourceId =>
            {
                string displayKey = string.Equals(
                    sourceId,
                    MarketDataSourceManager.SteamDtId,
                    StringComparison.OrdinalIgnoreCase)
                    ? MarketDataSourceManager.SteamDtDisplayKey
                    : MarketDataSourceManager.QaqDisplayKey;
                MarketDisplaySnapshot snapshot = MarketDataSourceManager.GetDisplaySnapshot(displayKey);
                return !snapshot.HasData || snapshot.IsStale || snapshot.RetrievedAt == default;
            });
        if (waitingForData > 0)
        {
            return new AlertReadinessSnapshot(
                "market-alert",
                "大盘预警",
                AlertReadinessState.Waiting,
                $"{waitingForData} 个数据源等待有效数据",
                "MarketAlerts",
                observedAt);
        }

        DateTime now = observedAt.LocalDateTime;
        IReadOnlyDictionary<string, DateTime> lastAlertTimes = _evaluator.GetLastAlertTimes();
        int coolingDown = rules.Count(rule =>
        {
            if (!lastAlertTimes.TryGetValue(rule.Id, out DateTime lastAlert))
                return false;
            int minutes = Math.Clamp(rule.CooldownMinutes, 1, 1440);
            return now - lastAlert < TimeSpan.FromMinutes(minutes);
        });
        if (coolingDown == rules.Length)
        {
            return new AlertReadinessSnapshot(
                "market-alert",
                "大盘预警",
                AlertReadinessState.CoolingDown,
                $"{coolingDown} 条规则处于冷却期",
                "MarketAlerts",
                observedAt);
        }

        return new AlertReadinessSnapshot(
            "market-alert",
            "大盘预警",
            AlertReadinessState.Ready,
            $"{rules.Length - coolingDown} 条规则已准备 · 冷却 {coolingDown} 条",
            "MarketAlerts",
            observedAt);
    }

    private static MarketSourceSnapshot ToPortableSnapshot(
        string id,
        string displayName,
        MarketDisplaySnapshot snapshot)
        => new(
            id,
            displayName,
            snapshot.Index,
            snapshot.Change,
            snapshot.Percent,
            snapshot.RetrievedAt,
            snapshot.HasData ? "桌面数据源" : "未获取",
            snapshot.HasData && !snapshot.IsStale ? "正常" : "等待刷新",
            "",
            snapshot.HasData,
            snapshot.IsStale);
}
