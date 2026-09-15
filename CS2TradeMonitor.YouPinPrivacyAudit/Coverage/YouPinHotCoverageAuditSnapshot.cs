using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Completed Full Coverage Audit. It keeps no shop link or share credential value.
    /// </summary>
    public sealed class YouPinHotCoverageAuditSnapshot
    {
        internal YouPinHotCoverageAuditSnapshot(
            YouPinAuditSubject subject,
            YouPinStoreReadResult storeRead,
            YouPinHotCoveragePlan plan,
            IReadOnlyList<long> completedTemplateIds,
            IReadOnlyList<long> incompleteTemplateIds,
            IReadOnlyList<YouPinHotPurchaseObservation> observations,
            IReadOnlyList<YouPinTemplateQueryFailure> failures,
            DateTimeOffset observedAt)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(storeRead);
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(completedTemplateIds);
            ArgumentNullException.ThrowIfNull(incompleteTemplateIds);
            ArgumentNullException.ThrowIfNull(observations);
            ArgumentNullException.ThrowIfNull(failures);
            if (storeRead.UserId != subject.UserId)
                throw new ArgumentException("完整覆盖审计中的目标用户 ID 不一致。", nameof(storeRead));
            if (observedAt == default)
                throw new ArgumentOutOfRangeException(nameof(observedAt));

            long[] resolvedIds = plan.OrderedResolvedItems
                .Select(item => item.TemplateId)
                .Order()
                .ToArray();
            long[] completedIds = completedTemplateIds.Distinct().Order().ToArray();
            long[] incompleteIds = incompleteTemplateIds.Distinct().Order().ToArray();
            if (completedIds.Intersect(incompleteIds).Any()
                || !completedIds.Concat(incompleteIds).Order().SequenceEqual(resolvedIds))
            {
                throw new ArgumentException("完整覆盖审计的模板完成状态与覆盖计划不一致。");
            }

            var resolvedSet = resolvedIds.ToHashSet();
            long[] failureIds = failures.Select(failure => failure.TemplateId).Order().ToArray();
            if (observations.Any(observation => !resolvedSet.Contains(observation.TemplateId))
                || failureIds.Distinct().Count() != failureIds.Length
                || !failureIds.SequenceEqual(incompleteIds))
            {
                throw new ArgumentException("完整覆盖审计包含计划之外的结果。");
            }

            Subject = subject;
            StoreRead = storeRead;
            PlanFingerprint = plan.Fingerprint;
            CatalogSourceSha256 = plan.Catalog.SourceSha256;
            CandidateCount = plan.CandidateCount;
            ResolvedCount = plan.ResolvedCount;
            UnresolvedCount = plan.UnresolvedCount;
            BatchCount = plan.BatchCount;
            CompletedTemplateIds = Array.AsReadOnly(completedIds);
            IncompleteTemplateIds = Array.AsReadOnly(incompleteIds);
            Observations = Array.AsReadOnly(observations.ToArray());
            Failures = Array.AsReadOnly(failures.ToArray());
            ObservedAt = observedAt.ToUniversalTime();
        }

        public YouPinAuditSubject Subject { get; }
        public YouPinStoreReadResult StoreRead { get; }
        public string PlanFingerprint { get; }
        public string CatalogSourceSha256 { get; }
        public int CandidateCount { get; }
        public int ResolvedCount { get; }
        public int UnresolvedCount { get; }
        public int BatchCount { get; }
        public IReadOnlyList<long> CompletedTemplateIds { get; }
        public IReadOnlyList<long> IncompleteTemplateIds { get; }
        public IReadOnlyList<YouPinHotPurchaseObservation> Observations { get; }
        public IReadOnlyList<YouPinTemplateQueryFailure> Failures { get; }
        public DateTimeOffset ObservedAt { get; }
        public int ObservedItemCount => Observations.Select(item => item.TemplateId).Distinct().Count();
        public bool IsPartial => UnresolvedCount > 0 || IncompleteTemplateIds.Count > 0;
    }
}
