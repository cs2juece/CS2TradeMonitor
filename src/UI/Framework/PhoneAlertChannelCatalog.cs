using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class PhoneAlertChannelCatalog
    {
        public static readonly PhoneAlertChannelType[] AdvancedBackupTypes =
            PhoneAlertChannelDefinitionCatalog.BackupTypes.ToArray();

        public static void NormalizeChannels(
            List<PhoneAlertChannelConfig> channels,
            string serverChanSendKey,
            string wxPusherSpt)
        {
            ArgumentNullException.ThrowIfNull(channels);
            List<PhoneAlertChannelConfig> ordered = PhoneAlertChannelDefinitionCatalog.Normalize(
                channels,
                serverChanSendKey,
                wxPusherSpt,
                enableMigratedChannels: false,
                useDisplayTitles: true);

            channels.Clear();
            channels.AddRange(ordered);
        }

        public static PhoneAlertChannelConfig GetOrCreateChannel(
            List<PhoneAlertChannelConfig> channels,
            PhoneAlertChannelType type,
            string serverChanSendKey,
            string wxPusherSpt)
        {
            ArgumentNullException.ThrowIfNull(channels);

            var channel = channels.FirstOrDefault(item => item.Type == type);
            if (channel == null)
            {
                channel = new PhoneAlertChannelConfig
                {
                    Type = type,
                    Priority = PhoneAlertChannelDefinitionCatalog.Get(type).DefaultPriority
                };
                channels.Add(channel);
            }

            PhoneAlertChannelDefinitionCatalog.ApplyDefaults(channel, useDisplayTitle: true);

            if (type == PhoneAlertChannelType.ServerChan
                && string.IsNullOrWhiteSpace(channel.Secret)
                && !string.IsNullOrWhiteSpace(serverChanSendKey))
            {
                channel.Secret = serverChanSendKey;
            }

            if (type == PhoneAlertChannelType.WxPusher
                && string.IsNullOrWhiteSpace(channel.Secret)
                && !string.IsNullOrWhiteSpace(wxPusherSpt))
            {
                channel.Secret = wxPusherSpt;
            }

            return channel;
        }

        public static (string ServerChanSendKey, string WxPusherSpt) BuildLegacyFields(IEnumerable<PhoneAlertChannelConfig> channels)
        {
            ArgumentNullException.ThrowIfNull(channels);

            var server = channels.FirstOrDefault(channel => channel.Type == PhoneAlertChannelType.ServerChan);
            var wx = channels.FirstOrDefault(channel => channel.Type == PhoneAlertChannelType.WxPusher);
            return (server?.Secret?.Trim() ?? "", wx?.Secret?.Trim() ?? "");
        }

        public static int CountEnabledBackupChannels(IEnumerable<PhoneAlertChannelConfig> channels)
        {
            ArgumentNullException.ThrowIfNull(channels);

            return channels.Count(channel => AdvancedBackupTypes.Contains(channel.Type) && channel.Enabled);
        }
    }
}
