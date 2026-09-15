using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Persistable, user-ID-free cursor and per-template baselines for a Coverage Watch.
    /// </summary>
    public sealed class YouPinCoverageWatchState
    {
        private YouPinCoverageWatchState(
            Guid watchId,
            string planFingerprint,
            int nextCursor,
            long completedCoverageCycles,
            DateTimeOffset? lastTickAt,
            IReadOnlyList<YouPinTemplateBaseline> baselines)
        {
            WatchId = watchId;
            PlanFingerprint = planFingerprint;
            NextCursor = nextCursor;
            CompletedCoverageCycles = completedCoverageCycles;
            LastTickAt = lastTickAt;
            Baselines = baselines;
        }

        public Guid WatchId { get; }
        public string PlanFingerprint { get; }
        public int NextCursor { get; }
        public long CompletedCoverageCycles { get; }
        public DateTimeOffset? LastTickAt { get; }
        public IReadOnlyList<YouPinTemplateBaseline> Baselines { get; }
        public int BaselineTemplateCount => Baselines.Count;
        public IReadOnlyList<YouPinPurchaseObservation> CurrentObservations => Array.AsReadOnly(
            Baselines.SelectMany(baseline => baseline.Observations).ToArray());

        // Initial traversal advances between scheduler polls; request throttling remains in the client.
        // A completed traversal does not imply every template was successfully read.
        public DateTimeOffset GetNextDueAt(TimeSpan interval)
            => LastTickAt?.Add(CompletedCoverageCycles == 0 ? TimeSpan.FromSeconds(2) : interval)
                ?? DateTimeOffset.MinValue;

        public static YouPinCoverageWatchState Create(
            YouPinAuthorizedCoverageWatch watch,
            YouPinHotCoveragePlan plan)
        {
            ValidateWatchAndPlan(watch, plan);
            return new YouPinCoverageWatchState(
                watch.Id,
                plan.Fingerprint,
                nextCursor: 0,
                completedCoverageCycles: 0,
                lastTickAt: null,
                Array.Empty<YouPinTemplateBaseline>());
        }

        public static YouPinCoverageWatchState BootstrapFromFullAudit(
            YouPinAuthorizedCoverageWatch watch,
            YouPinHotCoveragePlan plan,
            YouPinHotCoverageAuditSnapshot audit)
        {
            ValidateWatchAndPlan(watch, plan);
            ArgumentNullException.ThrowIfNull(audit);
            if (!string.Equals(audit.PlanFingerprint, plan.Fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("完整覆盖审计与授权计划不一致。", nameof(audit));
            if (audit.Subject.UserId != watch.Subject.UserId)
                throw new ArgumentException("完整覆盖审计与授权目标不一致。", nameof(audit));

            var observationsByTemplate = audit.Observations
                .Select(observation => new YouPinPurchaseObservation(
                    observation.TemplateId,
                    observation.CommodityName,
                    observation.PurchasePrice,
                    observation.SurplusQuantity,
                    observation.AbradeText,
                    observation.FadeText,
                    observation.AutoReceived))
                .GroupBy(observation => observation.TemplateId)
                .ToDictionary(group => group.Key, group => group.ToArray());
            YouPinTemplateBaseline[] baselines = audit.CompletedTemplateIds
                .Select(templateId => YouPinTemplateBaseline.Restore(
                    templateId,
                    audit.ObservedAt,
                    observationsByTemplate.GetValueOrDefault(
                        templateId,
                        Array.Empty<YouPinPurchaseObservation>())))
                .OrderBy(baseline => baseline.TemplateId)
                .ToArray();

            return new YouPinCoverageWatchState(
                watch.Id,
                plan.Fingerprint,
                nextCursor: 0,
                completedCoverageCycles: 1,
                audit.ObservedAt,
                Array.AsReadOnly(baselines));
        }

        public static YouPinCoverageWatchState Restore(
            Guid watchId,
            string planFingerprint,
            int nextCursor,
            long completedCoverageCycles,
            DateTimeOffset? lastTickAt,
            IReadOnlyCollection<YouPinTemplateBaseline> baselines)
        {
            if (watchId == Guid.Empty)
                throw new ArgumentException("监控 ID 不能为空。", nameof(watchId));
            ArgumentException.ThrowIfNullOrWhiteSpace(planFingerprint);
            if (planFingerprint.Length != 64
                || planFingerprint.Any(character =>
                    !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            {
                throw new ArgumentException(
                    "覆盖计划指纹必须为小写 SHA-256。",
                    nameof(planFingerprint));
            }
            if (nextCursor < 0)
                throw new ArgumentOutOfRangeException(nameof(nextCursor));
            if (completedCoverageCycles < 0)
                throw new ArgumentOutOfRangeException(nameof(completedCoverageCycles));
            if (lastTickAt is not null && lastTickAt.Value == default)
                throw new ArgumentOutOfRangeException(nameof(lastTickAt));
            ArgumentNullException.ThrowIfNull(baselines);
            if (baselines.Any(baseline => baseline is null))
                throw new ArgumentException("模板基线不能为空。", nameof(baselines));

            YouPinTemplateBaseline[] normalized = baselines
                .OrderBy(baseline => baseline.TemplateId)
                .ToArray();
            if (normalized.Select(baseline => baseline.TemplateId).Distinct().Count()
                != normalized.Length)
            {
                throw new ArgumentException("模板基线的模板 ID 必须唯一。", nameof(baselines));
            }
            if (normalized.Length > 0 && lastTickAt is null)
                throw new ArgumentException("存在模板基线时必须提供最后轮转时间。", nameof(lastTickAt));
            if (lastTickAt is not null
                && normalized.Any(baseline => baseline.ObservedAt > lastTickAt.Value))
            {
                throw new ArgumentException("模板基线时间不能晚于最后轮转时间。", nameof(baselines));
            }

            return new YouPinCoverageWatchState(
                watchId,
                planFingerprint,
                nextCursor,
                completedCoverageCycles,
                lastTickAt?.ToUniversalTime(),
                Array.AsReadOnly(normalized));
        }

        internal YouPinCoverageWatchState ApplyTick(
            int nextCursor,
            bool completedCoverageCycle,
            DateTimeOffset observedAt,
            IReadOnlyList<YouPinTemplateBaseline> baselines)
            => new(
                WatchId,
                PlanFingerprint,
                nextCursor,
                CompletedCoverageCycles + (completedCoverageCycle ? 1 : 0),
                observedAt.ToUniversalTime(),
                baselines);

        private static void ValidateWatchAndPlan(
            YouPinAuthorizedCoverageWatch watch,
            YouPinHotCoveragePlan plan)
        {
            ArgumentNullException.ThrowIfNull(watch);
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.BatchCount == 0)
                throw new InvalidOperationException("覆盖计划没有可轮转的已映射模板。");
            if (!string.Equals(watch.PlanFingerprint, plan.Fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("授权监控与覆盖计划指纹不一致。", nameof(plan));
        }
    }
}
