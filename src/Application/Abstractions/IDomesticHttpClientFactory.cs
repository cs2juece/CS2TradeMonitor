using System.Net;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IDomesticHttpClientFactory
    {
        HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = true,
            bool allowAutoRedirect = true);
    }
}
