using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.Notify
{
    internal static class NotificationProviderUrls
    {
        public const string WxPusherSendMessagePrefix = "https://wxpusher.zjiecode.com/api/send/message/";
        public const string WxPusherDocs = PhoneAlertUrls.WxPusherDocs;
        public const string ServerChanLogin = PhoneAlertUrls.ServerChanLogin;
        public const string ServerChanApiPrefix = "https://sctapi.ftqq.com/";
        public const string PushPlusHome = PhoneAlertUrls.PushPlusHome;
        public const string PushPlusSend = PushPlusHome + "send";
        public const string BarkDocs = PhoneAlertUrls.BarkDocs;
        public const string BarkDefaultServer = PhoneAlertUrls.BarkDefaultServer;
        public const string GotifyDocs = PhoneAlertUrls.GotifyDocs;
        public const string TelegramBotApiDocs = PhoneAlertUrls.TelegramBotApiDocs;
        public const string TelegramBotApiPrefix = "https://api.telegram.org/bot";

        public static string ServerChanFt07Send(string sendKey)
        {
            return "https://" + sendKey + ".push.ft07.com/send";
        }

        public static string ServerChanFt07FallbackSend(string host, string sendKey)
        {
            return "https://" + host + ".push.ft07.com/send/" + sendKey + ".send";
        }

        public static string ServerChanSctSend(string sendKey)
        {
            return ServerChanApiPrefix + sendKey + ".send";
        }

        public static string TelegramSendMessage(string botToken)
        {
            return TelegramBotApiPrefix + botToken + "/sendMessage";
        }
    }
}
