using System.Net;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Safe per-template failure metadata. It never contains response content or credentials.
    /// </summary>
    public sealed record YouPinTemplateQueryFailure(
        long TemplateId,
        string ReasonCode,
        HttpStatusCode? StatusCode = null,
        int? PlatformCode = null);
}
