using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;

namespace CS2TradeMonitor.Shared.Trading;

/// <summary>
/// Cross-platform composition root for the exact desktop grid service. The
/// platform only supplies paths, diagnostics, HTTP, auth, and inventory ports.
/// </summary>
public interface IHostDrivenYouPinGridTradingService : IYouPinGridTradingService
{
}

public sealed class YouPinGridTradingRuntime : IHostDrivenYouPinGridTradingService, IDisposable
{
    private readonly YouPinGridMarketGateway _market;
    private readonly YouPinGridExecutionGateway _executionGateway;
    private readonly YouPinGridTradingService _service;

    public YouPinGridTradingRuntime(
        IYouPinAuthService auth,
        IDomesticHttpClientFactory httpFactory,
        IYouPinInventoryService inventory,
        YouPinLandlordRuntime landlordRuntime,
        IAppDataPathProvider paths,
        IAppDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(landlordRuntime);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var store = new YouPinGridStrategyFileStore(paths, diagnostics);
        _market = new YouPinGridMarketGateway(auth, httpFactory, diagnostics);
        var journal = new YouPinGridExecutionJournalFileStore(paths, diagnostics);
        _executionGateway = new YouPinGridExecutionGateway(
            auth,
            httpFactory,
            _market,
            inventory,
            landlordRuntime.Gateway,
            diagnostics);
        var execution = new YouPinGridExecutionModule(journal, _executionGateway);
        _service = new YouPinGridTradingService(
            store,
            _market,
            inventory,
            journal,
            execution,
            usesInventoryRefreshConsumer: false);
    }

    public event Action? DataUpdated
    {
        add => _service.DataUpdated += value;
        remove => _service.DataUpdated -= value;
    }

    public void Configure(Settings settings) => _service.Configure(settings);

    public YouPinGridRuntimeSnapshot GetSnapshot() => _service.GetSnapshot();

    public Task<YouPinGridRuntimeSnapshot> RefreshAsync(
        Settings settings,
        CancellationToken cancellationToken = default)
        => _service.RefreshAsync(settings, cancellationToken);

    public Task<YouPinGridMutationResult> UpsertStrategyAsync(
        YouPinGridStrategy strategy,
        CancellationToken cancellationToken = default)
        => _service.UpsertStrategyAsync(strategy, cancellationToken);

    public Task<YouPinGridMutationResult> DeleteStrategyAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
        => _service.DeleteStrategyAsync(strategyId, cancellationToken);

    public void Dispose()
    {
        _service.Dispose();
        _executionGateway.Dispose();
        _market.Dispose();
    }
}
