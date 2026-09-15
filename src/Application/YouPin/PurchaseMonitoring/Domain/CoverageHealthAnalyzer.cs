using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Domain;

public sealed class CoverageHealthAnalyzer
{
    public CoverageHealthSample Analyze(YouPinCoverageTickResult tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        return Analyze(
            tick.ObservedAt,
            tick.CompletedTemplateIds.Count + tick.IncompleteTemplateIds.Count,
            tick.Failures);
    }

    public CoverageHealthSample Analyze(
        DateTimeOffset observedAt,
        int attemptedTemplateCount,
        IReadOnlyCollection<YouPinTemplateQueryFailure> failures)
    {
        if (observedAt == default)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        if (attemptedTemplateCount is < 1 or > 20)
            throw new ArgumentOutOfRangeException(nameof(attemptedTemplateCount));
        ArgumentNullException.ThrowIfNull(failures);
        int failedTemplateCount = failures.Select(item => item.TemplateId).Distinct().Count();
        if (failedTemplateCount != failures.Count)
            throw new ArgumentException("同一模板只能记录一个失败原因。", nameof(failures));
        if (failedTemplateCount > attemptedTemplateCount)
            throw new ArgumentException("失败模板数不能超过本批尝试模板数。", nameof(failures));
        FailureReasonCount[] distribution = failures
            .GroupBy(item => NormalizeReasonCode(item.ReasonCode)
                + (item.PlatformCode is { } code ? $".code_{code}" : ""), StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FailureReasonCount(group.Key, group.Count()))
            .ToArray();
        return new CoverageHealthSample(
            observedAt.ToUniversalTime(),
            attemptedTemplateCount,
            failedTemplateCount,
            Array.AsReadOnly(distribution));
    }

    private static string NormalizeReasonCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        string trimmed = value.Trim();
        if (trimmed.Length > 64
            || trimmed.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '_' and not '-' and not '.'))
        {
            return "other";
        }
        return trimmed.ToLowerInvariant();
    }
}
