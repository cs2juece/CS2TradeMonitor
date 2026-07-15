using CS2TradeMonitor.Application.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamSessionKeepAliveService
    {
        void Start();

        void Stop();

        Task<SteamOfferActionResult> CheckNowAsync(CancellationToken cancellationToken = default);
    }
}
