namespace CS2TradeMonitor.Shared.SystemServices;

public enum SoftwareUpdateAvailability
{
    NotChecked = 0,
    Latest = 1,
    Available = 2,
    Failed = 3
}

public sealed record SoftwareUpdateCheckProjection(
    SoftwareUpdateAvailability Availability,
    string CurrentVersion,
    string LatestVersion,
    string SourceName,
    string DownloadUrl,
    string Message,
    DateTimeOffset CheckedAt)
{
    public static SoftwareUpdateCheckProjection NotChecked(string currentVersion)
        => new(
            SoftwareUpdateAvailability.NotChecked,
            currentVersion,
            string.Empty,
            string.Empty,
            string.Empty,
            "尚未检查软件更新。",
            DateTimeOffset.MinValue);
}

public interface ISoftwareUpdateChecker
{
    string CurrentVersion { get; }

    Task<SoftwareUpdateCheckProjection> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record DiagnosticRuntimeProjection(
    bool DetailedEnabled,
    DateTimeOffset? DetailedExpiresAt,
    long RegularEventCount,
    long DetailedEventCount,
    string Message);

public interface IDiagnosticRuntimeManager
{
    Task<DiagnosticRuntimeProjection> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<DiagnosticRuntimeProjection> SetDetailedEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}

public sealed record SystemFeatureProjection(
    SoftwareUpdateCheckProjection SoftwareUpdate,
    DiagnosticRuntimeProjection Diagnostics);
