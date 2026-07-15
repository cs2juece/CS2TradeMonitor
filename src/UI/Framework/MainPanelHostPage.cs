using CS2TradeMonitor.src.UI.SettingsPage;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class MainPanelHostPage : FrameworkSettingsHostPage<MainPanelSettingsPage>, IMainPanelSettingsPageHost
    {
        public MainPanelHostPage()
            : base(new MainPanelSettingsPage())
        {
        }

        public string ActiveTab => HostedPage.ActiveTab;

        public void SelectTab(string key)
        {
            HostedPage.SelectTab(key);
        }
    }
}
