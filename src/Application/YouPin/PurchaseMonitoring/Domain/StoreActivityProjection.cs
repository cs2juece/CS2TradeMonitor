using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Domain;

public static class StoreActivityProjection
{
    public static IReadOnlyList<StoreActivity> Build(ChangeBatchView batch)
    {
        var result = new List<StoreActivity>();
        var paired = new Dictionary<(long, int, string?, decimal?, decimal?, int?, int?), int>();
        for (int index = 0; index < batch.Changes.Count; index++)
        {
            PurchaseChangeView change = batch.Changes[index];
            if (change.PriceTier is int tier && change.Kind is PurchaseChangeKind.PriceChanged
                or PurchaseChangeKind.QuantityIncreased or PurchaseChangeKind.QuantityDecreased
                or PurchaseChangeKind.ConfirmedFieldChange)
            {
                var key = (change.TemplateId, tier, change.WearRange, change.PreviousPrice,
                    change.CurrentPrice, change.PreviousQuantity, change.CurrentQuantity);
                if (paired.TryGetValue(key, out int existing))
                {
                    StoreActivity activity = result[existing];
                    result[existing] = activity with { Changes = activity.Changes.Append(change).ToArray() };
                    continue;
                }
                paired[key] = result.Count;
            }
            result.Add(new StoreActivity(batch.WatchId, batch.ObservedAt, index, batch.SafeNote, [change]));
        }
        return result;
    }

    public static IReadOnlyList<StoreActivity> Build(IEnumerable<ChangeBatchView> batches)
        => batches.DistinctBy(batch => (batch.WatchId, batch.ObservedAt))
            .OrderByDescending(batch => batch.ObservedAt).ThenByDescending(batch => batch.WatchId)
            .SelectMany(Build).ToArray();

    public static string Summary(IEnumerable<StoreActivity> source)
    {
        StoreActivity[] activities = source.ToArray();
        var parts = new List<string>();
        Add(PurchaseChangeKind.Appeared, "新增求购");
        Add(PurchaseChangeKind.CeasedToBeObserved, "未再观察到");
        Add(PurchaseChangeKind.QuantityIncreased, "数量增加");
        Add(PurchaseChangeKind.QuantityDecreased, "数量减少");
        Add(PurchaseChangeKind.PriceChanged, "价格变化");
        Add(PurchaseChangeKind.AmbiguousReplacement, "条件待核对");
        Add(PurchaseChangeKind.ConfirmedFieldChange, "条件变化");
        return parts.Count == 0 ? "成功读取范围内无可确认变化" : string.Join(" · ", parts);

        void Add(PurchaseChangeKind kind, string label)
        {
            StoreActivity[] matching = activities.Where(item => item.Contains(kind)).ToArray();
            if (matching.Length == 0) return;
            string species = kind is PurchaseChangeKind.Appeared or PurchaseChangeKind.CeasedToBeObserved
                ? $"（{matching.Select(item => item.Change.TemplateId).Distinct().Count()}种）" : "";
            parts.Add($"{label}{matching.Length}条{species}");
        }
    }
}
