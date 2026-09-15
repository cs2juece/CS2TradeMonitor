using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.Shared.Inventory;

public sealed record SaveLocalInventorySettingsCommand(
    bool Enabled,
    int RefreshMinutes,
    int MinimumChangeCount,
    bool PhoneAlertEnabled);

public sealed record SaveInventoryWatchListCommand(string WatchList);

public sealed record LocalInventoryFeatureProjection(
    bool Enabled,
    int RefreshMinutes,
    int MinimumChangeCount,
    bool PhoneAlertEnabled,
    bool HasApiToken,
    string WatchList,
    string Status,
    string Error,
    DateTimeOffset? LastRefreshAt,
    IReadOnlyList<LocalInventoryTargetSnapshot> Targets,
    IReadOnlyList<LocalInventoryChangeEvent> RecentEvents,
    DateTimeOffset CapturedAt);
