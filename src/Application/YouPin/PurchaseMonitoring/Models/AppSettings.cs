using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;

namespace YouPinPurchaseMonitor.Models;

public enum WatchPurposeChoice
{
    SelfAudit,
    ExplicitPermission,
    ResponsibleDisclosure
}

public enum ObservationScopeChoice
{
    Top100,
    Top300,
    Top1000,
    ObservedOnly
}

public sealed record AppSettings(
    string CatalogPath,
    string RawDirectory,
    string ReportDirectory,
    string StateDirectory,
    int WatchIntervalMinutes,
    WatchPurposeChoice WatchPurpose)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding Account { get; init; } = CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.PurchaseAccountBinding.Unselected;
    public bool NotificationsEnabled { get; init; } = true;
    public bool MinimizeToTray { get; init; } = true;
    public bool StartWithWindows { get; init; }

    public AppSettings Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(RawDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReportDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(StateDirectory);
        if (WatchIntervalMinutes is < 10 or > 1440)
            throw new InvalidDataException("监控间隔必须在 10 到 1440 分钟之间。");
        if (!Enum.IsDefined(WatchPurpose))
            throw new InvalidDataException("监控用途无效。");
        return this;
    }

    public YouPinWatchPurpose ToCorePurpose() => WatchPurpose switch
    {
        WatchPurposeChoice.SelfAudit => YouPinWatchPurpose.SelfAudit,
        WatchPurposeChoice.ExplicitPermission => YouPinWatchPurpose.ExplicitPermission,
        WatchPurposeChoice.ResponsibleDisclosure => YouPinWatchPurpose.ResponsibleDisclosure,
        _ => throw new ArgumentOutOfRangeException(nameof(WatchPurpose))
    };
}
