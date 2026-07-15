using System;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormMainPanelTabCoordinator
    {
        private const int RetryDelayMs = 120;
        private const int MaxRetries = 6;

        private readonly Func<bool> _isDisposedOrDisposing;
        private readonly Func<bool> _isWindowReady;
        private readonly Func<bool> _isFormCreated;
        private readonly Func<string> _getCurrentPageKey;
        private readonly Action<string> _switchPage;
        private readonly Func<IMainPanelSettingsPageHost?> _getMainPanelPage;
        private readonly Action<Action> _dispatch;
        private readonly Action<int, Action> _scheduleDelay;
        private readonly Func<Exception, bool> _isTransientWinFormsHandleException;
        private string? _pendingTab;
        private int _retryCount;
        private bool _retryScheduled;

        public SettingsFormMainPanelTabCoordinator(
            Func<bool> isDisposedOrDisposing,
            Func<bool> isWindowReady,
            Func<bool> isFormCreated,
            Func<string> getCurrentPageKey,
            Action<string> switchPage,
            Func<IMainPanelSettingsPageHost?> getMainPanelPage,
            Action<Action> dispatch,
            Action<int, Action> scheduleDelay,
            Func<Exception, bool> isTransientWinFormsHandleException)
        {
            _isDisposedOrDisposing = isDisposedOrDisposing ?? throw new ArgumentNullException(nameof(isDisposedOrDisposing));
            _isWindowReady = isWindowReady ?? throw new ArgumentNullException(nameof(isWindowReady));
            _isFormCreated = isFormCreated ?? throw new ArgumentNullException(nameof(isFormCreated));
            _getCurrentPageKey = getCurrentPageKey ?? throw new ArgumentNullException(nameof(getCurrentPageKey));
            _switchPage = switchPage ?? throw new ArgumentNullException(nameof(switchPage));
            _getMainPanelPage = getMainPanelPage ?? throw new ArgumentNullException(nameof(getMainPanelPage));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _scheduleDelay = scheduleDelay ?? throw new ArgumentNullException(nameof(scheduleDelay));
            _isTransientWinFormsHandleException = isTransientWinFormsHandleException ?? throw new ArgumentNullException(nameof(isTransientWinFormsHandleException));
        }

        public void SwitchTab(string tabKey)
        {
            _pendingTab = NormalizeTabKey(tabKey);
            _retryCount = 0;

            if (_isDisposedOrDisposing())
                return;

            if (!_isWindowReady())
            {
                ScheduleRetry();
                return;
            }

            if (!IsMainPanelCurrent())
                _switchPage("MainPanel");

            InvokePending();
        }

        public void InvokePending()
        {
            var tabKey = _pendingTab;
            if (string.IsNullOrWhiteSpace(tabKey) || !IsMainPanelCurrent())
                return;

            if (_isDisposedOrDisposing() || !_isWindowReady())
            {
                ScheduleRetry();
                return;
            }

            IMainPanelSettingsPageHost? mainPanel = _getMainPanelPage();
            if (mainPanel == null || !mainPanel.IsHandleCreated || mainPanel.IsDisposed || !mainPanel.Visible)
            {
                ScheduleRetry();
                return;
            }

            _pendingTab = null;
            try
            {
                _dispatch(() =>
                {
                    if (_isDisposedOrDisposing()
                        || !_isFormCreated()
                        || !IsMainPanelCurrent()
                        || mainPanel.IsDisposed
                        || !mainPanel.IsHandleCreated
                        || !mainPanel.Visible)
                    {
                        _pendingTab = tabKey;
                        ScheduleRetry();
                        return;
                    }

                    try
                    {
                        mainPanel.SelectTab(tabKey);
                    }
                    catch (Exception ex) when (_isTransientWinFormsHandleException(ex))
                    {
                        DiagnosticsLogger.Info("Settings", $"MainPanel tab select hit transient WinForms handle state; retry scheduled. Tab={tabKey}; Message={ex.Message}");
                        _pendingTab = tabKey;
                        ScheduleRetry();
                    }
                });
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("Settings", $"MainPanel tab select dispatch skipped; retry scheduled. Tab={tabKey}; Message={ex.Message}");
                _pendingTab = tabKey;
                ScheduleRetry();
            }
        }

        public static string NormalizeTabKey(string tabKey)
        {
            if (string.Equals(tabKey, "Taskbar", StringComparison.OrdinalIgnoreCase))
                return "Taskbar";

            if (string.Equals(tabKey, "Appearance", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tabKey, "Style", StringComparison.OrdinalIgnoreCase))
                return "Style";

            if (string.Equals(tabKey, "ItemMonitor", StringComparison.OrdinalIgnoreCase))
                return "ItemMonitor";

            if (string.Equals(tabKey, "InventoryTrend", StringComparison.OrdinalIgnoreCase))
                return "InventoryTrend";

            return "Float";
        }

        private void ScheduleRetry()
        {
            if (_isDisposedOrDisposing() || string.IsNullOrWhiteSpace(_pendingTab))
                return;

            if (_retryScheduled)
                return;

            if (_retryCount++ >= MaxRetries)
            {
                DiagnosticsLogger.Info("Settings", $"MainPanel pending tab select skipped after retries. Tab={_pendingTab}");
                _pendingTab = null;
                return;
            }

            _retryScheduled = true;
            _scheduleDelay(RetryDelayMs, () =>
            {
                _retryScheduled = false;

                if (_isDisposedOrDisposing() || string.IsNullOrWhiteSpace(_pendingTab))
                    return;

                if (!_isWindowReady() && !IsMainPanelCurrent())
                {
                    ScheduleRetry();
                    return;
                }

                if (!IsMainPanelCurrent())
                    _switchPage("MainPanel");

                InvokePending();
            });
        }

        private bool IsMainPanelCurrent()
        {
            return string.Equals(_getCurrentPageKey(), "MainPanel", StringComparison.OrdinalIgnoreCase);
        }
    }
}
