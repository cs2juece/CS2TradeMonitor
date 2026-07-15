using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamTradeOfferClient
    {
        Task<TradeOffersResult> GetTradeOffersAsync(SteamAuthCredential credential);

        Task<TradeOffersResult> GetTradeOffersForDetailLookupAsync(SteamAuthCredential credential);

        Task<TradeOffersResult> GetHistoricalTradeOffersForDetailLookupAsync(
            SteamAuthCredential credential,
            TimeSpan maxAge);

        Task<TradeOffersResult> GetTradeOffersFromWebSessionAsync(SteamAuthCredential credential);

        Task<TradeOfferDetail> GetTradeOfferFromWebSessionAsync(SteamAuthCredential credential, string tradeOfferId);

        Task EnrichTradeOfferDetailAssetsAsync(SteamAuthCredential credential, TradeOfferDetail detail);

        Task<TradeOfferDetail> GetTradeOfferAsync(SteamAuthCredential credential, string tradeOfferId);

        Task<bool> AcknowledgeNewTradeAsync(SteamAuthCredential credential, string tradeOfferId);

        Task<SteamTradeOfferAcceptResult> AcceptTradeOfferAsync(SteamAuthCredential credential, string tradeOfferId);
    }
}
