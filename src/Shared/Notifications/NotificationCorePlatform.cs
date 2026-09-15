using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;

namespace CS2TradeMonitor.Shared.Notifications;

public interface INotificationCoreHost
{
    Task SaveSettingsAsync(Settings settings, CancellationToken cancellationToken = default);

    void Info(string source, string eventCode);

    void Error(string source, string eventCode, Exception? exception = null);
}

public static class NotificationCoreFactory
{
    public static void ConfigurePlatform(
        Func<IDomesticHttpClientFactory> httpFactoryResolver,
        INotificationCoreHost host)
        => NotificationCorePlatform.Configure(httpFactoryResolver, host);

    public static Cs2UpdateReminderService CreateCs2UpdateReminder(
        IDomesticHttpClientFactory httpFactory,
        INotificationCoreHost host)
        => Cs2UpdateReminderService.Create(httpFactory, host);

    public static PhoneAlertDispatchService CreatePhoneAlerts(
        IDomesticHttpClientFactory httpFactory,
        INotificationCoreHost host)
        => PhoneAlertDispatchService.Create(httpFactory, host);
}

internal static class NotificationCorePlatform
{
    private static readonly object Gate = new();
    private static Func<IDomesticHttpClientFactory>? _httpFactoryResolver;
    private static INotificationCoreHost? _host;

    internal static void Configure(
        Func<IDomesticHttpClientFactory> httpFactoryResolver,
        INotificationCoreHost host)
    {
        ArgumentNullException.ThrowIfNull(httpFactoryResolver);
        ArgumentNullException.ThrowIfNull(host);

        lock (Gate)
        {
            if (_httpFactoryResolver is not null || _host is not null)
                throw new InvalidOperationException("通知共享核心的平台宿主不能重复配置。");

            _httpFactoryResolver = httpFactoryResolver;
            _host = host;
        }
    }

    internal static Cs2UpdateReminderService CreateCs2UpdateReminder()
    {
        (IDomesticHttpClientFactory httpFactory, INotificationCoreHost host) = Resolve();
        return NotificationCoreFactory.CreateCs2UpdateReminder(httpFactory, host);
    }

    internal static PhoneAlertDispatchService CreatePhoneAlerts()
    {
        (IDomesticHttpClientFactory httpFactory, INotificationCoreHost host) = Resolve();
        return NotificationCoreFactory.CreatePhoneAlerts(httpFactory, host);
    }

    internal static ServerChanPushService CreateServerChan()
    {
        (IDomesticHttpClientFactory httpFactory, INotificationCoreHost host) = Resolve();
        return new ServerChanPushService(httpFactory, host);
    }

    internal static WxPusherService CreateWxPusher()
    {
        (IDomesticHttpClientFactory httpFactory, INotificationCoreHost host) = Resolve();
        return new WxPusherService(httpFactory, host);
    }

    private static (IDomesticHttpClientFactory HttpFactory, INotificationCoreHost Host) Resolve()
    {
        Func<IDomesticHttpClientFactory>? resolver = Volatile.Read(ref _httpFactoryResolver);
        INotificationCoreHost? host = Volatile.Read(ref _host);
        if (resolver is null || host is null)
        {
            throw new InvalidOperationException(
                "通知服务尚未完成配置，请稍后重试。");
        }

        return (resolver(), host);
    }
}

internal sealed class NoopNotificationCoreHost : INotificationCoreHost
{
    internal static NoopNotificationCoreHost Instance { get; } = new();

    private NoopNotificationCoreHost()
    {
    }

    public Task SaveSettingsAsync(Settings settings, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Info(string source, string eventCode)
    {
    }

    public void Error(string source, string eventCode, Exception? exception = null)
    {
    }
}
