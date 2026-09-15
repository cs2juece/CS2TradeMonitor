using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using ApplicationClock = CS2TradeMonitor.Application.Abstractions.IClock;

namespace CS2TradeMonitor.Shared.Trading;

/// <summary>
/// Cross-platform composition root for the exact desktop landlord automation.
/// Android disables the desktop-owned timer and drives scheduled work through
/// the single shared core writer instead.
/// </summary>
public interface IHostDrivenYouPinLandlordAutomation : IYouPinLandlordAutomation
{
    Task RunScheduledCycleAsync(CancellationToken cancellationToken = default);
}

public sealed class YouPinLandlordRuntime : IHostDrivenYouPinLandlordAutomation
{
    private readonly YouPinLandlordGateway _gateway;
    private readonly YouPinLandlordAutomation _automation;

    public YouPinLandlordRuntime(
        IYouPinAuthService auth,
        IDomesticHttpClientFactory httpFactory,
        IAppDataPathProvider paths,
        IAppDiagnostics diagnostics,
        ApplicationClock clock)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(clock);

        _gateway = new YouPinLandlordGateway(auth, httpFactory, diagnostics);
        var audit = new YouPinLandlordAuditFileStore(paths, diagnostics);
        _automation = new YouPinLandlordAutomation(
            _gateway,
            audit,
            clock,
            writeInterval: TradeAutomationPolicy.MinimumWriteInterval,
            usesInternalTimer: false);
        AuditLogPath = paths.GetLogFilePath(YouPinLandlordAuditFileStore.FileName);
    }

    internal IYouPinLandlordGateway Gateway => _gateway;

    public string AuditLogPath { get; }

    public event Action? SnapshotChanged
    {
        add => _automation.SnapshotChanged += value;
        remove => _automation.SnapshotChanged -= value;
    }

    public void Configure(Settings settings) => _automation.Configure(settings);
    public YouPinLandlordPolicy ApplyPolicy(YouPinLandlordPolicy policy) => _automation.ApplyPolicy(policy);
    public YouPinLandlordPolicy GetPolicy() => _automation.GetPolicy();
    public YouPinLandlordSnapshot GetSnapshot() => _automation.GetSnapshot();

    public Task<YouPinLandlordRunResult> RunNowAsync(
        YouPinLandlordWorkflow workflow,
        string trigger = "用户立即检查",
        CancellationToken cancellationToken = default)
        => _automation.RunNowAsync(workflow, trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> RunRentalTypeNowAsync(
        YouPinRentalShelfType rentalType,
        string trigger = "用户立即检查",
        CancellationToken cancellationToken = default)
        => _automation.RunRentalTypeNowAsync(rentalType, trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> RunInventoryNowAsync(
        string trigger = "用户立即扫描库存",
        CancellationToken cancellationToken = default)
        => _automation.RunInventoryNowAsync(trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> ScanRentalTypeNowAsync(
        YouPinRentalShelfType rentalType,
        string trigger = "用户立即扫描货架",
        CancellationToken cancellationToken = default)
        => _automation.ScanRentalTypeNowAsync(rentalType, trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> ScanRentalNowAsync(
        YouPinRentalScanScope scope,
        string trigger = "用户立即扫描货架",
        CancellationToken cancellationToken = default)
        => _automation.ScanRentalNowAsync(scope, trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> ExecuteRentalTypeNowAsync(
        YouPinRentalShelfType rentalType,
        string trigger = "用户立即执行改价",
        CancellationToken cancellationToken = default)
        => _automation.ExecuteRentalTypeNowAsync(rentalType, trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> ExecuteRentalNowAsync(
        YouPinRentalScanScope scope,
        string trigger = "用户立即执行改价",
        CancellationToken cancellationToken = default)
        => _automation.ExecuteRentalNowAsync(scope, trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> ScanInventoryNowAsync(
        string trigger = "用户立即扫描库存",
        CancellationToken cancellationToken = default)
        => _automation.ScanInventoryNowAsync(trigger, cancellationToken);

    public Task<YouPinLandlordRunResult> ExecuteInventoryNowAsync(
        string trigger = "用户立即执行库存自动出租",
        CancellationToken cancellationToken = default)
        => _automation.ExecuteInventoryNowAsync(trigger, cancellationToken);

    public Task<YouPinLandlordPricingPreference> RefreshPricingPreferenceAsync(
        CancellationToken cancellationToken = default)
        => _automation.RefreshPricingPreferenceAsync(cancellationToken);

    public Task<IReadOnlyList<YouPinLandlordOperationRecord>> QueryHistoryAsync(
        YouPinLandlordAuditQuery query,
        CancellationToken cancellationToken = default)
        => _automation.QueryHistoryAsync(query, cancellationToken);

    public YouPinLandlordAuditHealth GetAuditHealth() => _automation.GetAuditHealth();

    public Task RunScheduledCycleAsync(CancellationToken cancellationToken = default)
        => _automation.RunScheduledCycleAsync(cancellationToken);

    public void Dispose()
    {
        _automation.Dispose();
        _gateway.Dispose();
    }
}
