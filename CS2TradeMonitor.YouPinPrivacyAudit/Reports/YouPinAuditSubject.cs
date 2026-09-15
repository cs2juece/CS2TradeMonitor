using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Links;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports
{
    /// <summary>
    /// Minimal identity carried into reporting. No original or sanitized URL is retained.
    /// </summary>
    public sealed record YouPinAuditSubject
    {
        private YouPinAuditSubject(long userId, bool shareCredentialWasRemoved)
        {
            UserId = userId;
            ShareCredentialWasRemoved = shareCredentialWasRemoved;
        }

        public long UserId { get; }
        public bool ShareCredentialWasRemoved { get; }

        public static YouPinAuditSubject FromLink(YouPinShopLinkInfo link)
        {
            ArgumentNullException.ThrowIfNull(link);
            if (!string.Equals(
                link.Host,
                YouPinShopLinkParser.OfficialHost,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("审计主体只接受悠悠官方店铺来源。", nameof(link));
            }

            return new YouPinAuditSubject(link.UserId, link.HasAuthSign);
        }

        public static YouPinAuditSubject FromUserId(
            long userId,
            bool shareCredentialWasRemoved = false)
        {
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId), "用户 ID 必须为正整数。");

            return new YouPinAuditSubject(userId, shareCredentialWasRemoved);
        }
    }
}
