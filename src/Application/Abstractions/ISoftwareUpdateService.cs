using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISoftwareUpdateService
    {
        Task<SoftwareUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

        Task<SoftwareUpdateDownloadResult> DownloadAsync(
            SoftwareUpdateCheckResult update,
            IProgress<SoftwareUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default);

        void LaunchUpdater(SoftwareUpdateDownloadResult download);
    }
}
