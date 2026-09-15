namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi
{
    /// <summary>
    /// Bounded result for a caller-supplied set of template IDs.
    /// </summary>
    public sealed class YouPinPurchaseExposureResult
    {
        public YouPinPurchaseExposureResult(
            long targetUserId,
            IReadOnlyCollection<long> requestedTemplateIds,
            IReadOnlyCollection<YouPinPurchaseExposureMatch> matches,
            IReadOnlyCollection<YouPinTemplateQueryFailure> failures)
        {
            if (targetUserId <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetUserId));

            ArgumentNullException.ThrowIfNull(requestedTemplateIds);
            ArgumentNullException.ThrowIfNull(matches);
            ArgumentNullException.ThrowIfNull(failures);

            long[] normalizedTemplateIds = requestedTemplateIds.Distinct().ToArray();
            if (normalizedTemplateIds.Length == 0 || normalizedTemplateIds.Any(templateId => templateId <= 0))
            {
                throw new ArgumentException(
                    "请求范围必须包含至少一个有效模板 ID。",
                    nameof(requestedTemplateIds));
            }

            YouPinPurchaseExposureMatch[] normalizedMatches = matches.ToArray();
            YouPinTemplateQueryFailure[] normalizedFailures = failures.ToArray();
            var requestedSet = normalizedTemplateIds.ToHashSet();
            if (normalizedMatches.Any(match => !requestedSet.Contains(match.TemplateId)))
            {
                throw new ArgumentException(
                    "命中记录包含请求范围之外的模板 ID。",
                    nameof(matches));
            }

            if (normalizedFailures.Any(failure => !requestedSet.Contains(failure.TemplateId))
                || normalizedFailures.Select(failure => failure.TemplateId).Distinct().Count()
                    != normalizedFailures.Length)
            {
                throw new ArgumentException(
                    "失败记录必须唯一对应请求范围内的模板 ID。",
                    nameof(failures));
            }

            TargetUserId = targetUserId;
            RequestedTemplateIds = Array.AsReadOnly(normalizedTemplateIds);
            Matches = Array.AsReadOnly(normalizedMatches);
            Failures = Array.AsReadOnly(normalizedFailures);
        }

        public long TargetUserId { get; }
        public IReadOnlyList<long> RequestedTemplateIds { get; }
        public int RequestedTemplateCount => RequestedTemplateIds.Count;
        public int CompletedTemplateCount => RequestedTemplateCount - Failures.Count;
        public IReadOnlyList<YouPinPurchaseExposureMatch> Matches { get; }
        public IReadOnlyList<YouPinTemplateQueryFailure> Failures { get; }
        public bool IsPartial => Failures.Count > 0;
    }
}
