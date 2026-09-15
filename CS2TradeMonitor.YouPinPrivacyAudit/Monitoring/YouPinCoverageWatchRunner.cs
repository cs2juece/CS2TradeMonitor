using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring
{
    public enum YouPinCoverageWatchRunStatus
    {
        NotDue,
        Completed
    }

    public sealed record YouPinCoverageWatchRunResult(
        YouPinCoverageWatchRunStatus Status,
        DateTimeOffset NextDueAt,
        YouPinCoverageWatchState State,
        YouPinCoverageTickResult? Tick);

    /// <summary>
    /// Host-facing scheduled runner. It evaluates due time, executes at most one Scan Batch,
    /// and atomically persists the resulting user-ID-free state.
    /// </summary>
    public sealed class YouPinCoverageWatchRunner
    {
        private readonly IYouPinPublicAuditClient _client;
        private readonly IYouPinCoverageWatchStateStore _stateStore;
        private readonly TimeProvider _timeProvider;

        public YouPinCoverageWatchRunner(
            IYouPinPublicAuditClient client,
            IYouPinCoverageWatchStateStore stateStore,
            TimeProvider? timeProvider = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<YouPinCoverageWatchRunResult> RunDueTickAsync(
            YouPinAuthorizedCoverageWatch watch,
            YouPinHotCoveragePlan plan,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(watch);
            ArgumentNullException.ThrowIfNull(plan);
            if (!string.Equals(watch.PlanFingerprint, plan.Fingerprint, StringComparison.Ordinal))
                throw new ArgumentException("授权监控与覆盖计划指纹不一致。", nameof(plan));

            YouPinCoverageWatchState state = await _stateStore
                .LoadAsync(watch.Id, cancellationToken)
                .ConfigureAwait(false)
                ?? YouPinCoverageWatchState.Create(watch, plan);
            if (!string.Equals(state.PlanFingerprint, plan.Fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("持久化状态与当前覆盖计划指纹不一致。");

            DateTimeOffset now = _timeProvider.GetUtcNow().ToUniversalTime();
            DateTimeOffset dueAt = state.GetNextDueAt(watch.Interval);
            if (now < dueAt)
            {
                return new YouPinCoverageWatchRunResult(
                    YouPinCoverageWatchRunStatus.NotDue,
                    dueAt,
                    state,
                    Tick: null);
            }

            var service = new YouPinCoverageWatchService(_client, _timeProvider);
            YouPinCoverageTickResult tick = await service.ScanNextBatchAsync(
                watch,
                plan,
                state,
                cancellationToken).ConfigureAwait(false);
            await _stateStore.SaveAsync(tick.UpdatedState, cancellationToken)
                .ConfigureAwait(false);
            return new YouPinCoverageWatchRunResult(
                YouPinCoverageWatchRunStatus.Completed,
                tick.UpdatedState.GetNextDueAt(watch.Interval),
                tick.UpdatedState,
                tick);
        }
    }
}
