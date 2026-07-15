using System;
using System.Collections.Generic;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    public sealed class SteamAuthSecureStore : ISteamAuthStore
    {
        public static SteamAuthSecureStore Instance { get; } = new();
        private readonly ISteamTokenVault _tokenVault;

        public string CredentialPath => _tokenVault.CredentialPath;
        public string LegacyCredentialPath => _tokenVault.LegacyCredentialPath;

        private SteamAuthSecureStore()
            : this(SteamServiceRuntimeServices.ResolveTokenVault())
        {
        }

        internal SteamAuthSecureStore(ISteamTokenVault tokenVault)
        {
            _tokenVault = tokenVault ?? throw new ArgumentNullException(nameof(tokenVault));
        }

        public SteamAuthCredential? Load()
        {
            return FromTokenEntry(_tokenVault.GetDefaultSteamToken());
        }

        public string Save(SteamAuthCredential credential)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            string currentDefaultId = _tokenVault.GetDefaultSteamToken()?.Id ?? "";
            var entry = ToTokenEntry(credential, currentDefaultId);
            string savedId = _tokenVault.SaveToken(entry);
            if (string.Equals(entry.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(savedId))
            {
                _tokenVault.SetDefaultSteamToken(savedId);
            }
            return savedId;
        }

        public void Clear()
        {
            _tokenVault.ClearDefaultSteamToken();
        }

        public bool ClearTokenSecrets()
        {
            var credential = Load();
            if (credential == null)
                return false;

            credential.SharedSecret = "";
            credential.IdentitySecret = "";
            credential.DeviceId = "";
            credential.SavedAt = DateTime.Now;
            _tokenVault.SaveToken(ToTokenEntry(credential, _tokenVault.GetDefaultSteamTokenId()));
            return true;
        }

        public bool ClearLoginState()
        {
            var credential = Load();
            if (credential == null)
                return false;

            credential.SessionId = "";
            credential.SteamLoginSecure = "";
            credential.SteamLogin = "";
            credential.RefreshToken = "";
            credential.AccessToken = "";
            credential.AccessTokenExpiresAt = DateTime.MinValue;
            credential.RefreshTokenExpiresAt = DateTime.MinValue;
            credential.ApiKey = "";
            credential.LoginAccountName = "";
            credential.LoginPassword = "";
            credential.SessionSavedAt = DateTime.MinValue;
            credential.LastAutoReloginAt = DateTime.MinValue;
            credential.LastAutoReloginResult = "";
            credential.AutoReloginCooldownUntil = DateTime.MinValue;
            _tokenVault.SaveToken(ToTokenEntry(credential, _tokenVault.GetDefaultSteamTokenId()));
            return true;
        }

        public List<SteamTokenEntry> GetAllTokens() => _tokenVault.GetAllTokens();

        public void SetDefaultToken(string id) => _tokenVault.SetDefaultSteamToken(id);

        public void DeleteToken(string id) => _tokenVault.DeleteToken(id);

        public SteamAuthStoreStatus GetStatus()
        {
            var credential = Load();
            if (credential == null)
            {
                string error = _tokenVault.LastError;
                return new SteamAuthStoreStatus
                {
                    HasCredential = false,
                    Message = string.IsNullOrWhiteSpace(error) ? "未绑定 Steam 令牌" : error,
                    Error = error
                };
            }

            bool hasSecrets = !string.IsNullOrWhiteSpace(credential.SharedSecret)
                && !string.IsNullOrWhiteSpace(credential.IdentitySecret);
            bool hasSession = !string.IsNullOrWhiteSpace(credential.SessionId)
                && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure);
            bool hasAutoLogin = !string.IsNullOrWhiteSpace(credential.LoginAccountName)
                && !string.IsNullOrWhiteSpace(credential.LoginPassword);
            bool hasAccessToken = !string.IsNullOrWhiteSpace(credential.AccessToken);
            bool hasRefreshToken = !string.IsNullOrWhiteSpace(credential.RefreshToken);

            return new SteamAuthStoreStatus
            {
                HasCredential = true,
                HasSecrets = hasSecrets,
                HasSession = hasSession,
                HasAutoLogin = hasAutoLogin,
                HasAccessToken = hasAccessToken,
                HasRefreshToken = hasRefreshToken,
                PersonaName = (credential.PersonaName ?? "").Trim(),
                AccountName = string.IsNullOrWhiteSpace(credential.AccountName) ? "未命名令牌" : credential.AccountName.Trim(),
                LoginAccountName = MaskAccount(credential.LoginAccountName),
                SteamId = MaskSteamId(credential.SteamId),
                DeviceId = MaskTail(credential.DeviceId, 4),
                SavedAt = credential.SavedAt,
                SessionSavedAt = credential.SessionSavedAt,
                LastAutoReloginAt = credential.LastAutoReloginAt,
                LastAutoReloginResult = credential.LastAutoReloginResult,
                AutoReloginCooldownUntil = credential.AutoReloginCooldownUntil,
                Message = hasSecrets
                    ? hasSession ? "已绑定令牌，Steam 已登录" : "已绑定令牌，Steam 未登录"
                    : "凭据缺少必要令牌字段",
                Error = hasSecrets ? "" : "缺少 shared_secret 或 identity_secret"
            };
        }

        public static string MaskTail(string? value, int tail = 4)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return "";
            if (text.Length <= tail) return new string('*', text.Length);
            return "***" + text[^Math.Min(tail, text.Length)..];
        }

        public static string MaskSteamId(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Length <= 6) return MaskTail(text, 2);
            return text[..3] + "****" + text[^4..];
        }

        public static string MaskAccount(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Length <= 2) return string.IsNullOrEmpty(text) ? "" : text[0] + "*";
            return text[0] + new string('*', Math.Min(4, text.Length - 2)) + text[^1];
        }

        private static SteamTokenEntry ToTokenEntry(SteamAuthCredential credential, string fallbackId = "")
        {
            SteamJwtTokenParser.ApplyTokenMetadata(credential);
            string steamId = (credential.SteamId ?? "").Trim();
            return new SteamTokenEntry
            {
                Id = fallbackId,
                Platform = "Steam",
                SteamId = steamId,
                AccountName = credential.AccountName,
                PersonaName = credential.PersonaName,
                DeviceId = credential.DeviceId,
                SharedSecret = credential.SharedSecret,
                IdentitySecret = credential.IdentitySecret,
                SessionId = credential.SessionId,
                SteamLoginSecure = credential.SteamLoginSecure,
                SteamLogin = credential.SteamLogin,
                RefreshToken = credential.RefreshToken,
                AccessToken = credential.AccessToken,
                AccessTokenExpiresAt = credential.AccessTokenExpiresAt,
                RefreshTokenExpiresAt = credential.RefreshTokenExpiresAt,
                ApiKey = credential.ApiKey,
                LoginAccountName = credential.LoginAccountName,
                LoginPassword = credential.LoginPassword,
                LastAutoReloginAt = credential.LastAutoReloginAt,
                LastAutoReloginResult = credential.LastAutoReloginResult,
                AutoReloginCooldownUntil = credential.AutoReloginCooldownUntil,
                SavedAt = credential.SavedAt == default ? DateTime.Now : credential.SavedAt,
                SessionSavedAt = credential.SessionSavedAt,
                CreatedAt = credential.SavedAt == default ? DateTime.Now : credential.SavedAt,
                UpdatedAt = DateTime.Now
            };
        }

        private static SteamAuthCredential? FromTokenEntry(SteamTokenEntry? entry)
        {
            if (entry == null) return null;
            return new SteamAuthCredential
            {
                SteamId = entry.SteamId,
                AccountName = entry.AccountName,
                PersonaName = entry.PersonaName,
                DeviceId = entry.DeviceId,
                SharedSecret = entry.SharedSecret,
                IdentitySecret = entry.IdentitySecret,
                SessionId = entry.SessionId,
                SteamLoginSecure = entry.SteamLoginSecure,
                SteamLogin = entry.SteamLogin,
                RefreshToken = entry.RefreshToken,
                AccessToken = entry.AccessToken,
                AccessTokenExpiresAt = entry.AccessTokenExpiresAt,
                RefreshTokenExpiresAt = entry.RefreshTokenExpiresAt,
                ApiKey = entry.ApiKey,
                LoginAccountName = entry.LoginAccountName,
                LoginPassword = entry.LoginPassword,
                LastAutoReloginAt = entry.LastAutoReloginAt,
                LastAutoReloginResult = entry.LastAutoReloginResult,
                AutoReloginCooldownUntil = entry.AutoReloginCooldownUntil,
                SavedAt = entry.SavedAt,
                SessionSavedAt = entry.SessionSavedAt
            };
        }
    }

    public sealed class SteamAuthStoreStatus
    {
        public bool HasCredential { get; set; }
        public bool HasSecrets { get; set; }
        public bool HasSession { get; set; }
        public bool HasAutoLogin { get; set; }
        public bool HasAccessToken { get; set; }
        public bool HasRefreshToken { get; set; }
        public string AccountName { get; set; } = "";
        public string PersonaName { get; set; } = "";
        public string LoginAccountName { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public DateTime SavedAt { get; set; }
        public DateTime SessionSavedAt { get; set; }
        public DateTime LastAutoReloginAt { get; set; }
        public string LastAutoReloginResult { get; set; } = "";
        public DateTime AutoReloginCooldownUntil { get; set; }
        public string Message { get; set; } = "";
        public string Error { get; set; } = "";
    }
}
