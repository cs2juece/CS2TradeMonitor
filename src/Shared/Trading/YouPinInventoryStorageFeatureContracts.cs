using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinInventoryStorageRefreshCommand(
    YouPinInventoryStorageDirection Direction,
    string StorageAssetId = "",
    bool ReloadUnits = true);

public sealed record YouPinInventoryStorageProjection(
    YouPinInventoryStorageDirection Direction,
    string StorageAssetId,
    YouPinInventoryStorageAccess Access,
    IReadOnlyList<YouPinInventoryStorageItem> Items,
    IReadOnlyList<YouPinInventoryStorageUnit> Units,
    string Message,
    string Error,
    bool WritePending,
    DateTime RefreshedAt);
