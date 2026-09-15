using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports
{
    /// <summary>
    /// Produces durable reports from a validated snapshot without writing full user identity,
    /// store name, link, credentials, order numbers, or raw responses.
    /// </summary>
    public static class YouPinPrivacyAuditReportBuilder
    {
        private static readonly JsonSerializerOptions ReportJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static readonly string[] Limitations =
        {
            "仅检查调用方明确提供的模板 ID，不发现或枚举其他模板。",
            "未命中只表示在本次有限范围内未观察到，不能证明全站不存在记录。",
            "验证只使用匿名、只读公开接口；如接口要求鉴权，验证应停止。"
        };

        public static string BuildJson(YouPinPrivacyAuditSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return JsonSerializer.Serialize(CreateProjection(snapshot), ReportJsonOptions);
        }

        public static string BuildMarkdown(YouPinPrivacyAuditSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ReportProjection report = CreateProjection(snapshot);
            var markdown = new StringBuilder();

            markdown.AppendLine("# 悠悠公开信息暴露审计（脱敏）");
            markdown.AppendLine();
            markdown.AppendLine($"- 生成时间：`{report.GeneratedAt:O}`");
            markdown.AppendLine($"- 目标用户：`{report.TargetUser}`");
            markdown.AppendLine($"- 来源域名：`{report.SourceHost}`");
            markdown.AppendLine($"- 分享凭据：{CredentialDispositionText(report.ShareCredentialDisposition)}");
            markdown.AppendLine($"- 店铺概况：{StoreStatusText(report.Store)}");
            markdown.AppendLine(
                $"- 查询范围：明确模板 {report.Scope.RequestedTemplateCount} 个，"
                + $"完成 {report.Scope.CompletedTemplateCount} 个；每模板最多 "
                + $"{report.Scope.MaximumPagesPerTemplate} 页、每页 "
                + $"{report.Scope.PageSize} 条");
            markdown.AppendLine(
                "- 模板 ID："
                + string.Join(
                    "、",
                    report.Scope.TemplateIds.Select(templateId => $"`{templateId}`")));
            markdown.AppendLine();
            markdown.AppendLine("## 结论");
            markdown.AppendLine();
            markdown.AppendLine("- " + Conclusion(report));
            markdown.AppendLine($"- 证据状态：`{report.Outcome.Status}`");

            if (report.Store.Status == "completed")
            {
                markdown.AppendLine();
                markdown.AppendLine("## 店铺公开状态");
                markdown.AppendLine();
                markdown.AppendLine($"- 在线：{BooleanText(report.Store.IsOnline)}");
                markdown.AppendLine($"- 注册描述：{MarkdownText(report.Store.RegistrationDescription)}");
                markdown.AppendLine($"- 交付成功率：{MarkdownText(report.Store.DeliverySuccessRate)}");
                markdown.AppendLine($"- 平均交付时间：{MarkdownText(report.Store.AverageDeliveryTime)}");
                markdown.AppendLine($"- 在售商品公开：{BooleanText(report.Store.StoreCommoditiesVisible)}");
                markdown.AppendLine($"- 动态墙公开：{BooleanText(report.Store.DynamicWallVisible)}");
            }
            else if (report.Store.Status == "failed")
            {
                markdown.AppendLine();
                markdown.AppendLine("## 店铺读取失败");
                markdown.AppendLine();
                markdown.AppendLine($"- 安全错误码：`{MarkdownCell(report.Store.FailureReasonCode)}`");
                markdown.AppendLine($"- HTTP：{IntegerText(report.Store.FailureHttpStatus)}");
                markdown.AppendLine($"- 平台码：{IntegerText(report.Store.FailurePlatformCode)}");
            }

            markdown.AppendLine();
            markdown.AppendLine("## 指定模板命中");
            markdown.AppendLine();
            if (report.Matches.Count == 0)
            {
                markdown.AppendLine("本次有限范围内未观察到匹配记录。");
            }
            else
            {
                markdown.AppendLine("| 模板 ID | 页码 | 商品 | 求购价 | 剩余数量 | 磨损 | 渐变 | 自动收货 | 排名第一 |");
                markdown.AppendLine("| --- | ---: | --- | ---: | ---: | --- | --- | --- | --- |");
                foreach (MatchProjection match in report.Matches)
                {
                    markdown.AppendLine(
                        $"| {match.TemplateId} | {match.PageIndex} | {MarkdownCell(match.CommodityName)} "
                        + $"| {DecimalText(match.PurchasePrice)} | {IntegerText(match.SurplusQuantity)} "
                        + $"| {MarkdownCell(match.AbradeText)} | {MarkdownCell(match.FadeText)} "
                        + $"| {BooleanText(match.AutoReceived)} | {BooleanText(match.IsRankFirst)} |");
                }
            }

            if (report.Failures.Count > 0)
            {
                markdown.AppendLine();
                markdown.AppendLine("## 未完成模板");
                markdown.AppendLine();
                markdown.AppendLine("| 模板 ID | 安全错误码 | HTTP | 平台码 |");
                markdown.AppendLine("| --- | --- | ---: | ---: |");
                foreach (FailureProjection failure in report.Failures)
                {
                    markdown.AppendLine(
                        $"| {failure.TemplateId} | {MarkdownCell(failure.ReasonCode)} "
                        + $"| {IntegerText(failure.HttpStatus)} | {IntegerText(failure.PlatformCode)} |");
                }
            }

            markdown.AppendLine();
            markdown.AppendLine("## 限制条件");
            markdown.AppendLine();
            foreach (string limitation in report.Limitations)
                markdown.AppendLine("- " + limitation);

            return markdown.ToString();
        }

        private static ReportProjection CreateProjection(YouPinPrivacyAuditSnapshot snapshot)
        {
            YouPinPurchaseExposureResult exposure = snapshot.PurchaseExposure;
            YouPinStoreReadResult storeRead = snapshot.StoreRead;
            YouPinPublicStoreSummary? store = storeRead.Summary;
            IReadOnlyList<MatchProjection> matches = exposure.Matches
                .Select(match => new MatchProjection(
                    match.TemplateId,
                    match.PageIndex,
                    YouPinSafeText.Normalize(match.CommodityName, 200),
                    match.PurchasePrice,
                    match.SurplusQuantity,
                    YouPinSafeText.Normalize(match.AbradeText, 80),
                    YouPinSafeText.Normalize(match.FadeText, 80),
                    match.AutoReceived,
                    match.IsRankFirst))
                .ToArray();
            IReadOnlyList<FailureProjection> failures = exposure.Failures
                .Select(failure => new FailureProjection(
                    failure.TemplateId,
                    YouPinSafeText.Normalize(failure.ReasonCode, 120) ?? "unknown",
                    failure.StatusCode is null ? null : (int)failure.StatusCode.Value,
                    failure.PlatformCode))
                .ToArray();

            string status = exposure.IsPartial
                ? "partial"
                : matches.Count > 0 ? "matched" : "not_observed";

            return new ReportProjection(
                SchemaVersion: 1,
                GeneratedAt: snapshot.ObservedAt.ToUniversalTime(),
                SourceHost: "hybrid.youpin898.com",
                TargetUser: MaskUserId(snapshot.Subject.UserId),
                ShareCredentialDisposition: snapshot.Subject.ShareCredentialWasRemoved
                    ? "removed"
                    : "not_present",
                Store: new StoreProjection(
                    Status: StoreStatusCode(storeRead.Status),
                    Name: store is null ? null : "[redacted]",
                    IsOnline: store?.IsOnline,
                    RegistrationDescription: YouPinSafeText.Normalize(store?.RegistrationDescription, 200),
                    DeliverySuccessRate: YouPinSafeText.Normalize(store?.DeliverySuccessRate, 80),
                    AverageDeliveryTime: YouPinSafeText.Normalize(store?.AverageDeliveryTime, 80),
                    StoreCommoditiesVisible: store?.StoreCommoditiesVisible,
                    DynamicWallVisible: store?.DynamicWallVisible,
                    FailureReasonCode: YouPinSafeText.Normalize(storeRead.ReasonCode, 120),
                    FailureHttpStatus: storeRead.StatusCode is null
                        ? null
                        : (int)storeRead.StatusCode.Value,
                    FailurePlatformCode: storeRead.PlatformCode),
                Scope: new ScopeProjection(
                    exposure.RequestedTemplateIds,
                    exposure.RequestedTemplateCount,
                    exposure.CompletedTemplateCount,
                    YouPinPublicAuditClient.PageSize,
                    YouPinPublicAuditClient.MaximumPagesPerTemplate),
                Outcome: new OutcomeProjection(status, matches.Count, failures.Count),
                Matches: matches,
                Failures: failures,
                Limitations: Limitations);
        }

        private static string Conclusion(ReportProjection report)
        {
            if (report.Matches.Count > 0)
            {
                string suffix = report.Outcome.Status == "partial"
                    ? "；另有部分模板未完成，结论仅覆盖成功响应。"
                    : "。";
                return $"在指定公开求购列表中观察到 {report.Matches.Count} 条与目标用户 ID 一致的记录{suffix}";
            }

            return report.Outcome.Status == "partial"
                ? "查询未完整完成，当前未观察到匹配记录，不能据此作否定结论。"
                : "在指定模板的本次公开响应中未观察到匹配记录；这不代表其他模板中不存在记录。";
        }

        private static string MaskUserId(long userId)
        {
            string value = userId.ToString(CultureInfo.InvariantCulture);
            return value.Length <= 4 ? "***" : "***" + value[^4..];
        }

        private static string CredentialDispositionText(string disposition)
            => disposition == "removed" ? "已识别并丢弃" : "输入未携带";

        private static string StoreStatusCode(YouPinStoreReadStatus status)
            => status switch
            {
                YouPinStoreReadStatus.NotAttempted => "not_attempted",
                YouPinStoreReadStatus.Completed => "completed",
                YouPinStoreReadStatus.Failed => "failed",
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };

        private static string StoreStatusText(StoreProjection store)
            => store.Status switch
            {
                "completed" => "已读取（店名已脱敏）",
                "failed" => $"失败（{MarkdownCell(store.FailureReasonCode)}）",
                _ => "未执行"
            };

        private static string MarkdownText(string? value)
            => value is null ? "—" : MarkdownCell(value);

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
            DateTimeOffset GeneratedAt,
            string SourceHost,
            string TargetUser,
            string ShareCredentialDisposition,
            StoreProjection Store,
            ScopeProjection Scope,
            OutcomeProjection Outcome,
            IReadOnlyList<MatchProjection> Matches,
            IReadOnlyList<FailureProjection> Failures,
            IReadOnlyList<string> Limitations);

        private sealed record StoreProjection(
            string Status,
            string? Name,
            bool? IsOnline,
            string? RegistrationDescription,
            string? DeliverySuccessRate,
            string? AverageDeliveryTime,
            bool? StoreCommoditiesVisible,
            bool? DynamicWallVisible,
            string? FailureReasonCode,
            int? FailureHttpStatus,
            int? FailurePlatformCode);

        private sealed record ScopeProjection(
            IReadOnlyList<long> TemplateIds,
            int RequestedTemplateCount,
            int CompletedTemplateCount,
            int PageSize,
            int MaximumPagesPerTemplate);

        private sealed record OutcomeProjection(string Status, int MatchCount, int FailureCount);

        private sealed record MatchProjection(
            long TemplateId,
            int PageIndex,
            string? CommodityName,
            decimal? PurchasePrice,
            int? SurplusQuantity,
            string? AbradeText,
            string? FadeText,
            bool? AutoReceived,
            bool? IsRankFirst);

        private sealed record FailureProjection(
            long TemplateId,
            string ReasonCode,
            int? HttpStatus,
            int? PlatformCode);
    }
}
