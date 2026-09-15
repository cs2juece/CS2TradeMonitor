using System.Text.Json;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.SystemServices;

public sealed class SystemFeatureModule : ITradeMonitorCoreModule
{
    public const string QueryBinding = "QuerySystemStatus";
    public const string CheckSoftwareUpdateBinding = "CheckSoftwareUpdate";
    public const string SetDetailedDiagnosticsBinding = "SetDetailedDiagnosticsMode";

    private readonly ISoftwareUpdateChecker _softwareUpdates;
    private readonly IDiagnosticRuntimeManager _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SoftwareUpdateCheckProjection _lastUpdate;
    private DiagnosticRuntimeProjection _lastDiagnostics = new(false, null, 0, 0, "详细诊断已关闭。");
    private long _version;
    private bool _initialized;

    public SystemFeatureModule(
        ISoftwareUpdateChecker softwareUpdates,
        IDiagnosticRuntimeManager diagnostics)
    {
        _softwareUpdates = softwareUpdates ?? throw new ArgumentNullException(nameof(softwareUpdates));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _lastUpdate = SoftwareUpdateCheckProjection.NotChecked(_softwareUpdates.CurrentVersion);
    }

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding or CheckSoftwareUpdateBinding or SetDetailedDiagnosticsBinding;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized))
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;
            _lastDiagnostics = await _diagnostics.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastDiagnostics = await _diagnostics.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return new FeatureStateProjection(
                query.SemanticId,
                FeatureAvailability.Available,
                "ok",
                "系统状态已更新。",
                JsonSerializer.Serialize(new SystemFeatureProjection(_lastUpdate, _lastDiagnostics)),
                _version,
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
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(command.BindingName, QueryBinding, StringComparison.Ordinal))
            {
                return CoreCommandResult.Disabled(
                    "system.query-read-only",
                    "系统状态查询不能作为写入命令执行。",
                    command.CorrelationId,
                    _version);
            }

            if (string.Equals(command.BindingName, CheckSoftwareUpdateBinding, StringComparison.Ordinal))
            {
                _lastUpdate = await _softwareUpdates.CheckAsync(cancellationToken).ConfigureAwait(false);
                _version++;
                return _lastUpdate.Availability == SoftwareUpdateAvailability.Failed
                    ? CoreCommandResult.Failed(
                        "system.update.check-failed",
                        _lastUpdate.Message,
                        command.CorrelationId,
                        _version)
                    : CoreCommandResult.Success(_lastUpdate.Message, command.CorrelationId, _version);
            }

            if (string.Equals(command.BindingName, SetDetailedDiagnosticsBinding, StringComparison.Ordinal))
            {
                bool enabled = ReadBoolean(command.PayloadJson);
                _lastDiagnostics = await _diagnostics.SetDetailedEnabledAsync(enabled, cancellationToken)
                    .ConfigureAwait(false);
                _version++;
                return CoreCommandResult.Success(
                    _lastDiagnostics.Message,
                    command.CorrelationId,
                    _version);
            }

            return CoreCommandResult.Disabled(
                "system.binding-unavailable",
                "该系统命令尚未注册。",
                command.CorrelationId,
                _version);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);

    private static bool ReadBoolean(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            throw new ArgumentException("请提供详细诊断开关状态。");
        using JsonDocument document = JsonDocument.Parse(payloadJson);
        if (document.RootElement.TryGetProperty("Value", out JsonElement value)
            || document.RootElement.TryGetProperty("value", out value))
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
        }
        throw new ArgumentException("详细诊断开关状态无效。");
    }
}
