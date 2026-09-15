using System.Collections.Concurrent;

namespace CS2TradeMonitor.Shared.Trading
{
    public sealed class TradeWriteCoordinator
    {
        private readonly ConcurrentDictionary<string, GateState> _gates = new(StringComparer.OrdinalIgnoreCase);

        public async Task WaitAsync(
            string key,
            CancellationToken cancellationToken = default,
            TimeSpan? minimumInterval = null)
        {
            GateState gate = _gates.GetOrAdd(NormalizeKey(key), _ => new GateState());
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan interval = minimumInterval ?? TradeAutomationPolicy.MinimumWriteInterval;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                TimeSpan wait = (gate.LastGrantUtc + interval) - now;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                gate.LastGrantUtc = DateTimeOffset.UtcNow;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }

        public async Task<T> RunWithRetryAsync<T>(
            string key,
            Func<Task<T>> operation,
            Func<Exception, bool> isRetryable,
            Func<TradeWriteRetryContext, ValueTask>? retrying = null,
            CancellationToken cancellationToken = default,
            int maxAttempts = TradeAutomationPolicy.MaximumWriteAttempts,
            TimeSpan? retryDelay = null,
            TimeSpan? minimumInterval = null)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(isRetryable);

            maxAttempts = Math.Max(1, maxAttempts);
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await RunExclusiveAsync(
                        key,
                        operation,
                        cancellationToken,
                        minimumInterval).ConfigureAwait(false);
                }
                catch (Exception exception) when (attempt < maxAttempts && isRetryable(exception))
                {
                    if (retrying != null)
                    {
                        await retrying(new TradeWriteRetryContext(
                            Attempt: attempt,
                            NextAttempt: attempt + 1,
                            MaximumAttempts: maxAttempts,
                            Exception: exception)).ConfigureAwait(false);
                    }

                    await Task.Delay(
                        retryDelay ?? TradeAutomationPolicy.TransientRetryDelay,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("写操作重试流程异常结束。");
        }

        public async Task<T> RunExclusiveAsync<T>(
            string key,
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default,
            TimeSpan? minimumInterval = null)
        {
            ArgumentNullException.ThrowIfNull(operation);

            GateState gate = _gates.GetOrAdd(NormalizeKey(key), _ => new GateState());
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan interval = minimumInterval ?? TradeAutomationPolicy.MinimumWriteInterval;
                DateTimeOffset now = DateTimeOffset.UtcNow;
                TimeSpan wait = (gate.LastGrantUtc + interval) - now;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                gate.LastGrantUtc = DateTimeOffset.UtcNow;
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }

        public static string NormalizeKey(string? key)
        {
            string value = (key ?? string.Empty).Trim();
            return value.Length == 0 ? "global" : value;
        }

        private sealed class GateState
        {
            public SemaphoreSlim Semaphore { get; } = new(1, 1);
            public DateTimeOffset LastGrantUtc { get; set; } = DateTimeOffset.MinValue;
        }
    }

    public sealed record TradeWriteRetryContext(
        int Attempt,
        int NextAttempt,
        int MaximumAttempts,
        Exception Exception);
}
