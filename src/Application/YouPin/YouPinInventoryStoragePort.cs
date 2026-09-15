using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.YouPin
{
    public interface IYouPinInventoryStorageAdapter
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

    public sealed record YouPinInventoryStorageWriteResult(bool Accepted, string Message);
}
