using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CS2MarketData.Core;

public sealed record SteamDtCatalogItem(
    string ItemId,
    string Name,
    string MarketHashName);

/// <summary>
/// Immutable SteamDT item catalog. JSON parsing and search normalization happen once at load time.
/// </summary>
public sealed class SteamDtItemCatalog
{
    private readonly Entry[] _entries;
    private readonly IReadOnlyDictionary<string, SteamDtCatalogItem> _byItemId;
    private readonly IReadOnlyDictionary<string, SteamDtCatalogItem> _byMarketHashName;
    private readonly IReadOnlyDictionary<string, SteamDtCatalogItem> _byName;
    private readonly IReadOnlyDictionary<string, SteamDtCatalogItem> _byNormalizedName;

    private SteamDtItemCatalog(IEnumerable<SteamDtCatalogItem> items)
    {
        _entries = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                && !string.IsNullOrWhiteSpace(item.MarketHashName))
            .DistinctBy(item => item.MarketHashName, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new Entry(item))
            .ToArray();
        _byItemId = BuildIndex(_entries, entry => entry.Item.ItemId);
        _byMarketHashName = BuildIndex(_entries, entry => entry.Item.MarketHashName);
        _byName = BuildIndex(_entries, entry => entry.Item.Name);
        _byNormalizedName = BuildIndex(_entries, entry => Normalize(entry.Item.Name));
    }

    public int Count => _entries.Length;

    public static SteamDtItemCatalog LoadGzip(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        return LoadJson(gzip);
    }

    public static SteamDtItemCatalog LoadJson(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream);
        return Create(document.RootElement, CancellationToken.None);
    }

    public static async Task<SteamDtItemCatalog> LoadGzipAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        return await LoadJsonAsync(gzip, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<SteamDtItemCatalog> LoadJsonAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return Create(document.RootElement, cancellationToken);
    }

    private static SteamDtItemCatalog Create(JsonElement root, CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("SteamDT item catalog root must be an array.");

        var items = new List<SteamDtCatalogItem>();
        foreach (JsonElement element in root.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string itemId = ReadString(element, "id");
            string name = ReadString(element, "name");
            string marketHashName = ReadString(element, "market_hash_name", "marketHashName");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(marketHashName))
                continue;

            items.Add(new SteamDtCatalogItem(
                itemId.Trim(),
                name.Trim(),
                marketHashName.Trim()));
        }

        var catalog = new SteamDtItemCatalog(items);
        if (catalog.Count == 0)
            throw new InvalidDataException("SteamDT item catalog does not contain valid items.");
        return catalog;
    }

    public IReadOnlyList<SteamDtCatalogItem> Search(string? keyword, int limit = 30)
    {
        string[] parts = GetNormalizedParts(keyword);
        if (parts.Length == 0 || _entries.Length == 0)
            return [];

        int safeLimit = Math.Clamp(limit, 1, 100);
        return _entries
            .Where(entry => parts.All(entry.Contains))
            .OrderBy(entry => entry.MatchRank(parts))
            .ThenBy(entry => entry.Item.Name.Length)
            .ThenBy(entry => entry.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(safeLimit)
            .Select(entry => entry.Item)
            .ToArray();
    }

    public SteamDtCatalogItem? FindExact(
        string? name = null,
        string? platformItemId = null,
        string? itemId = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            string normalizedName = Normalize(name);
            if (_byName.TryGetValue(name.Trim(), out SteamDtCatalogItem? nameMatch)
                || _byMarketHashName.TryGetValue(name.Trim(), out nameMatch)
                || _byNormalizedName.TryGetValue(normalizedName, out nameMatch))
            {
                return nameMatch;
            }
        }

        if (!string.IsNullOrWhiteSpace(platformItemId)
            && _byItemId.TryGetValue(platformItemId.Trim(), out SteamDtCatalogItem? platformMatch))
        {
            return platformMatch;
        }
        if (!string.IsNullOrWhiteSpace(itemId)
            && (_byItemId.TryGetValue(itemId.Trim(), out SteamDtCatalogItem? itemMatch)
                || _byMarketHashName.TryGetValue(itemId.Trim(), out itemMatch)))
        {
            return itemMatch;
        }
        return null;
    }

    public SteamDtCatalogItem? FindByMarketHashName(string? marketHashName)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
            return null;
        return _byMarketHashName.GetValueOrDefault(marketHashName.Trim());
    }

    public static bool Matches(string? name, string? marketHashName, string? keyword)
    {
        string[] parts = GetNormalizedParts(keyword);
        if (parts.Length == 0)
            return false;

        string normalizedName = Normalize(name);
        string normalizedHashName = Normalize(marketHashName);
        return parts.All(part => normalizedName.Contains(part, StringComparison.Ordinal)
            || normalizedHashName.Contains(part, StringComparison.Ordinal));
    }

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (!char.IsWhiteSpace(character)
                && character is not ('(' or ')' or '[' or ']' or '（' or '）' or '|' or '-'))
            {
                builder.Append(character == '*' ? '★' : char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string[] GetNormalizedParts(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return [];
        return keyword
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(static part => part.Length > 0)
            .ToArray();
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return string.Empty;
        foreach (string name in names)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out JsonElement property))
                continue;
            if (property.ValueKind == JsonValueKind.String)
                return property.GetString() ?? string.Empty;
            if (property.ValueKind == JsonValueKind.Number)
                return property.ToString();
        }

        return string.Empty;
    }

    private static IReadOnlyDictionary<string, SteamDtCatalogItem> BuildIndex(
        IEnumerable<Entry> entries,
        Func<Entry, string> keySelector)
    {
        return entries
            .Select(entry => (Key: keySelector(entry), entry.Item))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Item, StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class Entry
    {
        private readonly string _normalizedName;
        private readonly string _normalizedHashName;

        public Entry(SteamDtCatalogItem item)
        {
            Item = item;
            _normalizedName = Normalize(item.Name);
            _normalizedHashName = Normalize(item.MarketHashName);
        }

        public SteamDtCatalogItem Item { get; }

        public bool Contains(string part)
        {
            return _normalizedName.Contains(part, StringComparison.Ordinal)
                || _normalizedHashName.Contains(part, StringComparison.Ordinal);
        }

        public int MatchRank(IReadOnlyList<string> parts)
        {
            if (parts.Count != 1)
                return 4;
            string part = parts[0];
            if (_normalizedName == part || _normalizedHashName == part)
                return 0;
            if (_normalizedName.StartsWith(part, StringComparison.Ordinal))
                return 1;
            if (_normalizedHashName.StartsWith(part, StringComparison.Ordinal))
                return 2;
            return 3;
        }

    }
}
