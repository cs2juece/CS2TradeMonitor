using System.Net;
using CS2TradeMonitor.Shared.Trading;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IDomesticHttpClientFactory : IYouPinHttpClientFactory
    {
        HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = true,
            bool allowAutoRedirect = true);

        HttpClient IYouPinHttpClientFactory.Create(int timeoutSeconds)
            => Create(timeoutSeconds);
    }
}
