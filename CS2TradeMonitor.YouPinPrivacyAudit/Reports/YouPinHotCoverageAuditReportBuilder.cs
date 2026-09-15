using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports
{
    /// <summary>
    /// Redacted report answering what was observed during one Full Coverage Audit.
    /// </summary>
    public static class YouPinHotCoverageAuditReportBuilder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static readonly string[] Limitations =
        [
            "结果只覆盖热门目录中已精确映射模板在分页上限内实际读取的公开记录；分页未完整时仍保留已经确认的正向命中。",
            "未观察到不等于没有求购，公开列表分页、映射缺口或接口失败都可能造成遗漏。",
            "这是当前公开可见状态，不是该用户的完整求购或交易历史。"
        ];

        public static string BuildJson(YouPinHotCoverageAuditSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return JsonSerializer.Serialize(CreateProjection(snapshot), JsonOptions);
        }

        public static string BuildMarkdown(YouPinHotCoverageAuditSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ReportProjection report = CreateProjection(snapshot);
            var markdown = new StringBuilder();

            markdown.AppendLine("# 悠悠热门范围求购观察（脱敏）");
            markdown.AppendLine();
            markdown.AppendLine($"- 观察时间：`{report.ObservedAt:O}`");
            markdown.AppendLine($"- 目标用户：`{report.TargetUser}`");
            markdown.AppendLine($"- 店铺读取：`{report.StoreStatus}`");
            markdown.AppendLine($"- 热门候选：{report.Coverage.CandidateCount} 个");
            markdown.AppendLine(
                $"- 精确映射：{report.Coverage.ResolvedCount} 个；"
                + $"未映射：{report.Coverage.UnresolvedCount} 个");
            markdown.AppendLine(
                $"- 完成模板：{report.Coverage.CompletedTemplateCount} 个；"
                + $"失败模板：{report.Coverage.IncompleteTemplateCount} 个；"
                + $"扫描批次：{report.Coverage.BatchCount} 个");
            markdown.AppendLine();
            markdown.AppendLine("## 结论");
            markdown.AppendLine();
            if (report.Purchases.Count == 0)
            {
                markdown.AppendLine("当前成功完成的热门模板中未观察到该用户的公开求购记录。");
            }
            else
            {
                markdown.AppendLine(
                    $"观察到 {report.Outcome.ObservedItemCount} 个饰品、"
                    + $"{report.Outcome.ObservationCount} 条公开求购记录。");
            }

            markdown.AppendLine();
            markdown.AppendLine("## 观察到的求购");
            markdown.AppendLine();
            if (report.Purchases.Count == 0)
            {
                markdown.AppendLine("无。");
            }
            else
            {
                markdown.AppendLine("| 热度排名 | 品类 | 饰品 | 求购价 | 剩余数量 | 磨损 | 自动收货 | 模板读取 |");
                markdown.AppendLine("| ---: | --- | --- | ---: | ---: | --- | --- | --- |");
                foreach (PurchaseProjection purchase in report.Purchases)
                {
                    markdown.AppendLine(
                        $"| {purchase.Rank} | {MarkdownCell(purchase.Category)} "
                        + $"| {MarkdownCell(purchase.MarketHashName)} "
                        + $"| {DecimalText(purchase.PurchasePrice)} "
                        + $"| {IntegerText(purchase.SurplusQuantity)} "
                        + $"| {MarkdownCell(purchase.AbradeText)} "
                        + $"| {BooleanText(purchase.AutoReceived)} "
                        + $"| {(purchase.TemplateScanComplete ? "完整" : "分页受限")} |");
                }
            }

            if (report.Failures.Count > 0)
            {
                markdown.AppendLine();
                markdown.AppendLine("## 未完成模板");
                markdown.AppendLine();
                markdown.AppendLine("| 模板 ID | 安全错误码 |");
                markdown.AppendLine("| ---: | --- |");
                foreach (FailureProjection failure in report.Failures)
                    markdown.AppendLine($"| {failure.TemplateId} | {MarkdownCell(failure.ReasonCode)} |");
            }

            markdown.AppendLine();
            markdown.AppendLine("## 限制条件");
            markdown.AppendLine();
            foreach (string limitation in report.Limitations)
                markdown.AppendLine("- " + limitation);

            return markdown.ToString();
        }

        private static ReportProjection CreateProjection(YouPinHotCoverageAuditSnapshot snapshot)
        {
            var completedTemplates = snapshot.CompletedTemplateIds.ToHashSet();
            IReadOnlyList<PurchaseProjection> purchases = snapshot.Observations
                .Select(observation => new PurchaseProjection(
                    observation.Rank,
                    observation.TemplateId,
                    observation.MarketHashName,
                    observation.Category,
                    observation.CommodityName,
                    observation.PurchasePrice,
                    observation.SurplusQuantity,
                    observation.AbradeText,
                    observation.FadeText,
                    observation.AutoReceived,
                    completedTemplates.Contains(observation.TemplateId)))
                .ToArray();
            IReadOnlyList<FailureProjection> failures = snapshot.Failures
                .Select(failure => new FailureProjection(
                    failure.TemplateId,
                    YouPinSafeText.Normalize(failure.ReasonCode, 120) ?? "unknown",
                    failure.StatusCode is null ? null : (int)failure.StatusCode.Value,
                    failure.PlatformCode))
                .ToArray();

            return new ReportProjection(
                SchemaVersion: 1,
                ObservedAt: snapshot.ObservedAt,
                TargetUser: MaskUserId(snapshot.Subject.UserId),
                ShareCredentialDisposition: snapshot.Subject.ShareCredentialWasRemoved
                    ? "removed"
                    : "not_present",
                StoreStatus: snapshot.StoreRead.Status.ToString().ToLowerInvariant(),
                PlanFingerprint: snapshot.PlanFingerprint,
                CatalogSourceSha256: snapshot.CatalogSourceSha256,
                Coverage: new CoverageProjection(
                    snapshot.CandidateCount,
                    snapshot.ResolvedCount,
                    snapshot.UnresolvedCount,
                    snapshot.BatchCount,
                    snapshot.CompletedTemplateIds.Count,
                    snapshot.IncompleteTemplateIds.Count),
                Outcome: new OutcomeProjection(
                    snapshot.ObservedItemCount,
                    snapshot.Observations.Count,
                    snapshot.IsPartial),
                Purchases: purchases,
                Failures: failures,
                Limitations: Limitations);
        }

        private static string MaskUserId(long userId)
        {
            string value = userId.ToString(CultureInfo.InvariantCulture);
            return value.Length <= 4 ? "***" : "***" + value[^4..];
        }

        private static string MarkdownCell(string? value)
        {
            if (value is null)
                return "—";
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("|", "\\|", StringComparison.Ordinal);
        }

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

        private sealed record ReportProjection(
            int SchemaVersion,
            DateTimeOffset ObservedAt,
            string TargetUser,
            string ShareCredentialDisposition,
            string StoreStatus,
            string PlanFingerprint,
            string CatalogSourceSha256,
            CoverageProjection Coverage,
            OutcomeProjection Outcome,
            IReadOnlyList<PurchaseProjection> Purchases,
            IReadOnlyList<FailureProjection> Failures,
            IReadOnlyList<string> Limitations);

        private sealed record CoverageProjection(
            int CandidateCount,
            int ResolvedCount,
            int UnresolvedCount,
            int BatchCount,
            int CompletedTemplateCount,
            int IncompleteTemplateCount);

        private sealed record OutcomeProjection(
            int ObservedItemCount,
            int ObservationCount,
            bool IsPartial);

        private sealed record PurchaseProjection(
            int Rank,
            long TemplateId,
            string MarketHashName,
            string Category,
            string? CommodityName,
            decimal? PurchasePrice,
            int? SurplusQuantity,
            string? AbradeText,
            string? FadeText,
            bool? AutoReceived,
            bool TemplateScanComplete);

        private sealed record FailureProjection(
            long TemplateId,
            string ReasonCode,
            int? HttpStatus,
            int? PlatformCode);
    }
}
