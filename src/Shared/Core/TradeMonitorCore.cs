using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Ports;

namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// Production application boundary shared by every host. It routes only to
    /// registered Shared modules and never fabricates a successful operation.
    /// </summary>
    public sealed class TradeMonitorCore : ITradeMonitorCore
    {
        private readonly IReadOnlyList<ITradeMonitorCoreModule> _modules;
        private readonly IAppDiagnostics? _diagnostics;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private bool _initialized;

        public TradeMonitorCore(
            IEnumerable<ITradeMonitorCoreModule> modules,
            IAppDiagnostics? diagnostics = null)
        {
            ArgumentNullException.ThrowIfNull(modules);
            _modules = modules.ToArray();
            _diagnostics = diagnostics;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _initialized))
                return;

            await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized)
                    return;

                foreach (ITradeMonitorCoreModule module in _modules)
                    await module.InitializeAsync(cancellationToken).ConfigureAwait(false);

                Volatile.Write(ref _initialized, true);
            }
            finally
            {
                _initializationGate.Release();
            }
        }

        public async Task<T> QueryAsync<T>(
            ICoreQuery<T> query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

            if (query is CoreHealthQuery)
            {
                bool hasModules = _modules.Count > 0;
                var health = new CoreHealthProjection(
                    IsReady: hasModules,
                    ReasonCode: hasModules ? "ok" : "core.no-modules",
                    Message: hasModules ? "系统服务已连接。" : "系统服务尚未准备完成。",
                    RegisteredModuleCount: _modules.Count,
                    CapturedAt: DateTimeOffset.UtcNow);
                return (T)(object)health;
            }

            if (query is not FeatureStateQuery featureQuery)
                throw new NotSupportedException($"Shared core query type is not registered: {query.GetType().Name}.");

            ITradeMonitorCoreModule? module = Resolve(featureQuery.BindingName);
            FeatureStateProjection projection = module is null
                ? UnavailableProjection(featureQuery.SemanticId)
                : await module.QueryAsync(featureQuery, cancellationToken).ConfigureAwait(false);
            return (T)(object)projection;
        }

        public async Task<CoreCommandResult> ExecuteAsync(
            CoreCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

            if (command is not FeatureCommand featureCommand)
            {
                return CoreCommandResult.Disabled(
                    "core.command-unavailable",
                    "该操作暂不可用。",
                    command.CorrelationId);
            }

            ITradeMonitorCoreModule? module = Resolve(featureCommand.BindingName);
            if (module is null)
            {
                return CoreCommandResult.Disabled(
                    "core.binding-unavailable",
                    "该功能暂不可用。",
                    command.CorrelationId);
            }

            try
            {
                return await module.ExecuteAsync(featureCommand, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                await ReportFailureAsync("core.command.failed", command.CorrelationId, cancellationToken)
                    .ConfigureAwait(false);
                return CoreCommandResult.Failed(
                    "core.command.failed",
                    "系统未能完成该操作，请查看诊断后重试。",
                    command.CorrelationId);
            }
        }

        public async Task<AutomationCycleResult> RunCycleAsync(
            AutomationCycleTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

            var results = new List<AutomationCycleResult>();
            foreach (ITradeMonitorCoreModule module in _modules.OrderBy(module =>
                         (module as IAutomationCyclePriority)?.AutomationCyclePriority ?? 0))
            {
                AutomationCycleResult? result = await module.RunCycleAsync(trigger, cancellationToken)
                    .ConfigureAwait(false);
                if (result is not null)
                    results.Add(result);
            }

            if (results.Count == 0)
            {
                return new AutomationCycleResult(
                    AutomationCycleStatus.Skipped,
                    "core.no-automation-module",
                    "当前没有可运行的自动化任务。",
                    trigger.CorrelationId,
                    SnapshotVersion: 0);
            }

            AutomationCycleStatus status = results.Any(result => result.Status == AutomationCycleStatus.Failed)
                ? AutomationCycleStatus.Failed
                : results.Any(result => result.Status == AutomationCycleStatus.Pending)
                    ? AutomationCycleStatus.Pending
                    : results.All(result => result.Status == AutomationCycleStatus.Skipped)
                        ? AutomationCycleStatus.Skipped
                        : AutomationCycleStatus.Completed;
            return new AutomationCycleResult(
                status,
                status == AutomationCycleStatus.Completed ? "ok" : "core.cycle.partial",
                status == AutomationCycleStatus.Completed ? "自动化任务已完成。" : "自动化任务已完成检查。",
                trigger.CorrelationId,
                results.Max(result => result.SnapshotVersion),
                results.Sum(result => result.ProcessedCount));
        }

        private ITradeMonitorCoreModule? Resolve(string bindingName)
        {
            if (string.IsNullOrWhiteSpace(bindingName))
                return null;

            return _modules.FirstOrDefault(module => module.CanHandle(bindingName));
        }

        private static FeatureStateProjection UnavailableProjection(string semanticId)
        {
            return new FeatureStateProjection(
                semanticId,
                FeatureAvailability.NotAvailable,
                "core.binding-unavailable",
                "该功能暂不可用。",
                StateJson: "{}",
                Version: 0,
                CapturedAt: DateTimeOffset.UtcNow);
        }

        private ValueTask ReportFailureAsync(
            string code,
            string correlationId,
            CancellationToken cancellationToken)
        {
            return _diagnostics?.ReportAsync(
                new DiagnosticEvent(
                    code,
                    "系统操作执行失败。",
                    DiagnosticSeverity.Error,
                    DateTimeOffset.UtcNow,
                    correlationId),
                cancellationToken) ?? ValueTask.CompletedTask;
        }
    }
}
