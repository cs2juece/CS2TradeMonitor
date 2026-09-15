namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Redacted public observation used for change detection. Page position and rank are excluded
    /// because they can change without the underlying purchase changing.
    /// </summary>
    public sealed record YouPinPurchaseObservation(
        long TemplateId,
        string? CommodityName,
        decimal? PurchasePrice,
        int? SurplusQuantity,
        string? AbradeText,
        string? FadeText,
        bool? AutoReceived);
}
