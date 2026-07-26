using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2MarketData.Core;

public enum MlUniverseRole
{
    PersonalPriority,
    BroadReference,
    FrozenBenchmark
}

public enum MlItemWear
{
    NotApplicable,
    FactoryNew,
    MinimalWear,
    FieldTested,
    WellWorn,
    BattleScarred
}

public enum MlSpecialVariantKind
{
    None,
    Template,
    Pattern,
    SpecialVersion,
    Combined
}

public sealed record MlExactItemIdentity(
    int AppId,
    string ItemId,
    string MarketHashName,
    string Category,
    MlItemWear Wear,
    bool StatTrak,
    bool Souvenir,
    MlSpecialVariantKind SpecialVariantKind,
    string? Template,
    int? Pattern,
    string? SpecialVersion,
    DateTimeOffset SourceObservedAt,
    bool IdentityConfirmed);

public sealed record MlObservedCandle(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal? Turnover,
    DateTimeOffset ObservedAt);

public sealed record MlMarketSnapshotItem(
    MlExactItemIdentity Identity,
    MlUniverseRole Role,
    IReadOnlyList<MlObservedCandle> Candles);

public sealed record MlMarketSnapshotWriteResult(
    string Path,
    string Sha256,
    bool Created);

public static class MlMarketSnapshotV1Exporter
{
    private const string ContractVersion = "1.0";
    private const string SourceProduct = "CS2TradeMonitor";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<MlMarketSnapshotWriteResult> WriteAsync(
        string path,
        DateTimeOffset exportedAt,
        string? sourceCommit,
        IReadOnlyList<MlMarketSnapshotItem> items,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Market snapshot output path is required.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(items);
        MlMarketSnapshotDocument document = BuildDocument(exportedAt, sourceCommit, items);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(payload));
        string target = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(target)
            ?? throw new ArgumentException("Market snapshot output path needs a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        if (File.Exists(target))
        {
            byte[] existing = await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(payload))
            {
                throw new InvalidDataException("An existing market snapshot export cannot be overwritten with different content.");
            }

            return new MlMarketSnapshotWriteResult(target, sha256, false);
        }

        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, payload, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return new MlMarketSnapshotWriteResult(target, sha256, true);
    }

    private static MlMarketSnapshotDocument BuildDocument(
        DateTimeOffset exportedAt,
        string? sourceCommit,
        IReadOnlyList<MlMarketSnapshotItem> items)
    {
        if (exportedAt == default || items.Count == 0)
        {
            throw new InvalidDataException("A market snapshot needs an export time and at least one exact item.");
        }

        MlMarketSnapshotItem[] normalized = items
            .Select(item => Normalize(item, exportedAt))
            .OrderBy(item => item.Identity.ItemId, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Select(item => item.Identity.ItemId).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new InvalidDataException("Market snapshot item ids must be unique.");
        }

        string? normalizedCommit = string.IsNullOrWhiteSpace(sourceCommit) ? null : sourceCommit.Trim();
        return new MlMarketSnapshotDocument(
            ContractVersion,
            exportedAt,
            SourceProduct,
            normalizedCommit,
            normalized);
    }

    private static MlMarketSnapshotItem Normalize(MlMarketSnapshotItem item, DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        MlExactItemIdentity identity = item.Identity
            ?? throw new InvalidDataException("Market snapshot item identity is required.");
        if (identity.AppId != 730
            || !identity.IdentityConfirmed
            || string.IsNullOrWhiteSpace(identity.ItemId)
            || string.IsNullOrWhiteSpace(identity.MarketHashName)
            || string.IsNullOrWhiteSpace(identity.Category)
            || (identity.StatTrak && identity.Souvenir)
            || identity.SourceObservedAt == default
            || identity.SourceObservedAt > exportedAt
            || !Enum.IsDefined(identity.Wear)
            || !Enum.IsDefined(identity.SpecialVariantKind)
            || !Enum.IsDefined(item.Role))
        {
            throw new InvalidDataException("Market snapshot item identity is incomplete or unconfirmed.");
        }

        string? template = NormalizeOptional(identity.Template);
        string? specialVersion = NormalizeOptional(identity.SpecialVersion);
        ValidateSpecialVariant(identity.SpecialVariantKind, template, identity.Pattern, specialVersion);
        if (item.Candles is null || item.Candles.Count == 0)
        {
            throw new InvalidDataException("Market snapshot item history is required.");
        }

        MlObservedCandle[] candles = item.Candles.OrderBy(candle => candle.Date).ToArray();
        if (candles.Select(candle => candle.Date).Distinct().Count() != candles.Length)
        {
            throw new InvalidDataException("Market snapshot candle dates must be unique.");
        }

        foreach (MlObservedCandle candle in candles)
        {
            if (candle.Open <= 0m
                || candle.High <= 0m
                || candle.Low <= 0m
                || candle.Close <= 0m
                || candle.High < Math.Max(candle.Open, candle.Close)
                || candle.Low > Math.Min(candle.Open, candle.Close)
                || candle.Volume < 0m
                || candle.Turnover < 0m
                || candle.ObservedAt == default
                || candle.ObservedAt > exportedAt
                || DateOnly.FromDateTime(candle.ObservedAt.Date) < candle.Date)
            {
                throw new InvalidDataException("Market snapshot contains an invalid or future-observed candle.");
            }
        }

        MlExactItemIdentity normalizedIdentity = identity with
        {
            ItemId = identity.ItemId.Trim(),
            MarketHashName = identity.MarketHashName.Trim(),
            Category = identity.Category.Trim(),
            Template = template,
            SpecialVersion = specialVersion
        };
        return item with { Identity = normalizedIdentity, Candles = candles };
    }

    private static void ValidateSpecialVariant(
        MlSpecialVariantKind kind,
        string? template,
        int? pattern,
        string? specialVersion)
    {
        if (pattern < 0)
        {
            throw new InvalidDataException("A special pattern cannot be negative.");
        }

        int populated = (template is null ? 0 : 1) + (pattern.HasValue ? 1 : 0) + (specialVersion is null ? 0 : 1);
        bool valid = kind switch
        {
            MlSpecialVariantKind.None => populated == 0,
            MlSpecialVariantKind.Template => template is not null && populated == 1,
            MlSpecialVariantKind.Pattern => pattern.HasValue && populated == 1,
            MlSpecialVariantKind.SpecialVersion => specialVersion is not null && populated == 1,
            MlSpecialVariantKind.Combined => populated >= 2,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException("Special variant fields do not match specialVariantKind.");
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record MlMarketSnapshotDocument(
        string ContractVersion,
        DateTimeOffset ExportedAt,
        string SourceProduct,
        string? SourceCommit,
        IReadOnlyList<MlMarketSnapshotItem> Items);
}
