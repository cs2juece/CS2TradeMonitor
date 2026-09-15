using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinInventoryTrendProjection(
    YouPinInventoryTrendState Trend,
    IReadOnlyList<YouPinDailyPnl> DailyPoints,
    int RefreshSeconds);

public sealed record SaveYouPinInventoryTrendRefreshCommand(int RefreshSeconds);

/// <summary>
/// Exposes the one desktop-authoritative YouPin inventory service to every UI
/// and scheduler. Protocol parsing, snapshots, valuation, alerts, retention,
/// and due-time behavior remain inside that physical shared service.
/// </summary>
public sealed class YouPinInventoryTrendFeatureModule : ITradeMonitorCoreModule, IAutomationCyclePriority
{
    public const string RefreshBinding = "RefreshYouPinInventoryTrend";
    public const string SaveRefreshBinding = "SaveYouPinInventoryTrendRefresh";

    private readonly IYouPinInventoryService _service;
    private readonly ISettingsSnapshotStore _settings;

    public int AutomationCyclePriority => 100;

    public YouPinInventoryTrendFeatureModule(
        IYouPinInventoryService service,
        ISettingsSnapshotStore settings)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool CanHandle(string bindingName)
        => bindingName is RefreshBinding or SaveRefreshBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinInventoryTrendState state = _service.GetTrendState();
        YouPinInventoryState inventory = _service.GetState();
        var projection = new YouPinInventoryTrendProjection(
            state,
            inventory.DailyPoints.OrderBy(point => point.Date).TakeLast(90).ToArray(),
            settings.Settings.YouPinInventoryRefreshSec);
        bool needsLogin = state.LastStatus.Contains("未登录", StringComparison.Ordinal);
        return new FeatureStateProjection(
            query.SemanticId,
            needsLogin ? FeatureAvailability.NeedsUserAction : FeatureAvailability.Available,
            needsLogin ? "youpin.auth-required" : "ok",
            FirstText(state.LastError, state.LastStatus, "等待读取悠悠库存。"),
            JsonSerializer.Serialize(projection),
            settings.Version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.BindingName == SaveRefreshBinding)
            return await SaveRefreshAsync(command, cancellationToken).ConfigureAwait(false);

        SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinInventoryFetchResult result = await _service.FetchNowAsync(
            useMock: false,
            cancellationToken).ConfigureAwait(false);
        return Map(result, command.CorrelationId, settings.Version);
    }

    public async Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinInventoryFetchResult result = trigger.Kind == AutomationCycleTriggerKind.UserRequested
            ? await _service.FetchNowAsync(useMock: false, cancellationToken).ConfigureAwait(false)
            : await _service.FetchIfDueAsync(cancellationToken).ConfigureAwait(false);
        CoreCommandResult mapped = Map(result, trigger.CorrelationId, settings.Version);
        AutomationCycleStatus status = mapped.Status switch
        {
            CoreCommandStatus.Success => AutomationCycleStatus.Completed,
            CoreCommandStatus.Failed => AutomationCycleStatus.Failed,
            CoreCommandStatus.Pending => AutomationCycleStatus.Pending,
            _ => AutomationCycleStatus.Skipped
        };
        return new AutomationCycleResult(
            status,
            mapped.ReasonCode,
            mapped.Message,
            trigger.CorrelationId,
            settings.Version,
            status == AutomationCycleStatus.Completed ? 1 : 0);
    }

    private async Task<SettingsSnapshot> ConfigureAsync(CancellationToken cancellationToken)
    {
        SettingsSnapshot settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _service.Configure(settings.Settings);
        return settings;
    }

    private async Task<CoreCommandResult> SaveRefreshAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SaveYouPinInventoryTrendRefreshCommand? payload = Deserialize<SaveYouPinInventoryTrendRefreshCommand>(
            command.PayloadJson);
        if (payload is null || payload.RefreshSeconds < 5)
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.inventory-trend.refresh-interval-invalid",
                "刷新间隔不能低于 5 秒。",
                command.CorrelationId);
        }

        SettingsSnapshot current = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = current.Settings.DeepClone();
        next.YouPinInventoryRefreshSec = payload.RefreshSeconds;
        SettingsSnapshot saved = await _settings.SaveAsync(next, current.Version, cancellationToken).ConfigureAwait(false);
        _service.Configure(saved.Settings);
        return CoreCommandResult.Success("库存涨跌刷新间隔已保存。", command.CorrelationId, saved.Version);
    }

    private static CoreCommandResult Map(
        YouPinInventoryFetchResult result,
        string correlationId,
        long version)
    {
        if (result.Ok)
            return CoreCommandResult.Success(result.Message, correlationId, version);
        if (result.Skipped)
        {
            return CoreCommandResult.Skipped(
                "youpin.inventory-trend.skipped",
                result.Message,
                correlationId,
                version);
        }
        if (result.Message.Contains("登录", StringComparison.Ordinal))
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.auth-required",
                result.Message,
                correlationId,
                version);
        }
        return CoreCommandResult.Failed(
            "youpin.inventory-trend.refresh-failed",
            result.Message,
            correlationId,
            version);
    }

    private static string FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
