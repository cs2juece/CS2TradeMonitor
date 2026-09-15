namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Redacted progress for one explicitly started Full Coverage Audit.
    /// </summary>
    public sealed record YouPinHotCoverageProgress(
        int CompletedBatchCount,
        int TotalBatchCount,
        int CompletedTemplateCount,
        int IncompleteTemplateCount,
        int ObservationCount);
}
