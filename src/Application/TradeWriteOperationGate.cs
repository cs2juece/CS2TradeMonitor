using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application
{
    internal static class TradeWriteOperationGate
    {
        public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(1800);
        public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(5);
        public const int DefaultMaxAttempts = 3;

        private static readonly ConcurrentDictionary<string, GateState> Gates = new(StringComparer.OrdinalIgnoreCase);

        public static async Task WaitAsync(
            string key,
            CancellationToken cancellationToken = default,
            TimeSpan? minimumInterval = null)
        {
            var gate = Gates.GetOrAdd(NormalizeKey(key), _ => new GateState());
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                TimeSpan interval = minimumInterval ?? DefaultMinimumInterval;
                DateTime now = DateTime.UtcNow;
                TimeSpan wait = (gate.LastGrantUtc + interval) - now;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                gate.LastGrantUtc = DateTime.UtcNow;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }

        public static async Task<T> RunWithRetryAsync<T>(
            string key,
            Func<Task<T>> operation,
            Func<Exception, bool> isRetryable,
            string operationName,
            CancellationToken cancellationToken = default,
            int maxAttempts = DefaultMaxAttempts,
            TimeSpan? retryDelay = null,
            TimeSpan? minimumInterval = null)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (isRetryable == null) throw new ArgumentNullException(nameof(isRetryable));

            maxAttempts = Math.Max(1, maxAttempts);
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WaitAsync(key, cancellationToken, minimumInterval).ConfigureAwait(false);
                try
                {
                    return await operation().ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && isRetryable(ex))
                {
                    DiagnosticsLogger.InfoThrottled(
                        "TradeWrite",
                        NormalizeKey(key) + ":" + operationName + ":retry",
                        $"{operationName} 遇到临时错误，准备第 {attempt + 1}/{maxAttempts} 次重试：{DiagnosticsLogger.Redact(ex.Message)}",
                        TimeSpan.FromMinutes(1));
                    await Task.Delay(retryDelay ?? DefaultRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("写操作重试流程异常结束。");
        }

        public static bool IsRetryableTransient(Exception ex)
        {
            return ex is HttpRequestException
                || ex is TimeoutException
                || ex is TaskCanceledException
                || ex is SteamTransientSteamException;
        }

        internal static void ResetForTests()
        {
            Gates.Clear();
        }

        private static string NormalizeKey(string key)
        {
            string value = (key ?? "").Trim();
            return string.IsNullOrWhiteSpace(value) ? "global" : value;
        }

        private sealed class GateState
        {
            public readonly SemaphoreSlim Semaphore = new(1, 1);
            public DateTime LastGrantUtc = DateTime.MinValue;
        }
    }
}
