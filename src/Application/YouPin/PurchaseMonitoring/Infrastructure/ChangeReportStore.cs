using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using YouPinPurchaseMonitor.Domain;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed class ChangeReportStore
{
    public const int MaximumReportBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PurchaseChangeClassifier _classifier = new();
    private readonly CoverageHealthAnalyzer _healthAnalyzer = new();

    public async Task<ChangeBatchView> WriteAsync(
        YouPinCoverageTickResult tick,
        string safeNote,
        string reportDirectory,
        CancellationToken cancellationToken = default,
        IReadOnlyList<PurchaseChangeView>? shopChanges = null)
    {
        SafeNoteValidator.Validate(safeNote);
        string directory = Path.Combine(reportDirectory, "changes");
        Directory.CreateDirectory(directory);
        string stem = $"coverage-tick-{tick.ObservedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}-{tick.WatchId:N}";
        string markdownPath = Path.Combine(directory, stem + ".md");
        string coreJsonPath = Path.Combine(directory, stem + ".core.json");
        string classifiedPath = Path.Combine(directory, stem + ".json");
        IReadOnlyList<PurchaseChangeView> changes = shopChanges ?? _classifier.Classify(tick);
        var view = new ChangeBatchView(
            classifiedPath,
            tick.WatchId,
            safeNote,
            tick.ObservedAt,
            tick.BatchNumber,
            tick.TotalBatchCount,
            tick.IsPartial,
            changes)
        {
            Health = _healthAnalyzer.Analyze(tick)
        };

        await WriteAtomicAsync(markdownPath, YouPinCoverageTickReportBuilder.BuildMarkdown(tick), cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicAsync(coreJsonPath, YouPinCoverageTickReportBuilder.BuildJson(tick), cancellationToken)
            .ConfigureAwait(false);
        await WriteAtomicAsync(
            classifiedPath,
            JsonSerializer.Serialize(view, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        return view;
    }

    public async Task<IReadOnlyList<ChangeBatchView>> LoadRecentAsync(
        string reportDirectory,
        int maximumCount = 200,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? retainLatestFor = null)
    {
        string directory = Path.Combine(reportDirectory, "changes");
        if (!Directory.Exists(directory))
            return [];
        string[] files = Directory.GetFiles(directory, "coverage-tick-*.json")
            .Where(path => !path.EndsWith(".core.json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        int limit = Math.Clamp(maximumCount, 1, 500);
        var pending = retainLatestFor?.ToHashSet() ?? [];
        var result = new List<ChangeBatchView>();
        int fileIndex = 0;
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool recent = fileIndex++ < limit;
            if (!recent && pending.Count == 0) break;
            if (!recent && !pending.Any(id => file.EndsWith($"-{id:N}.json", StringComparison.OrdinalIgnoreCase)))
                continue;
            ChangeBatchView? view = await ReadReportAsync(file, cancellationToken).ConfigureAwait(false);
            if (view is not null)
            {
                bool retained = view.HasChanges && pending.Remove(view.WatchId);
                if (recent || retained) result.Add(view);
            }
        }
        return result.OrderByDescending(item => item.ObservedAt).ToArray();
    }

    private static bool IsValidHealth(CoverageHealthSample? health, DateTimeOffset observedAt)
    {
        if (health is null)
            return true;
        if (health.ObservedAt != observedAt.ToUniversalTime()
            || health.AttemptedTemplateCount is < 1 or > 20
            || health.FailedTemplateCount < 0
            || health.FailedTemplateCount > health.AttemptedTemplateCount
            || health.ReasonDistribution is null || health.ReasonDistribution.Count > 20
            || health.ReasonDistribution.Any(item => item is null || item.Count <= 0
                || string.IsNullOrEmpty(item.ReasonCode) || item.ReasonCode.Length > 64
                || item.ReasonCode.Any(character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '_' and not '-' and not '.')))
        {
            return false;
        }
        return health.ReasonDistribution.Sum(item => item.Count) == health.FailedTemplateCount;
    }

    internal static async Task<ChangeBatchView?> ReadReportAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > MaximumReportBytes) return null;
            await using FileStream stream = File.OpenRead(path);
            ChangeBatchView? batch = await JsonSerializer.DeserializeAsync<ChangeBatchView>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (batch is null || batch.WatchId == Guid.Empty || batch.ObservedAt == default
                || batch.SchemaVersion is < 1 or > 3 || batch.Changes is null || batch.Changes.Count > 1000
                || batch.Changes.Any(change => change is null || !IsValidChange(change))
                || !IsValidHealth(batch.Health, batch.ObservedAt)) return null;
            SafeNoteValidator.Validate(batch.SafeNote);
            return batch with { JsonPath = path };
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsValidChange(PurchaseChangeView change)
    {
        if (!Enum.IsDefined(change.Kind)
            || change.TemplateId <= 0
            || !IsSafeText(change.CommodityName, 300)
            || !IsSafeText(change.Description, 600)
            || change.PreviousPrice < 0
            || change.CurrentPrice < 0
            || change.PreviousQuantity < 0
            || change.CurrentQuantity < 0
            || change.PriceTier is < 1 or > 1000
            || change.WearRange is not null && !IsSafeText(change.WearRange, 120))
        {
            return false;
        }
        bool pairedKind = change.Kind is PurchaseChangeKind.PriceChanged
            or PurchaseChangeKind.QuantityIncreased
            or PurchaseChangeKind.QuantityDecreased;
        return !pairedKind || change.PriceTier is not null && change.WearRange is not null;
    }

    private static bool IsSafeText(string? value, int maximumLength)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumLength
            && !value.Any(char.IsControl);

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)!;
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            if (new FileInfo(temporaryPath).Length > MaximumReportBytes)
                throw new InvalidDataException("变化报告超过安全大小上限。");
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
