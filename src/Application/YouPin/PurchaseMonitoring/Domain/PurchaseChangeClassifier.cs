using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Domain;

public sealed class PurchaseChangeClassifier
{
    public IReadOnlyList<PurchaseChangeView> Classify(YouPinCoverageTickResult tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        return Classify(tick.Appeared, tick.CeasedToBeObserved);
    }

    public IReadOnlyList<PurchaseChangeView> Classify(
        IReadOnlyList<YouPinPurchaseObservation> appearedObservations,
        IReadOnlyList<YouPinPurchaseObservation> ceasedObservations)
    {
        ArgumentNullException.ThrowIfNull(appearedObservations);
        ArgumentNullException.ThrowIfNull(ceasedObservations);
        long[] templateIds = appearedObservations.Select(item => item.TemplateId)
            .Concat(ceasedObservations.Select(item => item.TemplateId))
            .Distinct()
            .Order()
            .ToArray();
        var changes = new List<PurchaseChangeView>();
        foreach (long templateId in templateIds)
        {
            YouPinPurchaseObservation[] appeared = appearedObservations
                .Where(item => item.TemplateId == templateId)
                .ToArray();
            YouPinPurchaseObservation[] ceased = ceasedObservations
                .Where(item => item.TemplateId == templateId)
                .ToArray();

            var appearedByRange = appeared.GroupBy(CreateRangeKey).ToDictionary(group => group.Key);
            var ceasedByRange = ceased.GroupBy(CreateRangeKey).ToDictionary(group => group.Key);
            var unmatchedAppeared = new List<YouPinPurchaseObservation>();
            var unmatchedCeased = new List<YouPinPurchaseObservation>();
            RangeKey[] rangeKeys = appearedByRange.Keys
                .Concat(ceasedByRange.Keys)
                .Distinct()
                .OrderBy(key => key.SortKey, StringComparer.Ordinal)
                .ToArray();
            foreach (RangeKey rangeKey in rangeKeys)
            {
                YouPinPurchaseObservation[] currentRange = appearedByRange.TryGetValue(
                    rangeKey,
                    out IGrouping<RangeKey, YouPinPurchaseObservation>? currentGroup)
                    ? currentGroup.ToArray()
                    : [];
                YouPinPurchaseObservation[] previousRange = ceasedByRange.TryGetValue(
                    rangeKey,
                    out IGrouping<RangeKey, YouPinPurchaseObservation>? previousGroup)
                    ? previousGroup.ToArray()
                    : [];
                if (TryPairByPriceTier(previousRange, currentRange, rangeKey, out ObservationPair[] pairs))
                {
                    foreach (ObservationPair pair in pairs)
                        changes.AddRange(ClassifyPair(templateId, pair));
                }
                else
                {
                    unmatchedAppeared.AddRange(currentRange);
                    unmatchedCeased.AddRange(previousRange);
                }
            }

            if (unmatchedAppeared.Count > 0 && unmatchedCeased.Count > 0)
            {
                changes.Add(new PurchaseChangeView(
                    PurchaseChangeKind.AmbiguousReplacement,
                    templateId,
                    unmatchedAppeared[0].CommodityName ?? unmatchedCeased[0].CommodityName ?? "未知饰品",
                    null,
                    null,
                    unmatchedCeased.Count,
                    unmatchedAppeared.Count,
                    $"同一模板仍有 {unmatchedCeased.Count} 条旧观察和 {unmatchedAppeared.Count} 条新观察无法按价格档位与磨损区间唯一配对。"));
                continue;
            }

            changes.AddRange(unmatchedAppeared.Select(item => new PurchaseChangeView(
                PurchaseChangeKind.Appeared,
                templateId,
                item.CommodityName ?? "未知饰品",
                null,
                item.PurchasePrice,
                null,
                item.SurplusQuantity,
                "新出现的公开求购观察。")
            { WearRange = CreateRangeKey(item).DisplayWear }));
            changes.AddRange(unmatchedCeased.Select(item => new PurchaseChangeView(
                PurchaseChangeKind.CeasedToBeObserved,
                templateId,
                item.CommodityName ?? "未知饰品",
                item.PurchasePrice,
                null,
                item.SurplusQuantity,
                null,
                "本次成功读取中不再观察到；不等同于撤单或成交。")
            { WearRange = CreateRangeKey(item).DisplayWear }));
        }
        return changes.AsReadOnly();
    }

