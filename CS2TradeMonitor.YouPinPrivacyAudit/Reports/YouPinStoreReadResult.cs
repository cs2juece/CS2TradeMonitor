using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using System.Net;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports
{
    public enum YouPinStoreReadStatus
    {
        NotAttempted,
        Completed,
        Failed
    }

    /// <summary>
    /// Store-read outcome carried into a report without raw error or response content.
    /// </summary>
    public sealed class YouPinStoreReadResult
    {
        private YouPinStoreReadResult(
            long userId,
            YouPinStoreReadStatus status,
            YouPinPublicStoreSummary? summary,
            string? reasonCode,
            HttpStatusCode? statusCode,
            int? platformCode)
        {
            UserId = userId;
            Status = status;
            Summary = summary;
            ReasonCode = reasonCode;
            StatusCode = statusCode;
            PlatformCode = platformCode;
        }

        public long UserId { get; }
        public YouPinStoreReadStatus Status { get; }
        public YouPinPublicStoreSummary? Summary { get; }
        public string? ReasonCode { get; }
        public HttpStatusCode? StatusCode { get; }
        public int? PlatformCode { get; }

        public static YouPinStoreReadResult NotAttempted(long userId)
        {
            EnsurePositiveUserId(userId);
            return new YouPinStoreReadResult(
                userId,
                YouPinStoreReadStatus.NotAttempted,
                summary: null,
                reasonCode: null,
                statusCode: null,
                platformCode: null);
        }

        public static YouPinStoreReadResult Completed(YouPinPublicStoreSummary summary)
        {
            ArgumentNullException.ThrowIfNull(summary);
            EnsurePositiveUserId(summary.UserId);
            return new YouPinStoreReadResult(
                summary.UserId,
                YouPinStoreReadStatus.Completed,
                summary,
                reasonCode: null,
                statusCode: null,
                platformCode: null);
        }

        internal static YouPinStoreReadResult Failed(long userId, YouPinPublicApiException error)
        {
            EnsurePositiveUserId(userId);
            ArgumentNullException.ThrowIfNull(error);
            return new YouPinStoreReadResult(
                userId,
                YouPinStoreReadStatus.Failed,
                summary: null,
                error.ReasonCode,
                error.StatusCode,
                error.PlatformCode);
        }

        private static void EnsurePositiveUserId(long userId)
        {
            if (userId <= 0)
                throw new ArgumentOutOfRangeException(nameof(userId), "用户 ID 必须为正整数。");
        }
    }
}
