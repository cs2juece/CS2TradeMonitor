using CS2TradeMonitor.Shared.Contracts;

namespace CS2TradeMonitor.Shared.Core
{
    /// <summary>
    /// Makes the shared core the single write boundary for UI, foreground-service,
    /// background-worker, and recovery triggers within one application process.
    /// </summary>
    public sealed class SingleWriterTradeMonitorCore : ITradeMonitorCore
    {
        private readonly ITradeMonitorCore _inner;
        private readonly SemaphoreSlim _initializationGate = new(1, 1);
        private readonly SemaphoreSlim _writerGate = new(1, 1);
        private Task? _initialization;

        public SingleWriterTradeMonitorCore(ITradeMonitorCore inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Task initialization;
            await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _initialization ??= _inner.InitializeAsync(CancellationToken.None);
                initialization = _initialization;
            }
            finally
            {
                _initializationGate.Release();
            }

            await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<T> QueryAsync<T>(
            ICoreQuery<T> query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return await _inner.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        }

        public async Task<CoreCommandResult> ExecuteAsync(
            CoreCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _inner.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writerGate.Release();
            }
        }

        public async Task<AutomationCycleResult> RunCycleAsync(
            AutomationCycleTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await _inner.RunCycleAsync(trigger, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writerGate.Release();
            }
        }
    }
}
