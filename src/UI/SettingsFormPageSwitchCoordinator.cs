using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal sealed class SettingsFormPageSwitchCoordinator
    {
        private readonly Control _owner;
        private readonly BufferedPanel _contentPanel;
        private readonly SettingsFormNavigationCoordinator _navigationCoordinator;
        private readonly SettingsFormPageLifecycleCoordinator _pageLifecycleCoordinator;
        private readonly SettingsFormUiTickCoordinator _uiTickCoordinator;
        private readonly Func<string> _getCurrentKey;
        private readonly Action<string> _setCurrentKey;
        private readonly Func<string, SettingsPageBase> _getOrCreateSettingsPage;
        private readonly Func<SettingsPageBase?> _getVisiblePage;
        private readonly Action<SettingsPageBase?> _setVisiblePage;
        private readonly Func<bool> _isDarkTheme;
        private readonly Action<Control?> _queueDeferredContentNativeTheme;
        private readonly Action<bool> _setSwitchingPage;
        private readonly Func<int> _getCachedPageCount;

        public SettingsFormPageSwitchCoordinator(
            Control owner,
            BufferedPanel contentPanel,
            SettingsFormNavigationCoordinator navigationCoordinator,
            SettingsFormPageLifecycleCoordinator pageLifecycleCoordinator,
            SettingsFormUiTickCoordinator uiTickCoordinator,
            Func<string> getCurrentKey,
            Action<string> setCurrentKey,
            Func<string, SettingsPageBase> getOrCreateSettingsPage,
            Func<SettingsPageBase?> getVisiblePage,
            Action<SettingsPageBase?> setVisiblePage,
            Func<bool> isDarkTheme,
            Action<Control?> queueDeferredContentNativeTheme,
            Action<bool> setSwitchingPage,
            Func<int> getCachedPageCount)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _contentPanel = contentPanel ?? throw new ArgumentNullException(nameof(contentPanel));
            _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
            _pageLifecycleCoordinator = pageLifecycleCoordinator ?? throw new ArgumentNullException(nameof(pageLifecycleCoordinator));
            _uiTickCoordinator = uiTickCoordinator ?? throw new ArgumentNullException(nameof(uiTickCoordinator));
            _getCurrentKey = getCurrentKey ?? throw new ArgumentNullException(nameof(getCurrentKey));
            _setCurrentKey = setCurrentKey ?? throw new ArgumentNullException(nameof(setCurrentKey));
            _getOrCreateSettingsPage = getOrCreateSettingsPage ?? throw new ArgumentNullException(nameof(getOrCreateSettingsPage));
            _getVisiblePage = getVisiblePage ?? throw new ArgumentNullException(nameof(getVisiblePage));
            _setVisiblePage = setVisiblePage ?? throw new ArgumentNullException(nameof(setVisiblePage));
            _isDarkTheme = isDarkTheme ?? throw new ArgumentNullException(nameof(isDarkTheme));
            _queueDeferredContentNativeTheme = queueDeferredContentNativeTheme ?? throw new ArgumentNullException(nameof(queueDeferredContentNativeTheme));
            _setSwitchingPage = setSwitchingPage ?? throw new ArgumentNullException(nameof(setSwitchingPage));
            _getCachedPageCount = getCachedPageCount ?? throw new ArgumentNullException(nameof(getCachedPageCount));
        }

        public void SwitchPage(string key)
        {
            key = SettingsPageRegistry.NormalizeKey(key);
            if (_getCurrentKey() == key)
                return;

            string fromKey = _getCurrentKey();
            string pageType = "";
            bool createdPage = false;
            bool ownerLayoutSuspended = false;
            bool contentLayoutSuspended = false;
            bool previousPageHidden = false;
            SettingsPageBase? previousPage = _getVisiblePage();
            SettingsPageBase? targetPage = null;
            var stopwatch = Stopwatch.StartNew();
            _setSwitchingPage(true);
            _uiTickCoordinator.Pause();

            try
            {
                _owner.SuspendLayout();
                ownerLayoutSuspended = true;
                _contentPanel.SuspendLayout();
                contentLayoutSuspended = true;

                using (UiJankProfiler.Measure("Settings.SwitchPage.GetOrCreate", $"To={key}", thresholdMs: 1))
                {
                    targetPage = _getOrCreateSettingsPage(key);
                }

                pageType = targetPage.GetType().Name;
                bool needsViewportRelayout = false;
                if (!_contentPanel.Controls.Contains(targetPage))
                {
                    using (UiJankProfiler.Measure("Settings.SwitchPage.AddPage", $"To={key}; PageType={pageType}", thresholdMs: 1))
                    {
                        createdPage = true;
                        targetPage.Bounds = _contentPanel.ClientRectangle;
                        targetPage.Dock = DockStyle.Fill;
                        targetPage.Visible = false;
                        _contentPanel.Controls.Add(targetPage);
                        needsViewportRelayout = true;
                    }
                }

                needsViewportRelayout |= EnsureTargetPageBounds(targetPage);
                if (needsViewportRelayout)
                    targetPage.RequestViewportRelayout();

                bool isDarkTheme = _isDarkTheme();
                if (targetPage.LastAppliedThemeDarkMode != isDarkTheme)
                {
                    using (UiJankProfiler.Measure("Settings.SwitchPage.QueueTheme", $"To={key}; PageType={pageType}", thresholdMs: 1))
                    {
                        _queueDeferredContentNativeTheme(targetPage);
                    }
                }

                if (previousPage != null && !ReferenceEquals(previousPage, targetPage))
                {
                    using (UiJankProfiler.Measure("Settings.SwitchPage.HideOld", $"From={fromKey}", thresholdMs: 1))
                    {
                        _pageLifecycleCoordinator.SafeInvokeOnHide(previousPage, fromKey);
                        previousPage.Visible = false;
                        previousPageHidden = true;
                    }
                }

                using (UiJankProfiler.Measure("Settings.SwitchPage.ShowNew", $"To={key}; PageType={pageType}", thresholdMs: 1))
                {
                    targetPage.Visible = true;
                    targetPage.BringToFront();
                    _setCurrentKey(key);
                    _navigationCoordinator.SetActive(key);
                    _setVisiblePage(targetPage);
                    RuntimeHealthLogger.RecordPageSwitch(key);
                    _pageLifecycleCoordinator.InvokePageOnShowWhenReady(key, targetPage);
                }
            }
            catch (Exception ex)
            {
                RollbackSwitch(fromKey, previousPage, targetPage, previousPageHidden);

                if (SettingsFormPageLifecycleCoordinator.IsTransientWinFormsHandleException(ex))
                {
                    DiagnosticsLogger.Info(
                        "Settings",
                        $"Switch page hit transient WinForms handle state; retry scheduled. From={fromKey}; To={key}; PageType={pageType}; CreatedPage={createdPage}; CachedPages={_getCachedPageCount()}; Message={ex.Message}");
                    _pageLifecycleCoordinator.MarkPendingPageOnShow(key);
                    _setCurrentKey(fromKey);
                    _pageLifecycleCoordinator.ScheduleSwitchPageRetry(key, fromKey, 150);
                    return;
                }

                DiagnosticsLogger.Error(
                    "Settings",
                    $"Switch page failed. From={fromKey}; To={key}; PageType={pageType}; CreatedPage={createdPage}; CachedPages={_getCachedPageCount()}",
                    ex);
            }
            finally
            {
                stopwatch.Stop();
                if (contentLayoutSuspended)
                    _contentPanel.ResumeLayout(false);
                if (ownerLayoutSuspended)
                    _owner.ResumeLayout(false);
                _setSwitchingPage(false);
                _uiTickCoordinator.Resume();

                if (UiJankProfiler.Enabled || stopwatch.ElapsedMilliseconds >= 300)
                {
                    DiagnosticsLogger.Info(
                        "Settings",
                        $"Switch page slow. From={fromKey}; To={key}; PageType={pageType}; CreatedPage={createdPage}; CachedPages={_getCachedPageCount()}; ElapsedMs={stopwatch.ElapsedMilliseconds}");
                }
            }
        }

        private void RollbackSwitch(
            string fromKey,
            SettingsPageBase? previousPage,
            SettingsPageBase? targetPage,
            bool previousPageHidden)
        {
            TryRollback(() => _setCurrentKey(fromKey), fromKey, "RestoreCurrentKey");
            TryRollback(() => _navigationCoordinator.SetActive(fromKey), fromKey, "RestoreNavigation");

            if (targetPage != null && !ReferenceEquals(targetPage, previousPage) && !targetPage.IsDisposed)
                TryRollback(() => targetPage.Visible = false, fromKey, "HideTargetPage");

            if (previousPage == null || previousPage.IsDisposed)
            {
                TryRollback(() => _setVisiblePage(null), fromKey, "ClearVisiblePage");
                return;
            }

            TryRollback(() => previousPage.Visible = true, fromKey, "ShowPreviousPage");
            TryRollback(previousPage.BringToFront, fromKey, "ActivatePreviousPage");
            TryRollback(() => _setVisiblePage(previousPage), fromKey, "RestoreVisiblePage");
            if (previousPageHidden)
            {
                TryRollback(
                    () => _pageLifecycleCoordinator.InvokePageOnShowWhenReady(fromKey, previousPage),
                    fromKey,
                    "RestorePreviousLifecycle");
            }
        }

        private static void TryRollback(Action action, string fromKey, string operation)
        {
            try
            {
                action();
            }
            catch (Exception rollbackException)
            {
                DiagnosticsLogger.Error(
                    "Settings",
                    $"Switch page rollback step failed. RestoredKey={fromKey}; Operation={operation}",
                    rollbackException);
            }
        }

        private bool EnsureTargetPageBounds(SettingsPageBase targetPage)
        {
            bool changed = false;
            if (targetPage.Dock != DockStyle.Fill)
            {
                targetPage.Dock = DockStyle.Fill;
                changed = true;
            }

            Rectangle targetBounds = _contentPanel.ClientRectangle;
            if (targetPage.Bounds != targetBounds)
            {
                targetPage.Bounds = targetBounds;
                changed = true;
            }

            return changed;
        }
    }
}
