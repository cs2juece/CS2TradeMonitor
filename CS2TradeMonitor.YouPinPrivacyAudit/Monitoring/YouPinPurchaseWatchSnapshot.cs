using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Persistable redacted state for one scan. The subject's user ID is deliberately replaced
    /// by the Authorized Watch ID.
    /// </summary>
    public sealed class YouPinPurchaseWatchSnapshot
    {
        private YouPinPurchaseWatchSnapshot(
            Guid watchId,
            DateTimeOffset observedAt,
            IReadOnlyList<long> requestedTemplateIds,
            IReadOnlyList<long> completedTemplateIds,
            IReadOnlyList<long> incompleteTemplateIds,
            IReadOnlyList<YouPinPurchaseObservation> observations)
        {
            WatchId = watchId;
            ObservedAt = observedAt;
            RequestedTemplateIds = requestedTemplateIds;
            CompletedTemplateIds = completedTemplateIds;
            IncompleteTemplateIds = incompleteTemplateIds;
            Observations = observations;
        }

        public Guid WatchId { get; }
        public DateTimeOffset ObservedAt { get; }
        public IReadOnlyList<long> RequestedTemplateIds { get; }
        public IReadOnlyList<long> CompletedTemplateIds { get; }
        public IReadOnlyList<long> IncompleteTemplateIds { get; }
        public IReadOnlyList<YouPinPurchaseObservation> Observations { get; }
        public bool IsPartial => IncompleteTemplateIds.Count > 0;

        public static YouPinPurchaseWatchSnapshot Create(
            YouPinAuthorizedWatch watch,
            YouPinPurchaseExposureResult exposure,
            DateTimeOffset observedAt)
        {
            ArgumentNullException.ThrowIfNull(watch);
            ArgumentNullException.ThrowIfNull(exposure);
            if (observedAt == default)
                throw new ArgumentOutOfRangeException(nameof(observedAt));
            if (watch.Subject.UserId != exposure.TargetUserId)
                throw new ArgumentException("监控目标与求购结果的用户 ID 不一致。", nameof(exposure));
            if (!watch.Scope.TemplateIds.SequenceEqual(exposure.RequestedTemplateIds))
                throw new ArgumentException("监控模板范围与求购结果不一致。", nameof(exposure));

            long[] incompleteTemplateIds = exposure.Failures
                .Select(failure => failure.TemplateId)
                .Distinct()
                .Order()
                .ToArray();
            var incompleteSet = incompleteTemplateIds.ToHashSet();
            long[] completedTemplateIds = exposure.RequestedTemplateIds
                .Where(templateId => !incompleteSet.Contains(templateId))
                .Order()
                .ToArray();
            YouPinPurchaseObservation[] observations = exposure.Matches
                .Where(match => !incompleteSet.Contains(match.TemplateId))
                .Select(match => new YouPinPurchaseObservation(
                    match.TemplateId,
                    YouPinSafeText.Normalize(match.CommodityName, 200),
                    match.PurchasePrice,
                    match.SurplusQuantity,
                    YouPinSafeText.Normalize(match.AbradeText, 80),
                    YouPinSafeText.Normalize(match.FadeText, 80),
                    match.AutoReceived))
                .OrderBy(observation => observation.TemplateId)
                .ThenBy(observation => observation.CommodityName, StringComparer.Ordinal)
                .ThenBy(observation => observation.PurchasePrice)
                .ThenBy(observation => observation.SurplusQuantity)
                .ToArray();

            return new YouPinPurchaseWatchSnapshot(
                watch.Id,
                observedAt.ToUniversalTime(),
                Array.AsReadOnly(exposure.RequestedTemplateIds.ToArray()),
                Array.AsReadOnly(completedTemplateIds),
                Array.AsReadOnly(incompleteTemplateIds),
                Array.AsReadOnly(observations));
        }

    }
}
