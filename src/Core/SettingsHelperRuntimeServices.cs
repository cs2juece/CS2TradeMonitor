using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor
{
    internal sealed class SettingsHelperRuntimeServices
    {
        private SettingsHelperRuntimeServices(IAppConfigState appConfigState)
        {
            AppConfigState = appConfigState ?? throw new ArgumentNullException(nameof(appConfigState));
        }

        public IAppConfigState AppConfigState { get; }

        public static SettingsHelperRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static SettingsHelperRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new SettingsHelperRuntimeServices(provider.GetRequiredService<IAppConfigState>());
        }
    }
}
