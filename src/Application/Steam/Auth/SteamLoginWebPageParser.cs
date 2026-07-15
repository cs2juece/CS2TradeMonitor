using System;
using System.Net;
using System.Text.RegularExpressions;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginHttpDiagnostics;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginTokenTextParser;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal static class SteamLoginWebPageParser
    {
        public static Uri? ResolveSteamRedirect(Uri currentUrl, Uri? location)
        {
            if (location == null)
                return null;

            Uri next = location.IsAbsoluteUri ? location : new Uri(currentUrl, location);
            return string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && string.Equals(next.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase)
                ? next
                : null;
        }

        public static string ExtractSteamApiKey(string html)
        {
            string text = html ?? "";
            string[] patterns =
            {
                @"(?is)\bKey\b\s*:?\s*(?:</?[^>]+>\s*)*([A-Fa-f0-9]{32})\b",
                @"(?is)<input[^>]+(?:name|id)\s*=\s*[""'][^""']*(?:key|apikey|api_key)[^""']*[""'][^>]+value\s*=\s*[""']([A-Fa-f0-9]{32})[""']",
                @"(?is)\b(?:apikey|api_key|Web\s+API\s+Key)\b[^A-Fa-f0-9]{0,160}([A-Fa-f0-9]{32})\b"
            };

            foreach (string pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }

            return "";
        }

        public static string ClassifyApiKeyPage(string html)
        {
            string text = html ?? "";
            if (LooksLikeLoginPage(text))
                return "login-page";
            if (text.Contains("Access Denied", StringComparison.OrdinalIgnoreCase)
                || text.Contains("access denied", StringComparison.OrdinalIgnoreCase))
                return "access-denied";
            if (text.Contains("Registering for a Steam Web API Key", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Register Steam Web API Key", StringComparison.OrdinalIgnoreCase)
                || text.Contains("domain name", StringComparison.OrdinalIgnoreCase))
                return "registration-page";
            if (text.Contains("Key", StringComparison.OrdinalIgnoreCase)
                || text.Contains("apikey", StringComparison.OrdinalIgnoreCase))
                return "key-page";
            return "unknown-page";
        }

        public static string ExtractPersonaName(string body)
        {
            string text = body ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var xmlMatch = Regex.Match(
                text,
                @"<steamID>\s*(?:<!\[CDATA\[(?<name>.*?)\]\]>|(?<plain>[^<]*))\s*</steamID>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (xmlMatch.Success)
                return CleanPersonaName(FirstText(xmlMatch.Groups["name"].Value, xmlMatch.Groups["plain"].Value));

            var htmlMatch = Regex.Match(
                text,
                @"class\s*=\s*[""'][^""']*\bactual_persona_name\b[^""']*[""'][^>]*>\s*(?<name>[^<]+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return htmlMatch.Success ? CleanPersonaName(htmlMatch.Groups["name"].Value) : "";
        }

        public static string CleanPersonaName(string value)
        {
            string text = WebUtility.HtmlDecode(value ?? "").Trim();
            text = Regex.Replace(text, @"\s+", " ");
            return text.Length > 80 ? text[..80] : text;
        }
    }
}
