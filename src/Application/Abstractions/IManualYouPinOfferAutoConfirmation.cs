using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IManualYouPinOfferAutoConfirmation
    {
        Task HandleManuallySentYouPinOfferAsync(
            YouPinSaleOrder order,
            YouPinSaleActionResult sendResult,
            CancellationToken cancellationToken = default);
    }
}
