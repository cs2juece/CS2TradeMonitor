using CS2TradeMonitor.Shared.Contracts;

namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// A cohesive slice of the production core. Modules own business behavior;
    /// platform hosts only provide ports.
    /// </summary>
    public interface ITradeMonitorCoreModule
    {
        bool CanHandle(string bindingName);

        Task InitializeAsync(CancellationToken cancellationToken = default);

        Task<FeatureStateProjection> QueryAsync(
            FeatureStateQuery query,
            CancellationToken cancellationToken = default);

        Task<CoreCommandResult> ExecuteAsync(
            FeatureCommand command,
            CancellationToken cancellationToken = default);

        Task<AutomationCycleResult?> RunCycleAsync(
            AutomationCycleTrigger trigger,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Keeps latency-sensitive trade and confirmation work ahead of slower
    /// local refresh modules without letting platform hosts reorder business behavior.
    /// Lower values run first; registration order is preserved for equal values.
    /// </summary>
    public interface IAutomationCyclePriority
    {
        int AutomationCyclePriority { get; }
    }
}
