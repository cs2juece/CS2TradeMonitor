using System;
using System.Net;
using System.Net.Http;
using CS2TradeMonitor.Infrastructure.Diagnostics;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class HttpClientProvider
    {
        public static HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = true,
            bool allowAutoRedirect = true)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = decompression,
                AllowAutoRedirect = allowAutoRedirect,
                UseCookies = useCookies,
                UseProxy = true,
                Proxy = CreateSystemProxy(),
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 8
            };

            var client = new HttpClient(new DetailedDiagnosticsHttpHandler("HTTP", handler))
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))
            };

            if (baseAddress is not null)
                client.BaseAddress = baseAddress;

            return client;
        }

        private static IWebProxy? CreateSystemProxy()
        {
            try
            {
                return AdaptiveSystemProxyFactory.Create();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("HTTP", "ResolveSystemProxy", ex, retryable: true, category: "Proxy");
                return null;
            }
        }
    }
}
