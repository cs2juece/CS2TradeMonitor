using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamConfirmationClient
    {
        long TimeOffset { get; set; }

        Task SyncTimeOffsetAsync();

        Task<string> FetchConfirmationsRawAsync(SteamAuthCredential credential);

        Task<string> FetchConfirmationDetailsHtmlAsync(
            SteamAuthCredential credential,
            string confirmationId);

        Task<bool> SendConfirmationAjaxAsync(
            SteamAuthCredential credential,
            string confirmationId,
            string confirmationKey,
            string op);

        Task<SteamConfirmationBatchResult> SendMultipleConfirmationsAsync(
            SteamAuthCredential credential,
            IReadOnlyList<SteamConfirmationRequest> confirmations,
            string op);
    }
}
