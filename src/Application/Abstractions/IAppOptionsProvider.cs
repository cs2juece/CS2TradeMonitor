using CS2TradeMonitor.Application.Options;

namespace CS2TradeMonitor.Application.Abstractions
{
    public interface IAppOptionsProvider
    {
        AppOptionsSnapshot GetCurrent(bool forceReload = false);
    }
}
