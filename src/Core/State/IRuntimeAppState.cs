using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.src.Core.State
{
    public interface IRuntimeAppState
    {
        MarketSnapshot Market { get; }

        IReadOnlyList<MonitoredItemSnapshot> Items { get; }

        YouPinInventorySnapshot YouPinInventory { get; }

        YouPinTodoSnapshot YouPinTodo { get; }

        SteamOfferSnapshot SteamOffers { get; }

        event EventHandler<StateChangedEventArgs>? StateChanged;

        void UpdateMarket(MarketSnapshot snapshot, string reason = "MarketUpdated");

        void UpdateItems(IReadOnlyList<MonitoredItemSnapshot> snapshots, string reason = "ItemsUpdated");

        void UpdateYouPinInventory(YouPinInventorySnapshot snapshot, string reason = "YouPinInventoryUpdated");

        void UpdateYouPinTodo(YouPinTodoSnapshot snapshot, string reason = "YouPinTodoUpdated");

        void UpdateSteamOffers(SteamOfferSnapshot snapshot, string reason = "SteamOffersUpdated");
    }
}
