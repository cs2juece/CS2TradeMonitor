using System;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class SteamApiKeyRefreshGate
    {
        private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(2);
        private readonly object _syncRoot = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private int _queuedRefreshInFlight;
        private DateTime _cooldownUntil = DateTime.MinValue;
        private DateTime _lastAttemptAt = DateTime.MinValue;
        private string _cooldownReason = "";

        public bool TryBeginQueuedRefresh()
        {
            if (IsCoolingDown())
                return false;

            return Interlocked.Exchange(ref _queuedRefreshInFlight, 1) == 0;
        }

        public void CompleteQueuedRefresh()
        {
            Interlocked.Exchange(ref _queuedRefreshInFlight, 0);
        }

        public async Task<SteamApiKeyRefreshLease> TryEnterAsync()
        {
            if (TryBuildBlockedResult(out var blocked))
                return SteamApiKeyRefreshLease.Blocked(blocked);

            if (!await _semaphore.WaitAsync(0).ConfigureAwait(false))
            {
                return SteamApiKeyRefreshLease.Blocked(
                    SteamOfferActionResult.Failed("Steam Web API Key 正在获取中，已避免重复请求。", "api-key-refresh-in-flight"));
            }

            if (TryBuildBlockedResult(out blocked))
            {
                _semaphore.Release();
                return SteamApiKeyRefreshLease.Blocked(blocked);
            }

            MarkAttempt();
            return SteamApiKeyRefreshLease.CreateEntered(_semaphore);
        }

        public void SetCooldown(TimeSpan cooldown, string reason = "")
        {
            lock (_syncRoot)
            {
                _cooldownUntil = DateTime.Now.Add(cooldown);
                _cooldownReason = (reason ?? "").Trim();
            }
        }

        public void ClearCooldown()
        {
            lock (_syncRoot)
            {
                _cooldownUntil = DateTime.MinValue;
                _cooldownReason = "";
            }
        }

        private bool IsCoolingDown()
        {
            lock (_syncRoot)
            {
                return DateTime.Now < _cooldownUntil;
            }
        }

        private bool TryBuildBlockedResult(out SteamOfferActionResult result)
        {
            lock (_syncRoot)
            {
                DateTime now = DateTime.Now;
                if (now < _cooldownUntil)
                {
                    string reason = FirstText(_cooldownReason, "Steam 已进入退避冷却");
                    result = SteamOfferActionResult.Failed(
                        $"Steam Web API Key 暂停获取：{reason}；{_cooldownUntil:HH:mm:ss} 后再试。",
                        "api-key-refresh-cooldown");
                    return true;
                }

                if (_lastAttemptAt != DateTime.MinValue && now - _lastAttemptAt < MinRefreshInterval)
                {
                    DateTime next = _lastAttemptAt.Add(MinRefreshInterval);
                    result = SteamOfferActionResult.Failed(
                        $"Steam Web API Key 刚请求过，已避免重复请求；{next:HH:mm:ss} 后再试。",
                        "api-key-refresh-min-interval");
                    return true;
                }
            }

            result = SteamOfferActionResult.Failed("");
            return false;
        }

        private void MarkAttempt()
        {
            lock (_syncRoot)
            {
                _lastAttemptAt = DateTime.Now;
            }
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }

    internal sealed class SteamApiKeyRefreshLease : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        private SteamApiKeyRefreshLease(bool entered, SteamOfferActionResult blockedResult, SemaphoreSlim? semaphore)
        {
            Entered = entered;
            BlockedResult = blockedResult;
            _semaphore = semaphore;
        }

        public bool Entered { get; }

        public SteamOfferActionResult BlockedResult { get; }

        public static SteamApiKeyRefreshLease CreateEntered(SemaphoreSlim semaphore)
        {
            return new SteamApiKeyRefreshLease(true, SteamOfferActionResult.Success(""), semaphore);
        }

        public static SteamApiKeyRefreshLease Blocked(SteamOfferActionResult result)
        {
            return new SteamApiKeyRefreshLease(false, result, null);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
