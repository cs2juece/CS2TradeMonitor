using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Writes the bounded exact-name request accepted by CsWeaponManager's local
    /// getYyypIdBySteamHashName endpoint. It performs no HTTP request.
    /// </summary>
    public static class YouPinTemplateMappingJsonWriter
    {
        public static Task WriteLookupRequestAsync(
            YouPinHotCatalog catalog,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
                throw new ArgumentException("输出流必须可写。", nameof(destination));

            var request = new MappingLookupRequest
            {
                SteamHashNames = catalog.Items
                    .OrderBy(item => item.Rank)
                    .Select(item => item.MarketHashName)
                    .ToArray()
            };
            return JsonSerializer.SerializeAsync(
                destination,
                request,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Writes a credential-free exact mapping artifact that can be read back by
        /// <see cref="YouPinTemplateMappingJsonReader"/>.
        /// </summary>
        public static async Task WriteMappingsAsync(
            IReadOnlyCollection<YouPinTemplateMapping> mappings,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mappings);
            ArgumentNullException.ThrowIfNull(destination);
            if (!destination.CanWrite)
                throw new ArgumentException("输出流必须可写。", nameof(destination));
            if (mappings.Count > YouPinTemplateMappingJsonReader.MaximumMappingCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mappings),
                    $"模板映射最多允许 {YouPinTemplateMappingJsonReader.MaximumMappingCount} 个条目。");
            }
            if (mappings.Any(mapping => mapping is null))
                throw new ArgumentException("模板映射不能为空。", nameof(mappings));

            YouPinTemplateMapping[] ordered = mappings
                .OrderBy(mapping => mapping.MarketHashName, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Select(mapping => mapping.MarketHashName).Distinct(StringComparer.Ordinal).Count()
                    != ordered.Length
                || ordered.Select(mapping => mapping.TemplateId).Distinct().Count() != ordered.Length)
            {
                throw new ArgumentException("模板映射名称和悠悠模板 ID 必须一一对应。", nameof(mappings));
            }

            var data = ordered.ToDictionary(
                mapping => mapping.MarketHashName,
                mapping => new MappingResponseItem
                {
                    SteamHashName = mapping.MarketHashName,
                    YouPinId = mapping.TemplateId
                },
                StringComparer.Ordinal);
            var response = new MappingResponse
            {
                Success = true,
                Data = data
            };
            await JsonSerializer.SerializeAsync(
                destination,
                response,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private sealed class MappingLookupRequest
        {
            [JsonPropertyName("steam_hash_names")]
            public required string[] SteamHashNames { get; init; }
        }

        private sealed class MappingResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; init; }

            [JsonPropertyName("data")]
            public required IReadOnlyDictionary<string, MappingResponseItem> Data { get; init; }
        }

        private sealed class MappingResponseItem
        {
            [JsonPropertyName("steam_hash_name")]
            public required string SteamHashName { get; init; }

            [JsonPropertyName("yyyp_id")]
            public long YouPinId { get; init; }
        }
    }
}
