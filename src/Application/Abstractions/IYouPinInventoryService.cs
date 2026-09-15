using CS2TradeMonitor.Application.YouPin;

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

        Task<YouPinInventoryFetchResult> FetchIfDueAsync(CancellationToken cancellationToken = default)
            => FetchNowAsync(useMock: false, cancellationToken);
    }
}
