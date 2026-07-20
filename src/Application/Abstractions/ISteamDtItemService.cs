using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISteamDtItemService : IDisposable
    {
        event Action? DataUpdated;

        bool IsLocalItemDatabaseAvailable { get; }

        void Configure(string apiKey);

        SteamDtItemData? GetItemData(string itemId);

        Task FetchAllEnabledItemsAsync();

        Task<bool> FetchItemPriceAsync(ItemMonitorConfig item);

        Task<List<SteamDtSearchCandidate>> SearchItemsAsync(string keyword);

        Task<(bool Success, string Message, int Count, DateTime RefreshTime)> ForceRefreshBaseCacheAsync();
    }
}
