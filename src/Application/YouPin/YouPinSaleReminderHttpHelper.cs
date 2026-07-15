using System.Net.Http;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleReminderHttpHelper
    {
        public static void ApplyYouPinHeaders(HttpRequestMessage req, string token, string device, string uk)
        {
            YouPinMobileApiClient.ApplyHeaders(req, token, device, uk);
        }

        public static void ApplyYouPinLegacyAndroidHeaders(HttpRequestMessage req, string token, string device, string uk)
        {
            ApplyYouPinHeaders(req, token, device, uk);

            req.Headers.Remove("user-agent");
            req.Headers.Remove("App-Version");
            req.Headers.Remove("App-Source");
            req.Headers.Remove("traceId");
            req.Headers.Remove("currentTheme");
            req.Headers.TryAddWithoutValidation("user-agent", "okhttp/3.14.9");
            req.Headers.TryAddWithoutValidation("App-Version", "5.28.3");
        }

        public static string Sanitize(string message)
        {
            return YouPinMobileApiClient.Sanitize(message);
        }
    }
}
