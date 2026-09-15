namespace YouPinPurchaseMonitor.Models;

public sealed record PurchaseRow(
    int Rank,
    long TemplateId,
    string Category,
    string MarketHashName,
    decimal? PurchasePrice,
    int? SurplusQuantity,
    string? AbradeText,
    bool? AutoReceived,
    bool TemplateScanComplete);

public sealed record AuditReportView(
    string JsonPath,
    DateTimeOffset ObservedAt,
    string TargetUser,
    string StoreStatus,
    string PlanFingerprint,
    int CandidateCount,
    int ResolvedCount,
    int CompletedTemplateCount,
    int IncompleteTemplateCount,
    int BatchCount,
    int ObservedItemCount,
    int ObservationCount,
    bool IsPartial,
    IReadOnlyList<PurchaseRow> Purchases,
    IReadOnlyList<long> IncompleteTemplateIds)
{
    public string DisplayName => $"{ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {TargetUser} · {ObservationCount} 条";
    public bool HasCompleteIncompleteTemplateIdentitySet
        => IncompleteTemplateIds.Count == IncompleteTemplateCount;
}

public sealed record AuditWriteResult(string MarkdownPath, string JsonPath, AuditReportView View);
