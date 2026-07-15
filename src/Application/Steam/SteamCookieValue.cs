using System;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamCookieValue
    {
        public static string Encode(string? value)
        {
            string text = (value ?? "").Trim();
            if (string.IsNullOrEmpty(text))
                return "";

            try
            {
                text = Uri.UnescapeDataString(text);
            }
            catch
            {
                // Keep the original value if Steam or WebView2 gives us a non-standard escape sequence.
            }

            return Uri.EscapeDataString(text);
        }
    }
}
