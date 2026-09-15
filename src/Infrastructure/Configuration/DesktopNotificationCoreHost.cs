using System.Runtime.CompilerServices;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Shared.Notifications;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Configuration;

internal sealed class DesktopNotificationCoreHost : INotificationCoreHost
{
    internal static DesktopNotificationCoreHost Instance { get; } = new();

    private DesktopNotificationCoreHost()
    {
    }

    public Task SaveSettingsAsync(Settings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = settings.Save();
        return Task.CompletedTask;
    }

    public void Info(string source, string eventCode)
        => DiagnosticsLogger.Info(source, eventCode);

    public void Error(string source, string eventCode, Exception? exception = null)
        => DiagnosticsLogger.Error(source, eventCode, exception);
}

internal static class DesktopNotificationCoreRegistration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        NotificationCoreFactory.ConfigurePlatform(
            NotifyRuntimeServices.ResolveDomesticHttpFactory,
            DesktopNotificationCoreHost.Instance);
    }
}
