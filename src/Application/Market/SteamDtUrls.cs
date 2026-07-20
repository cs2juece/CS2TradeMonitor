namespace CS2TradeMonitor.Application.Market
{
    internal static class SteamDtUrls
    {
        public const string OpenApiBase = "https://open.steamdt.com";
        public const string WebBase = "https://www.steamdt.com";
        public const string SettingsPage = WebBase + "/my/setting";
        public const string OfficialItemPriceEndpoint = "/open/cs2/item/v1/price";
        public const string PublicDefaultConfig = WebBase + "/api/user/system-config/v1/default-config";
        public const string PublicBlockSummary = WebBase + "/api/user/item/block/v1/summary";
        public const string PublicBlockSuggest = WebBase + "/api/user/item/block/v1/suggest";
        public const string PublicSkinItem = WebBase + "/api/user/skin/v1/item";
        public const string BroadSectionReferer = WebBase + "/section?type=BROAD";
        public const string ItemReferer = WebBase + "/item/";

        public static string WithTimestamp(string endpoint, long timestamp)
        {
            return endpoint + "?timestamp=" + timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string WithTimestamp(string endpoint, string timestamp)
        {
            return endpoint + "?timestamp=" + (timestamp ?? "");
        }

        public static string Cs2ItemReferer(string marketHashName)
        {
            return WebBase + "/cs2/" + System.Uri.EscapeDataString(marketHashName);
        }
    }
}
