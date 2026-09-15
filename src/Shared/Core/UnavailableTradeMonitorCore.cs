using CS2TradeMonitor.Shared.Contracts;

namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// Safe bootstrap implementation for hosts whose platform adapters are not ready yet.
    /// It never performs a write and never fabricates query data.
    /// </summary>
    public sealed class UnavailableTradeMonitorCore : ITradeMonitorCore
    {
        public const string ReasonCode = "core.unavailable";

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<T> QueryAsync<T>(
            ICoreQuery<T> query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (query is CoreHealthQuery)
            {
                var health = new CoreHealthProjection(
                    IsReady: false,
                    ReasonCode,
                    "共享交易核心尚未连接。",
                    RegisteredModuleCount: 0,
                    CapturedAt: DateTimeOffset.UtcNow);
                return Task.FromResult((T)(object)health);
            }

            if (query is FeatureStateQuery featureQuery)
            {
                var projection = new FeatureStateProjection(
                    featureQuery.SemanticId,
                    FeatureAvailability.NotAvailable,
                    ReasonCode,
                    "共享交易核心尚未连接。",
                    StateJson: "{}",
                    Version: 0,
                    CapturedAt: DateTimeOffset.UtcNow);
                return Task.FromResult((T)(object)projection);
            }

            return Task.FromException<T>(new CoreUnavailableException());
        }

        public Task<CoreCommandResult> ExecuteAsync(
            CoreCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            return Task.FromResult(CoreCommandResult.Disabled(
                ReasonCode,
                "共享交易核心尚未连接。",
                command.CorrelationId));
        }

        public Task<AutomationCycleResult> RunCycleAsync(
            AutomationCycleTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            return Task.FromResult(new AutomationCycleResult(
                AutomationCycleStatus.Skipped,
                ReasonCode,
                "共享交易核心尚未连接。",
                trigger.CorrelationId,
                SnapshotVersion: 0));
        }
    }

    public sealed class CoreUnavailableException : InvalidOperationException
    {
        public CoreUnavailableException()
            : base("共享交易核心尚未连接，无法返回查询结果。")
        {
        }
    }
}
