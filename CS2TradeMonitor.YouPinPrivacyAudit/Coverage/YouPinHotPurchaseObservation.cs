namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Minimal public purchase observation enriched with its verified Hot Catalog identity.
    /// </summary>
    public sealed record YouPinHotPurchaseObservation(
        int Rank,
        long TemplateId,
        string MarketHashName,
        string Category,
        string? CommodityName,
        decimal? PurchasePrice,
        int? SurplusQuantity,
        string? AbradeText,
        string? FadeText,
        bool? AutoReceived);
}
