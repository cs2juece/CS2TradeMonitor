using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Ports;
using IClock = CS2TradeMonitor.Shared.Ports.IClock;

namespace CS2TradeMonitor.Shared.Trading;

public interface IYouPinInventoryPlatformHost
{
    string InventoryHistoryPath { get; }

    bool UsesInternalTimer { get; }

    Task PublishValueAlertAsync(
        Settings settings,
        YouPinInventoryValueAlert alert,
        CancellationToken cancellationToken = default);

    Task PublishStopProfitLossAlertsAsync(
        Settings settings,
        IReadOnlyList<YouPinStopProfitLossAlert> alerts,
        CancellationToken cancellationToken = default);
}

public sealed record YouPinInventoryServiceDependencies(
    IYouPinAuthService AuthService,
    IDomesticHttpClientFactory HttpFactory,
    IClock Clock,
    IYouPinInventoryPlatformHost PlatformHost);
