namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Changes supported by two snapshots without retaining an order number.
    /// </summary>
    public sealed class YouPinPurchaseChangeSet
    {
        internal YouPinPurchaseChangeSet(
            Guid watchId,
            DateTimeOffset previousObservedAt,
            DateTimeOffset currentObservedAt,
            IReadOnlyList<long> comparableTemplateIds,
            IReadOnlyList<YouPinPurchaseObservation> appeared,
            IReadOnlyList<YouPinPurchaseObservation> ceasedToBeObserved,
            IReadOnlyList<YouPinPurchaseObservation> currentOutsideComparableScope)
        {
            WatchId = watchId;
            PreviousObservedAt = previousObservedAt;
            CurrentObservedAt = currentObservedAt;
            ComparableTemplateIds = comparableTemplateIds;
            Appeared = appeared;
            CeasedToBeObserved = ceasedToBeObserved;
            CurrentOutsideComparableScope = currentOutsideComparableScope;
        }

        public Guid WatchId { get; }
        public DateTimeOffset PreviousObservedAt { get; }
        public DateTimeOffset CurrentObservedAt { get; }
        public IReadOnlyList<long> ComparableTemplateIds { get; }
        public IReadOnlyList<YouPinPurchaseObservation> Appeared { get; }
        public IReadOnlyList<YouPinPurchaseObservation> CeasedToBeObserved { get; }
        public IReadOnlyList<YouPinPurchaseObservation> CurrentOutsideComparableScope { get; }
        public bool HasChanges => Appeared.Count > 0 || CeasedToBeObserved.Count > 0;
    }
}
