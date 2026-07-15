using CS2TradeMonitor.Application.Abstractions;
using System;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal static class SteamLoginMobileAccessTokenResolver
    {
        public static string GetStoredMobileAccessToken(ISteamAuthStore authStore, string steamId)
        {
            var credential = authStore.Load();
            if (credential == null || string.IsNullOrWhiteSpace(credential.AccessToken))
                return "";
            if (!string.IsNullOrWhiteSpace(credential.SteamId)
                && !string.Equals(credential.SteamId.Trim(), (steamId ?? "").Trim(), StringComparison.Ordinal))
                return "";
            return credential.AccessToken.Trim();
        }
    }
}
