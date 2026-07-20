using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinInventoryStorageService
    {
        Task<YouPinInventoryStorageViewState> LoadAsync(
            Settings settings,
            YouPinInventoryStorageQuery query,
            CancellationToken cancellationToken = default);

        Task<YouPinInventoryStorageTransferResult> ExecuteAsync(
            Settings settings,
            YouPinInventoryStorageTransferCommand command,
            CancellationToken cancellationToken = default);
    }
}
