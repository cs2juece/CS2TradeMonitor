using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ILocalInventoryMonitorService : IDisposable
    {
        event EventHandler? DataUpdated;

        void Start(Settings settings);

        Task StopAsync();

        void Configure(Settings settings);

        Task RefreshAsync(bool force = false, CancellationToken cancellationToken = default);

        LocalInventoryMonitorSnapshot GetSnapshot();
    }
}
