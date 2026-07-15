using System;
using System.Windows.Forms;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormNavigationCoordinator : IDisposable
    {
        private const int NavigationDebounceMs = 60;
        private const string NavigationDelayKey = "settings-navigation";
        private readonly FlowLayoutPanel _mainContainer;
        private readonly FlowLayoutPanel _systemContainer;
        private readonly Action<string> _switchPage;
        private readonly Action _layoutSidebar;
        private readonly Action<Action>? _queueActionForTests;
        private readonly UiDeferredActionScheduler _deferredNavigation;
        private int _navigationRequestVersion;

        public SettingsFormNavigationCoordinator(
            FlowLayoutPanel mainContainer,
            FlowLayoutPanel systemContainer,
            Action<string> switchPage,
            Action layoutSidebar)
            : this(mainContainer, systemContainer, switchPage, layoutSidebar, null)
        {
        }

        internal SettingsFormNavigationCoordinator(
            FlowLayoutPanel mainContainer,
            FlowLayoutPanel systemContainer,
            Action<string> switchPage,
            Action layoutSidebar,
            Action<Action>? queueActionForTests)
        {
            _mainContainer = mainContainer ?? throw new ArgumentNullException(nameof(mainContainer));
            _systemContainer = systemContainer ?? throw new ArgumentNullException(nameof(systemContainer));
            _switchPage = switchPage ?? throw new ArgumentNullException(nameof(switchPage));
            _layoutSidebar = layoutSidebar ?? throw new ArgumentNullException(nameof(layoutSidebar));
            _queueActionForTests = queueActionForTests;
            _deferredNavigation = new UiDeferredActionScheduler(
                () => !_mainContainer.IsDisposed && !_mainContainer.Disposing);
        }

        public void Rebuild()
        {
            var entries = SettingsFormNavigationModel.BuildEntries(SettingsPageRegistry.NavigationRoutes);

            _mainContainer.SuspendLayout();
            _systemContainer.SuspendLayout();
            try
            {
                _mainContainer.Controls.Clear();
                _systemContainer.Controls.Clear();
                AddEntries(_mainContainer, entries.MainEntries);
                AddEntries(_systemContainer, entries.SystemEntries);
            }
            finally
            {
                _systemContainer.ResumeLayout(false);
                _mainContainer.ResumeLayout(false);
            }

            _mainContainer.PerformLayout();
            _systemContainer.PerformLayout();
            _layoutSidebar();
        }

        public void SetActive(string key)
        {
            _navigationRequestVersion++;
            _deferredNavigation.Cancel(NavigationDelayKey);
            ApplyActive(key, flush: false);
        }

        private void AddEntries(Control parent, System.Collections.Generic.IEnumerable<SettingsFormNavigationEntry> entries)
        {
            foreach (var entry in entries)
            {
                var button = new LiteNavBtn(entry.Title) { Tag = entry.Key };
                button.Click += (_, __) => QueueNavigation(entry.Key);
                parent.Controls.Add(button);
            }
        }

        internal void QueueNavigation(string key)
        {
            ApplyActive(key, flush: true);

            int version = ++_navigationRequestVersion;
            Action switchLatest = () =>
            {
                if (_mainContainer.IsDisposed || version != _navigationRequestVersion)
                    return;
                _switchPage(key);
            };
            if (_queueActionForTests != null)
            {
                _queueActionForTests(switchLatest);
                return;
            }
            if (!_mainContainer.IsHandleCreated || _mainContainer.IsDisposed)
            {
                _switchPage(key);
                return;
            }

            _deferredNavigation.Schedule(NavigationDelayKey, NavigationDebounceMs, switchLatest);
        }

        public void Dispose()
        {
            _deferredNavigation.Dispose();
        }

        private void ApplyActive(string key, bool flush)
        {
            SetActive(_mainContainer, key, flush);
            SetActive(_systemContainer, key, flush);
        }

        private static void SetActive(Control parent, string key, bool flush)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is LiteNavBtn button && button.Tag is string tag)
                {
                    button.IsActive = string.Equals(tag, key, StringComparison.OrdinalIgnoreCase);
                    if (flush)
                        button.Update();
                }
            }
        }
    }
}
