using CS2TradeMonitor.Application.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.SettingsPage;

namespace CS2TradeMonitor.src.UI
{
    public class SettingsForm : Form
    {
        private const int PageOnShowDelayMs = 45;
        private const int DeferredContentNativeThemeDelayMs = 120;
        private Settings _cfg; // Live Settings
        private Settings _draftCfg; // Draft Settings
        private UIController _ui;
        private MainForm _mainForm;
        private TableLayoutPanel _chromeLayout = null!;
        private Panel _titleBar = null!;
        private Label _titleLabel = null!;
        private Button _btnTitleMin = null!;
        private Button _btnTitleMax = null!;
        private Button _btnTitleClose = null!;
        private FlowLayoutPanel _pnlNavContainer = null!;
        private BufferedPanel _pnlContent = null!; // 使用现有的 BufferedPanel
        private TableLayoutPanel _rootLayout = null!;
        private TableLayoutPanel _pnlMain = null!;
        private Panel _pnlSidebar = null!;
        private TableLayoutPanel _sidebarLayout = null!;
        private Panel _pnlSidebarLine = null!;
        private FlowLayoutPanel _sidebarSystemContainer = null!;
        private Panel _pnlBottom = null!;
        private Panel _themeSwitchHost = null!;
        private ThemeModeSwitch _themeSwitchButton = null!;
        // 缓存已创建的页面实例，未访问页面懒创建，降低设置面板打开和切换卡顿。
        private Dictionary<string, SettingsPageBase> _pages = new Dictionary<string, SettingsPageBase>();
        private string _currentKey = "";
        private SettingsPageBase? _visiblePage;
        private LiteButton? _btnApply;
        private Label? _statusLabel;
        private SettingsFormThemePalette? _latestThemeTransitionPalette;
        private bool _latestThemeTransitionDarkMode;
        private readonly UiDeferredActionScheduler _uiDelayScheduler;
        private readonly SettingsFormUiTickCoordinator _uiTickCoordinator;
        private string _savedSnapshot = "";
        private bool _switchingPage;
        private bool _applyingSettings;
        private bool _settingsWindowMaximized;
        private bool _suppressWindowPlacementSave;
        private Rectangle _normalWindowBounds;
        private SettingsFormWindowChrome.ResizeMessageFilter? _resizeMessageFilter;
        private readonly SettingsFormPageLifecycleCoordinator _pageLifecycleCoordinator;
        private readonly SettingsFormMainPanelTabCoordinator _mainPanelTabCoordinator;
        private readonly SettingsFormApplyCoordinator _settingsApplyCoordinator;
        private SettingsFormNavigationCoordinator _navigationCoordinator = null!;
        private SettingsFormPageSwitchCoordinator _pageSwitchCoordinator = null!;

        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            SyncSettingsWindowMaximizedFromWindowState();
            if (!_settingsWindowMaximized && WindowState == FormWindowState.Normal)
                _normalWindowBounds = Bounds;
            PersistSettingsWindowPlacement();
            PerformLayout();
            QueueSettingsContentRelayout();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            SyncSettingsWindowMaximizedFromWindowState();
            if (!_settingsWindowMaximized && WindowState == FormWindowState.Normal && !_suppressWindowPlacementSave)
                _normalWindowBounds = Bounds;

            if (IsHandleCreated && Visible)
            {
                QueueSettingsContentRelayout();
                Invalidate();
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg != SettingsFormWindowChrome.WM_NCHITTEST
                || m.Result != new IntPtr(SettingsFormWindowChrome.HTCLIENT)
                || WindowState != FormWindowState.Normal)
            {
                return;
            }

            Point screenPoint = SettingsFormWindowChrome.PointFromLParam(m.LParam);
            Point clientPoint = PointToClient(screenPoint);
            int hit = SettingsFormWindowChrome.ResolveResizeHitTest(
                ClientSize,
                clientPoint,
                UIUtils.S(8),
                allowResize: true);
            if (hit != SettingsFormWindowChrome.HTCLIENT)
                m.Result = new IntPtr(hit);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var p = new Pen(UIColors.Border);
            e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        public Settings LiveConfig => _cfg;

        public SettingsForm(Settings cfg, UIController ui, MainForm mainForm, string initialPageKey = "MainPanel")
            : this(cfg, ui, mainForm, initialPageKey, SettingsFormRuntimeServices.Resolve())
        {
        }

        internal SettingsForm(
            Settings cfg,
            UIController ui,
            MainForm mainForm,
            string initialPageKey,
            SettingsFormRuntimeServices runtimeServices)
            : this(
                cfg,
                ui,
                mainForm,
                initialPageKey,
                runtimeServices.Cs2UpdateReminder,
                runtimeServices.YouPinInventory)
        {
        }

