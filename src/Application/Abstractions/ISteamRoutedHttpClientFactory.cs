using System.Net;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamRoutedHttpClientFactory
    {
        HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = false,
            bool allowAutoRedirect = true);

        Task<HttpClient> CreateResolvedAsync(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = false,
            bool allowAutoRedirect = true,
            CancellationToken cancellationToken = default);
    }
}
