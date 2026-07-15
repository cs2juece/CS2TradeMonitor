using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Lifecycle;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    public interface ISettingsPage
    {
        void Save();
        void OnShow();
        void OnHide();
        void OnThemeChanged();
    }

    public class SettingsPageBase : UserControl, ISettingsPage
    {
        private readonly CompositeDisposable _disposables = new();
        private CancellationTokenSource? _pageLifetimeCts;

        protected Settings Config = null!;
        protected MainForm MainForm = null!;
        protected UIController UI = null!;
        public bool? LastAppliedThemeDarkMode { get; set; }

        public static readonly Color GlobalBackColor = Color.FromArgb(249, 249, 249);

        public SettingsPageBase()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = GlobalBackColor;
            this.Dock = DockStyle.Fill;
            this.DoubleBuffered = true;
        }

        public void SetContext(Settings cfg, MainForm form, UIController ui)
        {
            Config = cfg;
            MainForm = form;
            UI = ui;
            OnContextAttached();
        }

        protected CompositeDisposable Disposables => _disposables;

        protected CancellationToken PageLifetimeToken => _pageLifetimeCts?.Token ?? CancellationToken.None;

        protected CancellationToken BeginPageLifetime()
        {
            CancelPageLifetime();
            _pageLifetimeCts = new CancellationTokenSource();
            return _pageLifetimeCts.Token;
        }

        protected void CancelPageLifetime()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _pageLifetimeCts, null);
            if (cts is null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
                // Cancellation during form teardown must be non-fatal.
            }
            finally
            {
                cts.Dispose();
            }
        }

        protected virtual void OnContextAttached()
        {
        }

        public virtual void OnShow()
        {
            BeginPageLifetime();
        }

        public virtual void OnHide()
        {
            CancelPageLifetime();
        }

        public virtual void OnThemeChanged()
        {
        }

        public virtual void RequestViewportRelayout()
        {
            Dock = DockStyle.Fill;
            PerformLayout();
            Invalidate(true);
        }

        public virtual void Save()
        {
            // Immediate binding means we don't need to do anything here.
            // But we keep the method because ISettingsPage requires it.
            // Subclasses (like PluginPage) can override it for post-save logic.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelPageLifetime();
                _disposables.Dispose();
            }

            base.Dispose(disposing);
        }

        protected void EnsureSafeVisibility(LiteCheck? chkHideMain, LiteCheck? chkHideTray, LiteCheck? chkShowTaskbar)
        {
            bool hideMain = chkHideMain != null ? chkHideMain.Checked : Config.HideMainForm;
            bool hideTray = chkHideTray != null ? chkHideTray.Checked : Config.HideTrayIcon;
            bool showBar = chkShowTaskbar != null ? chkShowTaskbar.Checked : Config.ShowTaskbar;
            var candidate = new Settings
            {
                HideMainForm = hideMain,
                HideTrayIcon = hideTray,
                ShowTaskbar = showBar,
                ClickThrough = Config.ClickThrough,
                TaskbarClickThrough = Config.TaskbarClickThrough
            };

            if (Settings.HasNoInteractiveEntry(candidate))
            {
                GlobalPromptService.Show("为了防止程序无法唤出，不能同时隐藏或穿透所有可交互入口。",
                                "安全警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                if (chkHideMain != null) chkHideMain.Checked = false;
                if (chkHideTray != null) chkHideTray.Checked = false;
                if (chkShowTaskbar != null) chkShowTaskbar.Checked = true;
            }
        }
    }

}
