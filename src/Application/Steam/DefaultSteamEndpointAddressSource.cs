using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class DefaultSteamEndpointAddressSource : ISteamEndpointAddressSource
    {
        private static readonly Uri[] Providers =
        {
            new("https://dns.alidns.com/resolve"),
            new("https://doh.pub/resolve")
        };

        private static readonly HttpClient DirectClient = CreateDirectClient();

        public async Task<IReadOnlyList<IPAddress>> ResolveSystemAsync(
            string host,
            CancellationToken cancellationToken)
        {
            try
            {
                return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or HttpRequestException)
            {
                DiagnosticsLogger.Ignored(
                    "SteamEndpoint",
                    "SystemDns",
                    ex,
                    retryable: true,
                    category: "Dns");
                return Array.Empty<IPAddress>();
            }
        }

        public async Task<IReadOnlyList<IPAddress>> ResolveFallbackAsync(
            string host,
            CancellationToken cancellationToken)
        {
            foreach (Uri provider in Providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    Task<IReadOnlyList<IPAddress>> ipv4 = QueryProviderSafeAsync(provider, host, "A", timeout.Token);
                    Task<IReadOnlyList<IPAddress>> ipv6 = QueryProviderSafeAsync(provider, host, "AAAA", timeout.Token);
                    IReadOnlyList<IPAddress>[] results = await Task.WhenAll(ipv4, ipv6).ConfigureAwait(false);
                    IPAddress[] addresses = results
                        .SelectMany(static value => value)
                        .Where(IsPublicAddress)
                        .Distinct()
                        .ToArray();
                    if (addresses.Length > 0)
                        return addresses;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Try the next mainland-accessible provider.
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or SocketException)
                {
                    DiagnosticsLogger.Ignored(
                        "SteamEndpoint",
                        "FallbackDns",
                        ex,
                        retryable: true,
                        category: "Dns");
                }
            }

            return Array.Empty<IPAddress>();
        }

        private static async Task<IReadOnlyList<IPAddress>> QueryProviderSafeAsync(
            Uri provider,
            string host,
            string recordType,
            CancellationToken cancellationToken)
        {
            try
            {
                return await QueryProviderAsync(provider, host, recordType, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Array.Empty<IPAddress>();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or SocketException)
            {
                DiagnosticsLogger.Ignored(
                    "SteamEndpoint",
                    "FallbackDnsRecord",
                    ex,
                    retryable: true,
                    category: "Dns");
                return Array.Empty<IPAddress>();
            }
        }

        private static async Task<IReadOnlyList<IPAddress>> QueryProviderAsync(
            Uri provider,
            string host,
            string recordType,
            CancellationToken cancellationToken)
        {
            string url = provider
                + "?name=" + Uri.EscapeDataString(host)
                + "&type=" + recordType;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/dns-json");
            using HttpResponseMessage response = await DirectClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("Status", out JsonElement status)
                || status.GetInt32() != 0
                || !document.RootElement.TryGetProperty("Answer", out JsonElement answers)
                || answers.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<IPAddress>();
            }

            var addresses = new List<IPAddress>();
            foreach (JsonElement answer in answers.EnumerateArray())
            {
                if (!answer.TryGetProperty("type", out JsonElement type)
                    || !answer.TryGetProperty("data", out JsonElement data))
                {
                    continue;
                }

                int expectedType = recordType == "AAAA" ? 28 : 1;
                if (type.GetInt32() == expectedType
                    && IPAddress.TryParse(data.GetString(), out IPAddress? address))
                {
                    addresses.Add(address);
                }
            }

            return addresses;
        }

        private static HttpClient CreateDirectClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            };
            return new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        internal static bool IsPublicAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
                return IsPublicAddress(address.MapToIPv4());

            if (IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.Any)
                || address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.None)
                || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal)
            {
                return false;
            }

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return bytes[0] != 0
                    && bytes[0] != 10
                    && bytes[0] != 127
                    && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                    && !(bytes[0] == 169 && bytes[1] == 254)
                    && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                    && !(bytes[0] == 192 && bytes[1] == 168)
                    && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                    && bytes[0] < 224;
            }

            return address.AddressFamily != AddressFamily.InterNetworkV6
                || (bytes[0] & 0xFE) != 0xFC;
        }
    }
}
