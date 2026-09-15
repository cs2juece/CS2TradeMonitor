namespace YouPinPurchaseMonitor.Models;

public sealed record StoreActivity(
    Guid WatchId,
    DateTimeOffset ObservedAt,
    int Sequence,
    string SafeNote,
    IReadOnlyList<PurchaseChangeView> Changes)
{
    public string Id => $"{WatchId:N}:{ObservedAt.UtcTicks}:{Sequence}";
    public PurchaseChangeView Change => Changes[0];
    public bool Contains(PurchaseChangeKind kind) => Changes.Any(change => change.Kind == kind);
}

public sealed record StoreActivityCursor(DateTimeOffset ObservedAt, Guid WatchId, int Sequence);

public sealed record StoreActivityQuery(
    IReadOnlyCollection<Guid>? WatchIds,
    DateTimeOffset Since,
    IReadOnlyCollection<PurchaseChangeKind>? Kinds = null,
    StoreActivityCursor? Before = null,
    int PageSize = 50);

public sealed record StoreActivityPage(
    IReadOnlyList<StoreActivity> Items,
    StoreActivityCursor? NextCursor,
    int TotalCount);

public sealed record StoreActivityPreferences(DateTimeOffset ReadThrough, bool NotificationsEnabled = true);
