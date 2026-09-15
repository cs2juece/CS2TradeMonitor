using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Shared.Trading;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application
{
    internal static class TradeWriteOperationGate
    {
        public static readonly TimeSpan DefaultMinimumInterval = TradeAutomationPolicy.MinimumWriteInterval;
        public static readonly TimeSpan DefaultRetryDelay = TradeAutomationPolicy.TransientRetryDelay;
        public const int DefaultMaxAttempts = TradeAutomationPolicy.MaximumWriteAttempts;

        private static TradeWriteCoordinator _coordinator = new();

        public static async Task WaitAsync(
            string key,
            CancellationToken cancellationToken = default,
            TimeSpan? minimumInterval = null)
        {
            await _coordinator.WaitAsync(key, cancellationToken, minimumInterval).ConfigureAwait(false);
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
            return await _coordinator.RunWithRetryAsync(
                key,
                operation,
                isRetryable,
                retrying: context =>
                {
                    SteamOfferPlatform.Host.InfoThrottled(
                        NormalizeKey(key) + ":" + operationName + ":retry",
                        $"{operationName} 遇到临时错误，准备第 {context.NextAttempt}/{context.MaximumAttempts} 次重试：{SteamOfferPlatform.Host.RedactSecrets(context.Exception.Message)}",
                        TimeSpan.FromMinutes(1));
                    return ValueTask.CompletedTask;
                },
                cancellationToken,
                maxAttempts,
                retryDelay,
                minimumInterval).ConfigureAwait(false);
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
            _coordinator = new TradeWriteCoordinator();
        }

        private static string NormalizeKey(string key)
        {
            return TradeWriteCoordinator.NormalizeKey(key);
        }
    }
}
