using CS2TradeMonitor.src.Core.Actions;

namespace CS2TradeMonitor.src.Core
{
    /// <summary>
    /// Keeps the process-wide settings reference stable while allowing persisted values to refresh.
    /// </summary>
    internal sealed class SettingsInstanceCoordinator
    {
        private readonly object _gate = new();
        private Settings? _instance;

        public Settings Load(Func<Settings> loadFromPersistence, bool forceReload)
        {
            ArgumentNullException.ThrowIfNull(loadFromPersistence);

            lock (_gate)
            {
                if (_instance == null)
                {
                    _instance = loadFromPersistence();
                }
                else if (forceReload)
                {
                    SettingsChanger.Merge(_instance, loadFromPersistence());
                }

                return _instance;
            }
        }
    }
}
