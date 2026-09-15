namespace YouPinPurchaseMonitor.Models;

public enum StoreObservationStatus
{
    PendingReauthorization,
    Active,
    RunningTick,
    Paused,
    Failed
}

public enum ObservationScheduleLane
{
    Standalone,
    Priority,
    Fallback
}

public sealed record StoreObservationRegistration(
    Guid WatchId,
    string SafeNote,
    string TargetMask,
    WatchPurposeChoice Purpose,
    ObservationScopeChoice Scope,
    int IntervalMinutes,
    string PlanFingerprint,
    DateTimeOffset CreatedAt)
{
    public CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding Account { get; init; } = CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding.Unselected;
    public IReadOnlyList<long> SelectedTemplateIds { get; init; } = [];
    public ObservationScheduleLane ScheduleLane { get; init; }
    public Guid? PartnerWatchId { get; init; }
    public Guid StoreId { get; init; }
    public IReadOnlyList<Guid> HistoryWatchIds { get; init; } = [];

    public StoreObservationRegistration Validate()
    {
        if (Account is null) throw new InvalidDataException("读取账号选择无效。");
        Account.Validate();
        if (WatchId == Guid.Empty)
            throw new InvalidDataException("Watch ID 不能为空。");
        SafeNoteValidator.Validate(SafeNote);
        TargetMaskValidator.Validate(TargetMask);
        if (!Enum.IsDefined(Purpose) || !Enum.IsDefined(Scope))
            throw new InvalidDataException("店铺观察项的用途或优先范围无效。");
        if (!Enum.IsDefined(ScheduleLane))
            throw new InvalidDataException("店铺观察项的调度通道无效。");
        if (IntervalMinutes is < 10 or > 1440)
            throw new InvalidDataException("店铺观察项间隔必须在 10 到 1440 分钟之间。");
        if (PlanFingerprint.Length != 64
            || PlanFingerprint.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new InvalidDataException("店铺观察项的计划指纹无效。");
        }
        if (CreatedAt == default)
            throw new InvalidDataException("店铺观察项缺少创建时间。");
        if (HistoryWatchIds.Count > 1000 || HistoryWatchIds.Any(id => id == Guid.Empty)
            || HistoryWatchIds.Distinct().Count() != HistoryWatchIds.Count)
            throw new InvalidDataException("店铺历史观察关联无效。");
        if (SelectedTemplateIds.Count > 1000
            || SelectedTemplateIds.Any(templateId => templateId <= 0)
            || SelectedTemplateIds.Distinct().Count() != SelectedTemplateIds.Count)
        {
            throw new InvalidDataException("店铺观察项的显式模板范围无效。");
        }
        if (Scope == ObservationScopeChoice.ObservedOnly && SelectedTemplateIds.Count == 0)
            throw new InvalidDataException("已观察优先范围必须包含显式模板 ID。");
        if (ScheduleLane == ObservationScheduleLane.Standalone && PartnerWatchId is not null)
            throw new InvalidDataException("独立观察项不能关联调度通道。");
        if (ScheduleLane == ObservationScheduleLane.Priority
            && (Scope != ObservationScopeChoice.ObservedOnly
                || IntervalMinutes != DualLayerSchedulePolicy.PriorityIntervalMinutes
                || PartnerWatchId is null
                || PartnerWatchId == WatchId))
        {
            throw new InvalidDataException("重点通道必须是关联的 10 分钟已命中范围。");
        }
        if (ScheduleLane == ObservationScheduleLane.Fallback
            && (Scope != ObservationScopeChoice.Top1000
                || IntervalMinutes != DualLayerSchedulePolicy.FallbackIntervalMinutes
                || PartnerWatchId is null
                || PartnerWatchId == WatchId))
        {
            throw new InvalidDataException("兜底通道必须是关联的 240 分钟 Top 1000 范围。");
        }
        return this;
    }
}

public static class TargetMaskValidator
{
    public static string Validate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 3 or > 7
            || !value.StartsWith("***", StringComparison.Ordinal)
            || value.Skip(3).Any(character => !char.IsAsciiDigit(character)))
        {
            throw new InvalidDataException("目标掩码必须是三个星号和最多四位尾号。");
        }
        return value;
    }
}

public static class DualLayerSchedulePolicy
{
    public const int PriorityIntervalMinutes = 10;
    public const int FallbackIntervalMinutes = 240;

    public static int DuePriority(ObservationScheduleLane lane)
        => lane == ObservationScheduleLane.Priority ? 0 : 1;
}

public sealed record StoreObservationView(
    StoreObservationRegistration Registration,
    StoreObservationStatus Status,
    string TargetMask,
    DateTimeOffset? LastTickAt,
    DateTimeOffset? NextDueAt,
    int NextBatchNumber,
    int TotalBatchCount,
    int CandidateTemplateCount,
    long CompletedCoverageCycles,
    int BaselineTemplateCount,
    int CurrentObservationCount,
    DateTimeOffset? OldestBaselineAt,
    string? LastMessage)
{
    public IReadOnlyList<CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring.YouPinTemplateBaseline> Baselines { get; init; } = [];

    public double CoveragePercent => TotalBatchCount <= 0
        ? 0
        : Math.Clamp(BaselineTemplateCount / (double)Math.Max(1, CandidateTemplateCount) * 100d, 0d, 100d);
}

public static class SafeNoteValidator
{
    private static readonly string[] ForbiddenFragments =
    [
        "http://",
        "https://",
        "authsign",
        "cookie",
        "token",
        "#/shop/"
    ];

    public static string Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string trimmed = value.Trim();
        if (trimmed.Length > 40 || trimmed.Any(char.IsControl))
            throw new ArgumentException("备注必须是 1 到 40 个普通字符。", nameof(value));
        if (ForbiddenFragments.Any(fragment =>
                trimmed.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            || ContainsLongDigitRun(trimmed))
        {
            throw new ArgumentException("备注不能包含链接、认证字段或疑似完整平台 ID。", nameof(value));
        }
        return trimmed;
    }

    private static bool ContainsLongDigitRun(string value)
    {
        int run = 0;
        foreach (char character in value)
        {
            run = char.IsAsciiDigit(character) ? run + 1 : 0;
            if (run >= 5)
                return true;
        }
        return false;
    }
}
