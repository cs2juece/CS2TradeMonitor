using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.Domain.InventoryMonitoring;

public sealed class LocalInventoryMonitorHistory
{
    public int Version { get; set; } = 1;
    public List<LocalInventoryStoredTarget> Targets { get; set; } = new();
    public List<LocalInventoryChangeEvent> RecentEvents { get; set; } = new();
}

public sealed class LocalInventoryStoredTarget
{
    public string SteamId { get; set; } = string.Empty;
    public int TaskId { get; set; }
    public string SteamName { get; set; } = string.Empty;
    public int TotalItemCount { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastChangedAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string Status { get; set; } = "等待首次检查";
    public string Error { get; set; } = string.Empty;
    public bool BaselineEstablished { get; set; }
    public List<string> SeenEventKeys { get; set; } = new();
}

public sealed record LocalInventoryAlertBatch(
    string Title,
    string Message,
    string DeduplicationKey);
