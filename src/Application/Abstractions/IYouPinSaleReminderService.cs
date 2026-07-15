using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IYouPinSaleReminderService : IDisposable
    {
        event Action? DataUpdated;
        event Action<IReadOnlyList<YouPinSaleOrder>>? NewWaitDeliverOrdersDetected;

        void Configure(Settings settings);

        YouPinSaleReminderState GetState();

        Task<YouPinSaleReminderCheckResult> CheckTodoNowAsync(bool useMock = false, bool notify = true);

        Task<YouPinSaleReminderCheckResult> CheckQuoteNowAsync(string trigger = "立即刷新");

        Task<YouPinSaleReminderCheckResult> CheckMsgCenterNowAsync(bool useMock = false, bool notify = true);

        Task<YouPinSaleActionResult> SendOfferAsync(string orderNo, string trigger = "用户手动");

        Task<YouPinSaleActionResult> ConfirmOfferAsync(string orderNo, string tradeOfferId = "", string trigger = "用户手动");

        Task<YouPinSaleActionResult> QueryOfferStatusAsync(string orderNo, string trigger = "用户手动");

        Task<YouPinSaleActionResult> QueryTradeOfferIdAsync(string orderNo);

        Task<YouPinSaleOrder> EnrichOrderDetailAsync(YouPinSaleOrder order);

        void Notify(YouPinSaleOrder order);

        void Notify(YouPinSaleOrder order, bool isMsgCenter);

        string EnsureQuoteLogFile();
    }
}
