using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Shared.Ports;
using IClock = CS2TradeMonitor.Shared.Ports.IClock;

namespace CS2TradeMonitor.Shared.Trading;

public interface IYouPinProfitLossPlatformHost
{
    string HistoryPath { get; }
}

public sealed record YouPinProfitLossServiceDependencies(
    IYouPinAuthService AuthService,
    IDomesticHttpClientFactory HttpFactory,
    IClock Clock,
    IYouPinProfitLossPlatformHost PlatformHost,
    CS2TradeMonitor.Shared.Ports.IAppDiagnostics Diagnostics);
