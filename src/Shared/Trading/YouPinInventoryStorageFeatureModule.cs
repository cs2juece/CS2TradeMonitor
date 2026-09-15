using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

/// <summary>
/// Hosts the desktop-authoritative YouPin inventory storage service. The UI
/// chooses a view and assets; eligibility, duplicate suppression, write gates,
/// and read-back confirmation remain inside the one shared service.
/// </summary>
public sealed class YouPinInventoryStorageFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "QueryYouPinInventoryStorage";
    public const string StoreBinding = "StoreSelectedYouPinInventory";
    public const string TakeOutBinding = "TakeOutSelectedYouPinInventory";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IYouPinInventoryStorageService _service;
    private readonly ISettingsSnapshotStore _settings;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private YouPinInventoryStorageProjection _state = EmptyState();
    private long _version;

    public YouPinInventoryStorageFeatureModule(
        IYouPinInventoryStorageService service,
        ISettingsSnapshotStore settings)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or StoreBinding or TakeOutBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        YouPinInventoryStorageProjection state;
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            state = _state;
        }
        finally
        {
            _stateGate.Release();
        }

        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            "ok",
            string.IsNullOrWhiteSpace(state.Error) ? state.Message : state.Error,
            JsonSerializer.Serialize(state, JsonOptions),
            _version,
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.BindingName switch
        {
            QueryBinding => await RefreshAsync(command, cancellationToken).ConfigureAwait(false),
            StoreBinding => await TransferAsync(
                command,
                YouPinInventoryStorageDirection.Store,
                cancellationToken).ConfigureAwait(false),
            TakeOutBinding => await TransferAsync(
                command,
                YouPinInventoryStorageDirection.TakeOut,
                cancellationToken).ConfigureAwait(false),
            _ => CoreCommandResult.Disabled(
                "youpin.inventory-storage.command-unavailable",
                "该悠悠库存存取命令未注册。",
                command.CorrelationId,
                _version)
        };
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);

    private async Task<CoreCommandResult> RefreshAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        YouPinInventoryStorageRefreshCommand request = Deserialize<YouPinInventoryStorageRefreshCommand>(
            command.PayloadJson) ?? await CurrentRefreshRequestAsync(cancellationToken).ConfigureAwait(false);
        SettingsSnapshot settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _version = Math.Max(_version, settings.Version);
        await SetRequestedViewAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            YouPinInventoryStorageProjection next = request.Direction == YouPinInventoryStorageDirection.Store
                ? await LoadStoreAsync(settings.Settings, cancellationToken).ConfigureAwait(false)
                : await LoadTakeOutAsync(settings.Settings, request, cancellationToken).ConfigureAwait(false);
            await SetStateAsync(next, cancellationToken).ConfigureAwait(false);
            return CoreCommandResult.Success(next.Message, command.CorrelationId, _version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (YouPinInventoryStorageQueryPendingException exception)
        {
            await SetErrorAsync(exception.Message, cancellationToken).ConfigureAwait(false);
            return CoreCommandResult.Pending(
                "youpin.inventory-storage.query-pending",
                exception.Message,
                command.CorrelationId,
                _version);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string message = SafeMessage(exception);
            await SetErrorAsync(message, cancellationToken).ConfigureAwait(false);
            return message.Contains("登录", StringComparison.Ordinal)
                ? CoreCommandResult.NeedsUserAction(
                    "youpin.auth-required",
                    message,
                    command.CorrelationId,
                    _version)
                : CoreCommandResult.Failed(
                    "youpin.inventory-storage.refresh-failed",
                    message,
                    command.CorrelationId,
                    _version);
        }
    }

    private async Task<CoreCommandResult> TransferAsync(
        FeatureCommand command,
        YouPinInventoryStorageDirection expectedDirection,
        CancellationToken cancellationToken)
    {
        YouPinInventoryStorageTransferCommand? request = Deserialize<YouPinInventoryStorageTransferCommand>(
            command.PayloadJson);
        if (request is null || request.Direction != expectedDirection)
        {
            return CoreCommandResult.NeedsUserAction(
                "youpin.inventory-storage.selection-required",
                "请选择存储单元和要操作的真实饰品。",
                command.CorrelationId,
                _version);
        }

        SettingsSnapshot settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        _version = Math.Max(_version, settings.Version);
        try
        {
            YouPinInventoryStorageTransferResult result = await _service.ExecuteAsync(
                settings.Settings,
                request,
                cancellationToken).ConfigureAwait(false);
            if (result.RefreshedState is not null)
            {
                YouPinInventoryStorageProjection current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
                await SetStateAsync(Project(
                    expectedDirection,
                    request.StorageAssetId,
                    result.RefreshedState,
                    expectedDirection == YouPinInventoryStorageDirection.TakeOut ? current.Units : result.RefreshedState.Units,
                    result.Message,
                    writePending: false), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SetTransferMessageAsync(
                    result.Message,
                    result.Status == YouPinInventoryStorageTransferStatus.AcceptedPending,
                    cancellationToken).ConfigureAwait(false);
            }

            return result.Status switch
            {
                YouPinInventoryStorageTransferStatus.Confirmed => CoreCommandResult.Success(
                    result.Message,
                    command.CorrelationId,
                    _version),
                YouPinInventoryStorageTransferStatus.AcceptedPending => CoreCommandResult.Pending(
                    "youpin.inventory-storage.accepted-pending",
                    result.Message,
                    command.CorrelationId,
                    _version),
                _ => CoreCommandResult.NeedsUserAction(
                    "youpin.inventory-storage.rejected",
                    result.Message,
                    command.CorrelationId,
                    _version)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string message = SafeMessage(exception);
            await SetErrorAsync(message, cancellationToken).ConfigureAwait(false);
            return CoreCommandResult.Failed(
                "youpin.inventory-storage.transfer-failed",
                message,
                command.CorrelationId,
                _version);
        }
    }

    private async Task<YouPinInventoryStorageProjection> LoadStoreAsync(
        Settings settings,
        CancellationToken cancellationToken)
    {
        YouPinInventoryStorageViewState state = await _service.LoadAsync(
            settings,
            new YouPinInventoryStorageQuery(YouPinInventoryStorageView.Storable),
            cancellationToken).ConfigureAwait(false);
        string selectedUnit = state.Units.FirstOrDefault()?.StorageAssetId ?? string.Empty;
        return Project(
            YouPinInventoryStorageDirection.Store,
            selectedUnit,
            state,
            state.Units,
            state.Message,
            writePending: false);
    }

    private async Task<YouPinInventoryStorageProjection> LoadTakeOutAsync(
        Settings settings,
        YouPinInventoryStorageRefreshCommand request,
        CancellationToken cancellationToken)
    {
        YouPinInventoryStorageProjection current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<YouPinInventoryStorageUnit> units = current.Direction == YouPinInventoryStorageDirection.TakeOut
            ? current.Units
            : [];
        YouPinInventoryStorageViewState? unitsState = null;
        if (request.ReloadUnits || units.Count == 0)
        {
            unitsState = await _service.LoadAsync(
                settings,
                new YouPinInventoryStorageQuery(YouPinInventoryStorageView.StoredUnits),
                cancellationToken).ConfigureAwait(false);
            units = unitsState.Units;
        }

        string selectedUnit = ResolveUnit(units, request.StorageAssetId);
        if (selectedUnit.Length == 0)
        {
            YouPinInventoryStorageViewState empty = unitsState
                ?? YouPinInventoryStorageViewState.Empty(
                    new YouPinInventoryStorageQuery(YouPinInventoryStorageView.StoredUnits),
                    "请选择一个存储单元以查看其中的饰品。");
            return Project(
                YouPinInventoryStorageDirection.TakeOut,
                string.Empty,
                empty,
                units,
                empty.Message,
                writePending: false);
        }

        YouPinInventoryStorageViewState items = await _service.LoadAsync(
            settings,
            new YouPinInventoryStorageQuery(YouPinInventoryStorageView.StoredItems, selectedUnit),
            cancellationToken).ConfigureAwait(false);
        return Project(
            YouPinInventoryStorageDirection.TakeOut,
            selectedUnit,
            items,
            units,
            items.Message,
            writePending: false);
    }

    private async Task<YouPinInventoryStorageRefreshCommand> CurrentRefreshRequestAsync(
        CancellationToken cancellationToken)
    {
        YouPinInventoryStorageProjection state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        return new YouPinInventoryStorageRefreshCommand(
            state.Direction,
            state.StorageAssetId,
            ReloadUnits: true);
    }

    private async Task<YouPinInventoryStorageProjection> GetStateAsync(CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _state;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task SetStateAsync(
        YouPinInventoryStorageProjection state,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = state;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task SetErrorAsync(string error, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = _state with { Error = error };
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task SetRequestedViewAsync(
        YouPinInventoryStorageRefreshCommand request,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool directionChanged = _state.Direction != request.Direction;
            bool unitChanged = !string.Equals(
                _state.StorageAssetId,
                request.StorageAssetId,
                StringComparison.Ordinal);
            _state = _state with
            {
                Direction = request.Direction,
                StorageAssetId = request.StorageAssetId,
                Items = directionChanged || unitChanged ? [] : _state.Items,
                Units = directionChanged ? [] : _state.Units,
                Message = "正在读取悠悠库存…",
                Error = string.Empty
            };
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task SetTransferMessageAsync(
        string message,
        bool writePending,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = _state with
            {
                Message = message,
                Error = string.Empty,
                WritePending = writePending
            };
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static YouPinInventoryStorageProjection Project(
        YouPinInventoryStorageDirection direction,
        string storageAssetId,
        YouPinInventoryStorageViewState state,
        IReadOnlyList<YouPinInventoryStorageUnit> units,
        string message,
        bool writePending)
        => new(
            direction,
            storageAssetId,
            state.Access,
            state.Items,
            units,
            message,
            string.Empty,
            writePending,
            state.RefreshedAt);

    private static YouPinInventoryStorageProjection EmptyState()
        => new(
            YouPinInventoryStorageDirection.Store,
            string.Empty,
            YouPinInventoryStorageAccess.Empty,
            [],
            [],
            "等待读取悠悠库存。",
            string.Empty,
            false,
            DateTime.MinValue);

    private static string ResolveUnit(
        IReadOnlyList<YouPinInventoryStorageUnit> units,
        string preferredStorageAssetId)
        => units.FirstOrDefault(unit => string.Equals(
                unit.StorageAssetId,
                preferredStorageAssetId,
                StringComparison.Ordinal))?.StorageAssetId
            ?? units.FirstOrDefault()?.StorageAssetId
            ?? string.Empty;

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

    private static string SafeMessage(Exception exception)
    {
        string message = YouPinMobileApiClient.Sanitize(exception.Message);
        return string.IsNullOrWhiteSpace(message) ? "悠悠库存存取请求失败。" : message;
    }
}
