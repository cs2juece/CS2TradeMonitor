using CS2TradeMonitor.Application.Notify;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ICs2UpdateReminderService
    {
        event EventHandler<Cs2UpdateDetectedEventArgs>? UpdateDetected;

        Cs2UpdateCheckResult LastResult { get; }

        IReadOnlyList<Cs2UpdateLogItem> RecentItems { get; }

        void Tick(Settings cfg);

        void ResetSchedule();

        Task<Cs2UpdateCheckResult?> CheckIfDueAsync(
            Settings cfg,
            CancellationToken cancellationToken = default)
            => Task.FromResult<Cs2UpdateCheckResult?>(null);

        Task<Cs2UpdateCheckResult> ManualCheckAsync(Settings cfg, bool resetBaseline = false);

        Task<Cs2UpdateCheckResult> CheckAsync(
            Settings cfg,
            bool notify,
            bool resetBaseline,
            CancellationToken cancellationToken = default);
    }
}
