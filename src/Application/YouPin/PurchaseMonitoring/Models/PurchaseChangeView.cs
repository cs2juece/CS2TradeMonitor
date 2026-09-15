using System.Text.Json.Serialization;

namespace YouPinPurchaseMonitor.Models;

public enum PurchaseChangeKind
{
    Appeared,
    CeasedToBeObserved,
    PriceChanged,
    QuantityIncreased,
    QuantityDecreased,
    ConfirmedFieldChange,
    AmbiguousReplacement
}

public sealed record PurchaseChangeView(
    PurchaseChangeKind Kind,
    long TemplateId,
    string CommodityName,
    decimal? PreviousPrice,
    decimal? CurrentPrice,
    int? PreviousQuantity,
    int? CurrentQuantity,
    string Description)
{
    public int? PriceTier { get; init; }
    public string? WearRange { get; init; }
    [JsonIgnore]
    public string PairingKey => PriceTier is int tier
        ? $"{tier} 档 · {WearRange ?? "未提供磨损"}"
        : "—";
}

public sealed record FailureReasonCount(string ReasonCode, int Count);

public sealed record CoverageHealthSample(
    DateTimeOffset ObservedAt,
    int AttemptedTemplateCount,
    int FailedTemplateCount,
    IReadOnlyList<FailureReasonCount> ReasonDistribution)
{
    public double FailureRate => AttemptedTemplateCount == 0
        ? 0d
        : FailedTemplateCount / (double)AttemptedTemplateCount;

    public bool HasProtocolDriftWarning => FailureRate > 0.25d;
}

public sealed record ChangeBatchView(
    string JsonPath,
    Guid WatchId,
    string SafeNote,
    DateTimeOffset ObservedAt,
    int BatchNumber,
    int TotalBatchCount,
    bool IsPartial,
    IReadOnlyList<PurchaseChangeView> Changes)
{
    public int SchemaVersion { get; init; } = 3;
    public CoverageHealthSample? Health { get; init; }
    public bool HasChanges => Changes.Count > 0;
    public string DisplayName => $"{ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {SafeNote} · {Changes.Count} 项";
}
