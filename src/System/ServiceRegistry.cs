using CS2TradeMonitor.Application;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Infrastructure.Configuration;
using CS2TradeMonitor.Infrastructure.Diagnostics;
using CS2TradeMonitor.Infrastructure.YouPin;
using CS2TradeMonitor.Infrastructure.Http;
using CS2TradeMonitor.Infrastructure.Paths;
using CS2TradeMonitor.Infrastructure.Security;
using CS2TradeMonitor.Infrastructure.System;
using CS2TradeMonitor.Infrastructure.Windows;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices.InfoService;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.src.UI.Framework;
using Microsoft.Extensions.DependencyInjection;
using InfoServiceType = CS2TradeMonitor.src.SystemServices.InfoService.InfoService;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class ServiceRegistry
    {
        public static IServiceCollection AddCS2TradeMonitorServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // 阶段 0 只把 DI 桥接到现有单例，不能让容器 new 第二个实例。
            // 这些服务持有登录态、报价列表、待办状态、定时器或缓存；双实例会导致 UI 与后台状态分裂。
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton(_ => InstanceRuntimeContext.Current);
            services.AddSingleton<IInstanceRuntimeContext>(provider =>
                provider.GetRequiredService<InstanceRuntimeContext>());
            services.AddSingleton<IAppDataPathProvider, RuntimeDataPathProvider>();
            services.AddSingleton<ISecureDataProtector, DpapiSecureDataProtector>();
            services.AddSingleton<IDomesticHttpClientFactory, DomesticHttpClientFactoryAdapter>();
            services.AddSingleton<ISteamRoutedHttpClientFactory, SteamRoutedHttpClientFactoryAdapter>();
            services.AddSingleton<IAutoStartManager, AutoStartManagerAdapter>();
            services.AddSingleton<IAppDiagnostics, DiagnosticsLoggerAdapter>();
            services.AddSingleton<IDetailedDiagnosticsService, DetailedDiagnosticsRuntimeBridge>();
            services.AddSingleton<IDetailedDiagnosticsExportService, DetailedDiagnosticsExportRuntimeBridge>();
            services.AddSingleton<ISettingsRepository, SettingsRepositoryAdapter>();
            services.AddSingleton<IAppOptionsProvider, AppOptionsProvider>();

            services.AddSingleton(_ => AppConfigState.Instance);
            services.AddSingleton<IAppConfigState>(_ => AppConfigState.Instance);
            services.AddSingleton(_ => RuntimeAppState.Instance);
            services.AddSingleton<IRuntimeAppState>(_ => RuntimeAppState.Instance);
            services.AddSingleton(_ => MonitorModuleHost.Instance);
            services.AddSingleton<IMonitorModuleHost>(_ => MonitorModuleHost.Instance);
            services.AddSingleton(_ => MarketAlertService.Instance);
            services.AddSingleton<IMarketAlertService>(_ => MarketAlertService.Instance);
            services.AddSingleton(_ => ThemeScaleService.Instance);
            services.AddSingleton(_ => RenderScheduler.Instance);
            services.AddSingleton<IRenderScheduler>(_ => RenderScheduler.Instance);
            services.AddSingleton(_ => NetworkRouteMonitor.Instance);
            services.AddSingleton<INetworkRouteMonitor>(_ => NetworkRouteMonitor.Instance);
            services.AddSingleton<NetworkRecoverySignal>();
            services.AddSingleton<INetworkRecoverySignal>(provider => provider.GetRequiredService<NetworkRecoverySignal>());
            services.AddSingleton(provider => new NetworkRouteRecoveryCoordinator(
                provider.GetRequiredService<INetworkRouteMonitor>(),
                request => MarketDataSourceManager.RefreshMarketIndexesAsync(request),
                TimeSpan.FromSeconds(3),
                async () =>
                {
                    await provider.GetRequiredService<ISteamConnectionResolver>()
                        .ResolveAsync(force: true)
                        .ConfigureAwait(false);
                },
                provider.GetRequiredService<NetworkRecoverySignal>(),
                MarketDataSourceManager.HasRetryableNetworkFailure));

            services.AddSingleton(_ => CsqaqService.Instance);
            services.AddSingleton<ICsqaqService>(_ => CsqaqService.Instance);
            services.AddSingleton(_ => Cs2UpdateReminderService.Instance);
            services.AddSingleton<ICs2UpdateReminderService>(_ => Cs2UpdateReminderService.Instance);
            services.AddSingleton(_ => PhoneAlertDispatchService.Instance);
            services.AddSingleton<IPhoneAlertDispatchService>(_ => PhoneAlertDispatchService.Instance);
            services.AddSingleton(_ => ServerChanPushService.Instance);
            services.AddSingleton<IServerChanPushService>(_ => ServerChanPushService.Instance);
            services.AddSingleton(_ => SoftwareUpdateService.Instance);
            services.AddSingleton<ISoftwareUpdateService>(_ => SoftwareUpdateService.Instance);
            services.AddSingleton(_ => SteamAuthSecureStore.Instance);
            services.AddSingleton<ISteamAuthStore>(_ => SteamAuthSecureStore.Instance);
            services.AddSingleton(_ => SteamConnectionResolver.Instance);
            services.AddSingleton<ISteamConnectionResolver>(_ => SteamConnectionResolver.Instance);
            services.AddSingleton<SteamConnectivitySupervisor>();
            services.AddSingleton(_ => SteamManualProxyStore.Instance);
            services.AddSingleton<ISteamManualProxyStore>(_ => SteamManualProxyStore.Instance);
            services.AddSingleton(_ => SteamDtItemService.Instance);
            services.AddSingleton<ISteamDtItemService>(_ => SteamDtItemService.Instance);
            services.AddSingleton(_ => SteamDtService.Instance);
            services.AddSingleton<ISteamDtService>(_ => SteamDtService.Instance);
            services.AddSingleton<SteamConfirmationClient>();
            services.AddSingleton<ISteamConfirmationClient>(provider => provider.GetRequiredService<SteamConfirmationClient>());
            services.AddSingleton<SteamTradeOfferClient>();
            services.AddSingleton<ISteamTradeOfferClient>(provider => provider.GetRequiredService<SteamTradeOfferClient>());
            services.AddSingleton(_ => SteamLoginService.Instance);
            services.AddSingleton<ISteamLoginService>(_ => SteamLoginService.Instance);
            services.AddSingleton(_ => SteamSessionKeepAliveService.Instance);
            services.AddSingleton<ISteamSessionKeepAliveService>(_ => SteamSessionKeepAliveService.Instance);
            services.AddSingleton(_ => SteamOfferService.Instance);
            services.AddSingleton<ISteamOfferService>(_ => SteamOfferService.Instance);
            services.AddSingleton<IManualYouPinOfferAutoConfirmation>(_ => SteamOfferService.Instance);
            services.AddSingleton(_ => SteamTokenVault.Instance);
            services.AddSingleton<ISteamTokenVault>(_ => SteamTokenVault.Instance);
            services.AddSingleton(_ => WxPusherService.Instance);
            services.AddSingleton<IWxPusherService>(_ => WxPusherService.Instance);
            services.AddSingleton(_ => YouPinAuthService.Instance);
            services.AddSingleton<IYouPinAuthService>(_ => YouPinAuthService.Instance);
            services.AddSingleton(_ => YouPinInventoryService.Instance);
            services.AddSingleton<IYouPinInventoryService>(_ => YouPinInventoryService.Instance);
            services.AddSingleton<YouPinInventoryStorageHttpAdapter>();
            services.AddSingleton<IYouPinInventoryStorageAdapter>(provider =>
                provider.GetRequiredService<YouPinInventoryStorageHttpAdapter>());
            services.AddSingleton<YouPinInventoryStorageService>();
            services.AddSingleton<IYouPinInventoryStorageService>(provider =>
                provider.GetRequiredService<YouPinInventoryStorageService>());
            services.AddSingleton(_ => YouPinProfitLossService.Instance);
            services.AddSingleton<IYouPinProfitLossService>(_ => YouPinProfitLossService.Instance);
            services.AddSingleton(_ => YouPinSaleReminderService.Instance);
            services.AddSingleton<IYouPinSaleReminderService>(_ => YouPinSaleReminderService.Instance);
            services.AddSingleton<YouPinGridStrategyFileStore>();
            services.AddSingleton<IYouPinGridStrategyStore>(provider =>
                provider.GetRequiredService<YouPinGridStrategyFileStore>());
            services.AddSingleton<YouPinGridMarketGateway>();
            services.AddSingleton<IYouPinGridMarketGateway>(provider =>
                provider.GetRequiredService<YouPinGridMarketGateway>());
            services.AddSingleton<YouPinGridExecutionJournalFileStore>();
            services.AddSingleton<IYouPinGridExecutionJournal>(provider =>
                provider.GetRequiredService<YouPinGridExecutionJournalFileStore>());
            services.AddSingleton<YouPinGridExecutionGateway>();
            services.AddSingleton<IYouPinGridExecutionGateway>(provider =>
                provider.GetRequiredService<YouPinGridExecutionGateway>());
            services.AddSingleton<YouPinGridExecutionModule>();
            services.AddSingleton<YouPinGridTradingService>();
            services.AddSingleton<IYouPinGridTradingService>(provider =>
                provider.GetRequiredService<YouPinGridTradingService>());
            services.AddSingleton<YouPinLandlordGateway>();
            services.AddSingleton<IYouPinLandlordGateway>(provider =>
                provider.GetRequiredService<YouPinLandlordGateway>());
            services.AddSingleton<YouPinLandlordAuditFileStore>();
            services.AddSingleton<IYouPinLandlordAuditStore>(provider =>
                provider.GetRequiredService<YouPinLandlordAuditFileStore>());
            services.AddSingleton(provider => new YouPinLandlordAutomation(
                provider.GetRequiredService<IYouPinLandlordGateway>(),
                provider.GetRequiredService<IYouPinLandlordAuditStore>(),
                provider.GetRequiredService<IClock>(),
                writeInterval: TradeWriteOperationGate.DefaultMinimumInterval,
                persistSettings: settings =>
                {
                    SettingsSaveResult result = settings.Save();
                    if (!result.Succeeded)
                    {
                        DiagnosticsLogger.Error(
                            "YouPinLandlord",
                            $"Persisting execution cooldown failed; FailureType={result.FailureType ?? "unknown"}");
                    }
                }));
            services.AddSingleton<IYouPinLandlordAutomation>(provider =>
                provider.GetRequiredService<YouPinLandlordAutomation>());
            services.AddSingleton(_ => InfoServiceType.Instance);
            services.AddSingleton<IInfoService>(_ => InfoServiceType.Instance);

            return services;
        }
    }
}
