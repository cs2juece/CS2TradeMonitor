using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinProfitLossService : IDisposable
    {
        event Action? DataUpdated;

        YouPinProfitLossState GetState(Settings? settings);

        Task<YouPinProfitLossRefreshResult> RefreshAsync(Settings? settings, CancellationToken cancellationToken = default);
    }
}
