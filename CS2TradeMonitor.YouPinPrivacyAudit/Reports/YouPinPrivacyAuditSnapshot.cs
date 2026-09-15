using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports
{
    /// <summary>
    /// Inputs captured for one bounded audit. Identity consistency is enforced at construction.
    /// </summary>
    public sealed class YouPinPrivacyAuditSnapshot
    {
        public YouPinPrivacyAuditSnapshot(
            YouPinAuditSubject subject,
            YouPinStoreReadResult storeRead,
            YouPinPurchaseExposureResult purchaseExposure,
            DateTimeOffset observedAt)
        {
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(storeRead);
            ArgumentNullException.ThrowIfNull(purchaseExposure);

            if (subject.UserId != purchaseExposure.TargetUserId
                || storeRead.UserId != subject.UserId)
            {
                throw new ArgumentException("审计快照中的目标用户 ID 不一致。");
            }

            if (observedAt == default)
                throw new ArgumentOutOfRangeException(nameof(observedAt));

            Subject = subject;
            StoreRead = storeRead;
            PurchaseExposure = purchaseExposure;
            ObservedAt = observedAt;
        }

        public YouPinAuditSubject Subject { get; }
        public YouPinStoreReadResult StoreRead { get; }
        public YouPinPurchaseExposureResult PurchaseExposure { get; }
        public DateTimeOffset ObservedAt { get; }
    }
}