        internal SettingsForm(
            Settings cfg,
            UIController ui,
            MainForm mainForm,
            string initialPageKey,
            ICs2UpdateReminderService cs2UpdateReminder,
            IYouPinInventoryService youPinInventory)
        {
            using var constructionMeasure = UiJankProfiler.Measure(
                "Settings.FormConstructor",
                $"InitialPage={initialPageKey}",
                thresholdMs: 1);
            _cfg = cfg;
            _ui = ui;
            _mainForm = mainForm;
            ArgumentNullException.ThrowIfNull(cs2UpdateReminder);
            ArgumentNullException.ThrowIfNull(youPinInventory);
            _uiDelayScheduler = new UiDeferredActionScheduler(() => !IsDisposed && !Disposing);
            _pageLifecycleCoordinator = new SettingsFormPageLifecycleCoordinator(
                () => IsDisposed || Disposing,
                () => IsHandleCreated,
                () => _currentKey,
                key => _pages.TryGetValue(key, out var page) ? page : null,
                SchedulePageLifecycleDelay,
                SwitchPage,
                PageOnShowDelayMs);
            _mainPanelTabCoordinator = new SettingsFormMainPanelTabCoordinator(
                () => IsDisposed || Disposing,
                () => IsHandleCreated && Created && Visible && !_switchingPage,
                () => Created,
                () => _currentKey,
                SwitchPage,
                () => _pages.TryGetValue("MainPanel", out var page) && page is IMainPanelSettingsPageHost mainPanel ? mainPanel : null,
                action => { BeginInvoke(action); },
                ScheduleMainPanelTabDelay,
                SettingsFormPageLifecycleCoordinator.IsTransientWinFormsHandleException);

            // ★★★ Draft 机制核心：创建深拷贝 ★★★
            _draftCfg = _cfg.DeepClone();
            _settingsApplyCoordinator = new SettingsFormApplyCoordinator(
                _cfg,
                _draftCfg,
                _pages,
                () => _currentKey,
                () => DeviceDpi / 96f,
                ApplyFormScaling,
                snapshot => _savedSnapshot = snapshot,
                UpdateDirtyState,
                ShowSavedStatus,
                _mainForm,
                _ui);
            UIColors.ApplySettingsTheme(_draftCfg.SettingsPanelDarkMode);
            _uiTickCoordinator = new SettingsFormUiTickCoordinator(
                () => IsDisposed || Disposing,
                () => ApplyPendingSettings(showError: false));

            InitializeComponent();

            UIUtils.ScaleControlFontAndSize(this, recursive: true, skipContentPanel: true);

            InitPages(initialPageKey);
            StartDirtyTracking();
        }

        private void InitializeComponent()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            float initialDpiScale = _mainForm != null ? _mainForm.DeviceDpi / 96f : this.DeviceDpi / 96f;
            UIUtils.UpdateScale(initialDpiScale, (float)_draftCfg.UIScale);

            var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            this.MinimumSize = SettingsFormStateModel.BuildMinimumWindowSize(workingArea);
            this.MaximizedBounds = workingArea;
            Size restoredSize = SettingsFormStateModel.BuildRestoredWindowSize(
                workingArea,
                _draftCfg.SettingsPanelWindowWidth,
                _draftCfg.SettingsPanelWindowHeight);
            this.Bounds = SettingsFormStateModel.BuildCenteredWindowBounds(workingArea, restoredSize);
            _normalWindowBounds = Bounds;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.Manual;
            this.Text = LanguageManager.T("Menu.SettingsPanel");
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.BackColor = UIColors.MainBg;
            this.ShowInTaskbar = true;
            SettingsFormWindowChrome.ApplyWindowIcon(this, _mainForm);

