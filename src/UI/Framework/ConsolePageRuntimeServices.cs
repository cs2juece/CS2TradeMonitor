using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class ConsolePageRuntimeServices
    {
        private ConsolePageRuntimeServices(MonitoringConsoleSnapshotBuilder snapshotBuilder)
        {
            SnapshotBuilder = snapshotBuilder ?? throw new ArgumentNullException(nameof(snapshotBuilder));
        }

        public MonitoringConsoleSnapshotBuilder SnapshotBuilder { get; }

        public static ConsolePageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static ConsolePageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            return new ConsolePageRuntimeServices(
                provider.GetRequiredService<MonitoringConsoleSnapshotBuilder>());
        }
    }
}
