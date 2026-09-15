using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Domain;

/// <summary>Compares successful observations against the shop's newest baseline across scheduling lanes.</summary>
public static class StorePurchaseChangeClassifier
{
    public static IReadOnlyList<PurchaseChangeView> Classify(IEnumerable<YouPinTemplateBaseline> previous,
        IEnumerable<YouPinTemplateBaseline> current, IReadOnlyCollection<long> completedTemplateIds)
    {
        Dictionary<long, YouPinTemplateBaseline> old = previous.GroupBy(item => item.TemplateId)
            .ToDictionary(group => group.Key, group => group.MaxBy(item => item.ObservedAt)!);
        HashSet<long> completed = completedTemplateIds.ToHashSet();
        var appeared = new List<YouPinPurchaseObservation>();
        var ceased = new List<YouPinPurchaseObservation>();
        foreach (YouPinTemplateBaseline next in current.Where(item => completed.Contains(item.TemplateId)))
        {
            if (!old.TryGetValue(next.TemplateId, out YouPinTemplateBaseline? prior) || next.ObservedAt <= prior.ObservedAt) continue;
            appeared.AddRange(ExceptMultiset(next.Observations, prior.Observations));
            ceased.AddRange(ExceptMultiset(prior.Observations, next.Observations));
        }
        return new PurchaseChangeClassifier().Classify(appeared, ceased);
    }

    private static IEnumerable<YouPinPurchaseObservation> ExceptMultiset(IEnumerable<YouPinPurchaseObservation> source,
        IEnumerable<YouPinPurchaseObservation> subtract)
    {
        Dictionary<YouPinPurchaseObservation, int> remaining = subtract.GroupBy(item => item).ToDictionary(group => group.Key, group => group.Count());
        foreach (YouPinPurchaseObservation item in source)
        {
            if (remaining.TryGetValue(item, out int count) && count > 0) remaining[item] = count - 1;
            else yield return item;
        }
    }
}
