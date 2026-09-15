using System.Text.Json;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Shared.Trading
{
    public interface IAutoConfirmationSteamGateway
    {
        IReadOnlyList<SteamOfferItem> GetAutoTradeOffers();

        Task<SteamOfferActionResult> LoadOffersForAutoTradeAsync();

        Task<SteamOfferActionResult> AcceptAutoTradeOfferAsync(SteamAutoTradePlanItem plan);

        Task<SteamOfferActionResult> ConfirmMatchedMobileTradeAsync(SteamAutoTradePlanItem plan);

        Task<SteamTradeOfferStatusResult> QueryTradeOfferStatusAsync(string tradeOfferId);
    }

    public interface IAutoConfirmationYouPinGateway
    {
        event Action<IReadOnlyList<YouPinSaleOrder>>? NewWaitDeliverOrdersDetected;

        YouPinSaleReminderState GetState();

        Task<YouPinSaleReminderCheckResult> CheckQuoteNowAsync(string trigger = "立即刷新");

        Task<YouPinSaleActionResult> SendOfferAsync(string orderNo, string trigger = "用户手动");

        Task<YouPinSaleActionResult> ConfirmOfferAsync(
            string orderNo,
            string tradeOfferId = "",
            string trigger = "用户手动");

        Task<YouPinSaleActionResult> QueryOfferStatusAsync(string orderNo, string trigger = "用户手动");

        Task<YouPinSaleActionResult> QueryTradeOfferIdAsync(string orderNo);
    }

    public interface IAutoConfirmationRuntime
    {
        JsonSerializerOptions JsonSerializerOptions { get; }

        string GetDataFilePath(string fileName);

        void WriteTextAtomic(string path, string content);
    }

    public interface IAutoConfirmationAuditLog
    {
        string BackgroundTrigger { get; }

        string RedactSecrets(string? text);

        void LogAutoTradeStarted(bool enabled, int intervalSeconds);

        void LogAutoTradeFailure(string reason);

        void Error(string message, Exception? exception = null);

        void DiagnosticError(string message, Exception? exception = null);
    }
}
