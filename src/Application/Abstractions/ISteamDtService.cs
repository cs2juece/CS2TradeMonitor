using CS2TradeMonitor.Application.Market;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamDtService : IDisposable
    {
        SteamDtData? Latest { get; }

        string LastError { get; }

        event Action? DataUpdated;

        void Configure(string apiKey, int refreshSec);

        Task<bool> TestAndUpdateAsync(string apiKey, int refreshSec);

        Task FetchAsync(bool waitForLock = false);

        string GetValue(string key);
    }
}
