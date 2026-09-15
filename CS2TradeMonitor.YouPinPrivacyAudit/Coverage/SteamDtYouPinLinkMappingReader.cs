using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Resolves exact YouPin template IDs from the YOUPIN platform links already preserved in
    /// a frozen SteamDT Hot capture. It performs no network discovery and never treats a
    /// SteamDT item ID as a YouPin template ID.
    /// </summary>
    public static class SteamDtYouPinLinkMappingReader
    {
        public const int MaximumSourceCount = 100;
        public const int MaximumSourceBytes = 512 * 1024;

        public static async Task<IReadOnlyList<YouPinTemplateMapping>> LoadForCatalogAsync(
            YouPinHotCatalog catalog,
            IReadOnlyCollection<Stream> rawSources,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(rawSources);
            if (rawSources.Count == 0 || rawSources.Count > MaximumSourceCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rawSources),
                    $"SteamDT 原始分页必须包含 1 到 {MaximumSourceCount} 个输入。");
            }
            if (rawSources.Any(source => source is null))
                throw new ArgumentException("SteamDT 原始分页不能为空。", nameof(rawSources));

            var catalogNames = catalog.Items
                .Select(item => item.MarketHashName)
                .ToHashSet(StringComparer.Ordinal);
            var templateByName = new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (Stream source in rawSources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes = await BoundedInputReader
                    .ReadAllBytesAsync(source, MaximumSourceBytes, cancellationToken)
                    .ConfigureAwait(false);

                SteamDtRawPageDto? page;
                try
                {
                    page = JsonSerializer.Deserialize<SteamDtRawPageDto>(bytes);
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException("SteamDT 原始分页不是有效的 JSON。", exception);
                }

                IReadOnlyList<SteamDtRawItemDto>? items = page?.Response?.Data?.List;
                if (page?.Response?.Success != true || items is null)
                    throw new InvalidDataException("SteamDT 原始分页缺少成功响应或饰品列表。");

                foreach (SteamDtRawItemDto item in items)
                {
                    string? marketHashName = item.ItemInfo?.MarketHashName;
                    if (marketHashName is null || !catalogNames.Contains(marketHashName))
                        continue;

                    SteamDtPlatformInfoDto[] youPinLinks = item.PlatformInfoList?
                        .Where(platform => string.Equals(
                            platform.PlatformEnum,
                            "YOUPIN",
                            StringComparison.Ordinal))
                        .ToArray() ?? [];
                    if (youPinLinks.Length != 1)
                    {
                        throw new InvalidDataException(
                            "热门条目必须且只能包含一个 YOUPIN 平台链接。");
                    }

                    long templateId = ReadYouPinTemplateId(youPinLinks[0].LinkUrl);
                    if (templateByName.TryGetValue(marketHashName, out long existingId)
                        && existingId != templateId)
                    {
                        throw new InvalidDataException(
                            "同一 market_hash_name 的 YOUPIN 平台链接存在冲突。");
                    }

                    templateByName[marketHashName] = templateId;
                }
            }

            YouPinTemplateMapping[] mappings = catalog.Items
                .OrderBy(item => item.Rank)
                .Where(item => templateByName.ContainsKey(item.MarketHashName))
                .Select(item => YouPinTemplateMapping.Create(
                    item.MarketHashName,
                    templateByName[item.MarketHashName]))
                .ToArray();
            return Array.AsReadOnly(mappings);
        }

        private static long ReadYouPinTemplateId(string? linkUrl)
        {
            if (string.IsNullOrWhiteSpace(linkUrl)
                || !Uri.TryCreate(linkUrl, UriKind.Absolute, out Uri? link)
                || !string.Equals(link.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || !string.Equals(link.Host, "www.youpin898.com", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(link.AbsolutePath, "/market/goods-list", StringComparison.Ordinal))
            {
                throw new InvalidDataException("YOUPIN 平台链接格式无效。");
            }

            Dictionary<string, string[]> query = link.Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .GroupBy(
                    part => Uri.UnescapeDataString(part[0]),
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(part => part.Length == 2
                        ? Uri.UnescapeDataString(part[1])
                        : string.Empty).ToArray(),
                    StringComparer.Ordinal);
            string[] sensitiveNames =
            [
                "authSign",
                "authorization",
                "cookie",
                "deviceId",
                "deviceToken",
                "requestTag",
                "sessionId",
                "signature",
                "token"
            ];
            if (query.Keys.Any(key => sensitiveNames.Contains(key, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidDataException("YOUPIN 平台链接不得包含认证或设备参数。");

            if (!query.TryGetValue("templateId", out string[]? templateValues)
                || templateValues.Length != 1
                || !long.TryParse(
                    templateValues[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long templateId)
                || templateId <= 0)
            {
                throw new InvalidDataException("YOUPIN 平台链接缺少有效 templateId。");
            }

            if (!query.TryGetValue("gameId", out string[]? gameValues)
                || gameValues.Length != 1
                || !string.Equals(gameValues[0], "730", StringComparison.Ordinal))
            {
                throw new InvalidDataException("YOUPIN 平台链接不是 CS2 模板链接。");
            }

            return templateId;
        }

        private sealed class SteamDtRawPageDto
        {
            [JsonPropertyName("response")]
            public SteamDtRawResponseDto? Response { get; init; }
        }

        private sealed class SteamDtRawResponseDto
        {
            [JsonPropertyName("success")]
            public bool Success { get; init; }

            [JsonPropertyName("data")]
            public SteamDtRawDataDto? Data { get; init; }
        }

        private sealed class SteamDtRawDataDto
        {
            [JsonPropertyName("list")]
            public IReadOnlyList<SteamDtRawItemDto>? List { get; init; }
        }

        private sealed class SteamDtRawItemDto
        {
            [JsonPropertyName("itemInfoVO")]
            public SteamDtItemInfoDto? ItemInfo { get; init; }

            [JsonPropertyName("platformInfoList")]
            public IReadOnlyList<SteamDtPlatformInfoDto>? PlatformInfoList { get; init; }
        }

        private sealed class SteamDtItemInfoDto
        {
            [JsonPropertyName("marketHashName")]
            public string? MarketHashName { get; init; }
        }

        private sealed class SteamDtPlatformInfoDto
        {
            [JsonPropertyName("platformEnum")]
            public string? PlatformEnum { get; init; }

            [JsonPropertyName("linkUrl")]
            public string? LinkUrl { get; init; }
        }
    }
}
