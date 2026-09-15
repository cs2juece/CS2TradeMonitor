namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Minimal projection of the fields exposed by the public store summary endpoint.
    /// Image URLs, location fields, raw responses, and request metadata are excluded.
    /// </summary>
    public sealed record YouPinPublicStoreSummary(
        long UserId,
        string StoreName,
        bool IsOnline,
        string? RegistrationDescription,
        string? DeliverySuccessRate,
        string? AverageDeliveryTime,
        bool StoreCommoditiesVisible,
        bool DynamicWallVisible);
}
