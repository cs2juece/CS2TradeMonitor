namespace CS2TradeMonitor.Application.Abstractions
{
    public interface ISettingsRepository
    {
        string FilePath { get; }

        Settings Load(bool forceReload = false);

        SettingsSaveResult Save(Settings settings);

        void DeleteStoredSettings();
    }
}
