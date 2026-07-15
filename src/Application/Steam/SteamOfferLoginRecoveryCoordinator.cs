using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.Steam.SteamOfferLoginRecoveryHelper;
using static CS2TradeMonitor.Application.Steam.SteamOfferMappingHelper;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class SteamOfferLoginRecoveryCoordinator
    {
        private readonly ISteamAuthStore _authStore;
        private readonly ISteamLoginService _loginService;
        private readonly Action _raiseDataUpdated;

        public SteamOfferLoginRecoveryCoordinator(
            ISteamAuthStore authStore,
            ISteamLoginService loginService,
            Action raiseDataUpdated)
        {
            _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
            _raiseDataUpdated = raiseDataUpdated ?? throw new ArgumentNullException(nameof(raiseDataUpdated));
        }

        public async Task<SteamOfferActionResult> EnsureSteamLoginStateAsync(
            string reason,
            bool allowPasswordFallback = true,
            bool preferPasswordFallback = false)
        {
            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("未绑定 Steam 令牌，无法恢复 Steam 登录状态。");

            var failures = new List<string>();
            if (preferPasswordFallback && allowPasswordFallback && HasAutoLoginConfig(credential))
                return await TryPasswordReloginWithCooldownAsync(credential, reason);

            if (!string.IsNullOrWhiteSpace(credential.AccessToken))
            {
                var access = await _loginService.RestoreFromAccessTokenAsync(credential);
                if (access.Ok)
                {
                    _raiseDataUpdated();
                    return access;
                }
                failures.Add("AccessToken：" + access.Message);
                if (IsNetworkError(access.Code))
                    return SteamOfferActionResult.Failed("暂时无法验证 Steam 登录状态，已保留网页登录状态。" + access.Message, access.Code);
                credential = _authStore.Load() ?? credential;
            }

            if (!string.IsNullOrWhiteSpace(credential.RefreshToken))
            {
                var refresh = await _loginService.RestoreFromRefreshTokenAsync(credential);
                if (refresh.Ok)
                {
                    _raiseDataUpdated();
                    return refresh;
                }
                failures.Add("RefreshToken：" + refresh.Message);
                if (IsNetworkError(refresh.Code))
                    return SteamOfferActionResult.Failed("暂时无法验证 Steam 登录状态，已保留网页登录状态。" + refresh.Message, refresh.Code);
                credential = _authStore.Load() ?? credential;
            }

            if (!string.IsNullOrWhiteSpace(credential.SessionId) && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
            {
                var session = await _loginService.ValidateSavedSessionAsync(credential);
                if (session.Ok)
                    return session;
                failures.Add("已保存登录状态：" + session.Message);
                if (IsNetworkError(session.Code))
                    return SteamOfferActionResult.Failed("暂时无法验证 Steam 登录状态，已保留网页登录状态。" + session.Message, session.Code);
                credential = _authStore.Load() ?? credential;
            }

            if (!allowPasswordFallback)
                return SteamOfferActionResult.Failed(BuildLoginRestoreFailureMessage(failures), "restore-failed");

            if (string.IsNullOrWhiteSpace(credential.LoginAccountName) || string.IsNullOrWhiteSpace(credential.LoginPassword))
                return SteamOfferActionResult.Failed(BuildLoginRestoreFailureMessage(failures) + " 未保存 Steam 账号密码；请使用 Steam 网页登录或粘贴 Token 恢复。", "missing-auto-login");
            if (string.IsNullOrWhiteSpace(credential.SharedSecret) || string.IsNullOrWhiteSpace(credential.IdentitySecret))
                return SteamOfferActionResult.Failed("Steam 令牌缺少 shared_secret 或 identity_secret，无法使用账号密码兜底登录。", "missing-secrets");

            return await TryPasswordReloginWithCooldownAsync(credential, reason);
        }

        private async Task<SteamOfferActionResult> TryPasswordReloginWithCooldownAsync(SteamAuthCredential credential, string reason)
        {
            DateTime now = DateTime.Now;
            if (ClampPersistedRateLimitCooldown(credential, now))
                _authStore.Save(credential);
            if (credential.AutoReloginCooldownUntil > now)
            {
                if (WasLastAutoReloginNetworkFailure(credential))
                {
                    credential.AutoReloginCooldownUntil = DateTime.MinValue;
                    _authStore.Save(credential);
                }
                else
                {
                    return SteamOfferActionResult.Failed($"Steam 登录冷却中，{credential.AutoReloginCooldownUntil:HH:mm:ss} 后再试；请改用 Steam 网页登录或粘贴 Token 恢复。", "cooldown");
                }
            }

            TimeSpan localReloginInterval = WasLastAutoReloginNetworkFailure(credential)
                ? TimeSpan.FromSeconds(30)
                : TimeSpan.FromMinutes(3);
            if (credential.LastAutoReloginAt != DateTime.MinValue && now - credential.LastAutoReloginAt < localReloginInterval)
                return SteamOfferActionResult.Failed("账号密码兜底登录过于频繁，已按短间隔限制停止本次尝试。", "rate-local");

            return await PasswordReloginCoreAsync(credential, reason);
        }

        private async Task<SteamOfferActionResult> PasswordReloginCoreAsync(SteamAuthCredential credential, string reason)
        {
            credential.LastAutoReloginAt = DateTime.Now;
            credential.LastAutoReloginResult = "账号密码兜底登录中：" + FirstText(reason, "Steam 登录状态失效");
            _authStore.Save(credential);
            _raiseDataUpdated();

            var result = await _loginService.LoginAndConfigureAsync(new SteamAutoLoginRequest
            {
                SharedSecret = credential.SharedSecret,
                IdentitySecret = credential.IdentitySecret,
                AccountName = credential.LoginAccountName,
                Password = credential.LoginPassword
            });

            var updated = _authStore.Load() ?? credential;
            updated.LastAutoReloginAt = DateTime.Now;
            updated.LastAutoReloginResult = result.Ok ? "自动重登成功" : BuildAutoReloginResultText(result);
            if (result.Ok)
            {
                updated.AutoReloginCooldownUntil = DateTime.MinValue;
                _authStore.Save(updated);
                _raiseDataUpdated();
                return SteamOfferActionResult.Success("Steam 登录状态已通过账号密码兜底恢复。");
            }

            updated.AutoReloginCooldownUntil = IsNetworkError(result.Code)
                ? DateTime.MinValue
                : (ShouldEnterCooldown(result.Code)
                    ? DateTime.Now.Add(RateLimitCooldown)
                    : DateTime.Now.AddMinutes(5));
            _authStore.Save(updated);
            _raiseDataUpdated();
            return result;
        }
    }
}
