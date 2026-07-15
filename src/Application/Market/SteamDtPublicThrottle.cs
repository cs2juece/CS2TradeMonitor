using System;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Market
{
    internal sealed class SteamDtPublicLease : IDisposable
    {
        private readonly Action _release;
        private bool _released;

        public SteamDtPublicLease(Action release)
        {
            _release = release;
        }

        public void Dispose()
        {
            if (_released)
                return;

            _released = true;
            _release();
        }
    }

    internal static class SteamDtPublicThrottle
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly object StateLock = new();
        private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(5);
        private static DateTime _lastRequestUtc = DateTime.MinValue;
        private static DateTime _cooldownUntilUtc = DateTime.MinValue;
        private static string _cooldownReason = "";
        private static int _rateLimitFailures;

        public static bool IsCoolingDown(out string message)
        {
            lock (StateLock)
            {
                DateTime now = DateTime.UtcNow;
                if (now >= _cooldownUntilUtc)
                {
                    message = "";
                    return false;
                }

                message = $"SteamDT 数据源限流，使用缓存。原因：{_cooldownReason}；下一步：{_cooldownUntilUtc.ToLocalTime():MM-dd HH:mm:ss} 后自动重试。";
                return true;
            }
        }

        public static async Task<SteamDtPublicLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
        {
            if (IsCoolingDown(out _))
                return null;

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool acquired = true;
            try
            {
                if (IsCoolingDown(out _))
                    return null;

                TimeSpan wait;
                lock (StateLock)
                {
                    wait = (_lastRequestUtc + MinInterval) - DateTime.UtcNow;
                }

                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

                lock (StateLock)
                {
                    _lastRequestUtc = DateTime.UtcNow;
                }

                acquired = false;
                return new SteamDtPublicLease(() => Gate.Release());
            }
            finally
            {
                if (acquired)
                    Gate.Release();
            }
        }

        public static void ReportSuccess()
        {
            lock (StateLock)
            {
                _rateLimitFailures = 0;
                _cooldownUntilUtc = DateTime.MinValue;
                _cooldownReason = "";
            }
        }

        public static void ReportFailure(string reason)
        {
            if (!IsRateLimit(reason))
                return;

            lock (StateLock)
            {
                _rateLimitFailures = Math.Min(_rateLimitFailures + 1, 4);
                int minutes = Math.Min(30, 5 * (1 << (_rateLimitFailures - 1)));
                _cooldownUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
                _cooldownReason = string.IsNullOrWhiteSpace(reason) ? "请求过于频繁" : reason;
            }
        }

        private static bool IsRateLimit(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            return reason.Contains("访问速度太快", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("请求太快", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("请求过于频繁", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("HTTP 429", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("频繁", StringComparison.OrdinalIgnoreCase);
        }
    }
}
