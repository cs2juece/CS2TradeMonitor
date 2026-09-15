using CS2TradeMonitor.Shared.Trading;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Configuration;

public sealed class DesktopYouPinProfitLossPlatformHost : IYouPinProfitLossPlatformHost
{
    public static DesktopYouPinProfitLossPlatformHost Instance { get; } = new();

    private DesktopYouPinProfitLossPlatformHost()
    {
    }

    public string HistoryPath
        => RuntimeDataPaths.GetDataFilePath("youpin_profit_loss_history.json");
}
