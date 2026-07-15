using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class SteamOfferAccountRefreshCoordinator
    {
        private readonly ISteamAuthStore _authStore;
        private readonly ISteamLoginService _loginService;
        private readonly Action _raiseDataUpdated;
        private readonly SteamApiKeyRefreshGate _apiKeyRefreshGate = new();

        public SteamOfferAccountRefreshCoordinator(
            ISteamAuthStore authStore,
            ISteamLoginService loginService,
            Action raiseDataUpdated)
        {
            _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
            _raiseDataUpdated = raiseDataUpdated ?? throw new ArgumentNullException(nameof(raiseDataUpdated));
        }

        public void QueuePersonaNameRefresh()
        {
            _ = RefreshPersonaNameAsync();
        }

        public void QueueSteamApiKeyRefresh()
        {
            var credential = _authStore.Load();
            if (credential == null || !NeedsSteamApiKeyRefresh(credential))
                return;
            if (!_apiKeyRefreshGate.TryBeginQueuedRefresh())
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshSteamApiKeyAsync();
                }
                finally
                {
                    _apiKeyRefreshGate.CompleteQueuedRefresh();
                }
            });
        }

        public async Task<SteamOfferActionResult> RefreshSteamApiKeyAsync()
        {
            using var refreshLease = await _apiKeyRefreshGate.TryEnterAsync().ConfigureAwait(false);
            if (!refreshLease.Entered)
                return refreshLease.BlockedResult;

            try
            {
                var credential = _authStore.Load();
                if (credential == null)
                    return SteamOfferActionResult.Failed("请先保存 Steam 令牌密钥，再获取 Steam Web API Key。", "missing-credential");
                if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                    return SteamOfferActionResult.Failed("Steam 登录状态未保存，无法获取 Steam Web API Key。", "missing-session");

                string apiKey = await _loginService.FetchApiKeyFromSavedSessionAsync(credential);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _apiKeyRefreshGate.SetCooldown(TimeSpan.FromMinutes(30), "Steam dev/apikey 未返回 Key");
                    return SteamOfferActionResult.Failed("Steam Web API Key 未获取：已保留登录状态，请检查 Steam dev/apikey 页面是否能直接显示 Key。", "api-key-not-found");
                }

                var latest = _authStore.Load() ?? credential;
                latest.ApiKey = apiKey.Trim();
                _authStore.Save(latest);
                _apiKeyRefreshGate.ClearCooldown();
                _raiseDataUpdated();
                return SteamOfferActionResult.Success("Steam Web API Key 已获取并加密保存。");
            }
            catch (SteamLoginException ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (ex.Category == SteamLoginFailureCategory.AuthExpired)
                {
                    _apiKeyRefreshGate.SetCooldown(TimeSpan.FromMinutes(30), "Steam 返回登录页或登录状态未确认");
                    return SteamOfferActionResult.Failed("Steam Web API Key 未获取：请先登录 Steam。原因：" + error, ex.Category.ToString());
                }

                if (ex.Category == SteamLoginFailureCategory.RateLimited)
                {
                    _apiKeyRefreshGate.SetCooldown(SteamOfferLoginRecoveryHelper.RateLimitCooldown, "Steam Web API Key 页面被限流");
                    return SteamOfferActionResult.Failed("Steam Web API Key 获取被 Steam 限流，请稍后再试。原因：" + error, ex.Category.ToString());
                }

                if (ex.Category == SteamLoginFailureCategory.NetworkError)
                {
                    _apiKeyRefreshGate.SetCooldown(TimeSpan.FromMinutes(5), "Steam 网络/代理暂时不可用");
                    return SteamOfferActionResult.Failed("Steam Web API Key 暂时无法获取：" + error, ex.Category.ToString());
                }

                _apiKeyRefreshGate.SetCooldown(TimeSpan.FromMinutes(15), "Steam Web API Key 获取异常");
                return SteamOfferActionResult.Failed("Steam Web API Key 获取异常：" + error, ex.Category.ToString());
            }
            catch (Exception ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                _apiKeyRefreshGate.SetCooldown(TimeSpan.FromMinutes(5), "Steam Web API Key 获取失败");
                SteamOfferAuditLog.InfoThrottled(
                    "steam-api-key-refresh-failed",
                    "Steam Web API key refresh failed: " + error,
                    TimeSpan.FromMinutes(10));
                return SteamOfferActionResult.Failed("Steam Web API Key 获取失败：" + error, "api-key-refresh-failed");
            }
        }

        public static bool NeedsSteamApiKeyRefresh(SteamAuthCredential credential)
        {
            return string.IsNullOrWhiteSpace(credential.ApiKey)
                && !string.IsNullOrWhiteSpace(credential.SessionId)
                && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure);
        }

        private async Task RefreshPersonaNameAsync()
        {
            try
            {
                var credential = _authStore.Load();
                if (credential == null)
                    return;

                string personaName = await _loginService.RefreshPersonaNameAsync(credential);
                if (!string.IsNullOrWhiteSpace(personaName))
                    _raiseDataUpdated();
            }
            catch
            {
                // Steam nickname is display-only. Ignore lookup failures to avoid disrupting login state.
            }
        }
    }
}
