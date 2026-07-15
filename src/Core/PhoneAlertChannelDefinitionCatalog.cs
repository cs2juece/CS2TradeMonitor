using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.Core
{
    internal sealed class PhoneAlertChannelDefinition
    {
        public PhoneAlertChannelType Type { get; init; }
        public string DefaultName { get; init; } = "";
        public string Title { get; init; } = "";
        public int DefaultPriority { get; init; }
        public string SecretLabel { get; init; } = "密钥";
        public bool ShowServerField { get; init; }
        public string ServerLabel { get; init; } = "服务地址";
        public bool ShowExtraField { get; init; }
        public string ExtraLabel { get; init; } = "额外参数";
        public bool RequiresSecret { get; init; } = true;
        public bool RequiresValidServer { get; init; }
        public bool RequiresExtra { get; init; }
        public string DefaultServerUrl { get; init; } = "";
        public string DefaultExtra { get; init; } = "";
        public string HelpUrl { get; init; } = "";
        public int ShortMaskLimit { get; init; } = 10;
        public int ShortMaskPrefixLength { get; init; } = 3;
        public int LongMaskPrefixLength { get; init; } = 6;
        public int MaskSuffixLength { get; init; } = 4;
    }

    internal static class PhoneAlertChannelDefinitionCatalog
    {
        private static readonly IReadOnlyList<PhoneAlertChannelDefinition> Definitions = Array.AsReadOnly(new[]
        {
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.ServerChan,
                DefaultName = "Server酱",
                Title = "Server酱 / Server酱³",
                DefaultPriority = 1,
                SecretLabel = "SendKey",
                HelpUrl = PhoneAlertUrls.ServerChanLogin,
                ShortMaskLimit = 12,
                ShortMaskPrefixLength = 4,
                LongMaskPrefixLength = 7
            },
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.WxPusher,
                DefaultName = "WxPusher",
                Title = "WxPusher",
                DefaultPriority = 2,
                SecretLabel = "SPT 提醒码",
                HelpUrl = PhoneAlertUrls.WxPusherDocs,
                ShortMaskLimit = 12,
                ShortMaskPrefixLength = 4,
                LongMaskPrefixLength = 8
            },
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.PushPlus,
                DefaultName = "PushPlus",
                Title = "PushPlus",
                DefaultPriority = 3,
                SecretLabel = "Token",
                HelpUrl = PhoneAlertUrls.PushPlusHome
            },
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.Bark,
                DefaultName = "Bark",
                Title = "Bark",
                DefaultPriority = 4,
                SecretLabel = "Device Key",
                ShowServerField = true,
                DefaultServerUrl = PhoneAlertUrls.BarkDefaultServer,
                HelpUrl = PhoneAlertUrls.BarkDocs
            },
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.Gotify,
                DefaultName = "Gotify",
                Title = "Gotify",
                DefaultPriority = 5,
                SecretLabel = "App Token",
                ShowServerField = true,
                RequiresValidServer = true,
                DefaultServerUrl = PhoneAlertUrls.GotifyServerPlaceholder,
                HelpUrl = PhoneAlertUrls.GotifyDocs
            },
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.Telegram,
                DefaultName = "Telegram Bot",
                Title = "Telegram Bot",
                DefaultPriority = 6,
                SecretLabel = "Bot Token",
                ShowExtraField = true,
                ExtraLabel = "Chat ID",
                RequiresExtra = true,
                HelpUrl = PhoneAlertUrls.TelegramBotApiDocs
            },
            new PhoneAlertChannelDefinition
            {
                Type = PhoneAlertChannelType.Webhook,
                DefaultName = "自定义 Webhook",
                Title = "自定义 Webhook",
                DefaultPriority = 7,
                SecretLabel = "Bearer Token",
                ShowServerField = true,
                ServerLabel = "Webhook URL",
                ShowExtraField = true,
                ExtraLabel = "JSON 模板",
                RequiresSecret = false,
                RequiresValidServer = true,
                DefaultExtra = "{\"title\":\"{title}\",\"message\":\"{message}\"}"
            }
        });

        private static readonly IReadOnlyDictionary<PhoneAlertChannelType, PhoneAlertChannelDefinition> ByType =
            Definitions.ToDictionary(definition => definition.Type);

        public static IReadOnlyList<PhoneAlertChannelDefinition> All => Definitions;

        public static IReadOnlyList<PhoneAlertChannelType> BackupTypes { get; } = Array.AsReadOnly(
            Definitions.Where(definition => definition.Type != PhoneAlertChannelType.ServerChan)
                .Select(definition => definition.Type)
                .ToArray());

        public static PhoneAlertChannelDefinition Get(PhoneAlertChannelType type)
        {
            return ByType.TryGetValue(type, out PhoneAlertChannelDefinition? definition)
                ? definition
                : throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported phone alert channel type.");
        }

        public static List<PhoneAlertChannelConfig> Normalize(
            IEnumerable<PhoneAlertChannelConfig>? channels,
            string serverChanSendKey,
            string wxPusherSpt,
            bool enableMigratedChannels,
            bool useDisplayTitles)
        {
            var existing = (channels ?? Array.Empty<PhoneAlertChannelConfig>())
                .Where(channel => channel is not null && ByType.ContainsKey(channel.Type))
                .GroupBy(channel => channel.Type)
                .Select(group => group.OrderBy(channel => channel.Priority).First())
                .ToDictionary(channel => channel.Type);

            foreach (PhoneAlertChannelDefinition definition in Definitions)
            {
                if (!existing.TryGetValue(definition.Type, out PhoneAlertChannelConfig? channel))
                {
                    channel = new PhoneAlertChannelConfig { Type = definition.Type };
                    existing[definition.Type] = channel;
                }

                ApplyDefaults(channel, useDisplayTitles);
                string legacySecret = definition.Type switch
                {
                    PhoneAlertChannelType.ServerChan => serverChanSendKey,
                    PhoneAlertChannelType.WxPusher => wxPusherSpt,
                    _ => ""
                };
                if (string.IsNullOrWhiteSpace(channel.Secret) && !string.IsNullOrWhiteSpace(legacySecret))
                {
                    channel.Secret = legacySecret.Trim();
                    if (enableMigratedChannels)
                        channel.Enabled = true;
                }
            }

            List<PhoneAlertChannelConfig> ordered = existing.Values
                .OrderBy(channel => channel.Priority <= 0 ? Get(channel.Type).DefaultPriority : channel.Priority)
                .ThenBy(channel => Get(channel.Type).DefaultPriority)
                .ToList();
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].Priority = i + 1;

            return ordered;
        }

        public static void ApplyDefaults(PhoneAlertChannelConfig channel, bool useDisplayTitle)
        {
            ArgumentNullException.ThrowIfNull(channel);
            PhoneAlertChannelDefinition definition = Get(channel.Type);

            channel.Id = string.IsNullOrWhiteSpace(channel.Id) ? Guid.NewGuid().ToString("N") : channel.Id.Trim();
            channel.DisplayName = string.IsNullOrWhiteSpace(channel.DisplayName)
                ? (useDisplayTitle ? definition.Title : definition.DefaultName)
                : channel.DisplayName.Trim();
            if (channel.Priority <= 0)
                channel.Priority = definition.DefaultPriority;

            channel.Secret = (channel.Secret ?? "").Trim();
            channel.ServerUrl = (channel.ServerUrl ?? "").Trim();
            channel.Extra = (channel.Extra ?? "").Trim();
            channel.LastTestResult = (channel.LastTestResult ?? "").Trim();

            if (string.IsNullOrWhiteSpace(channel.ServerUrl))
                channel.ServerUrl = definition.DefaultServerUrl;
            if (string.IsNullOrWhiteSpace(channel.Extra))
                channel.Extra = definition.DefaultExtra;
        }

        public static bool IsConfigured(PhoneAlertChannelConfig? channel)
        {
            if (channel is null || !ByType.TryGetValue(channel.Type, out PhoneAlertChannelDefinition? definition))
                return false;

            if (definition.RequiresSecret && string.IsNullOrWhiteSpace(channel.Secret))
                return false;
            if (definition.RequiresValidServer && !IsValidHttpUrl(channel.ServerUrl))
                return false;
            if (definition.RequiresExtra && string.IsNullOrWhiteSpace(channel.Extra))
                return false;

            return true;
        }

        public static string MaskSecret(PhoneAlertChannelConfig channel)
        {
            ArgumentNullException.ThrowIfNull(channel);
            if (!ByType.TryGetValue(channel.Type, out PhoneAlertChannelDefinition? definition))
                return Mask(channel.Secret, 10, 3, 6, 4);

            return Mask(
                channel.Secret,
                definition.ShortMaskLimit,
                definition.ShortMaskPrefixLength,
                definition.LongMaskPrefixLength,
                definition.MaskSuffixLength);
        }

        private static string Mask(
            string? value,
            int shortLimit,
            int shortPrefixLength,
            int longPrefixLength,
            int suffixLength)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0)
                return "";
            if (text.Length <= shortLimit)
                return text[..Math.Min(shortPrefixLength, text.Length)] + "****";

            return text[..Math.Min(longPrefixLength, text.Length)]
                + "****"
                + text[^Math.Min(suffixLength, text.Length)..];
        }

        private static bool IsValidHttpUrl(string? value)
        {
            return Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
