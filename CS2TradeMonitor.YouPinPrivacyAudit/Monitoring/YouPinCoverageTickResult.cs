using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// User-ID-free result of one Coverage Tick.
    /// </summary>
    public sealed class YouPinCoverageTickResult
    {
        internal YouPinCoverageTickResult(
            Guid watchId,
            string planFingerprint,
            DateTimeOffset observedAt,
            int batchNumber,
            int totalBatchCount,
            IReadOnlyList<long> completedTemplateIds,
            IReadOnlyList<long> incompleteTemplateIds,
            IReadOnlyList<YouPinPurchaseObservation> appeared,
            IReadOnlyList<YouPinPurchaseObservation> ceasedToBeObserved,
            IReadOnlyList<YouPinPurchaseObservation> establishedBaseline,
            IReadOnlyList<YouPinTemplateQueryFailure> failures,
            YouPinCoverageWatchState updatedState)
        {
            WatchId = watchId;
            PlanFingerprint = planFingerprint;
            ObservedAt = observedAt;
            BatchNumber = batchNumber;
            TotalBatchCount = totalBatchCount;
            CompletedTemplateIds = completedTemplateIds;
            IncompleteTemplateIds = incompleteTemplateIds;
            Appeared = appeared;
            CeasedToBeObserved = ceasedToBeObserved;
            EstablishedBaseline = establishedBaseline;
            Failures = failures;
            UpdatedState = updatedState;
        }

        public Guid WatchId { get; }
        public string PlanFingerprint { get; }
        public DateTimeOffset ObservedAt { get; }
        public int BatchNumber { get; }
        public int TotalBatchCount { get; }
        public IReadOnlyList<long> CompletedTemplateIds { get; }
        public IReadOnlyList<long> IncompleteTemplateIds { get; }
        public IReadOnlyList<YouPinPurchaseObservation> Appeared { get; }
        public IReadOnlyList<YouPinPurchaseObservation> CeasedToBeObserved { get; }
        public IReadOnlyList<YouPinPurchaseObservation> EstablishedBaseline { get; }
        public IReadOnlyList<YouPinTemplateQueryFailure> Failures { get; }
        public YouPinCoverageWatchState UpdatedState { get; }
        public bool HasChanges => Appeared.Count > 0 || CeasedToBeObserved.Count > 0;
        public bool IsPartial => IncompleteTemplateIds.Count > 0;
    }
}
