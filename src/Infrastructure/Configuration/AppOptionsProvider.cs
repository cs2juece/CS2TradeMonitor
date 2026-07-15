using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Options;

namespace CS2TradeMonitor.Infrastructure.Configuration
{
    public sealed class AppOptionsProvider : IAppOptionsProvider
    {
        private readonly ISettingsRepository _settingsRepository;

        public AppOptionsProvider(ISettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        }

        public AppOptionsSnapshot GetCurrent(bool forceReload = false)
        {
            Settings settings = _settingsRepository.Load(forceReload);
            return SettingsOptionsMapper.ToSnapshot(settings);
        }
    }
}
