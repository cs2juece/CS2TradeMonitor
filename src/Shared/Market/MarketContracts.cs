using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Shared.Market;

public sealed record MarketItemCandidate(
    string ItemId,
    string Name,
    string MarketHashName,
    string PlatformItemId,
    string Source);

public sealed record SearchMarketItemsCommand(string Keyword);

public sealed record AddMonitoredItemCommand(MarketItemCandidate Candidate);

public sealed record RemoveMonitoredItemCommand(string ItemKey);

public sealed record SaveMarketSourceSettingsCommand(
    string? SteamDtApiKey,
    string? QaqApiToken,
    int SteamDtRefreshSeconds,
    int QaqRefreshSeconds,
    bool ShowPercent);

public sealed record SaveItemPriceAlertCommand(
    string ItemKey,
    ItemPriceAlertTriggerMode TriggerMode,
    bool DesktopEnabled,
    bool PhoneEnabled,
    double Above,
    double Below,
    double RisePercent,
    double FallPercent,
    int WindowMinutes,
    int CooldownMinutes);

public sealed record SaveMarketAlertSettingsCommand(
    bool Enabled,
    MarketAlertNotificationMode NotificationMode,
    bool DeferWhenFullscreen,
    int DefaultWindowMinutes,
    int DefaultCooldownMinutes,
    IReadOnlyList<MarketAlertRuleDraft> Rules);

public sealed record MarketAlertRuleDraft(
    string Id,
    string Name,
    bool Enabled,
    string SourceId,
    MarketAlertRuleType RuleType,
    double Threshold,
    int WindowMinutes,
    int CooldownMinutes);

public sealed record MarketSourceSnapshot(
    string Id,
    string DisplayName,
    double Index,
    double Change,
    double Percent,
    DateTime RetrievedAt,
    string Source,
    string Status,
    string Error,
    bool HasData,
    bool IsStale)
{
    public static MarketSourceSnapshot Empty(string id, string displayName)
        => new(
            id,
            displayName,
            0,
            0,
            0,
            DateTime.MinValue,
            "未获取",
            "等待刷新",
            "",
            false,
            false);
}

public sealed record MarketItemRefreshResult(
    string ItemKey,
    bool Success,
    double Price,
    double YouPinBidPrice,
    double Change,
    double ChangePercent,
    long UpdateTime,
    long YouPinBidUpdateTime,
    bool HasChangeData,
    string Source,
    string Status,
    string Error)
{
    public static MarketItemRefreshResult Failed(string itemKey, string error)
        => new(itemKey, false, 0, 0, 0, 0, 0, 0, false, "未获取", "刷新失败", error);

    public static MarketItemRefreshResult Succeeded(
        string itemKey,
        double price,
        double youPinBidPrice,
        double change,
        double changePercent,
        long updateTime,
        long youPinBidUpdateTime,
        bool hasChangeData,
        string source)
        => new(
            itemKey,
            true,
            price,
            youPinBidPrice,
            change,
            changePercent,
            updateTime,
            youPinBidUpdateTime,
            hasChangeData,
            source,
            "成功",
            "");
}

public interface IMarketDataClient
{
    Task<IReadOnlyList<MarketItemCandidate>> SearchItemsAsync(
        string keyword,
        string steamDtApiKey,
        CancellationToken cancellationToken = default);

    Task<MarketSourceSnapshot> RefreshSteamDtAsync(
        string apiKey,
        CancellationToken cancellationToken = default);

    Task<MarketSourceSnapshot> RefreshQaqAsync(
        string apiToken,
        CancellationToken cancellationToken = default);

    Task<MarketItemRefreshResult> RefreshItemAsync(
        ItemMonitorConfig item,
        string steamDtApiKey,
        CancellationToken cancellationToken = default);
}

public sealed record MarketSourceProjection(
    string Id,
    string DisplayName,
    bool HasCredential,
    double Index,
    double Change,
    double Percent,
    DateTime RetrievedAt,
    string Source,
    string Status,
    string Error,
    bool HasData,
    bool IsStale,
    int RefreshSeconds);

public sealed record MonitoredItemProjection(
    string ItemKey,
    string ItemId,
    string Name,
    string ShortName,
    bool Enabled,
    int RefreshIntervalSeconds,
    double Price,
    double YouPinBidPrice,
    double Change,
    double ChangePercent,
    long UpdateTime,
    bool HasChangeData,
    string Status,
    ItemPriceAlertProjection Alert);

public sealed record ItemPriceAlertProjection(
    ItemPriceAlertTriggerMode TriggerMode,
    bool DesktopEnabled,
    bool PhoneEnabled,
    double Above,
    double Below,
    double RisePercent,
    double FallPercent,
    int WindowMinutes,
    int CooldownMinutes,
    string LastMessage);

public sealed record MarketAlertRuleProjection(
    string Id,
    string Name,
    bool Enabled,
    string SourceId,
    MarketAlertRuleType RuleType,
    double Threshold,
    int WindowMinutes,
    int CooldownMinutes,
    bool IsBuiltIn);

public sealed record MarketFeatureProjection(
    MarketSourceProjection SteamDt,
    MarketSourceProjection Qaq,
    IReadOnlyList<MonitoredItemProjection> Items,
    string SearchKeyword,
    IReadOnlyList<MarketItemCandidate> SearchCandidates,
    bool ShowPercent,
    bool MarketAlertsEnabled,
    MarketAlertNotificationMode MarketAlertNotificationMode,
    bool DeferAlertsWhenFullscreen,
    int AlertDefaultWindowMinutes,
    int AlertDefaultCooldownMinutes,
    IReadOnlyList<MarketAlertRuleProjection> MarketAlertRules,
    DateTime CapturedAt);
