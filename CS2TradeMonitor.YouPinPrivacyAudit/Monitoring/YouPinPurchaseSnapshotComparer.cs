namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    public static class YouPinPurchaseSnapshotComparer
    {
        public static YouPinPurchaseChangeSet Compare(
            YouPinPurchaseWatchSnapshot previous,
            YouPinPurchaseWatchSnapshot current)
        {
            ArgumentNullException.ThrowIfNull(previous);
            ArgumentNullException.ThrowIfNull(current);
            if (previous.WatchId != current.WatchId)
                throw new ArgumentException("只能比较同一个授权监控目标的快照。", nameof(current));
            if (!previous.RequestedTemplateIds.SequenceEqual(current.RequestedTemplateIds))
                throw new ArgumentException("授权监控目标的模板范围不能在快照之间变化。", nameof(current));
            if (current.ObservedAt <= previous.ObservedAt)
                throw new ArgumentException("当前快照必须晚于上一份快照。", nameof(current));

            long[] comparableTemplateIds = previous.CompletedTemplateIds
                .Intersect(current.CompletedTemplateIds)
                .Order()
                .ToArray();
            var comparableSet = comparableTemplateIds.ToHashSet();
            YouPinPurchaseObservation[] previousComparable = previous.Observations
                .Where(observation => comparableSet.Contains(observation.TemplateId))
                .ToArray();
            YouPinPurchaseObservation[] currentComparable = current.Observations
                .Where(observation => comparableSet.Contains(observation.TemplateId))
                .ToArray();
            IReadOnlyList<YouPinPurchaseObservation> appeared = ExceptMultiset(
                currentComparable,
                previousComparable);
            IReadOnlyList<YouPinPurchaseObservation> ceased = ExceptMultiset(
                previousComparable,
                currentComparable);
            YouPinPurchaseObservation[] outsideComparable = current.Observations
                .Where(observation => !comparableSet.Contains(observation.TemplateId))
                .ToArray();

            return new YouPinPurchaseChangeSet(
                current.WatchId,
                previous.ObservedAt,
                current.ObservedAt,
                Array.AsReadOnly(comparableTemplateIds),
                appeared,
                ceased,
                Array.AsReadOnly(outsideComparable));
        }

        private static IReadOnlyList<YouPinPurchaseObservation> ExceptMultiset(
            IReadOnlyCollection<YouPinPurchaseObservation> source,
            IReadOnlyCollection<YouPinPurchaseObservation> subtract)
        {
            Dictionary<YouPinPurchaseObservation, int> remaining = subtract
                .GroupBy(observation => observation)
                .ToDictionary(group => group.Key, group => group.Count());
            var result = new List<YouPinPurchaseObservation>();

            foreach (YouPinPurchaseObservation observation in source)
            {
                if (remaining.TryGetValue(observation, out int count) && count > 0)
                {
                    remaining[observation] = count - 1;
                }
                else
                {
                    result.Add(observation);
                }
            }

            return result.AsReadOnly();
        }
    }
}
