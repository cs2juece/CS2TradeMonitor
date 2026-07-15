using CS2TradeMonitor.Domain.Steam;
using System;
using System.Text.Json;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal sealed record SteamJwtTokenInfo(string SteamId, DateTime ExpiresAt);

    internal static class SteamJwtTokenParser
    {
        public static bool TryParse(string token, out SteamJwtTokenInfo info)
        {
            info = new SteamJwtTokenInfo("", DateTime.MinValue);
            try
            {
                string[] parts = (token ?? "").Trim().Split('.');
                if (parts.Length != 3)
                    return false;

                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
                var root = doc.RootElement;
                string steamId = root.TryGetProperty("sub", out var sub) ? sub.GetString() ?? "" : "";
                DateTime expiresAt = DateTime.MinValue;
                if (root.TryGetProperty("exp", out var exp))
                {
                    long seconds = exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out long numeric)
                        ? numeric
                        : long.TryParse(exp.ToString(), out long parsed) ? parsed : 0;
                    if (seconds > 0)
                        expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                }

                if (string.IsNullOrWhiteSpace(steamId) && expiresAt == DateTime.MinValue)
                    return false;

                info = new SteamJwtTokenInfo(steamId.Trim(), expiresAt);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string TryGetSteamId(string token)
        {
            return TryParse(token, out var info) ? info.SteamId : "";
        }

        public static DateTime TryGetExpiresAt(string token)
        {
            return TryParse(token, out var info) ? info.ExpiresAt : DateTime.MinValue;
        }

        public static void ApplyTokenMetadata(SteamAuthCredential credential)
        {
            if (credential == null)
                return;

            Apply(
                credential.AccessToken,
                steamId => credential.SteamId = FirstText(credential.SteamId, steamId),
                expiresAt => credential.AccessTokenExpiresAt = expiresAt);
            Apply(
                credential.RefreshToken,
                steamId => credential.SteamId = FirstText(credential.SteamId, steamId),
                expiresAt => credential.RefreshTokenExpiresAt = expiresAt);
        }

        public static void ApplyTokenMetadata(SteamTokenEntry entry)
        {
            if (entry == null)
                return;

            Apply(
                entry.AccessToken,
                steamId => entry.SteamId = FirstText(entry.SteamId, steamId),
                expiresAt => entry.AccessTokenExpiresAt = expiresAt);
            Apply(
                entry.RefreshToken,
                steamId => entry.SteamId = FirstText(entry.SteamId, steamId),
                expiresAt => entry.RefreshTokenExpiresAt = expiresAt);
        }

        private static void Apply(string token, Action<string> setSteamId, Action<DateTime> setExpiresAt)
        {
            if (!TryParse(token, out var info))
                return;
            if (!string.IsNullOrWhiteSpace(info.SteamId))
                setSteamId(info.SteamId);
            if (info.ExpiresAt != DateTime.MinValue)
                setExpiresAt(info.ExpiresAt);
        }

        private static string FirstText(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }
    }
}
