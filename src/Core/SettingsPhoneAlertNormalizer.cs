using System;
using System.Linq;

namespace CS2TradeMonitor.src.Core
{
    internal static class SettingsPhoneAlertNormalizer
    {
        public static void Normalize(Settings settings)
        {
            if (!Enum.IsDefined(typeof(PhoneAlertDispatchMode), settings.PhoneAlertDispatchMode))
                settings.PhoneAlertDispatchMode = PhoneAlertDispatchMode.Failover;

            settings.PhoneAlertProvider = string.IsNullOrWhiteSpace(settings.PhoneAlertProvider)
                ? "ServerChan"
                : settings.PhoneAlertProvider.Trim();
            settings.ServerChanSendKey = (settings.ServerChanSendKey ?? "").Trim();
            settings.WxPusherSpt = (settings.WxPusherSpt ?? "").Trim();
            settings.PhoneAlertChannels = PhoneAlertChannelDefinitionCatalog.Normalize(
                settings.PhoneAlertChannels,
                settings.ServerChanSendKey,
                settings.WxPusherSpt,
                enableMigratedChannels: settings.PhoneAlertEnabled,
                useDisplayTitles: false);

            PhoneAlertChannelConfig server = settings.PhoneAlertChannels.Single(
                channel => channel.Type == PhoneAlertChannelType.ServerChan);
            PhoneAlertChannelConfig wx = settings.PhoneAlertChannels.Single(
                channel => channel.Type == PhoneAlertChannelType.WxPusher);

            if (string.IsNullOrWhiteSpace(settings.ServerChanSendKey))
                settings.ServerChanSendKey = server.Secret;
            if (string.IsNullOrWhiteSpace(settings.WxPusherSpt))
                settings.WxPusherSpt = wx.Secret;

        }
    }
}
