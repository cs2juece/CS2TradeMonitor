using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.Shared.Ports;
using IClock = CS2TradeMonitor.Shared.Ports.IClock;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinQuoteOrderCommand(string OrderNo, string TradeOfferId = "");

public sealed record YouPinQuoteOrderProjection(
    string OrderNo,
    string TradeOfferId,
    string Name,
    double Price,
    int OrderType,
    string StatusText,
    string ActionKind,
    string ActionLabel,
    string StatusReason,
    bool CanRun,
    bool IsOrderGroup,
    int OrderCount,
    DateTime DetectedAt);

public sealed record YouPinQuoteProjection(
    bool AutoRefreshEnabled,
    string AutoRefreshStatus,
    string Error,
    DateTime LastCheck,
    int SaleOrRentalPendingCount,
    int PurchasePendingCount,
    IReadOnlyList<YouPinQuoteOrderProjection> Orders);

public static class YouPinQuoteActionability
{
    public static bool IsActionableQuoteOrder(YouPinSaleOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (YouPinSaleOrderActionResolver.IsPendingBuyQuote(order))
            return true;

        YouPinSaleOrderAction action = YouPinSaleOrderActionResolver.Resolve(order);
        return action.CanRun
            && (action.Kind == YouPinSaleOrderActionKind.SendOffer
                || action.Kind == YouPinSaleOrderActionKind.ConfirmOffer
                || IsRentalSteamProcessingAction(order, action));
    }

    public static bool IsBatchActionableQuoteOrder(YouPinSaleOrder order)
        => !YouPinSaleOrderActionResolver.IsPendingBuyQuote(order)
            && IsActionableQuoteOrder(order);

    private static bool IsRentalSteamProcessingAction(
        YouPinSaleOrder order,
        YouPinSaleOrderAction action)
        => order.OrderType == 2
            && action.CanRun
            && action.Kind == YouPinSaleOrderActionKind.QueryStatus
            && action.StatusReason.Contains("Steam 报价", StringComparison.Ordinal);
}

/// <summary>
/// Shared query/command boundary around the physical desktop YouPin service.
/// Order action selection always comes from YouPinSaleOrderActionResolver.
/// </summary>
public sealed class YouPinQuoteFeatureModule : ITradeMonitorCoreModule
{
    public const string RefreshBinding = "RefreshYouPinQuoteTodos";
    public const string ProcessAllBinding = "ProcessAllYouPinQuotes";
    public const string SendBinding = "SendYouPinSaleOffer";
    public const string ConfirmBinding = "ConfirmYouPinSaleOffer";
    public const string QueryStatusBinding = "QueryYouPinSaleOfferStatus";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IYouPinSaleReminderService _service;
    private readonly IManualYouPinOfferAutoConfirmation? _manualConfirmation;
    private readonly ISettingsSnapshotStore _settings;
    private readonly IClock _clock;

    public YouPinQuoteFeatureModule(
        IYouPinSaleReminderService service,
        ISettingsSnapshotStore settings,
        IClock clock,
        IManualYouPinOfferAutoConfirmation? manualConfirmation = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _manualConfirmation = manualConfirmation;
    }

    public bool CanHandle(string bindingName)
        => bindingName is RefreshBinding
            or ProcessAllBinding
            or SendBinding
            or ConfirmBinding
            or QueryStatusBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinSaleReminderState state = _service.GetState();
        IReadOnlyList<YouPinQuoteOrderProjection> orders = state.RecentWaitDeliverOrders
            .Where(order => !string.IsNullOrWhiteSpace(order.OrderNo))
            .Select(ProjectOrder)
            .ToArray();
        int purchaseCount = state.RecentWaitDeliverOrders.Count(YouPinSaleOrderActionResolver.IsPendingBuyQuote);
        int saleCount = state.RecentWaitDeliverOrders.Count(YouPinQuoteActionability.IsBatchActionableQuoteOrder);
        var projection = new YouPinQuoteProjection(
            state.QuoteAutoRefreshEnabled,
            string.IsNullOrWhiteSpace(state.LastAutoDeliveryStatus) ? "未运行" : state.LastAutoDeliveryStatus,
            state.LastAutoDeliveryError,
            state.LastAutoDeliveryCheck,
            saleCount,
            purchaseCount,
            orders);

        bool requiresManualContinuation = query.BindingName is SendBinding or ProcessAllBinding;
        FeatureAvailability availability = requiresManualContinuation && _manualConfirmation is null
            ? FeatureAvailability.NotAvailable
            : FeatureAvailability.Available;
        string reasonCode = availability == FeatureAvailability.Available
            ? "ok"
            : "steam.manual-continuation-unavailable";
        string message = availability == FeatureAvailability.Available
            ? (string.IsNullOrWhiteSpace(state.LastAutoDeliveryError)
                ? $"当前显示 {orders.Count} 条真实报价记录。"
                : state.LastAutoDeliveryError)
            : "Steam 手动报价续接尚未迁入，发送操作保持禁用。";

