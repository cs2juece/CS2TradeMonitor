using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinGridStrategyCommand(YouPinGridStrategy Strategy);
public sealed record YouPinGridDeleteCommand(string StrategyId);
public sealed record YouPinGridInventoryChoice(
    string Name,
    string TemplateId,
    int Quantity,
    decimal Price);

public sealed record YouPinGridProjection(
    IReadOnlyList<YouPinGridStrategySnapshot> Strategies,
    IReadOnlyList<YouPinGridInventoryChoice> InventoryChoices,
    DateTime LastRefreshAt,
    int EnabledCount,
    int TriggeredCount,
    int UnavailableCount,
    string Status);

public sealed class YouPinGridFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "QueryYouPinGrid";
    public const string RefreshBinding = "RefreshYouPinGridMarket";
    public const string CreateBinding = "CreateYouPinGridStrategy";
    public const string UpdateBinding = "UpdateYouPinGridStrategy";
    public const string DeleteBinding = "DeleteYouPinGridStrategy";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IYouPinGridTradingService _service;
    private readonly IYouPinInventoryService _inventory;
    private readonly ISettingsSnapshotStore _settings;

    public YouPinGridFeatureModule(
        IYouPinGridTradingService service,
        IYouPinInventoryService inventory,
        ISettingsSnapshotStore settings)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or RefreshBinding or CreateBinding or UpdateBinding or DeleteBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinGridProjection projection = Project(_service.GetSnapshot());
        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            "ok",
            string.IsNullOrWhiteSpace(projection.Status) ? "交易网格未刷新。" : projection.Status,
            JsonSerializer.Serialize(projection, JsonOptions),
            settings.Version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        if (command.BindingName == RefreshBinding)
        {
            try
            {
                YouPinGridRuntimeSnapshot snapshot = await _service.RefreshAsync(
                    settings.Settings,
                    cancellationToken).ConfigureAwait(false);
                return CoreCommandResult.Success(snapshot.Status, command.CorrelationId, settings.Version);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                string message = CS2TradeMonitor.Application.YouPin.YouPinMobileApiClient.Sanitize(exception.Message);
                return message.Contains("登录", StringComparison.Ordinal)
                    ? CoreCommandResult.NeedsUserAction("youpin.auth-required", message, command.CorrelationId, settings.Version)
                    : CoreCommandResult.Failed("youpin.grid.refresh-failed", message, command.CorrelationId, settings.Version);
            }
        }

        if (command.BindingName is CreateBinding or UpdateBinding)
        {
            YouPinGridStrategyCommand? payload = Deserialize<YouPinGridStrategyCommand>(command.PayloadJson);
            if (payload?.Strategy is null)
            {
                return CoreCommandResult.NeedsUserAction(
                    "youpin.grid.strategy-required",
                    "请填写完整的交易网格策略。",
                    command.CorrelationId,
                    settings.Version);
            }
            YouPinGridMutationResult result = await _service.UpsertStrategyAsync(
                payload.Strategy,
                cancellationToken).ConfigureAwait(false);
            return MapMutation(result, command.CorrelationId, settings.Version);
        }

        if (command.BindingName == DeleteBinding)
        {
            YouPinGridDeleteCommand? payload = Deserialize<YouPinGridDeleteCommand>(command.PayloadJson);
            if (string.IsNullOrWhiteSpace(payload?.StrategyId))
            {
                return CoreCommandResult.NeedsUserAction(
                    "youpin.grid.strategy-required",
                    "请先选择要删除的交易网格策略。",
                    command.CorrelationId,
                    settings.Version);
            }
            YouPinGridMutationResult result = await _service.DeleteStrategyAsync(
                payload.StrategyId,
                cancellationToken).ConfigureAwait(false);
            return MapMutation(result, command.CorrelationId, settings.Version);
        }

        return CoreCommandResult.Disabled(
            "youpin.grid.command-unavailable",
            "该交易网格命令未注册。",
            command.CorrelationId,
            settings.Version);
    }

    public async Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (_service is not IHostDrivenYouPinGridTradingService)
            return null;

        SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinGridRuntimeSnapshot before = _service.GetSnapshot();
        int enabledCount = before.Strategies.Count(row => row.Strategy.Enabled);
        if (enabledCount == 0)
        {
            return new AutomationCycleResult(
                AutomationCycleStatus.Skipped,
                "youpin.grid.disabled",
                "没有启用的悠悠交易网格策略。",
                trigger.CorrelationId,
                settings.Version);
        }

        try
        {
            YouPinGridRuntimeSnapshot snapshot = await _service.RefreshAsync(
                settings.Settings,
                cancellationToken).ConfigureAwait(false);
            return new AutomationCycleResult(
                AutomationCycleStatus.Completed,
                "ok",
                string.IsNullOrWhiteSpace(snapshot.Status)
                    ? "悠悠交易网格周期已完成。"
                    : snapshot.Status,
                trigger.CorrelationId,
                settings.Version,
                snapshot.EnabledCount);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            string message = CS2TradeMonitor.Application.YouPin.YouPinMobileApiClient.Sanitize(exception.Message);
            return new AutomationCycleResult(
                AutomationCycleStatus.Failed,
                message.Contains("登录", StringComparison.Ordinal)
                    ? "youpin.auth-required"
                    : "youpin.grid.cycle-failed",
                message,
                trigger.CorrelationId,
                settings.Version);
        }
    }

    private async Task<SettingsSnapshot> ConfigureAsync(CancellationToken cancellationToken)
    {
        SettingsSnapshot settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _service.Configure(settings.Settings);
        return settings;
    }

    private YouPinGridProjection Project(YouPinGridRuntimeSnapshot snapshot)
        => new(
            snapshot.Strategies,
            _inventory.GetState().Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(item.TemplateId))
                .GroupBy(
                    item => item.TemplateId.Trim() + "\n" + item.Name.Trim(),
                    StringComparer.Ordinal)
                .Select(group => new YouPinGridInventoryChoice(
                    group.First().Name.Trim(),
                    group.First().TemplateId.Trim(),
                    group.Sum(item => Math.Max(1, item.Quantity)),
                    (decimal)group.Where(item => item.Price > 0d)
                        .Select(item => item.Price)
                        .DefaultIfEmpty(0d)
                        .Min()))
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ToArray(),
            snapshot.LastRefreshAt,
            snapshot.EnabledCount,
            snapshot.TriggeredCount,
            snapshot.UnavailableCount,
            snapshot.Status);

    private static CoreCommandResult MapMutation(
        YouPinGridMutationResult result,
        string correlationId,
        long version)
        => result.Succeeded
            ? CoreCommandResult.Success(result.Message, correlationId, version)
            : CoreCommandResult.NeedsUserAction(
                "youpin.grid.strategy-invalid",
                result.Message,
                correlationId,
                version);

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
