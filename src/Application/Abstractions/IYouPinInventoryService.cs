using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinInventoryService : IDisposable
    {
        event Action? DataUpdated;

        void Configure(Settings settings);

        void SetBackgroundRefreshConsumer(string consumerKey, TimeSpan? refreshInterval);

        YouPinInventoryState GetState();

        YouPinStopProfitLossState GetStopProfitLossState();

        YouPinInventoryTrendState GetTrendState();

        Task<YouPinInventoryFetchResult> FetchNowAsync(bool useMock = false, CancellationToken cancellationToken = default);
    }
}
