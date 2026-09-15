using System.Reflection;
using System.Text.Json;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Shared.Contracts;
using CS2TradeMonitor.Shared.Core;

namespace CS2TradeMonitor.Shared.Configuration
{
    public sealed class SettingsFeatureModule : ITradeMonitorCoreModule
    {
        public const string InterfaceSettingsQueryBinding = "QueryInterfaceSettings";
        public const string HorizontalModeBinding = "SetInterfaceHorizontalMode";
        public const string TaskbarStylePresetBinding = "ApplyTaskbarStylePreset";
        public const string TaskbarPresetBinding = "ApplyTaskbarPreset";
        public const string ResetSettingsBinding = "ResetSettingsToDefaults";

        private const string SettingPrefix = "SetSetting:";
        private const string InvertedSettingPrefix = "SetInvertedSetting:";
        private const string AutoTradePrefix = "SetAutoTradeRule:";
        private static readonly IReadOnlyDictionary<string, string> AutoTradePropertyNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [nameof(SteamAutoTradeSettings.AcceptPureIncomingEnabled)] =
                    nameof(Settings.SteamAutoTradeAcceptPureIncomingEnabled),
                [nameof(SteamAutoTradeSettings.AcceptYouPinPurchaseEnabled)] =
                    nameof(Settings.SteamAutoTradeAcceptYouPinPurchaseEnabled),
                [nameof(SteamAutoTradeSettings.SendYouPinSaleEnabled)] =
                    nameof(Settings.SteamAutoTradeSendYouPinSaleEnabled),
                [nameof(SteamAutoTradeSettings.SendYouPinRentalEnabled)] =
                    nameof(Settings.SteamAutoTradeSendYouPinRentalEnabled)
            };

        private readonly ISettingsSnapshotStore _store;
        private readonly ISettingsPlatformBridge? _platformBridge;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Settings _settings = new();
        private long _version;
        private bool _initialized;

        public SettingsFeatureModule(ISettingsSnapshotStore store)
            : this(store, null)
        {
        }

