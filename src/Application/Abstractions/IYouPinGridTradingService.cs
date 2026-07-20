using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinGridTradingService
    {
        event Action? DataUpdated;

        void Configure(Settings settings);

        YouPinGridRuntimeSnapshot GetSnapshot();

        Task<YouPinGridRuntimeSnapshot> RefreshAsync(
            Settings settings,
            CancellationToken cancellationToken = default);

        Task<YouPinGridMutationResult> UpsertStrategyAsync(
            YouPinGridStrategy strategy,
            CancellationToken cancellationToken = default);

        Task<YouPinGridMutationResult> DeleteStrategyAsync(
            string strategyId,
            CancellationToken cancellationToken = default);
    }
}
