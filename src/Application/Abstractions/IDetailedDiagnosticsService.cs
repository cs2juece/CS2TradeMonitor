namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IDetailedDiagnosticsService
    {
        string DiagnosticsDirectory { get; }

        DetailedDiagnosticsStatus GetStatus();

        DetailedDiagnosticsStatus Enable();

        DetailedDiagnosticsStatus Disable();

        Task FlushAsync(CancellationToken cancellationToken = default);
    }

    public sealed record DetailedDiagnosticsStatus(
        bool IsEnabled,
        DateTime? StartedAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? LastSessionEndedAtUtc,
        string? ActiveLogFilePath,
        long TotalBytes,
        long MaximumBytes,
        long DroppedEventCount,
        int CapacityCleanupCount,
        string? LastStopReason,
        string? LastError);

    public interface IDetailedDiagnosticsExportService
    {
        string? GetPreferredLogFilePath();

        Task<DetailedDiagnosticsExportResult> ExportAsync(
            string destinationZipPath,
            IReadOnlyDictionary<string, object?>? whitelistedConfiguration = null,
            CancellationToken cancellationToken = default);
    }

    public sealed record DetailedDiagnosticsExportResult(
        string ZipPath,
        DateTime SessionStartedAtUtc,
        DateTime SessionEndedAtUtc,
        long ZipSizeBytes,
        bool IncludedRegularLog);
}
