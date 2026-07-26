using CS2TradeMonitor.Application.Monitoring;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IAlertHistoryStore
    {
        Task AppendAsync(AlertHistoryEntry entry, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AlertHistoryEntry>> ReadLatestAsync(
            int limit,
            CancellationToken cancellationToken = default);
    }
}
