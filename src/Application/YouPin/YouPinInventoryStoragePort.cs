using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.YouPin
{
    internal interface IYouPinInventoryStorageAdapter
    {
        Task<YouPinInventoryStorageViewState> ReadAsync(
            Settings settings,
            YouPinInventoryStorageQuery query,
            CancellationToken cancellationToken);

        Task<YouPinInventoryStorageWriteResult> WriteAsync(
            Settings settings,
            YouPinInventoryStorageTransferCommand command,
            CancellationToken cancellationToken);
    }

    internal sealed record YouPinInventoryStorageWriteResult(bool Accepted, string Message);
}
