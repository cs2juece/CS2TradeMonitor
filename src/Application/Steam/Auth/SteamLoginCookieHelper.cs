using CS2TradeMonitor.Application.Steam;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal sealed class SteamWebCookies
    {
        public string SessionId { get; set; } = "";
        public string SteamLoginSecure { get; set; } = "";
        public string SteamLogin { get; set; } = "";
    }

    internal static class SteamLoginCookieHelper
    {
        public static IEnumerable<string> GetSetCookieHeaders(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                return values;
            return Array.Empty<string>();
        }

        public static int GetCookieHostPriority(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return 0;
            if (string.Equals(uri.Host, "steamcommunity.com", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (uri.Host.EndsWith(".steamcommunity.com", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(uri.Host, "store.steampowered.com", StringComparison.OrdinalIgnoreCase))
                return 1;
            return 0;
        }

        public static void StoreCookie(Dictionary<string, string> cookies, string setCookie, int priority = 0, Dictionary<string, int>? priorities = null)
        {
            if (string.IsNullOrWhiteSpace(setCookie)) return;
            string first = setCookie.Split(';', 2)[0];
            int eq = first.IndexOf('=');
            if (eq <= 0) return;
            string name = first[..eq].Trim();
            string value = first[(eq + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (priorities != null
                && priorities.TryGetValue(name, out int existingPriority)
                && existingPriority > priority)
            {
                return;
            }

            cookies[name] = Uri.UnescapeDataString(value);
            if (priorities != null)
                priorities[name] = priority;
        }

        public static string BuildCookieHeader(SteamWebCookies cookies)
        {
            var parts = new List<string>
            {
                "sessionid=" + SteamCookieValue.Encode(cookies.SessionId),
                "steamLoginSecure=" + SteamCookieValue.Encode(cookies.SteamLoginSecure),
                "mobileClient=android",
                "mobileClientVersion=" + SteamCookieValue.Encode("777777 3.6.4")
            };
            if (!string.IsNullOrWhiteSpace(cookies.SteamLogin))
                parts.Add("steamLogin=" + SteamCookieValue.Encode(cookies.SteamLogin));
            return string.Join("; ", parts);
        }
    }
}
