using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamOfferLoginRecoveryHelper
    {
        public static TimeSpan RateLimitCooldown { get; } = TimeSpan.FromMinutes(3);

        public static string BuildAutoReloginResultText(SteamOfferActionResult result)
        {
            string code = (result.Code ?? "").Trim();
            string message = (result.Message ?? "").Trim();
            if (string.IsNullOrWhiteSpace(code))
                return message;
            if (string.IsNullOrWhiteSpace(message))
                return code;
            return code + "：" + message;
        }

        public static bool ShouldEnterCooldown(string code)
        {
            return string.Equals(code, SteamLoginFailureCategory.InvalidPassword.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, SteamLoginFailureCategory.InvalidTwoFactor.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, SteamLoginFailureCategory.EmailCodeRequired.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, SteamLoginFailureCategory.CaptchaRequired.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, SteamLoginFailureCategory.RateLimited.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "rate-local", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRateLimited(string code)
        {
            return string.Equals(code, SteamLoginFailureCategory.RateLimited.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "rate-local", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsNetworkError(string code)
        {
            return string.Equals(code, SteamLoginFailureCategory.NetworkError.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool ClampPersistedRateLimitCooldown(SteamAuthCredential credential, DateTime now)
        {
            string lastResult = credential.LastAutoReloginResult ?? "";
            bool wasRateLimited = lastResult.Contains("限流", StringComparison.Ordinal)
                || lastResult.Contains(SteamLoginFailureCategory.RateLimited.ToString(), StringComparison.OrdinalIgnoreCase)
                || lastResult.Contains("rate-local", StringComparison.OrdinalIgnoreCase);
            if (!wasRateLimited || credential.AutoReloginCooldownUntil <= now)
                return false;

            DateTime maximumCooldownUntil = credential.LastAutoReloginAt == DateTime.MinValue
                ? now.Add(RateLimitCooldown)
                : credential.LastAutoReloginAt.Add(RateLimitCooldown);
            if (credential.AutoReloginCooldownUntil <= maximumCooldownUntil)
                return false;

            credential.AutoReloginCooldownUntil = maximumCooldownUntil > now
                ? maximumCooldownUntil
                : DateTime.MinValue;
            return true;
        }

        public static bool WasLastAutoReloginNetworkFailure(SteamAuthCredential credential)
        {
            string text = credential.LastAutoReloginResult ?? "";
            return text.Contains(SteamLoginFailureCategory.NetworkError.ToString(), StringComparison.OrdinalIgnoreCase)
                || text.Contains("网络", StringComparison.Ordinal)
                || text.Contains("超时", StringComparison.Ordinal)
                || text.Contains("代理", StringComparison.Ordinal)
                || text.Contains("暂时无法", StringComparison.Ordinal);
        }

        public static bool HasAutoLoginConfig(SteamAuthCredential credential)
        {
            return !string.IsNullOrWhiteSpace(credential.LoginAccountName)
                && !string.IsNullOrWhiteSpace(credential.LoginPassword)
                && !string.IsNullOrWhiteSpace(credential.SharedSecret)
                && !string.IsNullOrWhiteSpace(credential.IdentitySecret);
        }

        public static bool HasRecoverableLoginState(SteamAuthCredential credential)
        {
            return !string.IsNullOrWhiteSpace(credential.AccessToken)
                || !string.IsNullOrWhiteSpace(credential.RefreshToken)
                || (!string.IsNullOrWhiteSpace(credential.SessionId) && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                || HasAutoLoginConfig(credential);
        }

        public static string BuildLoginRestoreFailureMessage(List<string> failures)
        {
            if (failures.Count == 0)
                return "未找到可自动恢复的 Steam 登录状态。";

            string summary = string.Join("；", failures.Where(x => !string.IsNullOrWhiteSpace(x)).Take(2));
            return string.IsNullOrWhiteSpace(summary)
                ? "未找到可自动恢复的 Steam 登录状态。"
                : "Steam 登录状态自动恢复失败。" + summary + "。";
        }

        public static bool LooksLikeExplicitAuthExpired(string error)
        {
            string text = (error ?? "").ToLowerInvariant();
            return text.Contains("needauth", StringComparison.Ordinal)
                || text.Contains("needauthentication", StringComparison.Ordinal)
                || text.Contains("not logged", StringComparison.Ordinal)
                || text.Contains("notloggedin", StringComparison.Ordinal)
                || text.Contains("g_steamid = false", StringComparison.Ordinal)
                || text.Contains("steamcommunity.com/login", StringComparison.Ordinal)
                || text.Contains("login.steampowered.com", StringComparison.Ordinal);
        }

        public static bool IsNonAuthSteamOfferListFailure(Exception ex)
        {
            if (ex is SteamAuthExpiredException)
                return false;
            if (ex is SteamTransientSteamException
                || ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is JsonException)
                return true;
            if (ex is InvalidOperationException invalid)
                return !LooksLikeExplicitAuthExpired(invalid.Message);
            return false;
        }

        public static string BuildSteamOfferListWarning(string step, Exception ex)
        {
            if (ex is SteamTransientSteamException transient)
            {
                if (transient.StatusCode == 429 || transient.Code.Contains("rate", StringComparison.OrdinalIgnoreCase))
                    return "被 Steam 限流，请稍后再刷新。";
                return SteamOfferAuditLog.RedactSecrets(transient.Message);
            }

            if (ex is TaskCanceledException)
                return "连接 Steam 超时，请检查网络/代理后重试。";

            if (ex is HttpRequestException httpEx)
                return NetworkDiagnostics.BuildFailureMessage("Steam", step, httpEx);

            if (ex is JsonException)
                return "Steam 返回内容暂时无法解析，已保留登录状态。";

            return SteamOfferAuditLog.RedactSecrets(ex.Message);
        }
    }
}
