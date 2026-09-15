using System.Net;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Safe failure that contains status metadata but never raw request or response content.
    /// </summary>
    public sealed class YouPinPublicApiException : Exception
    {
        public YouPinPublicApiException(
            string reasonCode,
            string message,
            HttpStatusCode? statusCode = null,
            int? platformCode = null)
            : base(message)
        {
            ReasonCode = reasonCode;
            StatusCode = statusCode;
            PlatformCode = platformCode;
        }

        public string ReasonCode { get; }
        public HttpStatusCode? StatusCode { get; }
        public int? PlatformCode { get; }
    }
}
