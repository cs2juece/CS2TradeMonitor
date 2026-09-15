using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Shared.Configuration;

namespace CS2TradeMonitor.Infrastructure.Configuration
{
    /// <summary>
    /// Adapts the authoritative desktop settings repository to the Shared snapshot contract.
    /// Persistence, normalization, backup, and migration rules remain owned by the repository.
    /// </summary>
    public sealed class DesktopSettingsSnapshotStore : ISettingsSnapshotStore
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private long _version;

        public DesktopSettingsSnapshotStore(ISettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        }

        public async Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Settings settings = _settingsRepository.Load();
                return new SettingsSnapshot(settings.DeepClone(), _version);
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
                if (_version != expectedVersion)
                    throw new SettingsConcurrencyException(expectedVersion, _version);

                SettingsSaveResult result = _settingsRepository.Save(settings);
                if (!result.Succeeded)
                    throw new SettingsPersistenceException(result.FailureType);

                Settings persisted = _settingsRepository.Load(forceReload: true);
                long version = checked(++_version);
                return new SettingsSnapshot(persisted.DeepClone(), version);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
