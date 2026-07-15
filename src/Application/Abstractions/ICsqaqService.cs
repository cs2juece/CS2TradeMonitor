using CS2TradeMonitor.Application.Market;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ICsqaqService : IDisposable
    {
        CsqaqData? Latest { get; }

        string LastError { get; }

        event Action? DataUpdated;

        void Configure(string apiToken);

        string GetValue(string key);

        Task<bool> TestAndUpdateAsync(string apiToken, int refreshSec);

        Task FetchAsync(bool force = false);
    }
}
