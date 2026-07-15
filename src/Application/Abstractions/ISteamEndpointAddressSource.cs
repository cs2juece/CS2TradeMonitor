using System.Net;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamEndpointAddressSource
    {
        Task<IReadOnlyList<IPAddress>> ResolveSystemAsync(
            string host,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<IPAddress>> ResolveFallbackAsync(
            string host,
            CancellationToken cancellationToken);
    }
}
