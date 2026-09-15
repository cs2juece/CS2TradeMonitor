namespace CS2TradeMonitor.Shared.Ports
{
    public interface ISecureValueStore
    {
        Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
        Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    }

    public interface IAppDataPathProvider
    {
        string AppDataRoot { get; }
        string GetPath(string relativePath);
    }

    public interface IDeviceIdentityProvider
    {
        Task<string> GetDeviceIdentityAsync(CancellationToken cancellationToken = default);
    }

    public interface IAppDiagnostics
    {
        ValueTask ReportAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default);
    }

    public sealed record DiagnosticEvent(
        string Code,
        string Message,
        DiagnosticSeverity Severity,
        DateTimeOffset OccurredAt,
        string CorrelationId = "");

    public enum DiagnosticSeverity
    {
        Information = 0,
        Warning = 1,
        Error = 2,
        Critical = 3
    }

    public interface IUserNotificationSink
    {
        Task PublishAsync(UserNotification notification, CancellationToken cancellationToken = default);
    }

    public sealed record UserNotification(
        string Id,
        string Title,
        string Message,
        UserNotificationSeverity Severity,
        string Route = "");

    public enum UserNotificationSeverity
    {
        Information = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    public interface IOverlayDisplayAdapter
    {
        Task<OverlayDisplayResult> ShowAsync(OverlayContent content, CancellationToken cancellationToken = default);
        Task HideAsync(CancellationToken cancellationToken = default);
    }

    public sealed record OverlayContent(
        string Title,
        string Message,
        string Route = "",
        int Width = 0,
        float FontSize = 14f,
        bool FontBold = true,
        string BackgroundColor = "",
        string TextColor = "",
        double BackgroundOpacity = 1d,
        double TextOpacity = 1d,
        bool ClickThrough = false);

    public sealed record OverlayDisplayResult(
        bool Shown,
        bool PermissionRequired,
        string ReasonCode,
        string Message)
    {
        public static OverlayDisplayResult Displayed()
            => new(true, false, "ok", "悬浮窗已显示。");

        public static OverlayDisplayResult RequiresPermission()
            => new(false, true, "overlay.permission-required", "请允许悬浮窗权限；返回应用后会自动显示。");

        public static OverlayDisplayResult Failed(string failureType)
            => new(
                false,
                false,
                "overlay.show-failed",
                $"悬浮窗显示失败（{failureType}），设置已保存，请重试。");
    }

    public interface IBackgroundScheduler
    {
        Task ScheduleAsync(BackgroundSchedule schedule, CancellationToken cancellationToken = default);
        Task CancelAsync(string scheduleId, CancellationToken cancellationToken = default);
    }

    public sealed record BackgroundSchedule(
        string ScheduleId,
        TimeSpan MinimumInterval,
        bool RequiresNetwork);

    public interface IInteractiveLoginAdapter
    {
        Task<InteractiveLoginResult> LoginAsync(
            InteractiveLoginRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed record InteractiveLoginRequest(string Platform, string CorrelationId = "");

    public sealed record InteractiveLoginResult(
        bool Success,
        bool Cancelled,
        string ReasonCode,
        string Message);

    public interface IFileAndShareAdapter
    {
        Task<SelectedFile?> PickFileAsync(
            FileSelectionRequest request,
            CancellationToken cancellationToken = default);

        Task ShareAsync(ShareRequest request, CancellationToken cancellationToken = default);
    }

    public sealed record FileSelectionRequest(
        string Title,
        IReadOnlyList<string> AllowedExtensions);

    public sealed record SelectedFile(string DisplayName, Stream Content);

    public sealed record ShareRequest(string Title, string FilePath, string MimeType);

    public interface IPlatformActionAdapter
    {
        Task<PlatformActionResult> ExecuteAsync(
            PlatformActionRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed record PlatformActionRequest(
        string SemanticId,
        string PlatformAction,
        string CorrelationId = "");

    public sealed record PlatformActionResult(
        PlatformActionOutcome Outcome,
        string ReasonCode,
        string Message,
        SelectedFile? SelectedFile = null)
    {
        public bool Success => Outcome == PlatformActionOutcome.Succeeded;

        public static PlatformActionResult Succeeded(
            string reasonCode,
            string message,
            SelectedFile? selectedFile = null)
            => new(PlatformActionOutcome.Succeeded, reasonCode, message, selectedFile);

        public static PlatformActionResult Cancelled(string reasonCode, string message)
            => new(PlatformActionOutcome.Cancelled, reasonCode, message);

        public static PlatformActionResult NotAvailable(string reasonCode, string message)
            => new(PlatformActionOutcome.NotAvailable, reasonCode, message);

        public static PlatformActionResult Failed(string reasonCode, string message)
            => new(PlatformActionOutcome.Failed, reasonCode, message);
    }

    public enum PlatformActionOutcome
    {
        Succeeded = 0,
        Cancelled = 1,
        NotAvailable = 2,
        Failed = 3
    }

    public interface IAppInstallerAdapter
    {
        Task<AppInstallResult> InstallAsync(string packagePath, CancellationToken cancellationToken = default);
    }

    public sealed record AppInstallResult(bool Started, string ReasonCode, string Message);

    public interface ITradeStateStore
    {
        Task<TradeStateDocument?> LoadAsync(string partitionKey, CancellationToken cancellationToken = default);

        Task SaveAsync(
            TradeStateDocument document,
            long expectedVersion,
            CancellationToken cancellationToken = default);
    }

    public sealed record TradeStateDocument(string PartitionKey, long Version, string Json);

    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
    }
}