        public SettingsFeatureModule(
            ISettingsSnapshotStore store,
            ISettingsPlatformBridge? platformBridge)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _platformBridge = platformBridge;
        }

        public bool CanHandle(string bindingName)
            => string.Equals(bindingName, InterfaceSettingsQueryBinding, StringComparison.Ordinal)
                || string.Equals(bindingName, HorizontalModeBinding, StringComparison.Ordinal)
                || string.Equals(bindingName, TaskbarStylePresetBinding, StringComparison.Ordinal)
                || string.Equals(bindingName, TaskbarPresetBinding, StringComparison.Ordinal)
                || string.Equals(bindingName, ResetSettingsBinding, StringComparison.Ordinal)
                || ResolveSupportedProperty(bindingName) is not null;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _initialized))
                return;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized)
                    return;

                SettingsSnapshot snapshot = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                _settings = snapshot.Settings.DeepClone();
                _version = snapshot.Version;
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
                SettingsSnapshot latest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (latest.Version != _version)
                {
                    _settings = latest.Settings.DeepClone();
                    _version = latest.Version;
                }

                if (string.Equals(query.BindingName, InterfaceSettingsQueryBinding, StringComparison.Ordinal))
                {
                    return new FeatureStateProjection(
                        query.SemanticId,
                        FeatureAvailability.Available,
                        "ok",
                        "界面设置已读取。",
                        JsonSerializer.Serialize(InterfaceSettingsProjection.FromSettings(_settings)),
                        _version,
                        DateTimeOffset.UtcNow);
                }

                if (string.Equals(query.BindingName, ResetSettingsBinding, StringComparison.Ordinal))
                {
                    return new FeatureStateProjection(
                        query.SemanticId,
                        FeatureAvailability.Available,
                        "settings.reset-confirmation-required",
                        "恢复默认设置需要再次确认。",
                        "{}",
                        _version,
                        DateTimeOffset.UtcNow);
                }

                PropertyInfo property = ResolveSupportedProperty(query.BindingName)
                    ?? throw new InvalidOperationException("Settings module received an unsupported binding.");
                object? value = property.GetValue(_settings);
                if (query.BindingName.StartsWith(InvertedSettingPrefix, StringComparison.Ordinal))
                    value = !(bool)(value ?? false);

                return new FeatureStateProjection(
                    query.SemanticId,
                    FeatureAvailability.Available,
                    "ok",
                    value is bool boolean
                        ? boolean ? "已开启，修改后立即保存。" : "已关闭，修改后立即保存。"
                        : "当前设置已读取，修改后立即保存。",
                    JsonSerializer.Serialize(new SettingFeatureValue(value)),
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
            if (string.Equals(command.BindingName, InterfaceSettingsQueryBinding, StringComparison.Ordinal))
            {
                return CoreCommandResult.Disabled(
                    "settings.query-read-only",
                    "界面设置查询不能作为写入命令执行。",
                    command.CorrelationId,
                    _version);
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SettingsSnapshot latest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (latest.Version != _version)
                {
                    _settings = latest.Settings.DeepClone();
                    _version = latest.Version;
                }

                Settings previous = _settings.DeepClone();
                Settings next = _settings.DeepClone();
                IReadOnlyCollection<string> changedProperties;
                try
                {
                    changedProperties = string.Equals(
                        command.BindingName,
                        ResetSettingsBinding,
                        StringComparison.Ordinal)
                        ? ResetToDefaults(next)
                        : ApplyCommand(next, command);
                }
                catch (ArgumentException exception)
                {
                    return CoreCommandResult.NeedsUserAction(
                        "settings.value-invalid",
                        exception.Message,
                        command.CorrelationId,
                        _version);
                }

                SettingsSnapshot saved;
                try
                {
                    saved = await _store.SaveAsync(next, _version, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SettingsConcurrencyException)
                {
                    latest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                    previous = latest.Settings.DeepClone();
                    next = latest.Settings.DeepClone();
                    changedProperties = string.Equals(
                        command.BindingName,
                        ResetSettingsBinding,
                        StringComparison.Ordinal)
                        ? ResetToDefaults(next)
                        : ApplyCommand(next, command);
                    saved = await _store.SaveAsync(next, latest.Version, cancellationToken)
                        .ConfigureAwait(false);
                }
                _settings = saved.Settings.DeepClone();
                _version = saved.Version;
                if (_platformBridge is not null)
                {
                    SettingsPlatformApplyResult platformResult = await _platformBridge.ApplyAsync(
                        previous,
                        _settings.DeepClone(),
                        changedProperties,
                        cancellationToken).ConfigureAwait(false);
                    if (platformResult.RequiresUserAction)
                    {
                        return CoreCommandResult.NeedsUserAction(
                            platformResult.ReasonCode,
                            platformResult.Message,
                            command.CorrelationId,
                            _version);
                    }
                    if (!platformResult.Applied)
                    {
                        return CoreCommandResult.Failed(
                            platformResult.ReasonCode,
                            platformResult.Message,
                            command.CorrelationId,
                            _version);
                    }
                }
                return CoreCommandResult.Success(
                    "已保存并应用。",
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

        private static PropertyInfo? ResolveSupportedProperty(string bindingName)
        {
            string propertyName;
            if (bindingName.StartsWith(SettingPrefix, StringComparison.Ordinal))
            {
                propertyName = bindingName[SettingPrefix.Length..].Split('=', 2)[0];
            }
            else if (bindingName.StartsWith(InvertedSettingPrefix, StringComparison.Ordinal))
            {
                propertyName = bindingName[InvertedSettingPrefix.Length..].Split('=', 2)[0];
            }
            else if (bindingName.StartsWith(AutoTradePrefix, StringComparison.Ordinal)
                && AutoTradePropertyNames.TryGetValue(
                    bindingName[AutoTradePrefix.Length..],
                    out string? mappedProperty))
            {
                propertyName = mappedProperty;
            }
            else
            {
                return null;
            }

            PropertyInfo? property = typeof(Settings).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return property is not null
                && IsSupportedPropertyType(property.PropertyType)
                && (!bindingName.StartsWith(InvertedSettingPrefix, StringComparison.Ordinal)
                    || property.PropertyType == typeof(bool))
                && property.CanRead
                && property.CanWrite
                ? property
                : null;
        }

        private static bool IsSupportedPropertyType(Type type)
            => type == typeof(bool)
                || type == typeof(int)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(string);

        private static IReadOnlyCollection<string> ApplyCommand(Settings settings, FeatureCommand command)
        {
            if (string.Equals(command.BindingName, HorizontalModeBinding, StringComparison.Ordinal))
            {
                bool horizontal = ReadRequiredValue<bool>(command);
                settings.HorizontalMode = horizontal;
                settings.HorizontalSingleLine = horizontal;
                return [nameof(Settings.HorizontalMode), nameof(Settings.HorizontalSingleLine)];
            }

            if (string.Equals(command.BindingName, TaskbarStylePresetBinding, StringComparison.Ordinal))
            {
                bool bold = ReadRequiredValue<bool>(command);
                return ApplyAssignments(settings, InterfaceSettingsRules.BuildTaskbarStylePreset(bold));
            }

            if (string.Equals(command.BindingName, TaskbarPresetBinding, StringComparison.Ordinal))
            {
                int preset = ReadRequiredValue<int>(command);
                IReadOnlyList<InterfaceSettingAssignment> assignments = InterfaceSettingsRules.BuildTaskbarPreset(preset);
                if (assignments.Count == 0)
                    throw new ArgumentException("请选择有效的显示预设。");
                return ApplyAssignments(settings, assignments);
            }

            PropertyInfo property = ResolveSupportedProperty(command.BindingName)
                ?? throw new ArgumentException("该设置项暂不支持。");
            object value = ReadRequiredValue(command, property.PropertyType);
            if (command.BindingName.StartsWith(InvertedSettingPrefix, StringComparison.Ordinal))
                value = !(bool)value;
            value = InterfaceSettingsRules.NormalizeValue(property.Name, value);
            property.SetValue(settings, value);
            if (command.BindingName.StartsWith(AutoTradePrefix, StringComparison.Ordinal))
            {
                SteamAutoTradeSettings normalized = SteamAutoTradeSettingsPersistence.ReadFrom(settings);
                SteamAutoTradeSettingsPersistence.ApplyTo(settings, normalized);
            }
            return [property.Name];
        }

        private static IReadOnlyCollection<string> ApplyAssignments(
            Settings settings,
            IReadOnlyList<InterfaceSettingAssignment> assignments)
        {
            var changed = new List<string>(assignments.Count);
            foreach (InterfaceSettingAssignment assignment in assignments)
            {
                PropertyInfo property = typeof(Settings).GetProperty(
                        assignment.Key,
                        BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new ArgumentException($"设置项 {assignment.Key} 不存在。");
                object normalized = InterfaceSettingsRules.NormalizeValue(property.Name, assignment.Value);
                property.SetValue(settings, ConvertToType(normalized, property.PropertyType));
                changed.Add(property.Name);
            }
            return changed;
        }

        private static IReadOnlyCollection<string> ResetToDefaults(Settings target)
        {
            var defaults = new Settings();
            PropertyInfo[] properties = typeof(Settings).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                .ToArray();
            foreach (PropertyInfo property in properties)
                property.SetValue(target, property.GetValue(defaults));
            return properties.Select(property => property.Name).ToArray();
        }

        private static T ReadRequiredValue<T>(FeatureCommand command)
            => (T)ReadRequiredValue(command, typeof(T));

        private static object ReadRequiredValue(FeatureCommand command, Type targetType)
        {
            int literalIndex = command.BindingName.IndexOf('=');
            if (literalIndex >= 0)
            {
                string literal = command.BindingName[(literalIndex + 1)..];
                try
                {
                    return ConvertToType(literal, targetType);
                }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
                {
                    throw new ArgumentException("设置值格式无效。");
                }
            }

            if (string.IsNullOrWhiteSpace(command.PayloadJson))
                throw new ArgumentException("请提供要保存的设置值。");

            try
            {
                using JsonDocument document = JsonDocument.Parse(command.PayloadJson);
                if (!document.RootElement.TryGetProperty("Value", out JsonElement value)
                    && !document.RootElement.TryGetProperty("value", out value))
                {
                    throw new ArgumentException("请提供要保存的设置值。");
                }
                object? deserialized = value.Deserialize(targetType);
                return deserialized ?? throw new ArgumentException("设置值不能为空。");
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("设置值格式无效。", exception);
            }
        }

        private static object ConvertToType(object value, Type targetType)
        {
            if (targetType.IsInstanceOfType(value))
                return value;
            return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed record SettingFeatureValue(object? Value);
    }
}
