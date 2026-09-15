using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Validated, frozen popular-item candidate catalog. Loading it performs no network work
    /// and does not resolve or query YouPin templates.
    /// </summary>
    public sealed class YouPinHotCatalog
    {
        public const string SupportedSchemaVersion = "cs2chanquantaux.steamdt-ladder.v2";
        public const int MaximumItemCount = 1000;
        public const int MaximumSourceBytes = 4 * 1024 * 1024;

        private YouPinHotCatalog(
            string captureSetId,
            DateTimeOffset capturedAt,
            string sourceSha256,
            IReadOnlyList<YouPinHotCatalogItem> items,
            IReadOnlyDictionary<string, int> categoryCounts)
        {
            CaptureSetId = captureSetId;
            CapturedAt = capturedAt;
            SourceSha256 = sourceSha256;
            Items = items;
            CategoryCounts = categoryCounts;
        }

        public string SchemaVersion => SupportedSchemaVersion;
        public string CaptureSetId { get; }
        public DateTimeOffset CapturedAt { get; }
        public string SourceSha256 { get; }
        public IReadOnlyList<YouPinHotCatalogItem> Items { get; }
        public IReadOnlyDictionary<string, int> CategoryCounts { get; }

        public static async Task<YouPinHotCatalog> LoadSteamDtAsync(
            Stream source,
            CancellationToken cancellationToken = default)
        {
            byte[] bytes = await BoundedInputReader
                .ReadAllBytesAsync(source, MaximumSourceBytes, cancellationToken)
                .ConfigureAwait(false);

            SteamDtHotCatalogDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<SteamDtHotCatalogDto>(bytes);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("热门目录不是有效的 JSON。", exception);
            }

            if (dto is null)
                throw new InvalidDataException("热门目录不能为空。");
            if (!string.Equals(dto.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
                throw new InvalidDataException("热门目录版本不受支持。");
            if (!string.Equals(dto.Kind, "Hot", StringComparison.Ordinal))
                throw new InvalidDataException("输入不是热门榜目录。");

            string captureSetId = RequirePlainText(dto.CaptureSetId, "采集批次", 160);
            if (dto.CapturedAt == default)
                throw new InvalidDataException("热门目录缺少有效采集时间。");
            if (dto.Items is null || dto.Items.Count == 0)
                throw new InvalidDataException("热门目录至少需要一个条目。");
            if (dto.Items.Count > MaximumItemCount)
            {
                throw new InvalidDataException(
                    $"热门目录最多允许 {MaximumItemCount} 个条目。");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var ranks = new HashSet<int>();
            var normalizedItems = new List<YouPinHotCatalogItem>(dto.Items.Count);

            foreach (SteamDtHotCatalogItemDto item in dto.Items)
            {
                if (item.LadderRank <= 0 || !ranks.Add(item.LadderRank))
                    throw new InvalidDataException("热门目录包含无效或重复排名。");

                string marketHashName = RequirePlainText(
                    item.MarketHashName,
                    "market_hash_name",
                    512);
                if (!names.Add(marketHashName))
                    throw new InvalidDataException("热门目录包含重复的 market_hash_name。");

                string steamDtItemId = RequirePlainText(item.ItemId, "SteamDT itemId", 64);
                string category = NormalizeCategory(item.ItemType);
                normalizedItems.Add(new YouPinHotCatalogItem(
                    item.LadderRank,
                    marketHashName,
                    steamDtItemId,
                    category));
            }

            normalizedItems.Sort((left, right) => left.Rank.CompareTo(right.Rank));
            for (int index = 0; index < normalizedItems.Count; index++)
            {
                if (normalizedItems[index].Rank != index + 1)
                    throw new InvalidDataException("热门目录排名必须从 1 开始连续排列。");
            }

            Dictionary<string, int> counts = normalizedItems
                .GroupBy(item => item.Category, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            return new YouPinHotCatalog(
                captureSetId,
                dto.CapturedAt,
                sha256,
                Array.AsReadOnly(normalizedItems.ToArray()),
                new ReadOnlyDictionary<string, int>(counts));
        }

        private static string NormalizeCategory(string? sourceItemType)
        {
            string value = RequirePlainText(sourceItemType, "itemType", 80);
            string[] prefixes = ["CSGO_Type_", "CSGO_Tool_", "Type_"];
            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    value = value[prefix.Length..];
                    break;
                }
            }

            return value == "SniperRifle" ? "Sniper" : value;
        }

        private static string RequirePlainText(string? value, string fieldName, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"热门目录缺少 {fieldName}。");

            string trimmed = value.Trim();
            if (!string.Equals(trimmed, value, StringComparison.Ordinal)
                || trimmed.Length > maximumLength
                || trimmed.Any(char.IsControl))
            {
                throw new InvalidDataException($"热门目录的 {fieldName} 格式无效。");
            }

            return trimmed;
        }

        private sealed class SteamDtHotCatalogDto
        {
            [JsonPropertyName("schemaVersion")]
            public string? SchemaVersion { get; init; }

            [JsonPropertyName("captureSetId")]
            public string? CaptureSetId { get; init; }

            [JsonPropertyName("capturedAt")]
            public DateTimeOffset CapturedAt { get; init; }

            [JsonPropertyName("kind")]
            public string? Kind { get; init; }

            [JsonPropertyName("items")]
            public List<SteamDtHotCatalogItemDto>? Items { get; init; }
        }

        private sealed class SteamDtHotCatalogItemDto
        {
            [JsonPropertyName("ladderRank")]
            public int LadderRank { get; init; }

            [JsonPropertyName("marketHashName")]
            public string? MarketHashName { get; init; }

            [JsonPropertyName("itemId")]
            public string? ItemId { get; init; }

            [JsonPropertyName("itemType")]
            public string? ItemType { get; init; }
        }
    }
}
