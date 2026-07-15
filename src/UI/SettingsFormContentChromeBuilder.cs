using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormContentChromeBuilder
    {
        private readonly SettingsForm _owner;
        private readonly TableLayoutPanel _mainPanel;
        private readonly SettingsFormNavigationCoordinator _navigationCoordinator;
        private readonly SettingsFormPageLifecycleCoordinator _pageLifecycleCoordinator;
        private readonly SettingsFormUiTickCoordinator _uiTickCoordinator;
        private readonly Func<string> _getCurrentKey;
        private readonly Action<string> _setCurrentKey;
        private readonly Func<string, SettingsPageBase> _getOrCreateSettingsPage;
        private readonly Func<SettingsPageBase?> _getVisiblePage;
        private readonly Action<SettingsPageBase?> _setVisiblePage;
        private readonly Func<bool> _isDarkMode;
        private readonly Action<Control?> _queueDeferredNativeTheme;
        private readonly Action<bool> _setSwitchingPage;
        private readonly Func<int> _getPageCount;

        public SettingsFormContentChromeBuilder(
            SettingsForm owner,
            TableLayoutPanel mainPanel,
            SettingsFormNavigationCoordinator navigationCoordinator,
            SettingsFormPageLifecycleCoordinator pageLifecycleCoordinator,
            SettingsFormUiTickCoordinator uiTickCoordinator,
            Func<string> getCurrentKey,
            Action<string> setCurrentKey,
            Func<string, SettingsPageBase> getOrCreateSettingsPage,
            Func<SettingsPageBase?> getVisiblePage,
            Action<SettingsPageBase?> setVisiblePage,
            Func<bool> isDarkMode,
            Action<Control?> queueDeferredNativeTheme,
            Action<bool> setSwitchingPage,
            Func<int> getPageCount)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _mainPanel = mainPanel ?? throw new ArgumentNullException(nameof(mainPanel));
            _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
            _pageLifecycleCoordinator = pageLifecycleCoordinator ?? throw new ArgumentNullException(nameof(pageLifecycleCoordinator));
            _uiTickCoordinator = uiTickCoordinator ?? throw new ArgumentNullException(nameof(uiTickCoordinator));
            _getCurrentKey = getCurrentKey ?? throw new ArgumentNullException(nameof(getCurrentKey));
            _setCurrentKey = setCurrentKey ?? throw new ArgumentNullException(nameof(setCurrentKey));
            _getOrCreateSettingsPage = getOrCreateSettingsPage ?? throw new ArgumentNullException(nameof(getOrCreateSettingsPage));
            _getVisiblePage = getVisiblePage ?? throw new ArgumentNullException(nameof(getVisiblePage));
            _setVisiblePage = setVisiblePage ?? throw new ArgumentNullException(nameof(setVisiblePage));
            _isDarkMode = isDarkMode ?? throw new ArgumentNullException(nameof(isDarkMode));
            _queueDeferredNativeTheme = queueDeferredNativeTheme ?? throw new ArgumentNullException(nameof(queueDeferredNativeTheme));
            _setSwitchingPage = setSwitchingPage ?? throw new ArgumentNullException(nameof(setSwitchingPage));
            _getPageCount = getPageCount ?? throw new ArgumentNullException(nameof(getPageCount));
        }

        public SettingsFormContentChrome Build()
        {
            var bottomPanel = new Panel { Dock = DockStyle.Fill, BackColor = UIColors.MainBg, Visible = false };
            var applyButton = new LiteButton(LanguageManager.T("Menu.Apply"), false) { Visible = false };
            applyButton.Enabled = false;

            var statusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = UIUtils.S(new Padding(20, 0, 0, 0)),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent
            };
            bottomPanel.Controls.Add(statusLabel);

            var contentPanel = new BufferedPanel { Dock = DockStyle.Fill, Padding = new Padding(0), BackColor = UIColors.MainBg };
            var pageSwitchCoordinator = new SettingsFormPageSwitchCoordinator(
                _owner,
                contentPanel,
                _navigationCoordinator,
                _pageLifecycleCoordinator,
                _uiTickCoordinator,
                _getCurrentKey,
                _setCurrentKey,
                _getOrCreateSettingsPage,
                _getVisiblePage,
                _setVisiblePage,
                _isDarkMode,
                _queueDeferredNativeTheme,
                _setSwitchingPage,
                _getPageCount);

            _mainPanel.Controls.Add(contentPanel, 0, 0);
            _mainPanel.Controls.Add(bottomPanel, 0, 1);

            return new SettingsFormContentChrome(
                bottomPanel,
                contentPanel,
                applyButton,
                statusLabel,
                pageSwitchCoordinator);
        }
    }

    internal sealed record SettingsFormContentChrome(
        Panel BottomPanel,
        BufferedPanel ContentPanel,
        LiteButton ApplyButton,
        Label StatusLabel,
        SettingsFormPageSwitchCoordinator PageSwitchCoordinator);
}
