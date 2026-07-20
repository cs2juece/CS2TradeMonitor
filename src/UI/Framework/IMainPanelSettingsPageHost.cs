namespace CS2TradeMonitor.src.UI.Framework
{
    public interface IMainPanelSettingsPageHost
    {
        string ActiveTab { get; }
        bool IsHandleCreated { get; }
        bool IsDisposed { get; }
        bool Visible { get; }
        void SelectTab(string key);
    }
}
