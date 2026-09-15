using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinStopProfitLossProjection(
    YouPinStopProfitLossState Runtime,
    YouPinInventoryTrendState Inventory,
    bool Enabled,
    bool OnlySpecifiedItems,
    double ProfitPercentThreshold,
    double LossPercentThreshold,
    int WindowMinutes,
    int CooldownMinutes,
    YouPinSaleReminderNotificationMode NotificationMode,
    string SpecifiedItems,
    string ExcludedItems,
    string ItemRulesJson);

public sealed record SaveYouPinStopProfitLossSettingsCommand(
    bool OnlySpecifiedItems,
    double ProfitPercentThreshold,
    double LossPercentThreshold,
    int WindowMinutes,
    int CooldownMinutes,
    YouPinSaleReminderNotificationMode NotificationMode,
    string SpecifiedItems,
    string ExcludedItems);

public sealed record AddYouPinStopProfitLossItemsCommand(IReadOnlyList<string> Names);

public sealed class YouPinStopProfitLossFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "QueryYouPinStopProfitLoss";
    public const string ScanBinding = "ScanYouPinStopProfitLoss";
    public const string SyntheticBinding = "RunSyntheticStopProfitLossScan";
    public const string SaveSettingsBinding = "SaveYouPinStopProfitLossSettings";
    public const string AddItemsBinding = "AddStopProfitLossInventoryItems";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IYouPinInventoryService _inventory;
    private readonly ISettingsSnapshotStore _settings;

    public YouPinStopProfitLossFeatureModule(
        IYouPinInventoryService inventory,
        ISettingsSnapshotStore settings)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or ScanBinding or SyntheticBinding or SaveSettingsBinding or AddItemsBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        Settings settings = snapshot.Settings;
        var state = new YouPinStopProfitLossProjection(
            _inventory.GetStopProfitLossState(),
            _inventory.GetTrendState(),
            settings.YouPinStopProfitLossEnabled,
            settings.YouPinStopProfitLossOnlySpecifiedItems,
            settings.YouPinStopProfitPercentThreshold,
            settings.YouPinStopLossPercentThreshold,
            settings.YouPinStopProfitLossWindowMinutes,
            settings.YouPinStopProfitLossCooldownMinutes,
            settings.YouPinStopProfitLossNotificationMode,
            settings.YouPinStopProfitLossSpecifiedItems ?? string.Empty,
            settings.YouPinStopProfitLossExcludedItems ?? string.Empty,
            settings.YouPinStopProfitLossItemRulesJson ?? string.Empty);
        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            "ok",
            FirstText(state.Runtime.LastError, state.Runtime.LastStatus, "止盈/损监控未运行。"),
            JsonSerializer.Serialize(state, JsonOptions),
            snapshot.Version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.BindingName switch
        {
            ScanBinding => await ScanAsync(command, useMock: false, cancellationToken).ConfigureAwait(false),
            SyntheticBinding => await ScanAsync(command, useMock: true, cancellationToken).ConfigureAwait(false),
            SaveSettingsBinding => await SaveSettingsAsync(command, cancellationToken).ConfigureAwait(false),
            AddItemsBinding => await AddItemsAsync(command, cancellationToken).ConfigureAwait(false),
            _ => CoreCommandResult.Disabled(
                "youpin.stop-profit-loss.command-unavailable",
                "该库存止盈/损命令未注册。",
                command.CorrelationId)
        };
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);

    private async Task<CoreCommandResult> ScanAsync(
        FeatureCommand command,
        bool useMock,
        CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
        YouPinInventoryFetchResult result = await _inventory.FetchNowAsync(useMock, cancellationToken).ConfigureAwait(false);
        if (result.Ok)
            return CoreCommandResult.Success(result.Message, command.CorrelationId, snapshot.Version);
        if (result.Skipped)
        {
            return CoreCommandResult.Skipped(
                "youpin.stop-profit-loss.scan-skipped",
                result.Message,
                command.CorrelationId,
                snapshot.Version);
        }
        return result.Message.Contains("登录", StringComparison.Ordinal) && !useMock
            ? CoreCommandResult.NeedsUserAction(
                "youpin.auth-required",
                result.Message,
                command.CorrelationId,
                snapshot.Version)
            : CoreCommandResult.Failed(
                "youpin.stop-profit-loss.scan-failed",
                result.Message,
                command.CorrelationId,
                snapshot.Version);
    }

    private async Task<CoreCommandResult> SaveSettingsAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SaveYouPinStopProfitLossSettingsCommand? payload = Deserialize<SaveYouPinStopProfitLossSettingsCommand>(
            command.PayloadJson);
        if (payload is null)
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.stop-profit-loss.settings-required",
                "请提供库存止盈/损设置。",
                command.CorrelationId);
        }

        SettingsSnapshot current = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = current.Settings.DeepClone();
        ApplySettings(next, payload);
        SettingsSnapshot saved = await _settings.SaveAsync(next, current.Version, cancellationToken).ConfigureAwait(false);
        _inventory.Configure(saved.Settings);
        return CoreCommandResult.Success("库存止盈/损设置已保存。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> AddItemsAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        AddYouPinStopProfitLossItemsCommand? payload = Deserialize<AddYouPinStopProfitLossItemsCommand>(
            command.PayloadJson);
        string[] additions = NormalizeItems(payload?.Names ?? []);
        if (additions.Length == 0)
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.stop-profit-loss.items-required",
                "请至少选择一个真实库存饰品。",
                command.CorrelationId);
        }

        SettingsSnapshot current = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings next = current.Settings.DeepClone();
        next.YouPinStopProfitLossSpecifiedItems = string.Join(", ", NormalizeItems(
            SplitItems(next.YouPinStopProfitLossSpecifiedItems).Concat(additions)));
        SettingsSnapshot saved = await _settings.SaveAsync(next, current.Version, cancellationToken).ConfigureAwait(false);
        _inventory.Configure(saved.Settings);
        return CoreCommandResult.Success($"已加入 {additions.Length} 个库存单品。", command.CorrelationId, saved.Version);
    }

    private async Task<SettingsSnapshot> ConfigureAsync(CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _inventory.Configure(snapshot.Settings);
        return snapshot;
    }

    private static void ApplySettings(Settings settings, SaveYouPinStopProfitLossSettingsCommand payload)
    {
        settings.YouPinStopProfitLossOnlySpecifiedItems = payload.OnlySpecifiedItems;
        settings.YouPinStopProfitPercentThreshold = Math.Clamp(payload.ProfitPercentThreshold, 1, 100);
        settings.YouPinStopLossPercentThreshold = Math.Clamp(payload.LossPercentThreshold, 1, 100);
        settings.YouPinStopProfitLossWindowMinutes = Math.Clamp(payload.WindowMinutes, 5, 10080);
        settings.YouPinStopProfitLossCooldownMinutes = Math.Clamp(payload.CooldownMinutes, 1, 1440);
        settings.YouPinStopProfitLossNotificationMode = payload.NotificationMode;
        settings.YouPinStopProfitLossSpecifiedItems = string.Join(", ", NormalizeItems(SplitItems(payload.SpecifiedItems)));
        settings.YouPinStopProfitLossExcludedItems = string.Join(", ", NormalizeItems(SplitItems(payload.ExcludedItems)));
    }

    private static IEnumerable<string> SplitItems(string? value)
        => (value ?? string.Empty)
            .Replace('，', ',')
            .Replace('；', ',')
            .Replace('、', ',')
            .Replace(';', ',')
            .Split([',', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static string[] NormalizeItems(IEnumerable<string> values)
        => values.Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    private static string FirstText(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
