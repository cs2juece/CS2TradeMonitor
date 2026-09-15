using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinLandlordRentalScopeCommand(YouPinRentalScanScope Scope);
public sealed record YouPinLandlordRentalPolicyCommand(
    YouPinRentalShelfType? RentalType,
    YouPinLandlordRentalPolicy Policy);
public sealed record YouPinLandlordInventoryPolicyCommand(YouPinLandlordInventoryPolicy Policy);
public sealed record YouPinLandlordToggleCommand(bool Enabled, YouPinRentalShelfType? RentalType = null);
public sealed record YouPinLandlordRentalSelectionCommand(
    YouPinRentalShelfType? RentalType,
    YouPinLandlordSelectionScope Scope,
    bool Initialized,
    IReadOnlyList<string> SelectedAssetIds,
    IReadOnlyList<string> SelectedItemNames);
public sealed record YouPinLandlordInventorySelectionCommand(
    YouPinLandlordInventoryListMode ListMode,
    YouPinLandlordSelectionScope Scope,
    IReadOnlyList<string> SelectedAssetIds,
    IReadOnlyList<string> SelectedItemNames);

public sealed record YouPinLandlordProjection(
    YouPinLandlordPolicy Policy,
    YouPinLandlordSnapshot Snapshot,
    YouPinLandlordAuditHealth AuditHealth,
    IReadOnlyList<YouPinLandlordPlannedAction> RentalExecutionQueue,
    IReadOnlyList<YouPinLandlordPlannedAction> InventoryListingQueue);

/// <summary>
/// Presentation-independent bridge to the exact desktop landlord automation.
/// All Android reads and writes enter through the shared single-writer core.
/// </summary>
public sealed class YouPinLandlordFeatureModule : ITradeMonitorCoreModule, IAutomationCyclePriority
{
    public const string QueryBinding = "QueryYouPinLandlord";
    public const string UnifiedModeBinding = "SetLandlordPricingMode:Unified";
    public const string SeparateModeBinding = "SetLandlordPricingMode:Separate";
    public const string SetRentalEnabledBinding = "SetLandlordRentalRepriceEnabled";
    public const string SetInventoryEnabledBinding = "SetLandlordInventoryRentalEnabled";
    public const string ScanShelfBinding = "ScanYouPinRentalShelf";
    public const string ScanInventoryBinding = "ScanYouPinRentalInventory";
    public const string RunRentalOnceBinding = "RunLandlordRentalRepriceOnce";
    public const string RunInventoryOnceBinding = "RunLandlordInventoryRentalOnce";
    public const string RentalPerAssetListBinding = "ConfigureLandlordRentalPerAssetList";
    public const string RentalSameNameListBinding = "ConfigureLandlordRentalSameNameList";
    public const string InventoryWhitelistBinding = "ConfigureLandlordInventoryWhitelist";
    public const string InventoryBlacklistBinding = "ConfigureLandlordInventoryBlacklist";
    public const string RentalPricingPreferenceBinding = "ConfigureLandlordRentalPricingPreference";
    public const string InventoryPricingPreferenceBinding = "ConfigureLandlordInventoryPricingPreference";
    public const string RentalQueueBinding = "QueryLandlordRentalExecutionQueue";
    public const string InventoryQueueBinding = "QueryLandlordInventoryListingQueue";
    public const string SaveRentalPolicyBinding = "SaveLandlordRentalPolicy";
    public const string SaveInventoryPolicyBinding = "SaveLandlordInventoryPolicy";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IYouPinLandlordAutomation _automation;
    private readonly ISettingsSnapshotStore _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SettingsSnapshot? _configuredSettings;

