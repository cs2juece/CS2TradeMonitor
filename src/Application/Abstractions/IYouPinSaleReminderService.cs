using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Trading;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinSaleReminderService : IDisposable, IAutoConfirmationYouPinGateway
    {
        event Action? DataUpdated;
        void Configure(Settings settings);

        void ConfigureForExternalScheduler(Settings settings);

        Task RunDueChecksAsync();

        Task<YouPinSaleReminderCheckResult> CheckTodoNowAsync(bool useMock = false, bool notify = true);

        Task<YouPinSaleReminderCheckResult> CheckMsgCenterNowAsync(bool useMock = false, bool notify = true);

        Task<YouPinSaleOrder> EnrichOrderDetailAsync(YouPinSaleOrder order);

        void Notify(YouPinSaleOrder order);

        void Notify(YouPinSaleOrder order, bool isMsgCenter);

        string EnsureQuoteLogFile();
    }
}
