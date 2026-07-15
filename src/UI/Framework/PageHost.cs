using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using CS2TradeMonitor.Infrastructure.Diagnostics;

namespace CS2TradeMonitor.src.UI.Framework
{
    /// <summary>
    /// Hosts UI pages without letting pages reach into the parent window.
    /// </summary>
    public sealed class PageHost : UserControl
    {
        private const int PageActivateDelayMs = 45;

        private readonly HashSet<IUiPage> _initializedPages = new HashSet<IUiPage>();
        private readonly UiDeferredActionScheduler _deferredActions;
        private IUiPage? _currentPage;
        private SettingsStore? _settingsStore;
        private IUiPage? _pendingActivationPage;

        public PageHost()
        {
            Dock = DockStyle.Fill;
            BackColor = UIColors.MainBg;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            _deferredActions = new UiDeferredActionScheduler(() => !IsDisposed && !Disposing);
        }

        public IUiPage? CurrentPage => _currentPage;

        public void AttachSettings(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        }

        public void ShowPage(IUiPage page)
        {
            string pageType = page?.GetType().Name ?? "<null>";
            using (UiJankProfiler.Measure("PageHost.ShowPage", $"PageType={pageType}", thresholdMs: 1))
            {
                if (page is not Control pageControl)
                {
                    throw new ArgumentException("Hosted pages must also inherit from Control.", nameof(page));
                }

                if (_settingsStore is null)
                {
                    throw new InvalidOperationException("AttachSettings must be called before showing a page.");
                }

                if (ReferenceEquals(page, _currentPage))
                {
                    return;
                }

                CancelPendingActivation();
                bool requiresLayout = false;
                SuspendLayout();
                try
                {
                    if (_currentPage is Control oldControl)
                    {
                        _currentPage.Save();
                        _currentPage.Deactivate();
                        oldControl.Visible = false;
                    }
                    else if (_currentPage is not null)
                    {
                        _currentPage.Save();
                        _currentPage.Deactivate();
                    }

                    if (pageControl.Dock != DockStyle.Fill)
                    {
                        pageControl.Dock = DockStyle.Fill;
                        requiresLayout = true;
                    }

                    if (pageControl.Margin != Padding.Empty)
                    {
                        pageControl.Margin = Padding.Empty;
                        requiresLayout = true;
                    }

                    pageControl.Visible = false;
                    if (!Controls.Contains(pageControl))
                    {
                        Controls.Add(pageControl);
                        requiresLayout = true;
                    }

                    if (_initializedPages.Add(page))
                    {
                        page.Initialize(_settingsStore);
                        requiresLayout = true;
                    }

                    _currentPage = page;
                    DetailedDiagnosticsRuntime.Record(
                        "Information",
                        "UI",
                        "PageNavigation",
                        new Dictionary<string, object?> { ["page"] = pageType });
                    if (pageControl.Bounds != ClientRectangle)
                    {
                        pageControl.Bounds = ClientRectangle;
                        requiresLayout = true;
                    }

                    if (requiresLayout)
                        RequestCurrentPageRelayout();
                    pageControl.Visible = true;
                    pageControl.BringToFront();
                    QueuePageActivation(page);
                }
                finally
                {
                    ResumeLayout(performLayout: requiresLayout);
                }
            }
        }

        public void SaveCurrentPage()
        {
            _currentPage?.Save();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_currentPage is not Control control || control.IsDisposed)
                return;

            bool changed = false;
            if (control.Dock != DockStyle.Fill)
            {
                control.Dock = DockStyle.Fill;
                changed = true;
            }

            if (control.Bounds != ClientRectangle)
            {
                control.Bounds = ClientRectangle;
                changed = true;
            }

            if (!changed)
                return;

            control.PerformLayout();
            RequestCurrentPageRelayout();
            control.Invalidate(true);
        }

        public void RequestCurrentPageRelayout()
        {
            switch (_currentPage)
            {
                case FrameworkSettingsPageBase frameworkPage:
                    frameworkPage.RequestViewportRelayout();
                    break;
                case SettingsPageBase settingsPage:
                    settingsPage.RequestViewportRelayout();
                    break;
                case Control control when !control.IsDisposed:
                    control.Dock = DockStyle.Fill;
                    control.Bounds = ClientRectangle;
                    control.PerformLayout();
                    control.Invalidate(true);
                    break;
            }
        }

        private void QueuePageActivation(IUiPage page)
        {
            _pendingActivationPage = page;
            _deferredActions.Schedule("page-activation", PageActivateDelayMs, ActivatePendingPage);
        }

        private void ActivatePendingPage()
        {
            var page = _pendingActivationPage;
            _pendingActivationPage = null;

            if (page == null || IsDisposed || !ReferenceEquals(page, _currentPage))
                return;

            if (page is Control control && (control.IsDisposed || !control.Visible))
                return;

            page.Activate();
        }

        private void CancelPendingActivation()
        {
            _deferredActions.Cancel("page-activation");
            _pendingActivationPage = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPendingActivation();
                _deferredActions.Dispose();
                _currentPage?.Deactivate();

                foreach (IUiPage page in _initializedPages)
                {
                    page.Dispose();
                }

                _initializedPages.Clear();
            }

            base.Dispose(disposing);
        }
    }
}
