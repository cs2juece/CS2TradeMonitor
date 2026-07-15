using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    public sealed class SteamSessionKeepAliveService : ISteamSessionKeepAliveService, IDisposable
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StaleSessionAge = TimeSpan.FromHours(10);
        private readonly object _timerLock = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly ISteamAuthStore _authStore;
        private readonly ISteamLoginService _loginService;
        private System.Threading.Timer? _timer;
        private bool _started;
        private bool _disposed;

        public static SteamSessionKeepAliveService Instance { get; } = new();

        private SteamSessionKeepAliveService()
            : this(SteamAuthSecureStore.Instance, SteamLoginService.Instance)
        {
        }

        internal SteamSessionKeepAliveService(ISteamAuthStore authStore, ISteamLoginService loginService)
        {
            _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
        }

        public void Start()
        {
            lock (_timerLock)
            {
                if (_disposed || _started)
                    return;
                _started = true;
                _timer = new System.Threading.Timer(OnTimerTick, null, InitialDelay, Timeout.InfiniteTimeSpan);
            }
        }

        public void Stop()
        {
            lock (_timerLock)
            {
                _started = false;
                _timer?.Dispose();
                _timer = null;
            }
        }

        public async Task<SteamOfferActionResult> CheckNowAsync(CancellationToken cancellationToken = default)
        {
            return await RunOnceAsync(cancellationToken).ConfigureAwait(false);
        }

        internal static TimeSpan ComputeNextDelay(SteamAuthCredential? credential, DateTime utcNow)
        {
            if (credential == null)
                return TimeSpan.FromHours(1);

            DateTime accessExpiresAt = credential.AccessTokenExpiresAt.Kind == DateTimeKind.Local
                ? credential.AccessTokenExpiresAt.ToUniversalTime()
                : credential.AccessTokenExpiresAt;
            if (accessExpiresAt == DateTime.MinValue)
                return TimeSpan.FromHours(1);

            TimeSpan remaining = accessExpiresAt - utcNow;
            if (remaining <= TimeSpan.Zero)
                return TimeSpan.FromMinutes(5);
            if (remaining <= TimeSpan.FromHours(1))
                return TimeSpan.FromMinutes(10);
            if (remaining <= TimeSpan.FromHours(6))
                return TimeSpan.FromHours(1);
            return TimeSpan.FromHours(3);
        }

        private async void OnTimerTick(object? state)
        {
            try
            {
                await RunOnceAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("SteamSessionKeepAlive", "Steam session keep-alive tick failed.", ex);
            }
            finally
            {
                ScheduleNext();
            }
        }

        private async Task<SteamOfferActionResult> RunOnceAsync(CancellationToken cancellationToken)
        {
            if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return SteamOfferActionResult.Failed("Steam 会话保活正在运行，已跳过本次检查。", "busy");

            try
            {
                var credential = _authStore.Load();
                if (credential == null)
                    return SteamOfferActionResult.Failed("未绑定 Steam 令牌。", "missing-credential");

                SteamJwtTokenParser.ApplyTokenMetadata(credential);
                DateTime nowUtc = DateTime.UtcNow;
                bool hasRefreshToken = !string.IsNullOrWhiteSpace(credential.RefreshToken);
                bool accessMissingOrDue = credential.AccessTokenExpiresAt == DateTime.MinValue
                    || credential.AccessTokenExpiresAt.ToUniversalTime() <= nowUtc.AddHours(1);
                if (hasRefreshToken && accessMissingOrDue)
                {
                    var refresh = await _loginService.RefreshAccessTokenForAppAsync(credential, cancellationToken).ConfigureAwait(false);
                    if (!refresh.Ok)
                        SaveKeepAliveFailure(credential, refresh.Message);
                    return refresh;
                }

                bool hasSession = !string.IsNullOrWhiteSpace(credential.SessionId)
                    && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure);
                if (hasSession && IsSessionStale(credential.SessionSavedAt))
                {
                    var validation = await _loginService.ValidateSavedSessionAsync(credential, cancellationToken).ConfigureAwait(false);
                    if (!validation.Ok)
                        SaveKeepAliveFailure(credential, validation.Message);
                    return validation;
                }

                _authStore.Save(credential);
                return SteamOfferActionResult.Success("Steam 会话无需续期。");
            }
            finally
            {
                _gate.Release();
            }
        }

        private void SaveKeepAliveFailure(SteamAuthCredential credential, string message)
        {
            credential.LastAutoReloginAt = DateTime.Now;
            credential.LastAutoReloginResult = "后台保活失败：" + (message ?? "").Trim();
            _authStore.Save(credential);
        }

        private static bool IsSessionStale(DateTime sessionSavedAt)
        {
            if (sessionSavedAt == DateTime.MinValue)
                return false;
            DateTime savedUtc = sessionSavedAt.Kind == DateTimeKind.Utc ? sessionSavedAt : sessionSavedAt.ToUniversalTime();
            return DateTime.UtcNow - savedUtc >= StaleSessionAge;
        }

        private void ScheduleNext()
        {
            lock (_timerLock)
            {
                if (!_started || _timer == null || _disposed)
                    return;
                TimeSpan delay = ComputeNextDelay(_authStore.Load(), DateTime.UtcNow);
                _timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            Stop();
            _gate.Dispose();
        }
    }
}
