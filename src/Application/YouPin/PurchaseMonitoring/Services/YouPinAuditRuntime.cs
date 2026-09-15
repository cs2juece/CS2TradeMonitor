using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Monitoring;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.PublicApi;
using CS2TradeMonitor.Application.YouPin.PrivacyAudit.Reports;
using YouPinPurchaseMonitor.Infrastructure;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Services;

public interface IYouPinAuditOperations
{
    Task<AuditExecutionResult> RunAccountAuditAsync(YouPinAuditSubject subject, YouPinHotCoveragePlan plan,
        string reportDirectory, IProgress<YouPinHotCoverageProgress> progress, PurchaseAccountBinding account,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("该读取服务不支持账号选择，未发送请求。");

    Task<AuditExecutionResult> RunFullAuditAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        string reportDirectory,
        IProgress<YouPinHotCoverageProgress> progress,
        CancellationToken cancellationToken);

    Task<(YouPinAuthorizedCoverageWatch Watch, YouPinCoverageWatchState State)> CreateWatchAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task<(YouPinAuthorizedCoverageWatch Watch, YouPinCoverageWatchState State)> CreateWatchAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        YouPinHotCoverageAuditSnapshot snapshot,
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task<YouPinCoverageWatchState?> LoadWatchStateAsync(
        Guid watchId,
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task SaveWatchStateAsync(
        YouPinCoverageWatchState state,
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task DeleteWatchStateAsync(
        Guid watchId,
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task<YouPinCoverageWatchRunResult> RunWatchTickAsync(
        YouPinAuthorizedCoverageWatch watch,
        YouPinHotCoveragePlan plan,
        AppSettings settings,
        CancellationToken cancellationToken);
}

public sealed class YouPinAuditRuntime : IYouPinAuditOperations
{
    private readonly AuditReportStore _reportStore;
    private readonly PurchaseAccounts? _accounts;
    private readonly PurchaseReadGate _readGate = new();

    public YouPinAuditRuntime(AuditReportStore reportStore, PurchaseAccounts? accounts = null)
        => (_reportStore, _accounts) = (reportStore, accounts);

    public async Task<AuditExecutionResult> RunFullAuditAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        string reportDirectory,
        IProgress<YouPinHotCoverageProgress> progress,
        CancellationToken cancellationToken)
    {
        using YouPinPublicAuditClient client = YouPinPublicAuditClient.CreateStandalone();
        var service = new YouPinHotCoverageAuditService(client);
        YouPinHotCoverageAuditSnapshot snapshot = await service.RunFullCoverageAsync(
            subject,
            plan,
            progress,
            cancellationToken).ConfigureAwait(false);
        AuditWriteResult report = await _reportStore.WriteFullAuditAsync(
            snapshot,
            reportDirectory,
            cancellationToken).ConfigureAwait(false);
        return new AuditExecutionResult(snapshot, report);
    }

    public async Task<AuditExecutionResult> RunAccountAuditAsync(YouPinAuditSubject subject, YouPinHotCoveragePlan plan,
        string reportDirectory, IProgress<YouPinHotCoverageProgress> progress, PurchaseAccountBinding account,
        CancellationToken cancellationToken)
    {
        using var client = new AuthenticatedPurchaseClient(
            _accounts ?? throw new InvalidOperationException("读取账号服务不可用。"), account, _readGate);
        var service = new YouPinHotCoverageAuditService(client);
        var snapshot = await service.RunFullCoverageAsync(subject, plan, progress, cancellationToken).ConfigureAwait(false);
        var report = await _reportStore.WriteFullAuditAsync(snapshot, reportDirectory, cancellationToken).ConfigureAwait(false);
        return new(snapshot, report);
    }

    public async Task<(YouPinAuthorizedCoverageWatch Watch, YouPinCoverageWatchState State)> CreateWatchAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        YouPinAuthorizedCoverageWatch watch = YouPinAuthorizedCoverageWatch.Create(
            subject,
            plan,
            settings.ToCorePurpose(),
            TimeSpan.FromMinutes(settings.WatchIntervalMinutes));
        YouPinCoverageWatchState state = YouPinCoverageWatchState.Create(watch, plan);
        using var store = new YouPinJsonCoverageWatchStateStore(settings.StateDirectory);
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return (watch, state);
    }

    public async Task<(YouPinAuthorizedCoverageWatch Watch, YouPinCoverageWatchState State)> CreateWatchAsync(
        YouPinAuditSubject subject,
        YouPinHotCoveragePlan plan,
        YouPinHotCoverageAuditSnapshot snapshot,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        YouPinAuthorizedCoverageWatch watch = YouPinAuthorizedCoverageWatch.Create(
            subject,
            plan,
            settings.ToCorePurpose(),
            TimeSpan.FromMinutes(settings.WatchIntervalMinutes));
        YouPinCoverageWatchState state = YouPinCoverageWatchState.BootstrapFromFullAudit(
            watch,
            plan,
            snapshot);
        using var store = new YouPinJsonCoverageWatchStateStore(settings.StateDirectory);
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return (watch, state);
    }

    public async Task<YouPinCoverageWatchState?> LoadWatchStateAsync(
        Guid watchId,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var store = new YouPinJsonCoverageWatchStateStore(settings.StateDirectory);
        return await store.LoadAsync(watchId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveWatchStateAsync(
        YouPinCoverageWatchState state,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var store = new YouPinJsonCoverageWatchStateStore(settings.StateDirectory);
        await store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteWatchStateAsync(
        Guid watchId,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (watchId == Guid.Empty)
            throw new ArgumentException("Watch ID 不能为空。", nameof(watchId));
        string stateDirectory = Path.GetFullPath(settings.StateDirectory);
        string path = Path.Combine(stateDirectory, $"{watchId:N}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<YouPinCoverageWatchRunResult> RunWatchTickAsync(
        YouPinAuthorizedCoverageWatch watch,
        YouPinHotCoveragePlan plan,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        using var client = new AuthenticatedPurchaseClient(
            _accounts ?? throw new InvalidOperationException("读取账号服务不可用。"), settings.Account, _readGate);
        using var store = new YouPinJsonCoverageWatchStateStore(settings.StateDirectory);
        var runner = new YouPinCoverageWatchRunner(client, store);
        return await runner.RunDueTickAsync(watch, plan, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record AuditExecutionResult(
    YouPinHotCoverageAuditSnapshot Snapshot,
    AuditWriteResult Report);
