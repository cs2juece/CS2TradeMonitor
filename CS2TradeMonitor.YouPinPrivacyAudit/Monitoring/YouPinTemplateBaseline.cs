namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Latest successful observation set for one template. It carries no subject user ID.
    /// </summary>
    public sealed class YouPinTemplateBaseline
    {
        private YouPinTemplateBaseline(
            long templateId,
            DateTimeOffset observedAt,
            IReadOnlyList<YouPinPurchaseObservation> observations)
        {
            TemplateId = templateId;
            ObservedAt = observedAt;
            Observations = observations;
        }

        public long TemplateId { get; }
        public DateTimeOffset ObservedAt { get; }
        public IReadOnlyList<YouPinPurchaseObservation> Observations { get; }

        public static YouPinTemplateBaseline Restore(
            long templateId,
            DateTimeOffset observedAt,
            IReadOnlyCollection<YouPinPurchaseObservation> observations)
        {
            if (templateId <= 0)
                throw new ArgumentOutOfRangeException(nameof(templateId));
            if (observedAt == default)
                throw new ArgumentOutOfRangeException(nameof(observedAt));
            ArgumentNullException.ThrowIfNull(observations);
            if (observations.Any(observation => observation.TemplateId != templateId))
            {
                throw new ArgumentException(
                    "模板基线只能包含同一模板的观察记录。",
                    nameof(observations));
            }

            YouPinPurchaseObservation[] ordered = observations
                .OrderBy(observation => observation.CommodityName, StringComparer.Ordinal)
                .ThenBy(observation => observation.PurchasePrice)
                .ThenBy(observation => observation.SurplusQuantity)
                .ThenBy(observation => observation.AbradeText, StringComparer.Ordinal)
                .ThenBy(observation => observation.FadeText, StringComparer.Ordinal)
                .ToArray();
            return new YouPinTemplateBaseline(
                templateId,
                observedAt.ToUniversalTime(),
                Array.AsReadOnly(ordered));
        }
    }
}
