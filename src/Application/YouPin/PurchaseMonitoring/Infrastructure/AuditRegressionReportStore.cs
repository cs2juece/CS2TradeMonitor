using System.Text;
using System.Text.Json;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed record AuditRegressionDelta(
    DateTimeOffset BaselineObservedAt,
    DateTimeOffset CurrentObservedAt,
    int BaselineCompletedTemplateCount,
    int CurrentCompletedTemplateCount,
    int BaselineIncompleteTemplateCount,
    int CurrentIncompleteTemplateCount,
    int BaselineObservedItemCount,
    int CurrentObservedItemCount,
    int BaselineObservationCount,
    int CurrentObservationCount,
    IReadOnlyList<string> NewlyObservedIdentities,
    IReadOnlyList<string> NoLongerObservedIdentities,
    IReadOnlyList<string> CurrentlyUncomparableIdentities)
{
    public int CompletedTemplateDelta => CurrentCompletedTemplateCount - BaselineCompletedTemplateCount;
    public int IncompleteTemplateDelta => CurrentIncompleteTemplateCount - BaselineIncompleteTemplateCount;
    public int ObservedItemDelta => CurrentObservedItemCount - BaselineObservedItemCount;
    public int ObservationDelta => CurrentObservationCount - BaselineObservationCount;
}

public sealed record AuditRegressionWriteResult(
    AuditRegressionDelta Delta,
    string JsonPath,
    string MarkdownPath);

public sealed class AuditRegressionReportStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AuditReportStore _auditReportStore;

    public AuditRegressionReportStore(AuditReportStore auditReportStore)
        => _auditReportStore = auditReportStore ?? throw new ArgumentNullException(nameof(auditReportStore));

    public async Task<AuditRegressionWriteResult> CompareAndWriteAsync(
        string baselineJsonPath,
        string currentJsonPath,
        string reportDirectory,
        CancellationToken cancellationToken = default)
    {
        AuditReportView baseline = await _auditReportStore.LoadAsync(
            baselineJsonPath,
            cancellationToken).ConfigureAwait(false);
        AuditReportView current = await _auditReportStore.LoadAsync(
            currentJsonPath,
            cancellationToken).ConfigureAwait(false);
        AuditRegressionDelta delta = Compare(baseline, current);
        string directory = Path.Combine(Path.GetFullPath(reportDirectory), "regressions");
        Directory.CreateDirectory(directory);
        string stem = $"full-audit-regression-{current.ObservedAt.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}";
        string jsonPath = Path.Combine(directory, stem + ".json");
        string markdownPath = Path.Combine(directory, stem + ".md");
        await WriteAtomicAsync(
            jsonPath,
            JsonSerializer.Serialize(delta, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(markdownPath, BuildMarkdown(delta), cancellationToken)
            .ConfigureAwait(false);
        return new AuditRegressionWriteResult(delta, jsonPath, markdownPath);
    }

    public static AuditRegressionDelta Compare(AuditReportView baseline, AuditReportView current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (baseline.CandidateCount != 1000 || current.CandidateCount != 1000)
            throw new InvalidDataException("回归对比要求两份报告都是 Top 1000 覆盖结果。");
        string[] baselineNames = baseline.Purchases.Select(item => item.MarketHashName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] currentNames = current.Purchases.Select(item => item.MarketHashName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        PurchaseRow[] missingPriorRows = baseline.Purchases
            .Where(item => !currentNames.Contains(item.MarketHashName, StringComparer.Ordinal))
            .ToArray();
        bool planIsComparable = string.Equals(
            baseline.PlanFingerprint,
            current.PlanFingerprint,
            StringComparison.Ordinal);
        bool failuresAreIdentified = current.HasCompleteIncompleteTemplateIdentitySet;
        HashSet<long> incompleteTemplateIds = current.IncompleteTemplateIds.ToHashSet();
        string[] currentlyUncomparable = missingPriorRows
            .Where(item => !planIsComparable
                || !failuresAreIdentified
                || incompleteTemplateIds.Contains(item.TemplateId))
            .Select(item => item.MarketHashName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] noLongerObserved = missingPriorRows
            .Where(item => planIsComparable
                && failuresAreIdentified
                && !incompleteTemplateIds.Contains(item.TemplateId))
            .Select(item => item.MarketHashName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new AuditRegressionDelta(
            baseline.ObservedAt,
            current.ObservedAt,
            baseline.CompletedTemplateCount,
            current.CompletedTemplateCount,
            baseline.IncompleteTemplateCount,
            current.IncompleteTemplateCount,
            baseline.ObservedItemCount,
            current.ObservedItemCount,
            baseline.ObservationCount,
            current.ObservationCount,
            currentNames.Except(baselineNames, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            noLongerObserved,
            currentlyUncomparable);
    }

    private static string BuildMarkdown(AuditRegressionDelta delta)
    {
        var builder = new StringBuilder()
            .AppendLine("# Top 1000 真实回归差异")
            .AppendLine()
            .AppendLine($"- Baseline observed at: {delta.BaselineObservedAt:O}")
            .AppendLine($"- Current observed at: {delta.CurrentObservedAt:O}")
            .AppendLine($"- Completed templates: {delta.BaselineCompletedTemplateCount} → {delta.CurrentCompletedTemplateCount} ({delta.CompletedTemplateDelta:+#;-#;0})")
            .AppendLine($"- Incomplete templates: {delta.BaselineIncompleteTemplateCount} → {delta.CurrentIncompleteTemplateCount} ({delta.IncompleteTemplateDelta:+#;-#;0})")
            .AppendLine($"- Observed items: {delta.BaselineObservedItemCount} → {delta.CurrentObservedItemCount} ({delta.ObservedItemDelta:+#;-#;0})")
            .AppendLine($"- Purchase observations: {delta.BaselineObservationCount} → {delta.CurrentObservationCount} ({delta.ObservationDelta:+#;-#;0})")
            .AppendLine()
            .AppendLine("读取失败不等于没有求购；本报告只比较两次脱敏覆盖结果。")
            .AppendLine()
            .AppendLine("## Newly observed identities");
        foreach (string item in delta.NewlyObservedIdentities)
            builder.AppendLine($"- {item}");
        builder.AppendLine().AppendLine("## No longer observed identities");
        foreach (string item in delta.NoLongerObservedIdentities)
            builder.AppendLine($"- {item}");
        builder.AppendLine().AppendLine("## Currently uncomparable identities");
        foreach (string item in delta.CurrentlyUncomparableIdentities)
            builder.AppendLine($"- {item}");
        if (delta.CurrentlyUncomparableIdentities.Count > 0)
        {
            builder.AppendLine()
                .AppendLine("这些旧观察因当前模板失败、失败身份清单不完整或覆盖计划指纹变化而不可比较，不能解释为求购消失。");
        }
        return builder.ToString();
    }

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
