using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Trading;

public sealed record YouPinAuthProjection(
    bool HasCredential,
    string NickName,
    DateTime SavedAt,
    string Source,
    string DeviceTokenPreview,
    string Status,
    string Error);

/// <summary>
/// Exposes the physical desktop-authoritative YouPin authentication service to
/// every UI host without copying its protocol, fallback or credential rules.
/// </summary>
public sealed class YouPinAuthFeatureModule : ITradeMonitorCoreModule
{
    public const string ValidateBinding = "ValidateYouPinSession";
    public const string ClearBinding = "ClearYouPinCredentials";
    private readonly IYouPinAuthService _auth;
    private readonly ISettingsSnapshotStore _settings;

    public YouPinAuthFeatureModule(IYouPinAuthService auth, ISettingsSnapshotStore settings)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool CanHandle(string bindingName)
        => string.Equals(bindingName, ValidateBinding, StringComparison.Ordinal)
            || string.Equals(bindingName, ClearBinding, StringComparison.Ordinal);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        YouPinAuthState state = _auth.GetState(snapshot.Settings);
        var projection = new YouPinAuthProjection(
            state.HasCredential,
            state.NickName,
            state.SavedAt,
            state.Source,
            state.DeviceTokenPreview,
            state.Status,
            state.Error);
        FeatureAvailability availability = state.HasCredential
            ? FeatureAvailability.Available
            : FeatureAvailability.NeedsUserAction;
        string message = state.HasCredential
            ? (string.IsNullOrWhiteSpace(state.NickName) ? "悠悠登录凭据已保存。" : $"悠悠账户 {state.NickName} 已连接。")
            : "请先完成悠悠登录。";

        return new FeatureStateProjection(
            query.SemanticId,
            availability,
            state.HasCredential ? "ok" : "youpin.auth-required",
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
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(command.BindingName, ValidateBinding, StringComparison.Ordinal))
        {
            YouPinLoginResult result = await _auth.ValidateCurrentAsync(snapshot.Settings).ConfigureAwait(false);
            return result.Ok
                ? CoreCommandResult.Success(result.Message, command.CorrelationId, snapshot.Version)
                : CoreCommandResult.NeedsUserAction(
                    "youpin.auth-invalid",
                    result.Message,
                    command.CorrelationId,
                    snapshot.Version);
        }

        if (string.Equals(command.BindingName, ClearBinding, StringComparison.Ordinal))
        {
            _auth.ClearCredential(snapshot.Settings, clearLegacy: true);
            return CoreCommandResult.Success("悠悠登录凭据已清除。", command.CorrelationId, snapshot.Version);
        }

        return CoreCommandResult.Disabled(
            "youpin.auth-command-unavailable",
            "该悠悠登录命令未注册。",
            command.CorrelationId,
            snapshot.Version);
    }

    public Task<AutomationCycleResult?> RunCycleAsync(
        AutomationCycleTrigger trigger,
        CancellationToken cancellationToken = default)
        => Task.FromResult<AutomationCycleResult?>(null);
}
