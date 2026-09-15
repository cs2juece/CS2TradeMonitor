using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports
{
    /// <summary>
    /// Builds user-ID-free change output for one scheduled Coverage Tick.
    /// </summary>
    public static class YouPinCoverageTickReportBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public static string BuildJson(YouPinCoverageTickResult tick)
        {
            ArgumentNullException.ThrowIfNull(tick);
            return JsonSerializer.Serialize(CreateProjection(tick), JsonOptions);
        }

        public static string BuildMarkdown(YouPinCoverageTickResult tick)
        {
            ArgumentNullException.ThrowIfNull(tick);
            TickProjection report = CreateProjection(tick);
            var markdown = new StringBuilder();
            markdown.AppendLine("# 悠悠热门求购轮转变化（脱敏）");
            markdown.AppendLine();
            markdown.AppendLine($"- Watch ID：`{report.WatchId:N}`");
            markdown.AppendLine($"- 时间：`{report.ObservedAt:O}`");
            markdown.AppendLine($"- 批次：{report.BatchNumber}/{report.TotalBatchCount}");
            markdown.AppendLine(
                $"- 完成模板：{report.CompletedTemplateCount}；失败模板：{report.IncompleteTemplateCount}");
            markdown.AppendLine(
                $"- 新出现：{report.Appeared.Count}；不再观察到：{report.CeasedToBeObserved.Count}");
            markdown.AppendLine();
            markdown.AppendLine("## 新出现");
            markdown.AppendLine();
            AppendObservations(markdown, report.Appeared);
            markdown.AppendLine();
            markdown.AppendLine("## 不再观察到");
            markdown.AppendLine();
            AppendObservations(markdown, report.CeasedToBeObserved);
            if (report.Failures.Count > 0)
            {
                markdown.AppendLine();
                markdown.AppendLine("## 本批未完成模板");
                markdown.AppendLine();
                foreach (FailureProjection failure in report.Failures)
                    markdown.AppendLine($"- `{failure.TemplateId}`：`{failure.ReasonCode}`");
            }
            markdown.AppendLine();
            markdown.AppendLine(
                "说明：失败模板保留旧基线，不会据此报告求购消失；首次成功读取只建立基线，不产生变化告警。");
            return markdown.ToString();
        }

        private static void AppendObservations(
            StringBuilder markdown,
            IReadOnlyList<ObservationProjection> observations)
        {
            if (observations.Count == 0)
            {
                markdown.AppendLine("无。");
                return;
            }

            markdown.AppendLine("| 模板 ID | 饰品 | 求购价 | 剩余数量 | 磨损 | 自动收货 |");
            markdown.AppendLine("| ---: | --- | ---: | ---: | --- | --- |");
            foreach (ObservationProjection observation in observations)
            {
                markdown.AppendLine(
                    $"| {observation.TemplateId} | {MarkdownCell(observation.CommodityName)} "
                    + $"| {DecimalText(observation.PurchasePrice)} "
                    + $"| {IntegerText(observation.SurplusQuantity)} "
                    + $"| {MarkdownCell(observation.AbradeText)} "
                    + $"| {BooleanText(observation.AutoReceived)} |");
            }
        }

        private static TickProjection CreateProjection(YouPinCoverageTickResult tick)
            => new(
                SchemaVersion: 1,
                tick.WatchId,
                tick.PlanFingerprint,
                tick.ObservedAt,
                tick.BatchNumber,
                tick.TotalBatchCount,
                tick.CompletedTemplateIds.Count,
                tick.IncompleteTemplateIds.Count,
                tick.UpdatedState.NextCursor,
                tick.UpdatedState.CompletedCoverageCycles,
                tick.Appeared.Select(Project).ToArray(),
                tick.CeasedToBeObserved.Select(Project).ToArray(),
                tick.EstablishedBaseline.Count,
                tick.Failures.Select(failure => new FailureProjection(
                    failure.TemplateId,
                    YouPinSafeText.Normalize(failure.ReasonCode, 120) ?? "unknown",
                    failure.StatusCode is null ? null : (int)failure.StatusCode.Value,
                    failure.PlatformCode)).ToArray());

        private static ObservationProjection Project(YouPinPurchaseObservation observation)
            => new(
                observation.TemplateId,
                observation.CommodityName,
                observation.PurchasePrice,
                observation.SurplusQuantity,
                observation.AbradeText,
                observation.FadeText,
                observation.AutoReceived);

        private static string MarkdownCell(string? value)
            => value is null
                ? "—"
                : value.Replace("&", "&amp;", StringComparison.Ordinal)
                    .Replace("<", "&lt;", StringComparison.Ordinal)
                    .Replace(">", "&gt;", StringComparison.Ordinal)
                    .Replace("|", "\\|", StringComparison.Ordinal);

        private static string DecimalText(decimal? value)
            => value?.ToString("0.############################", CultureInfo.InvariantCulture) ?? "—";

        private static string IntegerText(int? value)
            => value?.ToString(CultureInfo.InvariantCulture) ?? "—";

        private static string BooleanText(bool? value)
            => value switch
            {
                true => "是",
                false => "否",
                null => "—"
            };

        private sealed record TickProjection(
            int SchemaVersion,
            Guid WatchId,
            string PlanFingerprint,
            DateTimeOffset ObservedAt,
            int BatchNumber,
            int TotalBatchCount,
            int CompletedTemplateCount,
            int IncompleteTemplateCount,
            int NextCursor,
            long CompletedCoverageCycles,
            IReadOnlyList<ObservationProjection> Appeared,
            IReadOnlyList<ObservationProjection> CeasedToBeObserved,
            int EstablishedBaselineCount,
            IReadOnlyList<FailureProjection> Failures);

        private sealed record ObservationProjection(
            long TemplateId,
            string? CommodityName,
            decimal? PurchasePrice,
            int? SurplusQuantity,
            string? AbradeText,
            string? FadeText,
            bool? AutoReceived);

        private sealed record FailureProjection(
            long TemplateId,
            string ReasonCode,
            int? HttpStatus,
            int? PlatformCode);
    }
}
