using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class SystemPageRuntimeServices
    {
        private SystemPageRuntimeServices(
            IMonitorModuleHost moduleHost,
            ISoftwareUpdateService softwareUpdates,
            ICs2UpdateReminderService cs2UpdateReminder,
            IDetailedDiagnosticsService detailedDiagnostics,
            IDetailedDiagnosticsExportService diagnosticsExport)
        {
            ModuleHost = moduleHost ?? throw new ArgumentNullException(nameof(moduleHost));
            SoftwareUpdates = softwareUpdates ?? throw new ArgumentNullException(nameof(softwareUpdates));
            Cs2UpdateReminder = cs2UpdateReminder ?? throw new ArgumentNullException(nameof(cs2UpdateReminder));
            DetailedDiagnostics = detailedDiagnostics ?? throw new ArgumentNullException(nameof(detailedDiagnostics));
            DiagnosticsExport = diagnosticsExport ?? throw new ArgumentNullException(nameof(diagnosticsExport));
        }

        public IMonitorModuleHost ModuleHost { get; }

        public ISoftwareUpdateService SoftwareUpdates { get; }

        public ICs2UpdateReminderService Cs2UpdateReminder { get; }

        public IDetailedDiagnosticsService DetailedDiagnostics { get; }

        public IDetailedDiagnosticsExportService DiagnosticsExport { get; }

        public static SystemPageRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SystemPageRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SystemPageRuntimeServices(
                provider.GetRequiredService<IMonitorModuleHost>(),
                provider.GetRequiredService<ISoftwareUpdateService>(),
                provider.GetRequiredService<ICs2UpdateReminderService>(),
                provider.GetRequiredService<IDetailedDiagnosticsService>(),
                provider.GetRequiredService<IDetailedDiagnosticsExportService>());
        }
    }
}
