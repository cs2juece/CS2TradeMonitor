using System;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamUrls
    {
        public const string CommunityBase = "https://steamcommunity.com";
        public const string StoreBase = "https://store.steampowered.com";
        public const string LoginBase = "https://login.steampowered.com";
        public const string WebApiBase = "https://api.steampowered.com";
        public const string EconServiceApiBase = WebApiBase + "/IEconService";
        public const string EconomyApiBase = WebApiBase + "/ISteamEconomy";
        public const string CommunityImageBase = "https://community.cloudflare.steamstatic.com/economy/image/";

        public const string TradeNewAcknowledge = CommunityBase + "/trade/new/acknowledge";
        public const string MyTradeOffers = CommunityBase + "/my/tradeoffers";
        public const string MyTradeOffersWithSlash = MyTradeOffers + "/";
        public const string WebLoginHome = CommunityBase + "/login/home/";
        public const string DevApiKey = CommunityBase + "/dev/apikey";
        public const string FinalizeLogin = LoginBase + "/jwt/finalizelogin";
        public const string QueryTimeV0001 = WebApiBase + "/ITwoFactorService/QueryTime/v0001";
        public const string QueryTimeV1 = WebApiBase + "/ITwoFactorService/QueryTime/v1/";
        public const string GenerateAccessTokenForApp = WebApiBase + "/IAuthenticationService/GenerateAccessTokenForApp/v1/";
        public const string ServerInfoProbe = WebApiBase + "/ISteamWebAPIUtil/GetServerInfo/v1/";

        public static string TradeOffer(string tradeOfferId)
        {
            return CommunityBase + "/tradeoffer/" + Uri.EscapeDataString(tradeOfferId);
        }

        public static string TradeOfferAccept(string tradeOfferId)
        {
            return TradeOffer(tradeOfferId) + "/accept";
        }

        public static string TradeOfferReferer(string? tradeOfferId = null)
        {
            return string.IsNullOrWhiteSpace(tradeOfferId)
                ? CommunityBase + "/tradeoffer/"
                : TradeOffer(tradeOfferId) + "/";
        }

        public static string ProfileTradeOffers(string steamId)
        {
            return CommunityBase + "/profiles/" + Uri.EscapeDataString(steamId) + "/tradeoffers";
        }

        public static string Profile(string steamId)
        {
            return CommunityBase + "/profiles/" + Uri.EscapeDataString(steamId);
        }

        public static string ProfileXml(string steamId)
        {
            return Profile(steamId) + "/?xml=1";
        }

        public static string AuthenticationService(string method)
        {
            return WebApiBase + "/IAuthenticationService/" + method + "/v1/";
        }

        public static string CommunityRelative(string path)
        {
            return new Uri(new Uri(CommunityBase), path).ToString();
        }

        public static string EconomyImage(string iconPath)
        {
            return CommunityImageBase + iconPath;
        }
    }
}
