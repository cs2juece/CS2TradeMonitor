using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;

namespace YouPinPurchaseMonitor.Models;

public sealed record StorePurchaseView(YouPinPurchaseObservation Purchase, DateTimeOffset ObservedAt);

public sealed record StoreMonitorView(
    Guid StoreId,
    StoreObservationRegistration Registration,
    StoreObservationStatus Status,
    IReadOnlyList<StoreObservationView> Lanes,
    IReadOnlyList<StorePurchaseView> Purchases,
    IReadOnlyList<ChangeBatchView> History,
    DateTimeOffset? LastSuccessfulObservationAt,
    DateTimeOffset? NextDueAt)
{
    public Guid WatchId => Registration.WatchId;
    public string Note => Registration.SafeNote;
    public ChangeBatchView? LatestChange => History.FirstOrDefault(batch => batch.HasChanges);
    public ChangeBatchView? LatestBatch => History.FirstOrDefault();
    public bool NeedsReauthorization => Lanes.Any(lane => lane.Status == StoreObservationStatus.PendingReauthorization);
    public bool HasPriorityLane => Lanes.Count > 1;
    public IReadOnlyList<StoreActivity> Activity => YouPinPurchaseMonitor.Domain.StoreActivityProjection.Build(History);
    public IReadOnlyCollection<Guid> HistoryIds => Lanes.SelectMany(lane =>
        lane.Registration.HistoryWatchIds.Append(lane.Registration.WatchId)).Distinct().ToArray();
    public DateTimeOffset ReadThrough { get; init; }
    public int UnreadCount { get; init; }
    public bool NotificationsEnabled { get; init; } = true;
    public int RecentActivityCount { get; init; }
    public int PurchaseSpeciesCount => Purchases.Select(item => item.Purchase.TemplateId).Distinct().Count();
}
