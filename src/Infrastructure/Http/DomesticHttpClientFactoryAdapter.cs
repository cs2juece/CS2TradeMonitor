using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using System.Net;

namespace CS2TradeMonitor.Infrastructure.Http
{
    public sealed class DomesticHttpClientFactoryAdapter : IDomesticHttpClientFactory
    {
        public HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = true,
            bool allowAutoRedirect = true)
        {
            return HttpClientProvider.Create(timeoutSeconds, baseAddress, decompression, useCookies, allowAutoRedirect);
        }
    }
}
