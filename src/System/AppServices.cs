using Microsoft.Extensions.DependencyInjection;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class AppServices
    {
        private static readonly object SyncRoot = new();
        private static IServiceProvider? _provider;

        public static IServiceProvider Provider
        {
            get
            {
                lock (SyncRoot)
                {
                    _provider ??= BuildServiceProvider();
                    return _provider;
                }
            }
        }

        public static T GetRequiredService<T>() where T : notnull
        {
            return Provider.GetRequiredService<T>();
        }

        public static void Initialize()
        {
            _ = Provider;
        }

        public static IServiceProvider BuildServiceProviderForTests()
        {
            return BuildServiceProvider();
        }

        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddCS2TradeMonitorServices();

            // 这里只验证容器配置，不主动解析服务；避免启动阶段提前触发网络、定时器或缓存加载。
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        }
    }
}
