using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelTabPanelHost
    {
        private readonly Control _container;
        private readonly Dictionary<string, Panel> _tabPanels;

        public MainPanelTabPanelHost(Control container, Dictionary<string, Panel> tabPanels)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _tabPanels = tabPanels ?? throw new ArgumentNullException(nameof(tabPanels));
        }

        public Panel EnsureTabPanel(string tab)
        {
            tab = MainPanelTabKeys.Normalize(tab);
            if (_tabPanels.TryGetValue(tab, out Panel? panel))
                return panel;

            panel = new Panel
            {
                BackColor = Color.Transparent
            };
            _tabPanels[tab] = panel;
            _container.Controls.Add(panel);
            return panel;
        }

        public void ShowActiveTabContent(string activeTab)
        {
            activeTab = MainPanelTabKeys.Normalize(activeTab);
            foreach (KeyValuePair<string, Panel> pair in _tabPanels)
                pair.Value.Visible = string.Equals(pair.Key, activeTab, StringComparison.OrdinalIgnoreCase);
        }

        public void ResetScrollToTop()
        {
            if (_container.IsDisposed)
                return;

            if (_container is ScrollableControl scrollable && scrollable.AutoScrollPosition != Point.Empty)
                scrollable.AutoScrollPosition = Point.Empty;
        }
    }
}
