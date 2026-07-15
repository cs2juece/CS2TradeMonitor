using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Infrastructure.Diagnostics;

namespace CS2TradeMonitor.Application.Steam
{
    public static class SteamHttpClientFactory
    {
        private static ISteamConnectionResolver SteamConnection => SteamServiceRuntimeServices.ResolveConnection();
        private static ISteamManualProxyStore ManualProxyStore => SteamServiceRuntimeServices.ResolveManualProxyStore();

        public static HttpClient Create(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = false,
            bool allowAutoRedirect = true)
        {
            SteamConnection.EnsureResolvedInBackground();
            var profile = SteamConnection.GetBestKnownProfile();
            return CreateForProfile(profile, timeoutSeconds, baseAddress, decompression, useCookies, allowAutoRedirect);
        }

        public static async Task<HttpClient> CreateResolvedAsync(
            int timeoutSeconds = 20,
            Uri? baseAddress = null,
            DecompressionMethods decompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            bool useCookies = false,
            bool allowAutoRedirect = true,
            CancellationToken cancellationToken = default)
        {
            var profile = await SteamConnection.ResolveAsync(force: false, cancellationToken).ConfigureAwait(false);
            return CreateForProfile(profile, timeoutSeconds, baseAddress, decompression, useCookies, allowAutoRedirect);
        }

        private static HttpClient CreateForProfile(
            SteamConnectionProfile profile,
            int timeoutSeconds,
            Uri? baseAddress,
            DecompressionMethods decompression,
            bool useCookies,
            bool allowAutoRedirect)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = decompression,
                AllowAutoRedirect = allowAutoRedirect,
                UseCookies = useCookies,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 8,
                ConnectTimeout = TimeSpan.FromSeconds(Math.Max(2, Math.Min(timeoutSeconds, 8)))
            };

            bool tracksDirectEndpoints = profile.Mode is SteamConnectionMode.Direct
                or SteamConnectionMode.Failed
                or SteamConnectionMode.Unknown;
            SteamEndpointRouteTracker? endpointTracker = tracksDirectEndpoints
                ? new SteamEndpointRouteTracker()
                : null;
            ConfigureTransport(handler, profile, endpointTracker);

            TimeSpan requestTimeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
            var healthHandler = new SteamConnectionHealthHandler(
                SteamConnection,
                handler,
                requestTimeout,
                endpointTracker);
            var client = new HttpClient(new DetailedDiagnosticsHttpHandler("SteamHTTP", healthHandler))
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            if (baseAddress is not null)
                client.BaseAddress = baseAddress;

            return client;
        }

        internal sealed class SteamConnectionHealthHandler : DelegatingHandler
        {
            private readonly ISteamConnectionResolver _resolver;
            private readonly TimeSpan _requestTimeout;
            private readonly SteamEndpointRouteTracker? _endpointTracker;

            public SteamConnectionHealthHandler(
                ISteamConnectionResolver resolver,
                HttpMessageHandler innerHandler,
                TimeSpan? requestTimeout = null,
                SteamEndpointRouteTracker? endpointTracker = null)
                : base(innerHandler)
            {
                _resolver = resolver;
                _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
                _endpointTracker = endpointTracker;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                HttpResponseMessage? response = null;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_requestTimeout);
                    response = await base.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    byte[] contentBytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                    var bufferedContent = new ByteArrayContent(contentBytes);
                    foreach (var header in response.Content.Headers)
                        bufferedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    response.Content.Dispose();
                    response.Content = bufferedContent;
                    _resolver.ReportSuccess();
                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    response?.Dispose();
                    ReportEndpointFailure(request);
                    _resolver.ReportFailure("Steam 网络请求超时。");
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    response?.Dispose();
                    ReportEndpointFailure(request);
                    _resolver.ReportFailure(NetworkDiagnostics.BuildFailureMessage("Steam", "Request", ex));
                    throw;
                }
                catch
                {
                    response?.Dispose();
                    throw;
                }
            }

            private void ReportEndpointFailure(HttpRequestMessage request)
            {
                if (_endpointTracker is null
                    || !_endpointTracker.TryGet(request, out SteamEndpointConnectionIdentity identity))
                    return;

                SteamEndpointConnectionManager.Instance.ReportTransportFailure(identity);
            }
        }

        internal static void ConfigureTransport(
            SocketsHttpHandler handler,
            SteamConnectionProfile profile,
            SteamEndpointRouteTracker? endpointTracker = null)
        {
            switch (profile.Mode)
            {
                case SteamConnectionMode.ManualProxy:
                    string manual = ManualProxyStore.Load();
                    handler.UseProxy = !string.IsNullOrWhiteSpace(manual);
                    handler.Proxy = SteamConnectionResolver.CreateProxy(manual);
                    handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
                    break;

                case SteamConnectionMode.AutoProxy:
                    handler.UseProxy = true;
                    handler.Proxy = ResolveAutoProxy(profile);
                    handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
                    if (handler.Proxy == null)
                        handler.UseProxy = false;
                    break;

                case SteamConnectionMode.Direct:
                case SteamConnectionMode.Failed:
                case SteamConnectionMode.Unknown:
                default:
                    handler.UseProxy = false;
                    endpointTracker ??= new SteamEndpointRouteTracker();
                    handler.ConnectCallback = (context, cancellationToken) =>
                        SteamEndpointConnectionManager.Instance.ConnectAsync(
                            context,
                            endpointTracker,
                            cancellationToken);
                    break;
            }
        }

        private static IWebProxy? ResolveAutoProxy(SteamConnectionProfile profile)
        {
            string proxyUri = (profile.ProxyUri ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(proxyUri))
                return SteamConnectionResolver.CreateProxy(proxyUri);

            try
            {
                var proxy = WebRequest.GetSystemWebProxy();
                proxy.Credentials = CredentialCache.DefaultCredentials;
                return proxy;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("SteamConnection", "ResolveCachedSystemProxy", ex, retryable: true, category: "Proxy");
                return null;
            }
        }
    }
}
