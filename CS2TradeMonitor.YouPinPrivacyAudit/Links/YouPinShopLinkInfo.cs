namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Links
{
    /// <summary>
    /// Safe projection of a YouPin shop link. The original link and authSign value
    /// are intentionally not retained.
    /// </summary>
    public sealed record YouPinShopLinkInfo(
        long UserId,
        string Host,
        bool HasAuthSign,
        bool IsSharePage,
        string? TargetFragment,
        string SanitizedUrl);
}
