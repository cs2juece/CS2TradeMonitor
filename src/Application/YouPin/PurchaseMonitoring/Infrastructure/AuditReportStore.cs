using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using System.Globalization;
using System.Text.Json;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed class AuditReportStore
{
    public const int MaximumReportBytes = 16 * 1024 * 1024;

    public async Task<AuditWriteResult> WriteFullAuditAsync(
        YouPinHotCoverageAuditSnapshot snapshot,
        string reportDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(reportDirectory);
        string stem = "full-audit-" + snapshot.ObservedAt.UtcDateTime.ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);
        string markdownPath = Path.Combine(reportDirectory, stem + ".md");
        string jsonPath = Path.Combine(reportDirectory, stem + ".json");
        await WriteAtomicAsync(
            markdownPath,
            YouPinHotCoverageAuditReportBuilder.BuildMarkdown(snapshot),
            cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(
            jsonPath,
            YouPinHotCoverageAuditReportBuilder.BuildJson(snapshot),
            cancellationToken).ConfigureAwait(false);
        AuditReportView view = await LoadAsync(jsonPath, cancellationToken).ConfigureAwait(false);
        return new AuditWriteResult(markdownPath, jsonPath, view);
    }

    public async Task<IReadOnlyList<AuditReportView>> LoadRecentAsync(
        IEnumerable<string> directories,
        int maximumCount = 40,
        CancellationToken cancellationToken = default)
    {
        string[] files = directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.GetFiles(directory, "full-audit-*.json"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(maximumCount)
            .ToArray();
        var result = new List<AuditReportView>(files.Length);
        foreach (string file in files)
        {
            try
            {
                result.Add(await LoadAsync(file, cancellationToken).ConfigureAwait(false));
            }
            catch (InvalidDataException)
            {
                // Ignore malformed or unsafe legacy report files.
            }
            catch (JsonException)
            {
                // Ignore malformed or unsafe legacy report files.
            }
        }

        return result.OrderByDescending(item => item.ObservedAt).ToArray();
    }

    public async Task<AuditReportView> LoadAsync(
        string jsonPath,
        CancellationToken cancellationToken = default)
    {
        FileInfo info = new(jsonPath);
        if (info.Length > MaximumReportBytes)
            throw new InvalidDataException("完整审计报告超过安全大小上限。");
        await using FileStream stream = File.OpenRead(jsonPath);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string targetUser = TargetMaskValidator.Validate(RequiredString(root, "targetUser"));
        string planFingerprint = RequiredString(root, "planFingerprint");
        if (planFingerprint.Length != 64
            || planFingerprint.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new InvalidDataException("报告计划指纹无效。");
        }

        JsonElement coverage = root.GetProperty("coverage");
        JsonElement outcome = root.GetProperty("outcome");
        var purchases = new List<PurchaseRow>();
        foreach (JsonElement purchase in root.GetProperty("purchases").EnumerateArray())
        {
            purchases.Add(new PurchaseRow(
                purchase.GetProperty("rank").GetInt32(),
                purchase.GetProperty("templateId").GetInt64(),
                RequiredString(purchase, "category"),
                RequiredString(purchase, "marketHashName"),
                NullableDecimal(purchase, "purchasePrice"),
                NullableInt32(purchase, "surplusQuantity"),
                NullableString(purchase, "abradeText"),
                NullableBoolean(purchase, "autoReceived"),
                purchase.GetProperty("templateScanComplete").GetBoolean()));
        }
        long[] incompleteTemplateIds = root.GetProperty("failures")
            .EnumerateArray()
            .Select(failure => failure.GetProperty("templateId").GetInt64())
            .ToArray();
        if (incompleteTemplateIds.Any(templateId => templateId <= 0)
            || incompleteTemplateIds.Distinct().Count() != incompleteTemplateIds.Length)
        {
            throw new InvalidDataException("报告失败模板身份无效或重复。");
        }

        return new AuditReportView(
            jsonPath,
            root.GetProperty("observedAt").GetDateTimeOffset(),
            targetUser,
            RequiredString(root, "storeStatus"),
            planFingerprint,
            coverage.GetProperty("candidateCount").GetInt32(),
            coverage.GetProperty("resolvedCount").GetInt32(),
            coverage.GetProperty("completedTemplateCount").GetInt32(),
            coverage.GetProperty("incompleteTemplateCount").GetInt32(),
            coverage.GetProperty("batchCount").GetInt32(),
            outcome.GetProperty("observedItemCount").GetInt32(),
            outcome.GetProperty("observationCount").GetInt32(),
            outcome.GetProperty("isPartial").GetBoolean(),
            purchases.AsReadOnly(),
            Array.AsReadOnly(incompleteTemplateIds));
    }

    private static string RequiredString(JsonElement element, string propertyName)
        => element.GetProperty(propertyName).GetString()
            ?? throw new InvalidDataException($"报告缺少 {propertyName}。");

    private static string? NullableString(JsonElement element, string propertyName)
    {
        JsonElement value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static decimal? NullableDecimal(JsonElement element, string propertyName)
    {
        JsonElement value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
    }

    private static int? NullableInt32(JsonElement element, string propertyName)
    {
        JsonElement value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }

    private static bool? NullableBoolean(JsonElement element, string propertyName)
    {
        JsonElement value = element.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetBoolean();
    }

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken)
                .ConfigureAwait(false);
            if (new FileInfo(temporaryPath).Length > MaximumReportBytes)
                throw new InvalidDataException("完整审计报告超过安全大小上限。");
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
