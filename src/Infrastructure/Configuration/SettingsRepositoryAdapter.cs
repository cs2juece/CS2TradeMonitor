using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Infrastructure.Configuration
{
    public sealed class SettingsRepositoryAdapter : ISettingsRepository
    {
        public string FilePath => SettingsHelper.FilePath;

        public Settings Load(bool forceReload = false)
            // 仓储适配器是 Settings 持久化边界，这里的读取是刻意保留的唯一职责。
            => Settings.Load(forceReload);

        public SettingsSaveResult Save(Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return settings.Save();
        }

        public void DeleteStoredSettings()
            => SettingsHelper.DeleteStoredSettings();
    }
}
