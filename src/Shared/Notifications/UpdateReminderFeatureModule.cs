using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Shared.Configuration;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Shared.Notifications;

public sealed class UpdateReminderFeatureModule : ITradeMonitorCoreModule, IAutomationCyclePriority
{
    public const string QueryBinding = "QueryCs2UpdateReminder";
    public const string CheckNowBinding = "CheckCs2UpdateNow";
    public const string ResetBaselineBinding = "ResetCs2UpdateBaseline";
    public const string SavePrimaryBinding = "SavePrimaryPhoneAlertChannel";
    public const string SaveBackupBinding = "ConfigureBackupPhoneAlertChannel";
    public const string TestPrimaryBinding = "SendSyntheticPrimaryPhoneAlert";
    public const string TestBackupBinding = "SendSyntheticBackupPhoneAlert";

    private readonly ISettingsSnapshotStore _settings;
    private readonly ICs2UpdateReminderService _updates;
    private readonly IPhoneAlertDispatchService _phoneAlerts;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _version;

    public UpdateReminderFeatureModule(
        ISettingsSnapshotStore settings,
        ICs2UpdateReminderService updates,
        IPhoneAlertDispatchService phoneAlerts)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        _phoneAlerts = phoneAlerts ?? throw new ArgumentNullException(nameof(phoneAlerts));
    }

    public int AutomationCyclePriority => 80;

    public bool CanHandle(string bindingName)
        => bindingName is QueryBinding
            or CheckNowBinding
            or ResetBaselineBinding
            or SavePrimaryBinding
            or SaveBackupBinding
            or TestPrimaryBinding
            or TestBackupBinding;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<FeatureStateProjection> QueryAsync(
        FeatureStateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings normalized = snapshot.Settings.DeepClone();
        SettingsPhoneAlertNormalizer.Normalize(normalized);
        Cs2UpdateReminderProjection projection = BuildProjection(normalized);
        return new FeatureStateProjection(
            query.SemanticId,
            FeatureAvailability.Available,
            "ok",
            "更新提醒状态已更新。",
            JsonSerializer.Serialize(projection),
            Math.Max(snapshot.Version, Volatile.Read(ref _version)),
            DateTimeOffset.UtcNow);
    }

    public async Task<CoreCommandResult> ExecuteAsync(
        FeatureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.Equals(command.BindingName, QueryBinding, StringComparison.Ordinal))
        {
            return CoreCommandResult.Disabled(
                "cs2-update.query-read-only",
                "更新提醒查询不能作为写入命令执行。",
                command.CorrelationId,
                Volatile.Read(ref _version));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return command.BindingName switch
            {
                CheckNowBinding => await CheckAsync(command, resetBaseline: false, cancellationToken),
                ResetBaselineBinding => await CheckAsync(command, resetBaseline: true, cancellationToken),
                SavePrimaryBinding => await SavePrimaryAsync(command, cancellationToken),
                SaveBackupBinding => await SaveBackupAsync(command, cancellationToken),
                TestPrimaryBinding => await TestPrimaryAsync(command, cancellationToken),
                TestBackupBinding => await TestBackupAsync(command, cancellationToken),
                _ => CoreCommandResult.Disabled(
                    "cs2-update.binding-unavailable",
                    "该更新提醒命令尚未注册。",
                    command.CorrelationId,
                    Volatile.Read(ref _version))
            };
        }
        catch (ArgumentException exception)
        {
            return CoreCommandResult.NeedsUserAction(
                "cs2-update.input-invalid",
                exception.Message,
                command.CorrelationId,
                Volatile.Read(ref _version));
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
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Cs2UpdateCheckResult? result = await _updates.CheckIfDueAsync(
            snapshot.Settings.DeepClone(),
            cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return new AutomationCycleResult(
                AutomationCycleStatus.Skipped,
                "cs2-update.not-due",
                "CS2 更新提醒未到检查时间。",
                trigger.CorrelationId,
                snapshot.Version);
        }

        long version = Interlocked.Increment(ref _version);
        return new AutomationCycleResult(
            result.Success ? AutomationCycleStatus.Completed : AutomationCycleStatus.Failed,
            result.Success ? "ok" : "cs2-update.check-failed",
            result.Message,
            trigger.CorrelationId,
            Math.Max(version, snapshot.Version),
            result.NewCount);
    }

    private async Task<CoreCommandResult> CheckAsync(
        FeatureCommand command,
        bool resetBaseline,
        CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Cs2UpdateCheckResult result = await _updates.ManualCheckAsync(
            snapshot.Settings.DeepClone(),
            resetBaseline).WaitAsync(cancellationToken).ConfigureAwait(false);
        long version = Interlocked.Increment(ref _version);
        return result.Success
            ? CoreCommandResult.Success(result.Message, command.CorrelationId, Math.Max(version, snapshot.Version))
            : CoreCommandResult.Failed(
                "cs2-update.check-failed",
                result.Message,
                command.CorrelationId,
                Math.Max(version, snapshot.Version));
    }

    private async Task<CoreCommandResult> SavePrimaryAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SavePrimaryPhoneAlertRequest request = Deserialize<SavePrimaryPhoneAlertRequest>(command.PayloadJson);
        string secret = (request.Secret ?? string.Empty).Trim();
        if (!request.Clear && string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("请输入 Server酱 SendKey，或明确选择清除。", nameof(command));

        SettingsSnapshot saved = await MutateSettingsAsync(settings =>
        {
            SettingsPhoneAlertNormalizer.Normalize(settings);
            PhoneAlertChannelConfig primary = settings.PhoneAlertChannels.Single(
                channel => channel.Type == PhoneAlertChannelType.ServerChan);
            primary.Secret = request.Clear ? string.Empty : secret;
            primary.Enabled = !string.IsNullOrWhiteSpace(primary.Secret);
            primary.LastTestResult = string.Empty;
            settings.ServerChanSendKey = primary.Secret;
            settings.PhoneAlertProvider = "ServerChan";
            settings.PhoneAlertEnabled = settings.PhoneAlertChannels.Any(channel => channel.Enabled);
            settings.PhoneAlertDispatchMode = PhoneAlertDispatchMode.Failover;
        }, cancellationToken).ConfigureAwait(false);

        Interlocked.Exchange(ref _version, Math.Max(_version + 1, saved.Version));
        return CoreCommandResult.Success(
            request.Clear ? "Server酱 SendKey 已清除。" : "Server酱 SendKey 已保存到系统安全存储。",
            command.CorrelationId,
            saved.Version);
    }

    private async Task<CoreCommandResult> SaveBackupAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SaveBackupPhoneAlertRequest request = Deserialize<SaveBackupPhoneAlertRequest>(command.PayloadJson);
        if (request.Type == PhoneAlertChannelType.ServerChan
            || !Enum.IsDefined(typeof(PhoneAlertChannelType), request.Type))
        {
            throw new ArgumentException("请选择一个有效的备用手机通道。", nameof(command));
        }

        SettingsSnapshot saved = await MutateSettingsAsync(settings =>
        {
            SettingsPhoneAlertNormalizer.Normalize(settings);
            PhoneAlertChannelConfig channel = settings.PhoneAlertChannels.Single(item => item.Type == request.Type);
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
                channel.DisplayName = request.DisplayName.Trim();
            if (request.ClearSecret)
                channel.Secret = string.Empty;
            else if (!string.IsNullOrWhiteSpace(request.Secret))
                channel.Secret = request.Secret.Trim();
            channel.ServerUrl = (request.ServerUrl ?? string.Empty).Trim();
            channel.Extra = (request.Extra ?? string.Empty).Trim();
            PhoneAlertChannelDefinitionCatalog.ApplyDefaults(channel, useDisplayTitle: true);
            channel.Enabled = request.Enabled;
            if (channel.Enabled && !_phoneAlerts.IsChannelConfigured(channel))
                throw new ArgumentException("备用通道配置不完整，暂不能启用。", nameof(command));
            channel.LastTestResult = string.Empty;
            settings.PhoneAlertEnabled = settings.PhoneAlertChannels.Any(item => item.Enabled);
            settings.PhoneAlertDispatchMode = PhoneAlertDispatchMode.Failover;
        }, cancellationToken).ConfigureAwait(false);

        Interlocked.Exchange(ref _version, Math.Max(_version + 1, saved.Version));
        return CoreCommandResult.Success("备用手机通道已保存。", command.CorrelationId, saved.Version);
    }

    private async Task<CoreCommandResult> TestPrimaryAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings settings = snapshot.Settings.DeepClone();
        SettingsPhoneAlertNormalizer.Normalize(settings);
        PhoneAlertChannelConfig primary = settings.PhoneAlertChannels.Single(
            channel => channel.Type == PhoneAlertChannelType.ServerChan);
        PhoneAlertSendResult result = await _phoneAlerts.SendChannelAsync(
            primary,
            "CS2交易监控测试提醒",
            "这是一条合成测试消息，用于验证手机主通道。",
            cancellationToken).ConfigureAwait(false);
        return ToCommandResult(result, "phone-reminder.primary-test", command, snapshot.Version);
    }

    private async Task<CoreCommandResult> TestBackupAsync(
        FeatureCommand command,
        CancellationToken cancellationToken)
    {
        SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Settings settings = snapshot.Settings.DeepClone();
        SettingsPhoneAlertNormalizer.Normalize(settings);
        foreach (PhoneAlertChannelConfig channel in settings.PhoneAlertChannels)
        {
            if (channel.Type == PhoneAlertChannelType.ServerChan)
                channel.Enabled = false;
        }

        List<PhoneAlertChannelTestResult> results = await _phoneAlerts.TestAllEnabledAsync(
            settings,
            "CS2交易监控备用通道测试",
            "这是一条合成测试消息，用于验证全部已启用备用通道。",
            cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            return CoreCommandResult.NeedsUserAction(
                "phone-reminder.backup-not-configured",
                "没有已启用的备用手机通道。",
                command.CorrelationId,
                snapshot.Version);
        }

        int succeeded = results.Count(result => result.Result.Success);
        int failed = results.Count - succeeded;
        string message = $"备用链路测试完成：成功 {succeeded} 个，失败 {failed} 个。";
        return failed == 0
            ? CoreCommandResult.Success(message, command.CorrelationId, snapshot.Version)
            : CoreCommandResult.Failed(
                "phone-reminder.backup-test-failed",
                message,
                command.CorrelationId,
                snapshot.Version);
    }

    private Cs2UpdateReminderProjection BuildProjection(Settings settings)
    {
        IReadOnlyList<PhoneAlertChannelProjection> channels = settings.PhoneAlertChannels
            .OrderBy(channel => channel.Priority)
            .Select(channel =>
            {
                PhoneAlertChannelDefinition definition = PhoneAlertChannelDefinitionCatalog.Get(channel.Type);
                return new PhoneAlertChannelProjection(
                    channel.Type,
                    definition.Title,
                    channel.DisplayName,
                    channel.Enabled,
                    channel.Priority,
                    _phoneAlerts.IsChannelConfigured(channel),
                    _phoneAlerts.MaskSecret(channel),
                    channel.ServerUrl,
                    channel.Extra,
                    definition.SecretLabel,
                    definition.ShowServerField,
                    definition.ServerLabel,
                    definition.ShowExtraField,
                    definition.ExtraLabel,
                    definition.HelpUrl,
                    channel.LastTestResult);
            })
            .ToArray();

        IReadOnlyList<Cs2UpdateItemProjection> recent = _updates.RecentItems
            .Take(10)
            .Select(item => new Cs2UpdateItemProjection(
                item.Key,
                item.Source,
                item.Title,
                string.IsNullOrWhiteSpace(item.Summary) ? item.Content : item.Summary,
                Cs2UpdateReminderService.FormatTime(item.PublishedAt)))
            .ToArray();
        return new Cs2UpdateReminderProjection(
            settings.Cs2UpdateReminderEnabled,
            Math.Clamp(settings.Cs2UpdateReminderRefreshSec <= 0 ? 600 : settings.Cs2UpdateReminderRefreshSec, 60, 86400),
            settings.Cs2UpdateReminderWechatEnabled,
            settings.Cs2UpdateReminderSoundEnabled,
            string.IsNullOrWhiteSpace(settings.Cs2UpdateLastStatus) ? "未检查" : settings.Cs2UpdateLastStatus,
            Cs2UpdateReminderService.FormatTime(settings.Cs2UpdateLastCheckTime),
            string.IsNullOrWhiteSpace(settings.Cs2UpdateBaselineTitle) ? "尚未建立" : settings.Cs2UpdateBaselineTitle,
            Cs2UpdateReminderService.FormatTime(settings.Cs2UpdateBaselinePublishedAt),
            channels.Any(channel => channel.Type == PhoneAlertChannelType.ServerChan && channel.Configured),
            channels.Count(channel => channel.Type != PhoneAlertChannelType.ServerChan && channel.Enabled),
            settings.PhoneAlertDispatchMode switch
            {
                PhoneAlertDispatchMode.SendAll => "全部发送",
                PhoneAlertDispatchMode.PrimaryOnly => "仅主通道",
                _ => "失败后切换备用通道"
            },
            recent,
            channels);
    }

    private async Task<SettingsSnapshot> MutateSettingsAsync(
        Action<Settings> mutation,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            SettingsSnapshot snapshot = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            Settings next = snapshot.Settings.DeepClone();
            mutation(next);
            try
            {
                return await _settings.SaveAsync(next, snapshot.Version, cancellationToken).ConfigureAwait(false);
            }
            catch (SettingsConcurrencyException) when (attempt < 2)
            {
            }
        }

        throw new InvalidOperationException("设置在保存时持续变化，请稍后重试。");
    }

    private static T Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("请求内容为空或格式无效。", nameof(json));

    private static CoreCommandResult ToCommandResult(
        PhoneAlertSendResult result,
        string reasonPrefix,
        FeatureCommand command,
        long version)
        => result.Success
            ? CoreCommandResult.Success(result.Message, command.CorrelationId, version)
            : result.Skipped
                ? CoreCommandResult.NeedsUserAction(
                    reasonPrefix + ".not-configured",
                    result.Message,
                    command.CorrelationId,
                    version)
                : CoreCommandResult.Failed(
                    reasonPrefix + ".failed",
                    result.Message,
                    command.CorrelationId,
                    version);
}
