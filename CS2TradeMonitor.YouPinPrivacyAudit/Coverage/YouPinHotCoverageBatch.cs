using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// One explicitly materialized network-safe Template Scope from a frozen coverage plan.
    /// </summary>
    public sealed class YouPinHotCoverageBatch
    {
        internal YouPinHotCoverageBatch(
            string planFingerprint,
            int cursor,
            int totalBatchCount,
            IReadOnlyList<YouPinResolvedHotCatalogItem> entries)
        {
            PlanFingerprint = planFingerprint;
            Cursor = cursor;
            BatchNumber = cursor + 1;
            TotalBatchCount = totalBatchCount;
            NextCursor = (cursor + 1) % totalBatchCount;
            CompletesCoverageCycle = cursor == totalBatchCount - 1;
            Entries = entries;
            Scope = YouPinPurchaseAuditScope.Create(entries.Select(entry => entry.TemplateId).ToArray());
        }

        public string PlanFingerprint { get; }
        public int Cursor { get; }
        public int BatchNumber { get; }
        public int TotalBatchCount { get; }
        public int NextCursor { get; }
        public bool CompletesCoverageCycle { get; }
        public IReadOnlyList<YouPinResolvedHotCatalogItem> Entries { get; }
        public YouPinPurchaseAuditScope Scope { get; }
    }
}
