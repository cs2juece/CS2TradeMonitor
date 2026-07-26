using CS2TradeMonitor.src.Core.Modules;

namespace CS2TradeMonitor.Application.Monitoring
{
    public static class AlertHistorySources
    {
        public const string Market = "大盘预警";
        public const string Item = "单品监控";
        public const string Inventory = "悠悠库存";
        public const string InventoryStopProfitLoss = "库存止盈止损";
        public const string Cs2Update = "CS2 更新";

        public static bool IsAppNotificationSource(string? source)
        {
            return source is Item or Inventory or InventoryStopProfitLoss;
        }
    }

    public enum AlertReadinessState
    {
        Disabled,
        Waiting,
        Ready,
        CoolingDown,
        Faulted
    }

    public sealed record AlertReadinessSnapshot(
        string Id,
        string DisplayName,
        AlertReadinessState State,
        string Message,
        string PageKey,
        DateTimeOffset ObservedAt);

    public enum AlertDeliveryStatus
    {
        Succeeded,
        Skipped,
        Failed
    }

    public sealed record AlertHistoryEntry
    {
        public string Id { get; init; } = string.Empty;
        public DateTimeOffset OccurredAt { get; init; }
        public string Source { get; init; } = string.Empty;
        public string EventType { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;
        public AlertDeliveryStatus Status { get; init; }
        public string Detail { get; init; } = string.Empty;
    }

    public sealed record MonitoringConsoleSnapshot(
        string OverallText,
        bool IsHealthy,
        IReadOnlyList<MonitorModuleHealth> Modules,
        IReadOnlyList<AlertReadinessSnapshot> AlertReadiness,
        IReadOnlyList<AlertHistoryEntry> RecentAlerts,
        IReadOnlyList<ConsoleUpdateSnapshot> RecentUpdates,
        DateTimeOffset CapturedAt);

    public sealed record ConsoleUpdateSnapshot(
        string Source,
        string Title,
        string Summary,
        long PublishedAt);
}
