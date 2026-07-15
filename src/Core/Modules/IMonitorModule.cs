using Microsoft.Extensions.Hosting;

namespace CS2TradeMonitor.src.Core.Modules
{
    public interface IMonitorModule : IHostedService
    {
        string Id { get; }
        string DisplayName { get; }
        MonitorModuleHealth GetHealth();
    }
}
