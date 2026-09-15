namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Validated, immutable set of caller-supplied template IDs. It cannot represent discovery.
    /// </summary>
    public sealed class YouPinPurchaseAuditScope
    {
        public const int MaximumTemplateCount = 20;

        private YouPinPurchaseAuditScope(IReadOnlyList<long> templateIds)
        {
            TemplateIds = templateIds;
        }

        public IReadOnlyList<long> TemplateIds { get; }
        public int Count => TemplateIds.Count;

        public static YouPinPurchaseAuditScope Create(IReadOnlyCollection<long> templateIds)
        {
            ArgumentNullException.ThrowIfNull(templateIds);

            if (templateIds.Count == 0)
                throw new ArgumentException("至少需要一个明确的模板 ID。", nameof(templateIds));
            if (templateIds.Count > MaximumTemplateCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(templateIds),
                    $"单次最多允许 {MaximumTemplateCount} 个模板 ID。");
            }

            long[] normalizedTemplateIds = templateIds.Distinct().ToArray();
            if (normalizedTemplateIds.Any(templateId => templateId <= 0))
                throw new ArgumentOutOfRangeException(nameof(templateIds), "模板 ID 必须为正整数。");

            return new YouPinPurchaseAuditScope(Array.AsReadOnly(normalizedTemplateIds));
        }
    }
}