    public YouPinLandlordFeatureModule(
        IYouPinLandlordAutomation automation,
        ISettingsSnapshotStore settings)
    {
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public int AutomationCyclePriority => 40;

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding
            or UnifiedModeBinding
            or SeparateModeBinding
            or SetRentalEnabledBinding
            or SetInventoryEnabledBinding
            or ScanShelfBinding
            or ScanInventoryBinding
            or RunRentalOnceBinding
            or RunInventoryOnceBinding
            or RentalPerAssetListBinding
            or RentalSameNameListBinding
            or InventoryWhitelistBinding
            or InventoryBlacklistBinding
            or RentalPricingPreferenceBinding
            or InventoryPricingPreferenceBinding
            or RentalQueueBinding
            or InventoryQueueBinding
            or SaveRentalPolicyBinding
            or SaveInventoryPolicyBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            YouPinLandlordProjection projection = Project();
            return new FeatureStateProjection(
                query.SemanticId,
                FeatureAvailability.Available,
                "ok",
                projection.Snapshot.Status,
                JsonSerializer.Serialize(projection, JsonOptions),
                settings.Version,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ExecuteCoreAsync(command, settings, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                return Failure(command, settings.Version, exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (_automation is not IHostDrivenYouPinLandlordAutomation hostDriven)
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettingsSnapshot settings = await ConfigureAsync(cancellationToken).ConfigureAwait(false);
            YouPinLandlordPolicy policy = _automation.GetPolicy();
            if (!policy.EffectiveFor(YouPinRentalShelfType.ZeroCd).Enabled
                && !policy.EffectiveFor(YouPinRentalShelfType.InventoryRental).Enabled
                && !policy.InventoryAutoRent.Enabled)
            {
                return new AutomationCycleResult(
                    AutomationCycleStatus.Skipped,
                    "youpin.landlord.disabled",
                    "包租公自动化未启用。",
                    trigger.CorrelationId,
                    settings.Version);
            }

            YouPinLandlordSnapshot before = _automation.GetSnapshot();
            ExecutionMarkers markers = ExecutionMarkers.From(settings.Settings);
            try
            {
                await hostDriven.RunScheduledCycleAsync(cancellationToken).ConfigureAwait(false);
                SettingsSnapshot persisted = await PersistExecutionMarkersIfChangedAsync(
                    settings,
                    markers,
                    cancellationToken).ConfigureAwait(false);
                YouPinLandlordSnapshot after = _automation.GetSnapshot();
                bool worked = after.LastCheckedAt != before.LastCheckedAt
                    || after.InventoryLastCheckedAt != before.InventoryLastCheckedAt
                    || after.LastRunId != before.LastRunId;
                return new AutomationCycleResult(
                    worked ? AutomationCycleStatus.Completed : AutomationCycleStatus.Skipped,
                    worked ? "ok" : "youpin.landlord.not-due",
                    worked ? after.Status : "包租公本周期尚未到扫描或执行时间。",
                    trigger.CorrelationId,
                    persisted.Version,
                    worked ? after.CurrentPlan.Actions.Count : 0);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                SettingsSnapshot persisted = await PersistExecutionMarkersIfChangedAsync(
                    settings,
                    markers,
                    cancellationToken).ConfigureAwait(false);
                return new AutomationCycleResult(
                    AutomationCycleStatus.Failed,
                    "youpin.landlord.cycle-failed",
                    Sanitize(exception.Message),
                    trigger.CorrelationId,
                    persisted.Version);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CoreCommandResult> ExecuteCoreAsync(
        FeatureCommand command,
        SettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        if (command.BindingName == UnifiedModeBinding)
        {
            return await ApplyPolicyAsync(
                _automation.GetPolicy() with
                {
                    PolicyVersion = _automation.GetPolicy().PolicyVersion + 1,
                    RepriceConfigurationMode = YouPinLandlordRepriceConfigurationMode.Unified
                },
                command,
                settings,
                "已切换为统一设置。",
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName == SeparateModeBinding)
        {
            return await ApplyPolicyAsync(
                _automation.GetPolicy() with
                {
                    PolicyVersion = _automation.GetPolicy().PolicyVersion + 1,
                    RepriceConfigurationMode = YouPinLandlordRepriceConfigurationMode.Separate
                },
                command,
                settings,
                "已切换为分别设置。",
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName == SetRentalEnabledBinding)
        {
            YouPinLandlordToggleCommand? payload = Deserialize<YouPinLandlordToggleCommand>(command.PayloadJson);
            if (payload is null)
                return InvalidPayload(command, settings.Version, "请提供自动改价开关状态。");
            YouPinLandlordPolicy current = _automation.GetPolicy();
            YouPinLandlordPolicy changed = ChangeRentalPolicy(
                current,
                payload.RentalType,
                policy => policy with { Enabled = payload.Enabled });
            CoreCommandResult saved = await ApplyPolicyAsync(
                changed,
                command,
                settings,
                payload.Enabled ? "租赁自动改价已开启。" : "租赁自动改价已关闭。",
                cancellationToken).ConfigureAwait(false);
            if (!payload.Enabled || !saved.IsSuccess)
                return saved;

            YouPinRentalScanScope scope = current.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified
                ? YouPinRentalScanScope.All
                : payload.RentalType == YouPinRentalShelfType.InventoryRental
                    ? YouPinRentalScanScope.InventoryRental
                    : YouPinRentalScanScope.ZeroCd;
            return await RunAsync(
                () => _automation.ScanRentalNowAsync(scope, "启用后立即扫描货架", cancellationToken),
                command,
                _configuredSettings ?? settings,
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName == SetInventoryEnabledBinding)
        {
            YouPinLandlordToggleCommand? payload = Deserialize<YouPinLandlordToggleCommand>(command.PayloadJson);
            if (payload is null)
                return InvalidPayload(command, settings.Version, "请提供库存自动出租开关状态。");
            YouPinLandlordPolicy current = _automation.GetPolicy();
            CoreCommandResult saved = await ApplyPolicyAsync(
                current with
                {
                    PolicyVersion = current.PolicyVersion + 1,
                    InventoryAutoRent = current.InventoryAutoRent with { Enabled = payload.Enabled }
                },
                command,
                settings,
                payload.Enabled ? "库存自动出租已开启。" : "库存自动出租已关闭。",
                cancellationToken).ConfigureAwait(false);
            if (!payload.Enabled || !saved.IsSuccess)
                return saved;

            return await RunAsync(
                () => _automation.ScanInventoryNowAsync("启用后立即扫描库存", cancellationToken),
                command,
                _configuredSettings ?? settings,
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName == SaveRentalPolicyBinding)
        {
            YouPinLandlordRentalPolicyCommand? payload = Deserialize<YouPinLandlordRentalPolicyCommand>(command.PayloadJson);
            if (payload?.Policy is null)
                return InvalidPayload(command, settings.Version, "请提供完整的租赁自动改价设置。");
            return await ApplyPolicyAsync(
                ChangeRentalPolicy(_automation.GetPolicy(), payload.RentalType, _ => payload.Policy),
                command,
                settings,
                "租赁自动改价设置已保存。",
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName == SaveInventoryPolicyBinding)
        {
            YouPinLandlordInventoryPolicyCommand? payload = Deserialize<YouPinLandlordInventoryPolicyCommand>(command.PayloadJson);
            if (payload?.Policy is null)
                return InvalidPayload(command, settings.Version, "请提供完整的库存自动出租设置。");
            YouPinLandlordPolicy current = _automation.GetPolicy();
            return await ApplyPolicyAsync(
                current with
                {
                    PolicyVersion = current.PolicyVersion + 1,
                    InventoryAutoRent = payload.Policy
                },
                command,
                settings,
                "库存自动出租设置已保存。",
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName is RentalPerAssetListBinding or RentalSameNameListBinding)
        {
            YouPinLandlordRentalSelectionCommand? payload = Deserialize<YouPinLandlordRentalSelectionCommand>(command.PayloadJson);
            YouPinLandlordSelectionScope scope = command.BindingName == RentalSameNameListBinding
                ? YouPinLandlordSelectionScope.SameItemName
                : YouPinLandlordSelectionScope.PerAsset;
            YouPinLandlordPolicy current = _automation.GetPolicy();
            YouPinRentalShelfType? rentalType = payload?.RentalType;
            YouPinLandlordPolicy changed = ChangeRentalPolicy(
                current,
                rentalType,
                policy => policy with
                {
                    Selection = new YouPinLandlordSelectionRule(
                        payload?.Initialized ?? policy.Selection.Initialized,
                        scope,
                        payload?.SelectedAssetIds ?? policy.Selection.SelectedAssetIds,
                        payload?.SelectedItemNames ?? policy.Selection.SelectedItemNames)
                });
            return await ApplyPolicyAsync(
                changed,
                command,
                settings,
                scope == YouPinLandlordSelectionScope.PerAsset
                    ? "已切换为逐件名单。"
                    : "已切换为同款名单。",
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName is InventoryWhitelistBinding or InventoryBlacklistBinding)
        {
            YouPinLandlordInventorySelectionCommand? payload = Deserialize<YouPinLandlordInventorySelectionCommand>(command.PayloadJson);
            YouPinLandlordInventoryListMode listMode = command.BindingName == InventoryBlacklistBinding
                ? YouPinLandlordInventoryListMode.Blacklist
                : YouPinLandlordInventoryListMode.Whitelist;
            YouPinLandlordPolicy current = _automation.GetPolicy();
            YouPinLandlordInventoryPolicy inventory = current.InventoryAutoRent with
            {
                ListMode = listMode,
                SelectionScope = payload?.Scope ?? current.InventoryAutoRent.SelectionScope,
                SelectedAssetIds = payload?.SelectedAssetIds ?? current.InventoryAutoRent.SelectedAssetIds,
                SelectedItemNames = payload?.SelectedItemNames ?? current.InventoryAutoRent.SelectedItemNames
            };
            return await ApplyPolicyAsync(
                current with
                {
                    PolicyVersion = current.PolicyVersion + 1,
                    InventoryAutoRent = inventory
                },
                command,
                settings,
                listMode == YouPinLandlordInventoryListMode.Whitelist
                    ? "库存名单已设为白名单。"
                    : "库存名单已设为黑名单。",
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName is ScanShelfBinding or RunRentalOnceBinding)
        {
            YouPinLandlordRentalScopeCommand? payload = Deserialize<YouPinLandlordRentalScopeCommand>(command.PayloadJson);
            YouPinRentalScanScope scope = payload?.Scope ?? YouPinRentalScanScope.All;
            return await RunAsync(
                command.BindingName == ScanShelfBinding
                    ? () => _automation.ScanRentalNowAsync(scope, "用户立即扫描货架", cancellationToken)
                    : () => _automation.ExecuteRentalNowAsync(scope, "用户立即执行一次租赁自动改价", cancellationToken),
                command,
                settings,
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName is ScanInventoryBinding or RunInventoryOnceBinding)
        {
            return await RunAsync(
                command.BindingName == ScanInventoryBinding
                    ? () => _automation.ScanInventoryNowAsync("用户立即扫描库存", cancellationToken)
                    : () => _automation.ExecuteInventoryNowAsync("用户立即执行一次库存自动出租", cancellationToken),
                command,
                settings,
                cancellationToken).ConfigureAwait(false);
        }

        if (command.BindingName is RentalPricingPreferenceBinding or InventoryPricingPreferenceBinding)
        {
            try
            {
                await _automation.RefreshPricingPreferenceAsync(cancellationToken).ConfigureAwait(false);
                return CoreCommandResult.Success("悠悠云端一键定价偏好已刷新。", command.CorrelationId, settings.Version);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                return Failure(command, settings.Version, exception);
            }
        }

        if (command.BindingName is RentalQueueBinding or InventoryQueueBinding)
            return CoreCommandResult.Success("执行队列已刷新。", command.CorrelationId, settings.Version);

        return CoreCommandResult.Disabled(
            "youpin.landlord.command-unavailable",
            "该包租公命令未注册。",
            command.CorrelationId,
            settings.Version);
    }

    private async Task<CoreCommandResult> RunAsync(
        Func<Task<YouPinLandlordRunResult>> run,
        FeatureCommand command,
        SettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        ExecutionMarkers markers = ExecutionMarkers.From(settings.Settings);
        try
        {
            YouPinLandlordRunResult result = await run().ConfigureAwait(false);
            SettingsSnapshot persisted = await PersistExecutionMarkersIfChangedAsync(
                settings,
                markers,
                cancellationToken).ConfigureAwait(false);
            if (result.Success)
                return CoreCommandResult.Success(result.Message, command.CorrelationId, persisted.Version);
            if (result.Skipped)
                return CoreCommandResult.Skipped("youpin.landlord.skipped", result.Message, command.CorrelationId, persisted.Version);
            return CoreCommandResult.Failed("youpin.landlord.run-failed", result.Message, command.CorrelationId, persisted.Version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            SettingsSnapshot persisted = await PersistExecutionMarkersIfChangedAsync(
                settings,
                markers,
                cancellationToken).ConfigureAwait(false);
            return Failure(command, persisted.Version, exception);
        }
    }

    private async Task<CoreCommandResult> ApplyPolicyAsync(
        YouPinLandlordPolicy policy,
        FeatureCommand command,
        SettingsSnapshot settings,
        string message,
        CancellationToken cancellationToken)
    {
        _automation.ApplyPolicy(policy);
        SettingsSnapshot saved = await _settings.SaveAsync(
            settings.Settings,
            settings.Version,
            cancellationToken).ConfigureAwait(false);
        _configuredSettings = new SettingsSnapshot(settings.Settings, saved.Version);
        return CoreCommandResult.Success(message, command.CorrelationId, saved.Version);
    }

    private async Task<SettingsSnapshot> ConfigureAsync(CancellationToken cancellationToken)
    {
        SettingsSnapshot settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (_configuredSettings?.Version == settings.Version)
            return _configuredSettings;

        _automation.Configure(settings.Settings);
        _configuredSettings = settings;
        return _configuredSettings;
    }

    private async Task<SettingsSnapshot> PersistExecutionMarkersIfChangedAsync(
        SettingsSnapshot settings,
        ExecutionMarkers before,
        CancellationToken cancellationToken)
    {
        if (before == ExecutionMarkers.From(settings.Settings))
            return settings;
        SettingsSnapshot saved = await _settings.SaveAsync(
            settings.Settings,
            settings.Version,
            cancellationToken).ConfigureAwait(false);
        _configuredSettings = new SettingsSnapshot(settings.Settings, saved.Version);
        return saved;
    }

    private YouPinLandlordProjection Project()
    {
        YouPinLandlordSnapshot snapshot = _automation.GetSnapshot();
        return new YouPinLandlordProjection(
            _automation.GetPolicy(),
            snapshot,
            _automation.GetAuditHealth(),
            snapshot.CurrentPlan.Actions
                .Where(action => action.Workflow == YouPinLandlordWorkflow.RentalReprice)
                .ToArray(),
            snapshot.CurrentPlan.Actions
                .Where(action => action.Workflow == YouPinLandlordWorkflow.InventoryAutoRent)
                .ToArray());
    }

    private static YouPinLandlordPolicy ChangeRentalPolicy(
        YouPinLandlordPolicy current,
        YouPinRentalShelfType? rentalType,
        Func<YouPinLandlordRentalPolicy, YouPinLandlordRentalPolicy> change)
    {
        YouPinLandlordPolicy changed;
        if (current.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified
            || rentalType is null)
        {
            changed = current with { UnifiedRental = change(current.UnifiedRental) };
        }
        else if (rentalType == YouPinRentalShelfType.ZeroCd)
        {
            changed = current with { ZeroCd = change(current.ZeroCd) };
        }
        else
        {
            changed = current with { InventoryRental = change(current.InventoryRental) };
        }
        return changed with { PolicyVersion = current.PolicyVersion + 1 };
    }

    private static CoreCommandResult InvalidPayload(FeatureCommand command, long version, string message)
        => CoreCommandResult.NeedsUserAction(
            "youpin.landlord.invalid-input",
            message,
            command.CorrelationId,
            version);

    private static CoreCommandResult Failure(FeatureCommand command, long version, Exception exception)
    {
        string message = Sanitize(exception.Message);
        return message.Contains("登录", StringComparison.Ordinal)
            ? CoreCommandResult.NeedsUserAction("youpin.auth-required", message, command.CorrelationId, version)
            : CoreCommandResult.Failed("youpin.landlord.failed", message, command.CorrelationId, version);
    }

    private static string Sanitize(string message)
        => YouPinMobileApiClient.Sanitize(message);

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

    private sealed record ExecutionMarkers(long ZeroCd, long InventoryRental, long InventoryAutoRent)
    {
        internal static ExecutionMarkers From(Settings settings)
            => new(
                settings.YouPinLandlordZeroCdLastExecutionUnixMilliseconds,
                settings.YouPinLandlordInventoryRentalLastExecutionUnixMilliseconds,
                settings.YouPinLandlordInventoryAutoRentLastExecutionUnixMilliseconds);
    }
}
