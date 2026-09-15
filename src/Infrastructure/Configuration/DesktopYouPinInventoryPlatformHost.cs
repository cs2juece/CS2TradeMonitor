using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Trading;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Infrastructure.Configuration;

public sealed class DesktopYouPinInventoryPlatformHost : IYouPinInventoryPlatformHost
{
    public static DesktopYouPinInventoryPlatformHost Instance { get; } = new();

    private DesktopYouPinInventoryPlatformHost()
    {
    }

    public string InventoryHistoryPath
        => RuntimeDataPaths.GetDataFilePath("youpin_inventory_history.json");

    public bool UsesInternalTimer => true;

    public Task PublishValueAlertAsync(
        Settings settings,
        YouPinInventoryValueAlert alert,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(alert);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.DoNotDisturbEnabled)
            return Task.CompletedTask;

        YouPinSaleReminderNotificationMode mode = settings.YouPinInventoryChangeAlertNotificationMode;
        bool showBubble = mode is YouPinSaleReminderNotificationMode.Bubble
            or YouPinSaleReminderNotificationMode.BubbleAndSound;
        bool playSound = mode is YouPinSaleReminderNotificationMode.Sound
            or YouPinSaleReminderNotificationMode.BubbleAndSound;
        if (!showBubble && !playSound)
            return Task.CompletedTask;

        AppNotificationHub.Instance.Request(
            "悠悠有品库存涨跌提醒",
            $"{alert.Message}\n¥{alert.OldValue:F2} -> ¥{alert.NewValue:F2}",
            AppNotificationSeverity.Info,
            AppNotificationPlacement.BottomLeft,
            playSound,
            showToast: showBubble,
            source: AlertHistorySources.Inventory);
        return Task.CompletedTask;
    }

    public Task PublishStopProfitLossAlertsAsync(
        Settings settings,
        IReadOnlyList<YouPinStopProfitLossAlert> alerts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(alerts);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.DoNotDisturbEnabled || alerts.Count == 0)
            return Task.CompletedTask;

        YouPinSaleReminderNotificationMode mode = settings.YouPinStopProfitLossNotificationMode;
        bool showBubble = mode is YouPinSaleReminderNotificationMode.Bubble
            or YouPinSaleReminderNotificationMode.BubbleAndSound;
        bool playSound = mode is YouPinSaleReminderNotificationMode.Sound
            or YouPinSaleReminderNotificationMode.BubbleAndSound;
        if (!showBubble && !playSound)
            return Task.CompletedTask;

        string title = alerts.Count == 1 ? "库存止盈/损报警" : $"库存止盈/损报警（{alerts.Count} 条）";
        string message = string.Join(Environment.NewLine, alerts.Take(3).Select(alert => alert.Message));
        if (alerts.Count > 3)
            message += Environment.NewLine + $"另有 {alerts.Count - 3} 条达到阈值。";

        AppNotificationHub.Instance.Request(
            title,
            message,
            AppNotificationSeverity.Warning,
            AppNotificationPlacement.BottomLeft,
            playSound,
            showToast: showBubble,
            source: AlertHistorySources.InventoryStopProfitLoss);
        return Task.CompletedTask;
    }
}
