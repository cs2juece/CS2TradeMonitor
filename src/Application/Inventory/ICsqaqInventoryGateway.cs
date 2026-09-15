using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.Application.Inventory
{
    public interface ICsqaqInventoryGateway : IDisposable
    {
        Task<CsqaqInventoryTargetRecord?> ResolveTargetAsync(
            string apiToken,
            string steamId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<LocalInventoryChangeEvent>> GetRecentEventsAsync(
            string apiToken,
            CsqaqInventoryTargetRecord target,
            CancellationToken cancellationToken);
    }

    public sealed record CsqaqInventoryTargetRecord(
        int TaskId,
        string SteamId,
        string SteamName,
        int TotalItemCount,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? LastChangedAt);
}
