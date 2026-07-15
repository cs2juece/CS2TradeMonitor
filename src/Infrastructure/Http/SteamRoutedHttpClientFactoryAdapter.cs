using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using System.Net;

namespace CS2TradeMonitor.Infrastructure.Http
{
    public sealed class SteamRoutedHttpClientFactoryAdapter : ISteamRoutedHttpClientFactory
    {
        public HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = false,
            bool allowAutoRedirect = true)
        {
            return SteamHttpClientFactory.Create(timeoutSeconds, baseAddress, decompression, useCookies, allowAutoRedirect);
        }

        public Task<HttpClient> CreateResolvedAsync(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = false,
            bool allowAutoRedirect = true,
            CancellationToken cancellationToken = default)
        {
            return SteamHttpClientFactory.CreateResolvedAsync(
                timeoutSeconds,
                baseAddress,
                decompression,
                useCookies,
                allowAutoRedirect,
                cancellationToken);
        }
    }
}