    private static IReadOnlyList<PurchaseChangeView> ClassifyPair(
        long templateId,
        ObservationPair pair)
    {
        string commodityName = pair.Current.CommodityName
            ?? pair.Previous.CommodityName
            ?? "未知饰品";
        var changes = new List<PurchaseChangeView>(2);
        if (pair.Previous.PurchasePrice != pair.Current.PurchasePrice)
        {
            changes.Add(CreatePairedChange(
                PurchaseChangeKind.PriceChanged,
                templateId,
                commodityName,
                pair,
                $"价格 {Text(pair.Previous.PurchasePrice)} → {Text(pair.Current.PurchasePrice)}。"));
        }
        if (pair.Previous.SurplusQuantity is int previousQuantity
            && pair.Current.SurplusQuantity is int currentQuantity
            && previousQuantity != currentQuantity)
        {
            PurchaseChangeKind kind = currentQuantity > previousQuantity
                ? PurchaseChangeKind.QuantityIncreased
                : PurchaseChangeKind.QuantityDecreased;
            changes.Add(CreatePairedChange(
                kind,
                templateId,
                commodityName,
                pair,
                $"数量 {previousQuantity} → {currentQuantity}。"));
        }
        else if (pair.Previous.SurplusQuantity != pair.Current.SurplusQuantity)
        {
            changes.Add(CreatePairedChange(
                PurchaseChangeKind.ConfirmedFieldChange,
                templateId,
                commodityName,
                pair,
                $"数量信息 {Text(pair.Previous.SurplusQuantity)} → {Text(pair.Current.SurplusQuantity)}。"));
        }
        var otherFieldChanges = new List<string>(2);
        if (!string.Equals(pair.Previous.FadeText, pair.Current.FadeText, StringComparison.Ordinal))
        {
            otherFieldChanges.Add(
                $"渐变描述 {Text(pair.Previous.FadeText)} → {Text(pair.Current.FadeText)}");
        }
        if (pair.Previous.AutoReceived != pair.Current.AutoReceived)
        {
            otherFieldChanges.Add(
                $"自动收货 {Text(pair.Previous.AutoReceived)} → {Text(pair.Current.AutoReceived)}");
        }
        if (otherFieldChanges.Count > 0)
        {
            changes.Add(CreatePairedChange(
                PurchaseChangeKind.ConfirmedFieldChange,
                templateId,
                commodityName,
                pair,
                string.Join("；", otherFieldChanges) + "。"));
        }
        if (changes.Count == 0)
        {
            changes.Add(CreatePairedChange(
                PurchaseChangeKind.ConfirmedFieldChange,
                templateId,
                commodityName,
                pair,
                "公开字段发生变化。"));
        }
        return changes;
    }

    private static PurchaseChangeView CreatePairedChange(
        PurchaseChangeKind kind,
        long templateId,
        string commodityName,
        ObservationPair pair,
        string description)
        => new(
            kind,
            templateId,
            commodityName,
            pair.Previous.PurchasePrice,
            pair.Current.PurchasePrice,
            pair.Previous.SurplusQuantity,
            pair.Current.SurplusQuantity,
            $"价格档位 {pair.PriceTier} · 磨损 {pair.Range.DisplayWear}；{description}")
        {
            PriceTier = pair.PriceTier,
            WearRange = pair.Range.DisplayWear
        };

    private static bool TryPairByPriceTier(
        IReadOnlyList<YouPinPurchaseObservation> previous,
        IReadOnlyList<YouPinPurchaseObservation> current,
        RangeKey range,
        out ObservationPair[] pairs)
    {
        pairs = [];
        if (previous.Count == 0 || previous.Count != current.Count)
            return false;
        if (previous.Count == 1)
        {
            pairs = [new ObservationPair(previous[0], current[0], 1, range)];
            return true;
        }
        if (previous.Any(item => item.PurchasePrice is null)
            || current.Any(item => item.PurchasePrice is null)
            || previous.Select(item => item.PurchasePrice).Distinct().Count() != previous.Count
            || current.Select(item => item.PurchasePrice).Distinct().Count() != current.Count)
        {
            return false;
        }
        YouPinPurchaseObservation[] previousByPrice = previous.OrderBy(item => item.PurchasePrice).ToArray();
        YouPinPurchaseObservation[] currentByPrice = current.OrderBy(item => item.PurchasePrice).ToArray();
        pairs = previousByPrice.Zip(
                currentByPrice,
                (oldItem, newItem) => (oldItem, newItem))
            .Select((pair, index) => new ObservationPair(
                pair.oldItem,
                pair.newItem,
                index + 1,
                range))
            .ToArray();
        return true;
    }

    private static RangeKey CreateRangeKey(YouPinPurchaseObservation observation)
        => new(
            observation.CommodityName ?? string.Empty,
            observation.AbradeText ?? string.Empty);

    private static string Text<T>(T? value) where T : struct
        => value?.ToString() ?? "—";

    private static string Text(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private readonly record struct RangeKey(
        string CommodityName,
        string AbradeText)
    {
        internal string DisplayWear => string.IsNullOrWhiteSpace(AbradeText)
            ? "未提供"
            : AbradeText;

        internal string SortKey => $"{CommodityName}\u001f{AbradeText}";
    }

    private sealed record ObservationPair(
        YouPinPurchaseObservation Previous,
        YouPinPurchaseObservation Current,
        int PriceTier,
        RangeKey Range);
}
