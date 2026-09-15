namespace CS2TradeMonitor.Shared.Contracts
{
    /// <summary>
    /// Marker for a typed, read-only request against the shared core.
    /// </summary>
    /// <typeparam name="T">The query result type.</typeparam>
    public interface ICoreQuery<out T>
    {
    }

    /// <summary>
    /// Base type for every state-changing request accepted by the shared core.
    /// </summary>
    public abstract record CoreCommand(string CorrelationId = "");

    /// <summary>
    /// Routes a ledger semantic ID through the shared core. Payload must contain no credentials.
    /// </summary>
    public sealed record FeatureCommand(
        string SemanticId,
        string PayloadJson = "",
        string CorrelationId = "",
        string BindingName = "") : CoreCommand(CorrelationId);

    public sealed record FeatureStateQuery(
        string SemanticId,
        string BindingName = "") : ICoreQuery<FeatureStateProjection>;

    public sealed record BooleanFeatureValue(bool Value);

    /// <summary>
    /// Reads only the availability of the production core boundary. It does not
    /// imply that every optional trading module has already been migrated.
    /// </summary>
    public sealed record CoreHealthQuery : ICoreQuery<CoreHealthProjection>;

    public sealed record CoreHealthProjection(
        bool IsReady,
        string ReasonCode,
        string Message,
        int RegisteredModuleCount,
        DateTimeOffset CapturedAt);

    public enum CoreCommandStatus
    {
        Success = 0,
        Pending = 1,
        Skipped = 2,
        NeedsUserAction = 3,
        Failed = 4,
        Disabled = 5
    }

    /// <summary>
    /// Stable, presentation-independent result returned for all commands.
    /// </summary>
    public sealed record CoreCommandResult(
        CoreCommandStatus Status,
        string ReasonCode,
        string Message,
        string CorrelationId,
        long SnapshotVersion)
    {
        public bool IsSuccess => Status == CoreCommandStatus.Success;

        public static CoreCommandResult Success(
            string message,
            string correlationId = "",
            long snapshotVersion = 0)
            => new(CoreCommandStatus.Success, "ok", message, correlationId, snapshotVersion);

        public static CoreCommandResult Pending(
            string reasonCode,
            string message,
            string correlationId = "",
            long snapshotVersion = 0)
            => new(CoreCommandStatus.Pending, reasonCode, message, correlationId, snapshotVersion);

        public static CoreCommandResult Skipped(
            string reasonCode,
            string message,
            string correlationId = "",
            long snapshotVersion = 0)
            => new(CoreCommandStatus.Skipped, reasonCode, message, correlationId, snapshotVersion);

        public static CoreCommandResult NeedsUserAction(
            string reasonCode,
            string message,
            string correlationId = "",
            long snapshotVersion = 0)
            => new(CoreCommandStatus.NeedsUserAction, reasonCode, message, correlationId, snapshotVersion);

        public static CoreCommandResult Failed(
            string reasonCode,
            string message,
            string correlationId = "",
            long snapshotVersion = 0)
            => new(CoreCommandStatus.Failed, reasonCode, message, correlationId, snapshotVersion);

        public static CoreCommandResult Disabled(
            string reasonCode,
            string message,
            string correlationId = "",
            long snapshotVersion = 0)
            => new(CoreCommandStatus.Disabled, reasonCode, message, correlationId, snapshotVersion);
    }

    public enum AutomationCycleTriggerKind
    {
        StartupRecovery = 0,
        ForegroundService = 1,
        BackgroundWorker = 2,
        UserRequested = 3,
        Scheduled = 4
    }

    public sealed record AutomationCycleTrigger(
        AutomationCycleTriggerKind Kind,
        string CorrelationId = "",
        DateTimeOffset? RequestedAt = null);

    public enum AutomationCycleStatus
    {
        Completed = 0,
        Pending = 1,
        Skipped = 2,
        Failed = 3
    }

    public sealed record AutomationCycleResult(
        AutomationCycleStatus Status,
        string ReasonCode,
        string Message,
        string CorrelationId,
        long SnapshotVersion,
        int ProcessedCount = 0);

    /// <summary>
    /// Base projection returned to a host. Version is monotonic within one data store.
    /// </summary>
    public abstract record CoreSnapshot(long Version, DateTimeOffset CapturedAt);

    public enum FeatureAvailability
    {
        Available = 0,
        Disabled = 1,
        NotAvailable = 2,
        NeedsUserAction = 3
    }

    public sealed record FeatureStateProjection(
        string SemanticId,
        FeatureAvailability Availability,
        string ReasonCode,
        string Message,
        string StateJson,
        long Version,
        DateTimeOffset CapturedAt) : CoreSnapshot(Version, CapturedAt);

    /// <summary>
    /// Base notification emitted by the core. Events contain identifiers, never secrets.
    /// </summary>
    public abstract record CoreEvent(
        string EventId,
        string CorrelationId,
        DateTimeOffset OccurredAt,
        long SnapshotVersion);
}
