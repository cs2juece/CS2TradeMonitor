using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class UIFrameworkRuntimeServices
    {
        private UIFrameworkRuntimeServices(
            IRenderScheduler renderScheduler,
            ISoftwareUpdateService softwareUpdates)
        {
            RenderScheduler = renderScheduler ?? throw new ArgumentNullException(nameof(renderScheduler));
            SoftwareUpdates = softwareUpdates ?? throw new ArgumentNullException(nameof(softwareUpdates));
        }

        public IRenderScheduler RenderScheduler { get; }

        public ISoftwareUpdateService SoftwareUpdates { get; }

        public static UIFrameworkRuntimeServices Resolve()
        {
            return Resolve(AppServices.Provider);
        }

        public static UIFrameworkRuntimeServices Resolve(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return new UIFrameworkRuntimeServices(
                provider.GetRequiredService<IRenderScheduler>(),
                provider.GetRequiredService<ISoftwareUpdateService>());
        }

        public static IRenderScheduler ResolveRenderScheduler()
        {
            return ResolveRenderScheduler(AppServices.Provider);
        }

        public static IRenderScheduler ResolveRenderScheduler(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<IRenderScheduler>();
        }

        public static ISoftwareUpdateService ResolveSoftwareUpdates()
        {
            return ResolveSoftwareUpdates(AppServices.Provider);
        }

        public static ISoftwareUpdateService ResolveSoftwareUpdates(IServiceProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            return provider.GetRequiredService<ISoftwareUpdateService>();
        }
    }
}
