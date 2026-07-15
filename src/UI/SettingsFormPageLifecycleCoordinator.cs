using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.ComponentModel;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormPageLifecycleCoordinator
    {
        private readonly Func<bool> _isFormDisposedOrDisposing;
        private readonly Func<bool> _isFormHandleCreated;
        private readonly Func<string> _getCurrentKey;
        private readonly Func<string, SettingsPageBase?> _tryGetPage;
        private readonly Action<int, Action> _scheduleDelay;
        private readonly Action<string> _switchPage;
        private readonly int _pageOnShowDelayMs;
        private string? _pendingPageOnShowKey;

        public SettingsFormPageLifecycleCoordinator(
            Func<bool> isFormDisposedOrDisposing,
            Func<bool> isFormHandleCreated,
            Func<string> getCurrentKey,
            Func<string, SettingsPageBase?> tryGetPage,
            Action<int, Action> scheduleDelay,
            Action<string> switchPage,
            int pageOnShowDelayMs)
        {
            _isFormDisposedOrDisposing = isFormDisposedOrDisposing ?? throw new ArgumentNullException(nameof(isFormDisposedOrDisposing));
            _isFormHandleCreated = isFormHandleCreated ?? throw new ArgumentNullException(nameof(isFormHandleCreated));
            _getCurrentKey = getCurrentKey ?? throw new ArgumentNullException(nameof(getCurrentKey));
            _tryGetPage = tryGetPage ?? throw new ArgumentNullException(nameof(tryGetPage));
            _scheduleDelay = scheduleDelay ?? throw new ArgumentNullException(nameof(scheduleDelay));
            _switchPage = switchPage ?? throw new ArgumentNullException(nameof(switchPage));
            _pageOnShowDelayMs = Math.Max(1, pageOnShowDelayMs);
        }

        public void MarkPendingPageOnShow(string key)
        {
            _pendingPageOnShowKey = key;
        }

        public void SafeInvokeOnHide(SettingsPageBase page, string key)
        {
            if (page.IsDisposed)
                return;

            try
            {
                page.OnHide();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("Settings", $"OnHide skipped. Key={key}; PageType={page.GetType().Name}; Message={ex.Message}");
            }
        }

        public void InvokePageOnShowWhenReady(string key, SettingsPageBase targetPage)
        {
            if (_isFormDisposedOrDisposing() || _getCurrentKey() != key || targetPage.IsDisposed)
                return;

            if (!_isFormHandleCreated())
            {
                _pendingPageOnShowKey = key;
                return;
            }

            if (!targetPage.IsHandleCreated)
            {
                _pendingPageOnShowKey = key;
                EventHandler? handler = null;
                handler = (s, e) =>
                {
                    targetPage.HandleCreated -= handler;
                    if (_isFormDisposedOrDisposing() || _getCurrentKey() != key || targetPage.IsDisposed)
                        return;

                    BeginInvokePageOnShow(key, targetPage, "HandleCreated");
                };
                targetPage.HandleCreated += handler;
                return;
            }

            _pendingPageOnShowKey = null;
            BeginInvokePageOnShow(key, targetPage, "Ready");
        }

        public void ScheduleSwitchPageRetry(string key, string expectedCurrentKey, int delayMs)
        {
            if (_isFormDisposedOrDisposing())
                return;

            _scheduleDelay(delayMs, () =>
            {
                if (_isFormDisposedOrDisposing() || _getCurrentKey() != expectedCurrentKey)
                    return;
                _switchPage(key);
            });
        }

        public void InvokePendingPageOnShow()
        {
            var key = _pendingPageOnShowKey;
            if (string.IsNullOrWhiteSpace(key) || _getCurrentKey() != key)
                return;

            var page = _tryGetPage(key);
            if (page != null)
                InvokePageOnShowWhenReady(key, page);
        }

        internal static bool IsTransientWinFormsHandleException(Exception ex)
        {
            string message = ex.Message ?? string.Empty;
            bool knownTransientType = ex is InvalidOperationException || ex is Win32Exception;
            return knownTransientType
                && (message.IndexOf("Win32", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("父窗口", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("window handle", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("窗口句柄", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("Invoke", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("BeginInvoke", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void BeginInvokePageOnShow(string key, SettingsPageBase targetPage, string source)
        {
            if (_isFormDisposedOrDisposing() || _getCurrentKey() != key || targetPage.IsDisposed || !_isFormHandleCreated())
                return;

            try
            {
                _scheduleDelay(_pageOnShowDelayMs, () =>
                {
                    if (_isFormDisposedOrDisposing() || _getCurrentKey() != key || targetPage.IsDisposed || !targetPage.IsHandleCreated)
                        return;
                    SafeInvokeOnShow(targetPage, key, 0);
                });
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("Settings", $"Deferred OnShow skipped. Source={source}; Key={key}; Message={ex.Message}");
                if (IsTransientWinFormsHandleException(ex))
                    SchedulePendingPageOnShowRetry(key, 150);
            }
        }

        private void SafeInvokeOnShow(SettingsPageBase targetPage, string key, int retryCount)
        {
            if (_isFormDisposedOrDisposing() || _getCurrentKey() != key || targetPage.IsDisposed || !targetPage.IsHandleCreated)
                return;

            try
            {
                using (UiJankProfiler.Measure("Settings.PageOnShow", $"Key={key}; PageType={targetPage.GetType().Name}", thresholdMs: 1))
                {
                    targetPage.OnShow();
                }
            }
            catch (InvalidOperationException ex)
            {
                bool canRetry = retryCount == 0 && IsTransientWinFormsHandleException(ex);
                DiagnosticsLogger.Info("Settings", $"OnShow threw InvalidOperationException (will retry={canRetry}): {ex.Message}");
                if (canRetry && !_isFormDisposedOrDisposing() && !targetPage.IsDisposed)
                {
                    _scheduleDelay(100, () =>
                    {
                        if (_isFormDisposedOrDisposing() || _getCurrentKey() != key || targetPage.IsDisposed || !targetPage.IsHandleCreated)
                            return;
                        SafeInvokeOnShow(targetPage, key, 1);
                    });
                }
            }
            catch (Exception ex)
            {
                if (IsTransientWinFormsHandleException(ex))
                {
                    DiagnosticsLogger.Info("Settings", $"OnShow hit transient WinForms handle state; retry={retryCount == 0}. key={key}; Message={ex.Message}");
                    if (retryCount == 0)
                        SchedulePendingPageOnShowRetry(key, 150);
                    return;
                }

                DiagnosticsLogger.Error("Settings", $"OnShow threw unexpected exception. key={key}", ex);
            }
        }

        private void SchedulePendingPageOnShowRetry(string key, int delayMs)
        {
            if (_isFormDisposedOrDisposing())
                return;

            _scheduleDelay(delayMs, () =>
            {
                if (_isFormDisposedOrDisposing() || _getCurrentKey() != key)
                    return;
                InvokePendingPageOnShow();
            });
        }
    }
}
