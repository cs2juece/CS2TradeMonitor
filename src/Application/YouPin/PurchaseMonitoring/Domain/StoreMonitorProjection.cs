using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Domain;

/// <summary>Projects shops, not scheduling lanes or masked platform identities.</summary>
public static class StoreMonitorProjection
{
    public static IReadOnlyList<StoreMonitorView> Build(
        IReadOnlyList<StoreObservationView> observations,
        IReadOnlyList<ChangeBatchView> history)
    {
        var result = new List<StoreMonitorView>();
        foreach (StoreObservationView root in observations.Where(item =>
                     item.Registration.ScheduleLane != ObservationScheduleLane.Priority))
        {
            StoreObservationView[] lanes = observations.Where(item =>
                item.Registration.WatchId == root.Registration.WatchId
                || item.Registration.WatchId == root.Registration.PartnerWatchId
                    && item.Registration.PartnerWatchId == root.Registration.WatchId).ToArray();
            HashSet<Guid> watchIds = lanes.SelectMany(item =>
                item.Registration.HistoryWatchIds.Append(item.Registration.WatchId)).ToHashSet();
            ChangeBatchView[] changes = history.Where(batch => watchIds.Contains(batch.WatchId))
                .OrderByDescending(batch => batch.ObservedAt).ToArray();
            // Choose an entire baseline, including an empty observation set, before flattening.
            // Otherwise an older fallback lane can resurrect a purchase removed by the priority lane.
            YouPinTemplateBaseline[] baselines = lanes.SelectMany(item => item.Baselines)
                .GroupBy(item => item.TemplateId)
                .Select(group => group.OrderByDescending(item => item.ObservedAt).First()).ToArray();
            StorePurchaseView[] purchases = baselines.SelectMany(baseline => baseline.Observations
                .Select(purchase => new StorePurchaseView(purchase, baseline.ObservedAt)))
                .OrderByDescending(item => item.ObservedAt)
                .ThenBy(item => item.Purchase.CommodityName, StringComparer.Ordinal)
                .ThenBy(item => item.Purchase.PurchasePrice).ToArray();
            StoreObservationStatus status = lanes.OrderBy(item => StatusPriority(item.Status)).First().Status;
            result.Add(new StoreMonitorView(
                root.Registration.StoreId == Guid.Empty ? root.Registration.WatchId : root.Registration.StoreId,
                root.Registration,
                status,
                lanes,
                purchases,
                changes,
                baselines.Length == 0 ? null : baselines.Max(item => item.ObservedAt),
                lanes.Where(item => item.NextDueAt is not null).Select(item => item.NextDueAt).Min()));
        }
        return result.OrderByDescending(item => item.LatestChange?.ObservedAt)
            .ThenBy(item => item.Registration.CreatedAt).ToArray();
    }

    public static string ChangeSummary(IReadOnlyList<PurchaseChangeView> changes)
    {
        int appeared = changes.Count(item => item.Kind == PurchaseChangeKind.Appeared);
        int ceased = changes.Count(item => item.Kind == PurchaseChangeKind.CeasedToBeObserved);
        int quantities = changes.Count(item => item.Kind is PurchaseChangeKind.QuantityIncreased or PurchaseChangeKind.QuantityDecreased);
        int prices = changes.Count(item => item.Kind == PurchaseChangeKind.PriceChanged);
        int other = changes.Count - appeared - ceased - quantities - prices;
        var parts = new List<string>();
        if (appeared > 0) parts.Add($"新增 {appeared} 项");
        if (ceased > 0) parts.Add($"本次未再观察到 {ceased} 项");
        if (quantities > 0) parts.Add($"数量变化 {quantities} 项");
        if (prices > 0) parts.Add($"价格变化 {prices} 项");
        if (other > 0) parts.Add($"其他变化 {other} 项");
        return parts.Count == 0 ? "本批无可确认变化" : string.Join(" / ", parts);
    }

    private static int StatusPriority(StoreObservationStatus status) => status switch
    {
        StoreObservationStatus.RunningTick => 0,
        StoreObservationStatus.PendingReauthorization => 1,
        StoreObservationStatus.Failed => 2,
        StoreObservationStatus.Active => 3,
        _ => 4
    };
}
