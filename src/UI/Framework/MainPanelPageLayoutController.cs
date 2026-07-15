using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelPageLayoutController
    {
        private readonly Control _owner;
        private readonly Panel _container;
        private readonly MainPanelPreviewController _previewController;
        private readonly IReadOnlyDictionary<string, Panel> _tabPanels;
        private readonly Func<string> _getActiveTab;
        private readonly Func<MainPanelTabBar?> _getTabBar;
        private readonly Func<bool> _canRun;
        private readonly Action<ScrollableControl> _hideHorizontalScroll;
        private Panel? _scrollSentinel;
        private bool _layoutQueued;
        private bool _inLayout;
        private bool _dirty = true;
        private int _lastWidth = -1;
        private string? _lastTab;

        public MainPanelPageLayoutController(
            Control owner,
            Panel container,
            MainPanelPreviewController previewController,
            IReadOnlyDictionary<string, Panel> tabPanels,
            Func<string> getActiveTab,
            Func<MainPanelTabBar?> getTabBar,
            Func<bool> canRun,
            Action<ScrollableControl> hideHorizontalScroll)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _previewController = previewController ?? throw new ArgumentNullException(nameof(previewController));
            _tabPanels = tabPanels ?? throw new ArgumentNullException(nameof(tabPanels));
            _getActiveTab = getActiveTab ?? throw new ArgumentNullException(nameof(getActiveTab));
            _getTabBar = getTabBar ?? throw new ArgumentNullException(nameof(getTabBar));
            _canRun = canRun ?? throw new ArgumentNullException(nameof(canRun));
            _hideHorizontalScroll = hideHorizontalScroll ?? throw new ArgumentNullException(nameof(hideHorizontalScroll));
        }

        public bool IsDirty => _dirty;

        public bool IsInLayout => _inLayout;

        public void MarkDirty()
        {
            _dirty = true;
        }

        public void Reset()
        {
            _dirty = true;
            _lastWidth = -1;
            _lastTab = null;
            _scrollSentinel = null;
            _layoutQueued = false;
        }

        public void InvalidateActiveTabCache()
        {
            string activeTab = _getActiveTab();
            if (!_tabPanels.TryGetValue(activeTab, out Panel? panel))
                return;

            foreach (Control wrapper in panel.Controls)
                wrapper.Tag = null;
        }

        public void Queue(bool force = false)
        {
            if (_container.IsDisposed || !_canRun())
                return;

            if (force)
                _dirty = true;

            if (_layoutQueued)
                return;

            if (!_owner.IsHandleCreated)
            {
                Layout(force: true);
                return;
            }

            _layoutQueued = true;
            try
            {
                _owner.BeginInvoke((MethodInvoker)(() =>
                {
                    _layoutQueued = false;
                    Layout();
                }));
            }
            catch
            {
                _layoutQueued = false;
            }
        }

        public void Layout(bool force = false)
        {
            if (_container.IsDisposed)
                return;

            if (_inLayout)
            {
                _dirty = true;
                return;
            }

            int x = _container.Padding.Left;
            int fullWidth = Math.Max(1, _container.ClientSize.Width - _container.Padding.Horizontal);
            int width = fullWidth;
            if (_container.VerticalScroll.Visible && width > SystemInformation.VerticalScrollBarWidth)
                width -= SystemInformation.VerticalScrollBarWidth;

            string activeTab = _getActiveTab();
            if (!force &&
                !_dirty &&
                width == _lastWidth &&
                string.Equals(activeTab, _lastTab, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _inLayout = true;
            _dirty = false;
            _lastWidth = width;
            _lastTab = activeTab;

            _container.SuspendLayout();
            try
            {
                int contentBottom = ArrangeContent(x, width, activeTab);
                Panel sentinel = EnsureScrollSentinel();
                sentinel.SetBounds(x, Math.Max(0, contentBottom - 1), 1, 1);
                sentinel.BringToFront();

                int minHeight = contentBottom > _container.ClientSize.Height ? contentBottom : 0;
                var minSize = new Size(0, minHeight);
                if (_container.AutoScrollMinSize != minSize)
                    _container.AutoScrollMinSize = minSize;
                _container.HorizontalScroll.Enabled = false;
                _container.HorizontalScroll.Visible = false;
                _container.HorizontalScroll.Maximum = 0;
                _hideHorizontalScroll(_container);
            }
            finally
            {
                _container.ResumeLayout(false);
                _inLayout = false;
            }
        }

        private int ArrangeContent(int x, int width, string activeTab)
        {
            int y = _container.Padding.Top;
            y = _previewController.Layout(x, y, width);

            MainPanelTabBar? tabBar = _getTabBar();
            if (tabBar != null)
            {
                tabBar.Wrapper.SetBounds(x, y, width, UIUtils.S(42));
                y = tabBar.Wrapper.Bottom;
            }

            foreach (KeyValuePair<string, Panel> pair in _tabPanels)
            {
                Panel panel = pair.Value;
                bool active = string.Equals(pair.Key, activeTab, StringComparison.OrdinalIgnoreCase);
                int panelHeight = active ? MainPanelLayoutHelper.LayoutTabPanel(panel, width) : 0;
                panel.SetBounds(x, y, width, panelHeight);
                panel.Visible = active;
                if (active)
                    y = panel.Bottom;
            }

            return y + _container.Padding.Bottom;
        }

        private Panel EnsureScrollSentinel()
        {
            if (_scrollSentinel is { IsDisposed: false })
                return _scrollSentinel;

            _scrollSentinel = new Panel
            {
                BackColor = Color.Transparent,
                Enabled = false,
                TabStop = false,
                Width = 1,
                Height = 1
            };
            _container.Controls.Add(_scrollSentinel);
            return _scrollSentinel;
        }
    }
}
