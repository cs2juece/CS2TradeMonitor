using System.Runtime.CompilerServices;
using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Infrastructure.Configuration
{
    internal static class DesktopSettingsPlatformRegistration
    {
        private static readonly SettingsInstanceCoordinator InstanceCoordinator = new();

        [ModuleInitializer]
        internal static void Initialize()
        {
            SettingsPlatformBridge.Configure(
                Load,
                () => SettingsHelper.GlobalBlockSave,
                value => SettingsHelper.GlobalBlockSave = value,
                (scope, detail, thresholdMs) => AppPerformanceProfiler.Measure(scope, detail, thresholdMs));
        }

        private static Settings Load(bool forceReload)
        {
            Settings settings = InstanceCoordinator.Load(
                () => SettingsHelper.Load(forceReload),
                forceReload);
            settings.Language = "zh";
            return settings;
        }
    }
}
