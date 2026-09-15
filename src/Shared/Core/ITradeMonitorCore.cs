using CS2TradeMonitor.Shared.Contracts;

namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// The single application boundary used by every presentation and scheduling host.
    /// </summary>
    public interface ITradeMonitorCore
    {
        Task InitializeAsync(CancellationToken cancellationToken = default);

        Task<T> QueryAsync<T>(
            ICoreQuery<T> query,
            CancellationToken cancellationToken = default);

        Task<CoreCommandResult> ExecuteAsync(
            CoreCommand command,
            CancellationToken cancellationToken = default);

        Task<AutomationCycleResult> RunCycleAsync(
            AutomationCycleTrigger trigger,
            CancellationToken cancellationToken = default);
    }
}
