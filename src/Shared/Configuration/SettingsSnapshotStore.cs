using System.Text.Json;
using CS2TradeMonitor.Shared.Ports;

namespace CS2TradeMonitor.Shared.Configuration
{
    public interface ISettingsSnapshotStore
    {
        Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);

        Task<SettingsSnapshot> SaveAsync(
            Settings settings,
            long expectedVersion,
            CancellationToken cancellationToken = default);
    }

    public sealed record SettingsSnapshot(Settings Settings, long Version);

    /// <summary>
    /// Stores the complete authoritative settings document in platform secure storage.
    /// This avoids placing API keys and notification secrets in the SQLite state database.
    /// </summary>
    public sealed class SecureSettingsSnapshotStore : ISettingsSnapshotStore
    {
        private const string StorageKey = "core.settings.v1";
        private readonly ISecureValueStore _secureValues;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public SecureSettingsSnapshotStore(ISecureValueStore secureValues)
        {
            _secureValues = secureValues ?? throw new ArgumentNullException(nameof(secureValues));
        }

        public async Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task<SettingsSnapshot> SaveAsync(
            Settings settings,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SettingsSnapshot current = await LoadNoLockAsync(cancellationToken).ConfigureAwait(false);
                if (current.Version != expectedVersion)
                    throw new SettingsConcurrencyException(expectedVersion, current.Version);

                var envelope = new SettingsEnvelope(expectedVersion + 1, settings.DeepClone());
                string json = JsonSerializer.Serialize(envelope);
                await _secureValues.SetAsync(StorageKey, json, cancellationToken).ConfigureAwait(false);
                return new SettingsSnapshot(envelope.Settings.DeepClone(), envelope.Version);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<SettingsSnapshot> LoadNoLockAsync(CancellationToken cancellationToken)
        {
            string? json = await _secureValues.GetAsync(StorageKey, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new SettingsSnapshot(new Settings(), 0);

            try
            {
                SettingsEnvelope? envelope = JsonSerializer.Deserialize<SettingsEnvelope>(json);
                return envelope is null
                    ? throw new InvalidDataException("安全设置文档为空。")
                    : new SettingsSnapshot(envelope.Settings.DeepClone(), envelope.Version);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("安全设置文档无法解析。", exception);
            }
        }

        private sealed record SettingsEnvelope(long Version, Settings Settings);
    }

    public sealed class SettingsConcurrencyException : InvalidOperationException
    {
        public SettingsConcurrencyException(long expectedVersion, long actualVersion)
            : base($"Settings version mismatch. Expected {expectedVersion}, actual {actualVersion}.")
        {
            ExpectedVersion = expectedVersion;
            ActualVersion = actualVersion;
        }

        public long ExpectedVersion { get; }
        public long ActualVersion { get; }
    }
}
