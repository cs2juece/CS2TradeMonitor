namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Minimal evidence that the target user ID appeared in one explicitly queried template.
    /// Order numbers, usernames, avatars, and third-party identities are excluded.
    /// </summary>
    public sealed record YouPinPurchaseExposureMatch(
        long TemplateId,
        int PageIndex,
        string? CommodityName,
        decimal? PurchasePrice,
        int? SurplusQuantity,
        string? AbradeText,
        string? FadeText,
        bool? AutoReceived,
        bool? IsRankFirst);
}