            _chromeLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UIColors.MainBg
            };
            _chromeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(34)));
            _chromeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _chromeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            this.Controls.Add(_chromeLayout);

            SettingsFormTitleBarChrome titleChrome = SettingsFormChromeControls.CreateTitleBar(
                LanguageManager.T("Menu.SettingsPanel"),
                Close,
                () => WindowState = FormWindowState.Minimized,
                ToggleSettingsWindowMaximized,
                TitleBar_MouseDown);
            _titleBar = titleChrome.TitleBar;
            _titleLabel = titleChrome.TitleLabel;
            _btnTitleMin = titleChrome.MinimizeButton;
            _btnTitleMax = titleChrome.MaximizeButton;
            _btnTitleClose = titleChrome.CloseButton;
            _titleBar.DoubleClick += (_, __) => ToggleSettingsWindowMaximized();
            _titleLabel.DoubleClick += (_, __) => ToggleSettingsWindowMaximized();
            _chromeLayout.Controls.Add(_titleBar, 0, 0);

            // 侧边栏
            _rootLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UIColors.MainBg
            };
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(210)));
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _chromeLayout.Controls.Add(_rootLayout, 0, 1);

            _pnlMain = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UIColors.MainBg
            };
            _pnlMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _pnlMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _pnlMain.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));

            _pnlSidebar = new Panel { Dock = DockStyle.Fill, BackColor = UIColors.SidebarBg };
            _sidebarLayout = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Height = UIUtils.S(96),
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UIColors.SidebarBg
            };
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(50)));
            _sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(46)));

            _pnlNavContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.None,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = UIUtils.S(new Padding(0, 20, 0, 0)),
                BackColor = UIColors.SidebarBg
            };

            _sidebarSystemContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.None,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = UIUtils.S(new Padding(0, 2, 0, 0)),
                BackColor = UIColors.SidebarBg
            };

            _themeSwitchHost = new Panel
            {
                Dock = DockStyle.None,
                Padding = UIUtils.S(new Padding(12, 8, 12, 12)),
                BackColor = UIColors.SidebarBg
            };
            _themeSwitchButton = SettingsFormChromeControls.CreateThemeSwitchButton(
                _draftCfg.SettingsPanelDarkMode,
                (_, __) => ToggleSettingsPanelTheme());
            _themeSwitchHost.Controls.Add(_themeSwitchButton);

            _pnlSidebarLine = new Panel { Dock = DockStyle.None, Width = 1, BackColor = UIColors.Border };
            _pnlSidebar.Controls.Add(_pnlNavContainer);
            _pnlSidebar.Controls.Add(_sidebarSystemContainer);
            _pnlSidebar.Controls.Add(_themeSwitchHost);
            _pnlSidebar.Controls.Add(_pnlSidebarLine);
            _pnlSidebar.Resize += (_, __) => LayoutSidebar();
            _navigationCoordinator = new SettingsFormNavigationCoordinator(
                _pnlNavContainer,
                _sidebarSystemContainer,
                SwitchPage,
                LayoutSidebar);
            LayoutSidebar();
            _rootLayout.Controls.Add(_pnlSidebar, 0, 0);
            _rootLayout.Controls.Add(_pnlMain, 1, 0);

            SettingsFormContentChrome contentChrome = new SettingsFormContentChromeBuilder(
                this,
                _pnlMain,
                _navigationCoordinator,
                _pageLifecycleCoordinator,
                _uiTickCoordinator,
                () => _currentKey,
                key => _currentKey = key,
                GetOrCreateSettingsPage,
                () => _visiblePage,
                page => _visiblePage = page,
                () => _draftCfg.SettingsPanelDarkMode,
                root => QueueDeferredContentNativeTheme(root),
                value => _switchingPage = value,
                () => _pages.Count).Build();
            _pnlBottom = contentChrome.BottomPanel;
            _pnlContent = contentChrome.ContentPanel;
            _btnApply = contentChrome.ApplyButton;
            _statusLabel = contentChrome.StatusLabel;
            _pageSwitchCoordinator = contentChrome.PageSwitchCoordinator;
            ApplyInitialSettingsWindowPlacement(workingArea, restoredSize);
            InstallSettingsResizeMessageFilter();
        }

        private void LayoutSidebar()
        {
            if (_pnlSidebar == null || _pnlNavContainer == null || _sidebarSystemContainer == null || _themeSwitchHost == null || _pnlSidebarLine == null) return;

            var layout = SettingsFormStateModel.BuildSidebarLayout(_pnlSidebar.ClientSize, _pnlSidebarLine.Width);
            _pnlNavContainer.Bounds = layout.NavBounds;
            _sidebarSystemContainer.Bounds = layout.SystemBounds;
            _themeSwitchHost.Bounds = layout.ThemeHostBounds;
            _themeSwitchButton.Bounds = layout.ThemeSwitchBounds;
            _pnlSidebarLine.Bounds = layout.LineBounds;
            _sidebarSystemContainer.BringToFront();
            _themeSwitchHost.BringToFront();
            _pnlSidebarLine.BringToFront();
        }

        private void QueueSettingsContentRelayout()
        {
            if (!IsHandleCreated || IsDisposed || Disposing)
                return;

            try
            {
                BeginInvoke(new Action(ForceSettingsContentRelayout));
            }
            catch
            {
                ForceSettingsContentRelayout();
            }
        }

        private void ForceSettingsContentRelayout()
        {
            if (IsDisposed || Disposing || _pnlContent == null || _pnlContent.IsDisposed)
                return;

            _rootLayout?.PerformLayout();
            _pnlMain?.PerformLayout();
            _pnlContent.PerformLayout();
            if (_visiblePage != null && !_visiblePage.IsDisposed)
            {
                _visiblePage.Dock = DockStyle.Fill;
                _visiblePage.Bounds = _pnlContent.ClientRectangle;
                _visiblePage.PerformLayout();
                _visiblePage.RequestViewportRelayout();
                _visiblePage.Invalidate(true);
            }
        }

        private void InstallSettingsResizeMessageFilter()
        {
            if (_resizeMessageFilter != null)
                return;

            _resizeMessageFilter = new SettingsFormWindowChrome.ResizeMessageFilter(
                this,
                () => WindowState == FormWindowState.Normal && Visible && !IsDisposed && !Disposing,
                CompleteManualSettingsWindowResize);
            System.Windows.Forms.Application.AddMessageFilter(_resizeMessageFilter);
        }

        private void RemoveSettingsResizeMessageFilter()
        {
            if (_resizeMessageFilter == null)
                return;

            System.Windows.Forms.Application.RemoveMessageFilter(_resizeMessageFilter);
            _resizeMessageFilter.Dispose();
            _resizeMessageFilter = null;
        }

        private void ApplyInitialSettingsWindowPlacement(Rectangle workingArea, Size restoredSize)
        {
            _suppressWindowPlacementSave = true;
            try
            {
                MaximizedBounds = workingArea;
                _normalWindowBounds = SettingsFormStateModel.BuildCenteredWindowBounds(workingArea, restoredSize);
                Bounds = _normalWindowBounds;
                if (_draftCfg.SettingsPanelWindowMaximized)
                {
                    _settingsWindowMaximized = true;
                    WindowState = FormWindowState.Maximized;
                }
                else
                {
                    _settingsWindowMaximized = false;
                    WindowState = FormWindowState.Normal;
                }
            }
            finally
            {
                _suppressWindowPlacementSave = false;
            }

            UpdateSettingsWindowMaximizeChrome();
        }

        private void ToggleSettingsWindowMaximized()
        {
            SyncSettingsWindowMaximizedFromWindowState();
            if (_settingsWindowMaximized)
                RestoreSettingsWindow();
            else
                MaximizeSettingsWindow();
        }

        private void MaximizeSettingsWindow()
        {
            if (!_settingsWindowMaximized && WindowState == FormWindowState.Normal)
                _normalWindowBounds = Bounds;

            _suppressWindowPlacementSave = true;
            try
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                _settingsWindowMaximized = true;
                WindowState = FormWindowState.Maximized;
            }
            finally
            {
                _suppressWindowPlacementSave = false;
            }

            UpdateSettingsWindowMaximizeChrome();
            QueueSettingsContentRelayout();
            PersistSettingsWindowPlacement();
        }

        private void RestoreSettingsWindow()
        {
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            Rectangle restored = SettingsFormStateModel.BuildCenteredWindowBounds(workingArea, _normalWindowBounds.Size);
            if (_normalWindowBounds.Width > 0 && _normalWindowBounds.Height > 0)
            {
                Size clamped = SettingsFormStateModel.ClampWindowSize(_normalWindowBounds.Size, workingArea);
                int left = Math.Clamp(_normalWindowBounds.Left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - clamped.Width));
                int top = Math.Clamp(_normalWindowBounds.Top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - clamped.Height));
                restored = new Rectangle(left, top, clamped.Width, clamped.Height);
            }

            _suppressWindowPlacementSave = true;
            try
            {
                _settingsWindowMaximized = false;
                if (WindowState != FormWindowState.Normal)
                    WindowState = FormWindowState.Normal;
                Bounds = restored;
                _normalWindowBounds = Bounds;
            }
            finally
            {
                _suppressWindowPlacementSave = false;
            }

            UpdateSettingsWindowMaximizeChrome();
            QueueSettingsContentRelayout();
            PersistSettingsWindowPlacement();
        }

        private void UpdateSettingsWindowMaximizeChrome()
        {
            if (_btnTitleMax != null)
                SettingsFormChromeControls.UpdateMaximizeButton(_btnTitleMax, _settingsWindowMaximized);
        }

        private void SyncSettingsWindowMaximizedFromWindowState()
        {
            if (WindowState == FormWindowState.Minimized)
                return;

            bool isMaximized = WindowState == FormWindowState.Maximized;
            if (_settingsWindowMaximized == isMaximized)
                return;

            _settingsWindowMaximized = isMaximized;
            UpdateSettingsWindowMaximizeChrome();
        }

        private void PersistSettingsWindowPlacement()
        {
            if (_suppressWindowPlacementSave || IsDisposed || Disposing)
                return;

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            SyncSettingsWindowMaximizedFromWindowState();
            Rectangle normalBounds = _settingsWindowMaximized ? _normalWindowBounds : Bounds;
            Size savedSize = SettingsFormStateModel.ClampWindowSize(normalBounds.Size, workingArea);

            _cfg.SettingsPanelWindowWidth = savedSize.Width;
            _cfg.SettingsPanelWindowHeight = savedSize.Height;
            _cfg.SettingsPanelWindowMaximized = _settingsWindowMaximized;
            _draftCfg.SettingsPanelWindowWidth = savedSize.Width;
            _draftCfg.SettingsPanelWindowHeight = savedSize.Height;
            _draftCfg.SettingsPanelWindowMaximized = _settingsWindowMaximized;
            SettingsSaveResult saveResult = _cfg.Save();
            if (!saveResult.Succeeded)
            {
                UpdateDirtyState(false);
                if (_statusLabel != null)
                {
                    _statusLabel.Text = "窗口设置保存失败";
                    _statusLabel.ForeColor = UIColors.TextWarn;
                }
                return;
            }

            _savedSnapshot = Snapshot(_cfg);
            UpdateDirtyState(false);
        }

        private void CompleteManualSettingsWindowResize()
        {
            SyncSettingsWindowMaximizedFromWindowState();
            if (!_settingsWindowMaximized && WindowState == FormWindowState.Normal)
                _normalWindowBounds = Bounds;

            PersistSettingsWindowPlacement();
            PerformLayout();
            QueueSettingsContentRelayout();
            Invalidate();
        }

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            SyncSettingsWindowMaximizedFromWindowState();
            if (e.Clicks > 1 || _settingsWindowMaximized)
                return;

            SettingsFormWindowChrome.BeginWindowDrag(this, e.Button);
        }

        private void UpdateThemeSwitchButton(ThemeModeSwitch? button = null)
        {
            button ??= _themeSwitchButton;
            if (button == null) return;

            SettingsFormChromeControls.UpdateThemeSwitchButton(button, _draftCfg.SettingsPanelDarkMode);
        }

        private void ToggleSettingsPanelTheme()
        {
            if (_themeSwitchButton.Enabled == false) return;
            string pageKey = _currentKey;
            string? mainTab = null;
            if (pageKey == "MainPanel"
                && _pages.TryGetValue("MainPanel", out var page)
                && page is IMainPanelSettingsPageHost mainPanel)
            {
                mainTab = mainPanel.ActiveTab;
            }

            var stopwatch = Stopwatch.StartNew();
            _themeSwitchButton.Enabled = false;
            _uiTickCoordinator.Pause();
            using (SettingsFormThemeTreeStyler.FreezeRedraw(this))
            {
                SuspendLayout();
                _rootLayout.SuspendLayout();
                _pnlContent.SuspendLayout();
                try
                {
                    var previousPalette = SettingsFormThemePalette.Capture();
                    _draftCfg.SettingsPanelDarkMode = !_draftCfg.SettingsPanelDarkMode;
                    UIColors.ApplySettingsTheme(_draftCfg.SettingsPanelDarkMode);
                    _latestThemeTransitionPalette = previousPalette;
                    _latestThemeTransitionDarkMode = _draftCfg.SettingsPanelDarkMode;
                    ApplyShellThemeColors(includeContentTree: false, previousPalette);
                    QueueDeferredContentNativeTheme(
                        GetVisibleContentThemeRoot(),
                        previousPalette,
                        applyManagedTheme: true);
                    if (pageKey == "MainPanel"
                        && mainTab != null
                        && _pages.TryGetValue("MainPanel", out var currentPage)
                        && currentPage is IMainPanelSettingsPageHost currentMainPanel)
                    {
                        currentMainPanel.SelectTab(mainTab);
                    }
                    UpdateDirtyState();
                }
                finally
                {
                    _pnlContent.ResumeLayout(false);
                    _rootLayout.ResumeLayout(false);
                    ResumeLayout(false);
                    _themeSwitchButton.Enabled = true;
                    _uiTickCoordinator.Resume();
                }
            }

            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds >= 300)
            {
                DiagnosticsLogger.Info("Settings", $"Theme switch slow. Page={pageKey}; CachedPages={_pages.Count}; ElapsedMs={stopwatch.ElapsedMilliseconds}");
            }
        }

        private void DropHiddenPagesForTheme(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey) || !_pages.TryGetValue(pageKey, out var currentPage) || currentPage.IsDisposed)
            {
                RecreatePagesForTheme(pageKey, null);
                return;
            }

            var removeKeys = new List<string>();
            foreach (var pair in _pages)
            {
                if (!string.Equals(pair.Key, pageKey, StringComparison.Ordinal))
                    removeKeys.Add(pair.Key);
            }

            foreach (var key in removeKeys)
            {
                if (_pages.TryGetValue(key, out var page) && !page.IsDisposed)
                    page.Dispose();
                _pages.Remove(key);
            }

            _visiblePage = currentPage;
        }

        private void RecreatePagesForTheme(string pageKey, string? mainTab)
        {
            _pnlContent.SuspendLayout();
            try
            {
                var pagesToDispose = new List<SettingsPageBase>(_pages.Values);
                foreach (var page in pagesToDispose)
                {
                    if (!page.IsDisposed)
                        page.Dispose();
                }

                _pages.Clear();
                _visiblePage = null;
                _pnlContent.Controls.Clear();
                _currentKey = "";
                SwitchPage(string.IsNullOrWhiteSpace(pageKey) ? "MainPanel" : pageKey);
                if (pageKey == "MainPanel"
                    && mainTab != null
                    && _pages.TryGetValue("MainPanel", out var newPage)
                    && newPage is IMainPanelSettingsPageHost newMainPanel)
                {
                    newMainPanel.SelectTab(mainTab);
                }
            }
            finally
            {
                _pnlContent.ResumeLayout(true);
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            UIUtils.UpdateScale(this.DeviceDpi / 96f, (float)_draftCfg.UIScale);
            ApplyFormScaling();
        }

        private void ApplyFormScaling()
        {
            SuspendLayout();

            // 1. Keep the user's window size while refreshing scale-dependent chrome metrics.
            var workingArea = Screen.FromControl(this).WorkingArea;
            MaximizedBounds = workingArea;
            this.MinimumSize = SettingsFormStateModel.BuildMinimumWindowSize(workingArea);
            _suppressWindowPlacementSave = true;
            try
            {
                SyncSettingsWindowMaximizedFromWindowState();
                if (_settingsWindowMaximized)
                {
                    if (WindowState != FormWindowState.Maximized)
                        WindowState = FormWindowState.Maximized;
                }
                else
                {
                    Size clamped = SettingsFormStateModel.ClampWindowSize(Size, workingArea);
                    if (Size != clamped)
                        Size = clamped;
                    _normalWindowBounds = Bounds;
                }
            }
            finally
            {
                _suppressWindowPlacementSave = false;
            }

            // 2. Update RowStyles and ColumnStyles of TableLayoutPanels
            if (_chromeLayout.RowStyles.Count > 0)
                _chromeLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, UIUtils.S(34));

            if (_rootLayout.ColumnStyles.Count > 0)
                _rootLayout.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, UIUtils.S(210));

            if (_pnlMain.RowStyles.Count > 1)
                _pnlMain.RowStyles[1] = new RowStyle(SizeType.Absolute, 0);

            if (_sidebarLayout.RowStyles.Count > 1)
            {
                _sidebarLayout.RowStyles[0] = new RowStyle(SizeType.Absolute, UIUtils.S(50));
                _sidebarLayout.RowStyles[1] = new RowStyle(SizeType.Absolute, UIUtils.S(46));
            }

            // 3. Update heights / widths / paddings / margins of specific controls
            _sidebarLayout.Height = UIUtils.S(96);
            _pnlNavContainer.Padding = UIUtils.S(new Padding(0, 20, 0, 0));
            _sidebarSystemContainer.Padding = UIUtils.S(new Padding(0, 2, 0, 0));
            _pnlBottom.Padding = Padding.Empty;

            SettingsFormChromeControls.ApplyTitleBarScale(_titleBar, _titleLabel, _btnTitleMin, _btnTitleMax, _btnTitleClose);
            UpdateSettingsWindowMaximizeChrome();

            // 4. Scale font of SettingsForm controls recursively (excluding _pnlContent)
            UIUtils.ScaleControlFontAndSize(this, recursive: true, skipContentPanel: true);

            // 5. Recreate pages with new scale
            RecreatePagesForTheme(_currentKey, null);

            ResumeLayout(true);
        }

        private void ApplyShellThemeColors(bool includeContentTree = true, SettingsFormThemePalette? previousPalette = null)
        {
            BackColor = UIColors.MainBg;
            _chromeLayout.BackColor = UIColors.MainBg;
            _rootLayout.BackColor = UIColors.MainBg;
            _pnlMain.BackColor = UIColors.MainBg;
            _pnlSidebar.BackColor = UIColors.SidebarBg;
            _sidebarLayout.BackColor = UIColors.SidebarBg;
            _pnlSidebarLine.BackColor = UIColors.Border;
            _pnlNavContainer.BackColor = UIColors.SidebarBg;
            _sidebarSystemContainer.BackColor = UIColors.SidebarBg;
            _themeSwitchHost.BackColor = UIColors.SidebarBg;
            _pnlBottom.BackColor = UIColors.MainBg;
            _pnlContent.BackColor = UIColors.MainBg;
            if (_statusLabel != null)
                _statusLabel.ForeColor = _btnApply?.Enabled == true ? UIColors.TextWarn : UIColors.TextSub;

            UpdateThemeSwitchButton();
            SettingsFormChromeControls.ApplyTitleBarTheme(_titleBar, _titleLabel, _btnTitleMin, _btnTitleMax, _btnTitleClose);
            SettingsFormThemeTreeStyler.ApplyToControlTree(_pnlSidebar, previousPalette);
            SettingsFormThemeTreeStyler.ApplyToControlTree(_pnlBottom, previousPalette);
            if (includeContentTree)
                SettingsFormThemeTreeStyler.ApplyToControlTree(GetVisibleContentThemeRoot(), previousPalette);

            UIColors.ApplyNativeTheme(this);
            UIColors.ApplyNativeThemeRecursively(_pnlSidebar);
            UIColors.ApplyNativeThemeRecursively(_pnlBottom);
            if (includeContentTree)
                QueueDeferredContentNativeTheme(GetVisibleContentThemeRoot());
            if (includeContentTree)
            {
                if (_visiblePage != null && !_visiblePage.IsDisposed)
                {
                    _visiblePage.LastAppliedThemeDarkMode = _draftCfg.SettingsPanelDarkMode;
                    _visiblePage.OnThemeChanged();
                }
            }
            Invalidate(true);
        }

        private Control GetVisibleContentThemeRoot()
        {
            return _visiblePage != null && !_visiblePage.IsDisposed ? _visiblePage : _pnlContent;
        }

        private void QueueDeferredContentNativeTheme(
            Control? root,
            SettingsFormThemePalette? previousPalette = null,
            bool applyManagedTheme = false)
        {
            if (root == null || root.IsDisposed)
                return;

            bool darkMode = _draftCfg.SettingsPanelDarkMode;
            bool needsManagedTheme = applyManagedTheme
                || root is SettingsPageBase page && page.LastAppliedThemeDarkMode != darkMode;
            SettingsFormThemePalette? effectivePreviousPalette = previousPalette;
            if (!effectivePreviousPalette.HasValue
                && needsManagedTheme
                && _latestThemeTransitionPalette.HasValue
                && _latestThemeTransitionDarkMode == darkMode)
            {
                effectivePreviousPalette = _latestThemeTransitionPalette;
            }
            string delayKey = SettingsFormDeferredActionKeys.ContentNativeTheme
                + ":"
                + RuntimeHelpers.GetHashCode(root).ToString();

            _uiDelayScheduler.Schedule(
                delayKey,
                DeferredContentNativeThemeDelayMs,
                () => ApplyDeferredContentTheme(root, darkMode, effectivePreviousPalette, needsManagedTheme));
        }

        private void ApplyDeferredContentTheme(
            Control root,
            bool darkMode,
            SettingsFormThemePalette? previousPalette,
            bool applyManagedTheme)
        {
            if (root.IsDisposed || IsDisposed || Disposing)
                return;

            if (darkMode != _draftCfg.SettingsPanelDarkMode)
                return;

            try
            {
                using (SettingsFormThemeTreeStyler.FreezeRedraw(root))
                {
                    if (applyManagedTheme)
                        SettingsFormThemeTreeStyler.ApplyToControlTree(root, previousPalette);
                    SettingsFormThemeTreeStyler.ApplyNativeThemeToInteractiveTree(root);
                }

                if (applyManagedTheme && root is SettingsPageBase themedPage && !themedPage.IsDisposed)
                {
                    themedPage.OnThemeChanged();
                    themedPage.LastAppliedThemeDarkMode = darkMode;
                }

                root.Invalidate(true);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("Settings", "Deferred content native theme skipped: " + ex.Message);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            RemoveSettingsResizeMessageFilter();
            if (_visiblePage != null)
            {
                _pageLifecycleCoordinator.SafeInvokeOnHide(_visiblePage, _currentKey);
                _visiblePage = null;
            }

            DisposeSettingsUiTimers();
            float dpiScale = _mainForm != null ? _mainForm.DeviceDpi / 96f : 1.0f;
            UIUtils.UpdateScale(dpiScale, (float)_cfg.UIScale);
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                RemoveSettingsResizeMessageFilter();

            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SyncSettingsWindowMaximizedFromWindowState();
            if (!_settingsWindowMaximized && WindowState == FormWindowState.Normal)
                _normalWindowBounds = Bounds;
            PersistSettingsWindowPlacement();
            ApplyPendingSettings(showError: true);
            base.OnFormClosing(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _pageLifecycleCoordinator.InvokePendingPageOnShow();
            _mainPanelTabCoordinator.InvokePending();
        }

        private void StartDirtyTracking()
        {
            _savedSnapshot = Snapshot(_cfg);
            UpdateDirtyState(false);
            _uiTickCoordinator.StartDirtyTracking();
        }

        private void DisposeSettingsUiTimers()
        {
            _uiTickCoordinator.Dispose();
            _navigationCoordinator.Dispose();

            _uiDelayScheduler.Dispose();
        }

        private string Snapshot(Settings settings)
        {
            return SettingsFormStateModel.CaptureSnapshot(settings);
        }

        private void UpdateDirtyState(bool allowSavedMessage = true)
        {
            if (_btnApply == null || _statusLabel == null) return;

            var state = SettingsFormStateModel.BuildDirtyStatus(_draftCfg, _savedSnapshot);
            _btnApply.Enabled = state.ApplyEnabled;
            _statusLabel.Text = state.Text;
            _statusLabel.ForeColor = state.ForeColor;
        }

        private void ShowSavedStatus()
        {
            if (_statusLabel == null) return;

            var state = SettingsFormStateModel.BuildSavedStatus();
            _statusLabel.Text = state.Text;
            _statusLabel.ForeColor = state.ForeColor;
        }

        private bool IsDraftDirty()
        {
            return SettingsFormStateModel.IsDirty(_draftCfg, _savedSnapshot);
        }

        private bool ApplyPendingSettings(bool showError)
        {
            if (_applyingSettings || _switchingPage || IsDisposed || Disposing)
                return false;

            try
            {
                _visiblePage?.Save();
                if (!IsDraftDirty())
                    return false;

                _applyingSettings = true;
                _settingsApplyCoordinator.Apply(this);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Settings", $"Auto apply settings failed. CurrentPage={_currentKey}; LoadedPages={string.Join(",", _pages.Keys)}", ex);
                if (_statusLabel != null)
                {
                    _statusLabel.Text = "自动应用失败：" + ex.Message;
                    _statusLabel.ForeColor = UIColors.TextWarn;
                }
                if (showError)
                {
                    GlobalPromptService.Show(this, "自动应用设置失败：\n" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
            finally
            {
                _applyingSettings = false;
            }
        }

        private void InitPages(string initialPageKey)
        {
            SettingsPageRegistry.ValidateProductionRoutes();
            _pages.Clear();
            RebuildNavigation();

            string normalized = string.IsNullOrWhiteSpace(initialPageKey)
                ? "MainPanel"
                : SettingsPageRegistry.NormalizeKey(initialPageKey);
            SwitchPage(normalized);
        }

        private void RebuildNavigation()
        {
            _navigationCoordinator.Rebuild();
        }

        private SettingsPageBase GetOrCreateSettingsPage(string key)
        {
            key = SettingsPageRegistry.NormalizeKey(key);
            if (_pages.TryGetValue(key, out var page))
                return page;

            using (UiJankProfiler.Measure("Settings.CreatePage", $"Page={key}", thresholdMs: 1))
            {
                page = CreateSettingsPage(key);
                page.AutoScaleMode = AutoScaleMode.None;
                using (UiJankProfiler.Measure("Settings.PageSetContext", $"Page={key}", thresholdMs: 1))
                {
                    page.SetContext(_draftCfg, _mainForm, _ui);
                }

                using (UiJankProfiler.Measure("Settings.PageScale", $"Page={key}", thresholdMs: 1))
                {
                    UIUtils.ScaleControlFontAndSize(page, recursive: true, skipContentPanel: false);
                }
            }

            _pages[key] = page;
            return page;
        }

        private SettingsPageBase CreateSettingsPage(string key)
        {
            return SettingsPageRegistry.CreatePage(key);
        }

        public void SwitchPage(string key)
        {
            string pageKey = SettingsPageRegistry.GetBaseKey(key);
            string? subRoute = SettingsPageRegistry.GetSubRoute(key);

            _pageSwitchCoordinator.SwitchPage(pageKey);
            if (!string.IsNullOrWhiteSpace(subRoute))
            {
                ScheduleSubRouteDelay(PageOnShowDelayMs + 30, () => SwitchVisibleSubRoute(pageKey, subRoute));
            }
        }

        private void SchedulePageLifecycleDelay(int delayMs, Action action)
        {
            ScheduleUiDelay(SettingsFormDeferredActionKeys.PageLifecycle, delayMs, action);
        }

        private void ScheduleMainPanelTabDelay(int delayMs, Action action)
        {
            ScheduleUiDelay(SettingsFormDeferredActionKeys.MainPanelTab, delayMs, action);
        }

        private void ScheduleSubRouteDelay(int delayMs, Action action)
        {
            ScheduleUiDelay(SettingsFormDeferredActionKeys.SubRoute, delayMs, action);
        }

        private void ScheduleUiDelay(string key, int delayMs, Action action)
        {
            _uiDelayScheduler.Schedule(key, delayMs, action);
        }

        public void SwitchMainPanelTab(string tabKey)
        {
            _mainPanelTabCoordinator.SwitchTab(tabKey);
        }

        private void SwitchVisibleSubRoute(string pageKey, string subRoute)
        {
            string normalizedPageKey = SettingsPageRegistry.GetBaseKey(pageKey);
            if (!_pages.TryGetValue(normalizedPageKey, out var page))
                return;

            if (page is ISettingsSubRouteHost subRouteHost)
            {
                if (!subRouteHost.SwitchSubRoute(subRoute))
                {
                    DiagnosticsLogger.Info("Settings", $"Unknown sub-route ignored. Page={normalizedPageKey}; SubRoute={subRoute}");
                }
            }
        }

        public static void PerformFactoryReset(IWin32Window? owner)
        {
            SettingsFormFactoryReset.Perform(owner);
        }

    }

}
