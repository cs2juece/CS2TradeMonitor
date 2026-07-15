using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ICs2UpdateReminderService
    {
        event EventHandler<Cs2UpdateDetectedEventArgs>? UpdateDetected;

        Cs2UpdateCheckResult LastResult { get; }

        IReadOnlyList<Cs2UpdateLogItem> RecentItems { get; }

        void Tick(Settings cfg);

        void ResetSchedule();

        Task<Cs2UpdateCheckResult> ManualCheckAsync(Settings cfg, bool resetBaseline = false);

        Task<Cs2UpdateCheckResult> CheckAsync(
            Settings cfg,
            bool notify,
            bool resetBaseline,
            CancellationToken cancellationToken = default);
    }
}
