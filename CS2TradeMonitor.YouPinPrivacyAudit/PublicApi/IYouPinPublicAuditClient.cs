namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Bounded, read-only access to the YouPin data needed by a privacy audit.
    /// </summary>
    public interface IYouPinPublicAuditClient
    {
        Task<YouPinPublicStoreSummary> GetStoreSummaryAsync(
            long userId,
            CancellationToken cancellationToken = default);

        Task<YouPinPurchaseExposureResult> FindPurchaseExposureAsync(
            long targetUserId,
            YouPinPurchaseAuditScope scope,
            CancellationToken cancellationToken = default);
    }
}
