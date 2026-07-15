using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core.State
{
    public sealed class RuntimeAppState : IRuntimeAppState
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly object _gate = new();
        private readonly SemaphoreSlim _cacheWriteLock = new(1, 1);

        private RuntimeAppState()
        {
        }

        public static RuntimeAppState Instance { get; } = new();

        public MarketSnapshot Market { get; private set; } = MarketSnapshot.Empty;

        public IReadOnlyList<MonitoredItemSnapshot> Items { get; private set; } = Array.Empty<MonitoredItemSnapshot>();

        public YouPinInventorySnapshot YouPinInventory { get; private set; } = YouPinInventorySnapshot.Empty;

        public YouPinTodoSnapshot YouPinTodo { get; private set; } = YouPinTodoSnapshot.Empty;

        public SteamOfferSnapshot SteamOffers { get; private set; } = SteamOfferSnapshot.Empty;

        public event EventHandler<StateChangedEventArgs>? StateChanged;

        public void UpdateMarket(MarketSnapshot snapshot, string reason = "MarketUpdated")
        {
            UpdateCore("Market", reason, () => Market = snapshot);
        }

        public void UpdateItems(IReadOnlyList<MonitoredItemSnapshot> snapshots, string reason = "ItemsUpdated")
        {
            UpdateCore("Items", reason, () => Items = snapshots);
        }

        public void UpdateYouPinInventory(YouPinInventorySnapshot snapshot, string reason = "YouPinInventoryUpdated")
        {
            UpdateCore("YouPinInventory", reason, () => YouPinInventory = snapshot);
        }

        public void UpdateYouPinTodo(YouPinTodoSnapshot snapshot, string reason = "YouPinTodoUpdated")
        {
            UpdateCore("YouPinTodo", reason, () => YouPinTodo = snapshot);
        }

        public void UpdateSteamOffers(SteamOfferSnapshot snapshot, string reason = "SteamOffersUpdated")
        {
            UpdateCore("SteamOffers", reason, () => SteamOffers = snapshot);
        }

        private void UpdateCore(string section, string reason, Action update)
        {
            var changedAt = DateTime.Now;
            lock (_gate)
            {
                update();
            }

            StateChanged?.Invoke(this, new StateChangedEventArgs(section, reason, changedAt));
            _ = SaveCacheSnapshotAsync();
        }

        private async Task SaveCacheSnapshotAsync()
        {
            if (!await _cacheWriteLock.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }

            try
            {
                RuntimeSnapshotEnvelope envelope;
                lock (_gate)
                {
                    envelope = new RuntimeSnapshotEnvelope
                    {
                        SavedAt = DateTime.Now,
                        Market = Market,
                        Items = Items,
                        YouPinInventory = YouPinInventory,
                        YouPinTodo = YouPinTodo,
                        SteamOffers = SteamOffers
                    };
                }

                string path = RuntimeDataPaths.GetCacheFilePath("runtime_snapshot.json");
                Directory.CreateDirectory(RuntimeDataPaths.CacheDirectory);
                var tempPath = path + ".tmp";
                var json = JsonSerializer.Serialize(envelope, JsonOptions);
                await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
                File.Move(tempPath, path, overwrite: true);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("RuntimeState", "Failed to write runtime snapshot cache: " + ex.Message);
            }
            finally
            {
                _cacheWriteLock.Release();
            }
        }

        private sealed class RuntimeSnapshotEnvelope
        {
            public DateTime SavedAt { get; init; }
            public MarketSnapshot Market { get; init; } = MarketSnapshot.Empty;
            public IReadOnlyList<MonitoredItemSnapshot> Items { get; init; } = Array.Empty<MonitoredItemSnapshot>();
            public YouPinInventorySnapshot YouPinInventory { get; init; } = YouPinInventorySnapshot.Empty;
            public YouPinTodoSnapshot YouPinTodo { get; init; } = YouPinTodoSnapshot.Empty;
            public SteamOfferSnapshot SteamOffers { get; init; } = SteamOfferSnapshot.Empty;
        }
    }
}
