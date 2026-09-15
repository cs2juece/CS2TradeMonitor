using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Safety;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    /// <summary>
    /// Executes exactly one Scan Batch for a Coverage Watch and updates only successful
    /// per-template baselines.
    /// </summary>
    public sealed class YouPinCoverageWatchService
    {
        private readonly IYouPinPublicAuditClient _client;
        private readonly TimeProvider _timeProvider;

        public YouPinCoverageWatchService(
            IYouPinPublicAuditClient client,
            TimeProvider? timeProvider = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<YouPinCoverageTickResult> ScanNextBatchAsync(
            YouPinAuthorizedCoverageWatch watch,
            YouPinHotCoveragePlan plan,
            YouPinCoverageWatchState state,
            CancellationToken cancellationToken = default)
        {
            ValidateInputs(watch, plan, state);
            YouPinHotCoverageBatch batch = plan.GetBatchByCursor(state.NextCursor);
            YouPinPurchaseExposureResult exposure = await _client.FindPurchaseExposureAsync(
                watch.Subject.UserId,
                batch.Scope,
                cancellationToken).ConfigureAwait(false);
            if (exposure.TargetUserId != watch.Subject.UserId
                || !exposure.RequestedTemplateIds.SequenceEqual(batch.Scope.TemplateIds))
            {
                throw new InvalidOperationException("覆盖批次响应与授权目标或模板范围不一致。");
            }

            DateTimeOffset observedAt = _timeProvider.GetUtcNow().ToUniversalTime();
            if (state.LastTickAt is not null && observedAt <= state.LastTickAt.Value)
                throw new InvalidOperationException("覆盖轮转时间必须晚于上一份状态。");

            long[] incompleteIds = exposure.Failures
                .Select(failure => failure.TemplateId)
                .Distinct()
                .Order()
                .ToArray();
            var incompleteSet = incompleteIds.ToHashSet();
            long[] completedIds = exposure.RequestedTemplateIds
                .Where(templateId => !incompleteSet.Contains(templateId))
                .Order()
                .ToArray();
            YouPinPurchaseObservation[] current = exposure.Matches
                .Where(match => !incompleteSet.Contains(match.TemplateId))
                .Select(ToObservation)
                .ToArray();

            Dictionary<long, YouPinTemplateBaseline> baselines = state.Baselines
                .ToDictionary(baseline => baseline.TemplateId);
            var appeared = new List<YouPinPurchaseObservation>();
            var ceased = new List<YouPinPurchaseObservation>();
            var established = new List<YouPinPurchaseObservation>();

            foreach (long templateId in completedIds)
            {
                YouPinPurchaseObservation[] currentForTemplate = current
                    .Where(observation => observation.TemplateId == templateId)
                    .ToArray();
                if (baselines.TryGetValue(templateId, out YouPinTemplateBaseline? previous))
                {
                    appeared.AddRange(ExceptMultiset(
                        currentForTemplate,
                        previous.Observations));
                    ceased.AddRange(ExceptMultiset(
                        previous.Observations,
                        currentForTemplate));
                }
                else
                {
                    established.AddRange(currentForTemplate);
                }

                baselines[templateId] = YouPinTemplateBaseline.Restore(
                    templateId,
                    observedAt,
                    currentForTemplate);
            }

            YouPinTemplateBaseline[] orderedBaselines = baselines.Values
                .OrderBy(baseline => baseline.TemplateId)
                .ToArray();
            YouPinCoverageWatchState updatedState = state.ApplyTick(
                batch.NextCursor,
                batch.CompletesCoverageCycle,
                observedAt,
                Array.AsReadOnly(orderedBaselines));

            return new YouPinCoverageTickResult(
                watch.Id,
                plan.Fingerprint,
                observedAt,
                batch.BatchNumber,
                batch.TotalBatchCount,
                Array.AsReadOnly(completedIds),
                Array.AsReadOnly(incompleteIds),
                Array.AsReadOnly(appeared.ToArray()),
                Array.AsReadOnly(ceased.ToArray()),
                Array.AsReadOnly(established.ToArray()),
                Array.AsReadOnly(exposure.Failures.ToArray()),
                updatedState);
        }

        private static void ValidateInputs(
            YouPinAuthorizedCoverageWatch watch,
            YouPinHotCoveragePlan plan,
            YouPinCoverageWatchState state)
        {
            ArgumentNullException.ThrowIfNull(watch);
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(state);
            if (!string.Equals(watch.PlanFingerprint, plan.Fingerprint, StringComparison.Ordinal)
                || !string.Equals(state.PlanFingerprint, plan.Fingerprint, StringComparison.Ordinal))
            {
                throw new ArgumentException("授权监控、覆盖计划和轮转状态的指纹必须一致。");
            }

            if (state.WatchId != watch.Id)
                throw new ArgumentException("轮转状态不属于当前授权监控。", nameof(state));
            if (state.NextCursor < 0 || state.NextCursor >= plan.BatchCount)
                throw new ArgumentOutOfRangeException(nameof(state), "轮转游标超出覆盖计划。");

            var planTemplates = plan.OrderedResolvedItems
                .Select(item => item.TemplateId)
                .ToHashSet();
            if (state.Baselines.Any(baseline => !planTemplates.Contains(baseline.TemplateId)))
                throw new ArgumentException("轮转状态包含覆盖计划之外的模板基线。", nameof(state));
        }

        private static YouPinPurchaseObservation ToObservation(YouPinPurchaseExposureMatch match)
            => new(
                match.TemplateId,
                YouPinSafeText.Normalize(match.CommodityName, 200),
                match.PurchasePrice,
                match.SurplusQuantity,
                YouPinSafeText.Normalize(match.AbradeText, 80),
                YouPinSafeText.Normalize(match.FadeText, 80),
                match.AutoReceived);

        private static IReadOnlyList<YouPinPurchaseObservation> ExceptMultiset(
            IReadOnlyCollection<YouPinPurchaseObservation> source,
            IReadOnlyCollection<YouPinPurchaseObservation> subtract)
        {
            Dictionary<YouPinPurchaseObservation, int> remaining = subtract
                .GroupBy(observation => observation)
                .ToDictionary(group => group.Key, group => group.Count());
            var result = new List<YouPinPurchaseObservation>();

            foreach (YouPinPurchaseObservation observation in source)
            {
                if (remaining.TryGetValue(observation, out int count) && count > 0)
                    remaining[observation] = count - 1;
                else
                    result.Add(observation);
            }

            return result;
        }
    }
}
