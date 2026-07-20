using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Domain.YouPin
{
    public enum YouPinInventoryStorageView
    {
        Storable,
        StoredUnits,
        StoredItems
    }

    public enum YouPinInventoryStorageDirection
    {
        Store,
        TakeOut
    }

    public enum YouPinInventoryStorageTransferStatus
    {
        Rejected,
        Confirmed,
        AcceptedPending
    }

    public sealed record YouPinInventoryStorageQuery(
        YouPinInventoryStorageView View,
        string StorageAssetId = "",
        int RequestedCount = 0);

    public sealed record YouPinInventoryStorageAccess(
        bool IsBusy,
        bool CanStore,
        bool CanTakeOut,
        int StorableCount,
        int StoredCount,
        int TakeOutCount,
        string StoreMessage,
        string TakeOutMessage)
    {
        public static YouPinInventoryStorageAccess Empty { get; } = new(
            false,
            false,
            false,
            0,
            0,
            0,
            "暂无可存入信息",
            "暂无可取出信息");
    }

    public sealed record YouPinInventoryStorageItem(
        string AssetId,
        string StorageAssetId,
        string Name,
        string MarketHashName,
        string TemplateId,
        string ExteriorName,
        string IconUrl,
        decimal MarkPrice,
        bool IsMerged,
        string StatusText);

    public sealed record YouPinInventoryStorageUnit(
        string StorageAssetId,
        string Name,
        string IconUrl,
        int ItemCount,
        string CountText,
        int Status);

    public sealed record YouPinInventoryStorageViewState(
        YouPinInventoryStorageQuery Query,
        YouPinInventoryStorageAccess Access,
        IReadOnlyList<YouPinInventoryStorageItem> Items,
        IReadOnlyList<YouPinInventoryStorageUnit> Units,
        string Message,
        DateTime RefreshedAt)
    {
        public static YouPinInventoryStorageViewState Empty(YouPinInventoryStorageQuery query, string message) => new(
            query,
            YouPinInventoryStorageAccess.Empty,
            Array.Empty<YouPinInventoryStorageItem>(),
            Array.Empty<YouPinInventoryStorageUnit>(),
            message,
            DateTime.MinValue);
    }

    public sealed record YouPinInventoryStorageTransferCommand(
        YouPinInventoryStorageDirection Direction,
        string StorageAssetId,
        IReadOnlyList<string> AssetIds);

    public sealed record YouPinInventoryStorageTransferResult(
        YouPinInventoryStorageTransferStatus Status,
        string Message,
        YouPinInventoryStorageViewState? RefreshedState = null)
    {
        public bool IsSuccess => Status != YouPinInventoryStorageTransferStatus.Rejected;
    }
}
