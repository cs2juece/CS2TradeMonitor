using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    public sealed class DetailedDiagnosticsRuntimeBridge : IDetailedDiagnosticsService
    {
        private static IDetailedDiagnosticsService Service => DetailedDiagnosticsRuntime.Service;

        public string DiagnosticsDirectory => Service.DiagnosticsDirectory;
        public DetailedDiagnosticsStatus GetStatus() => Service.GetStatus();
        public DetailedDiagnosticsStatus Enable() => Service.Enable();
        public DetailedDiagnosticsStatus Disable() => Service.Disable();
        public Task FlushAsync(CancellationToken cancellationToken = default)
            => Service.FlushAsync(cancellationToken);
    }

    public sealed class DetailedDiagnosticsExportRuntimeBridge : IDetailedDiagnosticsExportService
    {
        private readonly IInstanceRuntimeContext _instance;
        private readonly IClock _clock;

        public DetailedDiagnosticsExportRuntimeBridge(IInstanceRuntimeContext instance, IClock clock)
        {
            _instance = instance;
            _clock = clock;
        }

        public string? GetPreferredLogFilePath() => Resolve().GetPreferredLogFilePath();

        public Task<DetailedDiagnosticsExportResult> ExportAsync(
            string destinationZipPath,
            IReadOnlyDictionary<string, object?>? whitelistedConfiguration = null,
            CancellationToken cancellationToken = default)
        {
            return Resolve().ExportAsync(destinationZipPath, whitelistedConfiguration, cancellationToken);
        }

        private DetailedDiagnosticsExportService Resolve()
        {
            DetailedDiagnosticsService service = DetailedDiagnosticsRuntime.Current
                ?? throw new InvalidOperationException("详细诊断服务尚未初始化。");
            return new DetailedDiagnosticsExportService(service, _instance, _clock);
        }
    }
}