        return new FeatureStateProjection(
            query.SemanticId,
            availability,
            reasonCode,
            message,
            JsonSerializer.Serialize(projection),
            snapshot.Version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        SettingsSnapshot snapshot = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        if (command.BindingName == RefreshBinding)
        {
            YouPinSaleReminderCheckResult result = await _service.CheckQuoteNowAsync("用户立即检查")
                .ConfigureAwait(false);
            return FromCheckResult(result, command.CorrelationId, snapshot.Version);
        }

        if (command.BindingName == ProcessAllBinding)
        {
            if (_manualConfirmation is null)
            {
                return CoreCommandResult.Disabled(
                    "steam.manual-continuation-unavailable",
                    "Steam 手动报价续接尚未迁入，批量发送保持禁用。",
                    command.CorrelationId,
                    snapshot.Version);
            }

            return await ProcessAllAsync(command, snapshot.Version, cancellationToken).ConfigureAwait(false);
        }

        YouPinQuoteOrderCommand? payload = ReadPayload(command.PayloadJson);
        if (payload is null || string.IsNullOrWhiteSpace(payload.OrderNo))
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.order-required",
                "请先选择一条真实悠悠订单。",
                command.CorrelationId,
                snapshot.Version);
        }

        YouPinSaleOrder? order = FindOrder(payload.OrderNo);
        if (order is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.order-stale",
                "该订单不在当前列表中，请先刷新。",
                command.CorrelationId,
                snapshot.Version);
        }

        YouPinSaleOrderAction action = YouPinSaleOrderActionResolver.Resolve(order);
        if (command.BindingName == SendBinding)
        {
            if (_manualConfirmation is null)
            {
                return CoreCommandResult.Disabled(
                    "steam.manual-continuation-unavailable",
                    "Steam 手动报价续接尚未迁入，发送操作保持禁用。",
                    command.CorrelationId,
                    snapshot.Version);
            }
            if (!action.CanRun || action.Kind != YouPinSaleOrderActionKind.SendOffer)
                return InvalidResolvedAction(action, command, snapshot.Version);

            YouPinSaleActionResult result = await _service.SendOfferAsync(order.OrderNo).ConfigureAwait(false);
            if (result.Ok)
                await _manualConfirmation.HandleManuallySentYouPinOfferAsync(order, result, cancellationToken).ConfigureAwait(false);
            return FromActionResult(result, command.CorrelationId, snapshot.Version);
        }

        if (command.BindingName == ConfirmBinding)
        {
            if (!action.CanRun || action.Kind != YouPinSaleOrderActionKind.ConfirmOffer)
                return InvalidResolvedAction(action, command, snapshot.Version);
            YouPinSaleActionResult result = await _service.ConfirmOfferAsync(
                order.OrderNo,
                string.IsNullOrWhiteSpace(payload.TradeOfferId) ? order.TradeOfferId : payload.TradeOfferId)
                .ConfigureAwait(false);
            return FromActionResult(result, command.CorrelationId, snapshot.Version);
        }

        if (command.BindingName == QueryStatusBinding)
        {
            YouPinSaleActionResult result = await _service.QueryOfferStatusAsync(order.OrderNo).ConfigureAwait(false);
            return FromActionResult(result, command.CorrelationId, snapshot.Version);
        }

        return CoreCommandResult.Disabled(
            "youpin.quote-command-unavailable",
            "该悠悠报价命令未注册。",
            command.CorrelationId,
            snapshot.Version);
    }

    public async Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        SettingsSnapshot snapshot = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        Settings settings = snapshot.Settings;
        if (!settings.YouPinSaleReminderEnabled
            && !settings.YouPinQuoteAutoRefreshEnabled
            && !settings.YouPinMsgCenterEnabled)
        {
            return new AutomationCycleResult(
                AutomationCycleStatus.Skipped,
                "youpin.automation-disabled",
                "悠悠后台检查未启用。",
                trigger.CorrelationId,
                snapshot.Version);
        }

        try
        {
            await _service.RunDueChecksAsync().ConfigureAwait(false);
            return new AutomationCycleResult(
                AutomationCycleStatus.Completed,
                "ok",
                "悠悠后台检查周期已完成。",
                trigger.CorrelationId,
                snapshot.Version,
                _service.GetState().RecentWaitDeliverOrders.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new AutomationCycleResult(
                AutomationCycleStatus.Failed,
                "youpin.cycle-failed",
                YouPinAuthService.Sanitize(exception.Message),
                trigger.CorrelationId,
                snapshot.Version);
        }
    }

    private async Task<SettingsSnapshot> ConfigureAsync(CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _service.ConfigureForExternalScheduler(snapshot.Settings);
        return snapshot;
    }

    private async Task<CoreCommandResult> ProcessAllAsync(
        FeatureCommand command,
        long version,
        CancellationToken cancellationToken)
    {
        List<YouPinSaleOrder> orders = _service.GetState().RecentWaitDeliverOrders
            .Where(order => !string.IsNullOrWhiteSpace(order.OrderNo))
            .Where(YouPinQuoteActionability.IsBatchActionableQuoteOrder)
            .GroupBy(order => order.OrderNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (orders.Count == 0)
        {
            return CoreCommandResult.Skipped(
                "youpin.no-actionable-orders",
                "当前没有可发送或确认的报价。",
                command.CorrelationId,
                version);
        }

        int success = 0;
        int skipped = 0;
        int failed = 0;
        for (int index = 0; index < orders.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            YouPinSaleOrder order = orders[index];
            YouPinSaleOrderAction resolved = YouPinSaleOrderActionResolver.Resolve(order);
            YouPinSaleActionResult result;
            if (!resolved.CanRun)
            {
                result = YouPinSaleActionResult.Skip(resolved.StatusReason);
            }
            else
            {
                result = resolved.Kind switch
                {
                    YouPinSaleOrderActionKind.SendOffer => await SendAndContinueAsync(order, cancellationToken).ConfigureAwait(false),
                    YouPinSaleOrderActionKind.ConfirmOffer => await _service.ConfirmOfferAsync(order.OrderNo, order.TradeOfferId).ConfigureAwait(false),
                    YouPinSaleOrderActionKind.QueryStatus => await _service.QueryOfferStatusAsync(order.OrderNo).ConfigureAwait(false),
                    _ => YouPinSaleActionResult.Skip(resolved.StatusReason)
                };
            }

            if (result.Ok)
                success++;
            else if (result.Skipped)
                skipped++;
            else
                failed++;

            if (index + 1 < orders.Count)
                await _clock.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        string message = $"一键处理全部报价完成：成功 {success} 条";
        if (skipped > 0)
            message += $"，跳过 {skipped} 条";
        if (failed > 0)
            message += $"，失败 {failed} 条";
        message += "。";
        return failed == 0
            ? CoreCommandResult.Success(message, command.CorrelationId, version)
            : CoreCommandResult.Failed("youpin.batch-partial-failure", message, command.CorrelationId, version);
    }

    private async Task<YouPinSaleActionResult> SendAndContinueAsync(
        YouPinSaleOrder order,
        CancellationToken cancellationToken)
    {
        YouPinSaleActionResult result = await _service.SendOfferAsync(order.OrderNo).ConfigureAwait(false);
        if (result.Ok && _manualConfirmation is not null)
            await _manualConfirmation.HandleManuallySentYouPinOfferAsync(order, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private YouPinSaleOrder? FindOrder(string orderNo)
    {
        string normalized = orderNo.Trim();
        return _service.GetState().RecentWaitDeliverOrders.FirstOrDefault(order =>
            string.Equals(order.OrderNo, normalized, StringComparison.OrdinalIgnoreCase)
            || order.OrderNos.Any(member => string.Equals(member, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static YouPinQuoteOrderProjection ProjectOrder(YouPinSaleOrder order)
    {
        YouPinSaleOrderAction action = YouPinSaleOrderActionResolver.Resolve(order);
        return new YouPinQuoteOrderProjection(
            order.OrderNo,
            order.TradeOfferId,
            string.IsNullOrWhiteSpace(order.Name) ? "未命名饰品" : order.Name,
            order.Price,
            order.OrderType,
            string.IsNullOrWhiteSpace(order.OrderStatusDesc) ? action.StatusReason : order.OrderStatusDesc,
            action.Kind.ToString(),
            action.ButtonText,
            action.StatusReason,
            action.CanRun,
            order.IsOrderGroup,
            Math.Max(1, Math.Max(order.OrderNos.Count, order.OrderItems.Count)),
            order.DetectedAt);
    }

    private static YouPinQuoteOrderCommand? ReadPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<YouPinQuoteOrderCommand>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CoreCommandResult FromCheckResult(
        YouPinSaleReminderCheckResult result,
        string correlationId,
        long version)
    {
        if (result.Ok)
            return CoreCommandResult.Success(result.Message, correlationId, version);
        if (result.Skipped)
            return CoreCommandResult.Skipped("youpin.check-skipped", result.Message, correlationId, version);
        return CoreCommandResult.Failed("youpin.check-failed", result.Message, correlationId, version);
    }

    private static CoreCommandResult FromActionResult(
        YouPinSaleActionResult result,
        string correlationId,
        long version)
    {
        if (result.Ok)
            return CoreCommandResult.Success(result.Message, correlationId, version);
        if (result.Skipped)
            return CoreCommandResult.Skipped("youpin.action-skipped", result.Message, correlationId, version);
        return CoreCommandResult.Failed("youpin.action-failed", result.Message, correlationId, version);
    }

    private static CoreCommandResult InvalidResolvedAction(
        YouPinSaleOrderAction action,
        FeatureCommand command,
        long version)
        => CoreCommandResult.NeedsUserAction(
            "youpin.action-mismatch",
            string.IsNullOrWhiteSpace(action.StatusReason) ? "当前订单状态不允许该操作。" : action.StatusReason,
            command.CorrelationId,
            version);
}
