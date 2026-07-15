using System;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// PoC lifecycle contract for pages hosted by PageHost.
    /// </summary>
    public interface IUiPage : IDisposable
    {
        void Initialize(SettingsStore settingsStore);

        void Activate();

        void Deactivate();

        void Save();
    }
}
