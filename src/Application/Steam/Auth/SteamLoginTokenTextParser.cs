using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal sealed class ParsedTokenText
    {
        public string SteamId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string SteamLoginSecure { get; set; } = "";
        public string SteamLogin { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }

    internal static class SteamLoginTokenTextParser
    {
        public static ParsedTokenText ParseTokenText(string text)
        {
            var parsed = new ParsedTokenText();
            string raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return parsed;

            if (raw.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    parsed.AccessToken = FirstText(GetJsonString(root, "access_token", "AccessToken", "accessToken", "oauth_token", "OAuthToken", "oauthToken"));
                    parsed.RefreshToken = FirstText(GetJsonString(root, "refresh_token", "RefreshToken", "refreshToken"));
                    parsed.SessionId = FirstText(GetJsonString(root, "sessionid", "SessionId", "session_id"));
                    parsed.SteamLoginSecure = FirstText(GetJsonString(root, "steamLoginSecure", "SteamLoginSecure", "steam_login_secure"));
                    parsed.SteamLogin = FirstText(GetJsonString(root, "steamLogin", "SteamLogin", "steam_login"));
                    parsed.SteamId = FirstText(GetJsonString(root, "steamid", "steam_id", "SteamID", "account_steamid"));
                    parsed.AccessToken = FirstText(parsed.AccessToken, TryGetAccessTokenFromSteamLoginSecure(parsed.SteamLoginSecure));
                    parsed.SteamId = FirstText(parsed.SteamId, TryGetSteamIdFromSteamLoginSecure(parsed.SteamLoginSecure), TryGetSteamIdFromJwt(parsed.AccessToken), TryGetSteamIdFromJwt(parsed.RefreshToken));
                    return parsed;
                }
                catch (JsonException)
                {
                    // Fall through to cookie/plain-text parsing.
                }
            }

            foreach (var pair in ParseLooseKeyValueText(raw))
            {
                string key = pair.Key;
                string value = TrimToken(pair.Value);
                if (key.Equals("steamLoginSecure", StringComparison.OrdinalIgnoreCase))
                    parsed.SteamLoginSecure = value;
                else if (key.Equals("sessionid", StringComparison.OrdinalIgnoreCase))
                    parsed.SessionId = value;
                else if (key.Equals("steamLogin", StringComparison.OrdinalIgnoreCase))
                    parsed.SteamLogin = value;
                else if (key.Equals("steamRefresh_steam", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("refresh_token", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("refreshToken", StringComparison.OrdinalIgnoreCase))
                    parsed.RefreshToken = value;
                else if (key.Equals("access_token", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("accessToken", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("oauth_token", StringComparison.OrdinalIgnoreCase))
                    parsed.AccessToken = value;
                else if (key.Equals("steamid", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("steam_id", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("SteamID", StringComparison.OrdinalIgnoreCase))
                    parsed.SteamId = value;
            }

            parsed.AccessToken = FirstText(parsed.AccessToken, TryGetAccessTokenFromSteamLoginSecure(parsed.SteamLoginSecure));
            parsed.SteamId = FirstText(parsed.SteamId, TryGetSteamIdFromSteamLoginSecure(parsed.SteamLoginSecure), TryGetSteamIdFromJwt(parsed.AccessToken), TryGetSteamIdFromJwt(parsed.RefreshToken));
            return parsed;
        }

        public static Dictionary<string, string> ParseLooseKeyValueText(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(text ?? "", @"(?i)\b(steamLoginSecure|steamLogin|sessionid|steamRefresh_steam|refresh_token|refreshToken|access_token|accessToken|oauth_token|steamid|steam_id|SteamID)\b\s*[:=]\s*""?([^"";\r\n\s]+)"))
                result[match.Groups[1].Value] = Uri.UnescapeDataString(match.Groups[2].Value);

            foreach (string part in (text ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part[..eq].Trim();
                string value = part[(eq + 1)..].Trim();
                if (key.Length > 0 && value.Length > 0)
                    result[key] = Uri.UnescapeDataString(value);
            }

            return result;
        }

        public static string TryGetSteamIdFromJwt(string jwt)
        {
            return SteamJwtTokenParser.TryGetSteamId(jwt);
        }

        public static string TryGetSteamIdFromSteamLoginSecure(string value)
        {
            string text = Uri.UnescapeDataString((value ?? "").Trim());
            int separator = text.IndexOf("||", StringComparison.Ordinal);
            if (separator <= 0)
                return "";
            string steamId = text[..separator].Trim();
            return steamId.All(char.IsDigit) ? steamId : "";
        }

        public static string TryGetAccessTokenFromSteamLoginSecure(string value)
        {
            string text = Uri.UnescapeDataString((value ?? "").Trim());
            int separator = text.IndexOf("||", StringComparison.Ordinal);
            if (separator < 0 || separator + 2 >= text.Length)
                return "";
            string token = text[(separator + 2)..].Trim();
            return LooksLikeJwt(token) ? token : "";
        }

        public static bool LooksLikeJwt(string value)
        {
            string text = (value ?? "").Trim();
            return text.Count(c => c == '.') == 2 && text.Length > 20;
        }

        public static string TrimToken(string value)
        {
            return (value ?? "")
                .Trim()
                .Trim('"')
                .Trim('\'')
                .Trim();
        }

        public static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static string GetJsonString(JsonElement root, params string[] names)
        {
            foreach (string name in names)
            {
                if (root.TryGetProperty(name, out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? "";
                    if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                        return value.ToString();
                }
            }

            return "";
        }
    }
}
