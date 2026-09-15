using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;

public enum YouPinPurchaseScope
{
    Top100,
    Top300,
    Top1000
}

public sealed record YouPinPurchaseMonitoringSnapshot(
    bool IsStarted,
    bool IsBusy,
    bool NotificationsEnabled,
    string Status,
    string? Error,
    string DataDirectory,
    int CandidateCount,
    int ResolvedCount,
    int BatchCount,
    int CompletedTemplates,
    int TotalTemplates,
    AuditReportView? LatestAudit,
    IReadOnlyList<StoreObservationView> Observations,
    IReadOnlyList<ChangeBatchView> RecentChanges)
{
    public IReadOnlyList<StoreMonitorView> Stores { get; init; } = [];
    public IReadOnlyDictionary<Guid, string> ArchivedWatchNotes { get; init; } = new Dictionary<Guid, string>();
}

public interface IYouPinPurchaseMonitoringModule : IDisposable
{
    event EventHandler? StateChanged;

    PurchaseAccounts? Accounts => null;
    Task RunAccountAuditAsync(string shopLink, YouPinPurchaseScope scope, PurchaseAccountSource source,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("请选择读取账号。");

    Task SaveAccountStoreAsync(Guid? watchId, string shopLink, string safeNote, YouPinPurchaseScope scope,
        int intervalMinutes, PurchaseAccountSource source, bool resume, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("读取账号服务不可用。");

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    YouPinPurchaseMonitoringSnapshot GetSnapshot();

    Task RunFullAuditAsync(
        string shopLink,
        YouPinPurchaseScope scope,
        CancellationToken cancellationToken = default);

    Task AddObservationAsync(
        string shopLink,
        string safeNote,
        YouPinPurchaseScope scope,
        int intervalMinutes,
        CancellationToken cancellationToken = default);

    Task ReauthorizeAsync(
        Guid watchId,
        string shopLink,
        CancellationToken cancellationToken = default);

    Task RunNextDueAsync(CancellationToken cancellationToken = default);

    void Pause(Guid watchId);

    void Resume(Guid watchId);

    string? ValidateShopLink(string shopLink);

    Task UpdateObservationAsync(Guid watchId, string safeNote, YouPinPurchaseScope scope,
        int intervalMinutes, string? shopLink = null, CancellationToken cancellationToken = default);

    Task EnablePriorityAsync(Guid watchId, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid watchId, CancellationToken cancellationToken = default);

    void CancelActive();

    Task MarkStoreReadAsync(Guid storeId, DateTimeOffset through, CancellationToken cancellationToken = default);

    Task SetStoreNotificationsAsync(Guid storeId, bool enabled, CancellationToken cancellationToken = default);

    Task<StoreActivityPage> LoadStoreActivityAsync(StoreActivityQuery query, CancellationToken cancellationToken = default);

    Task SetNotificationsEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditReportView>> LoadAuditHistoryAsync(
        CancellationToken cancellationToken = default);
}
