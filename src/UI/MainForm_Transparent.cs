using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.Helpers;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    public class MainForm : Form, ILayeredRenderTarget
    {
        // 主悬浮窗创建时需要完整配置初始化窗口行为；运行期刷新再走快照和渲染调度。
        private readonly Settings _cfg = Settings.Load();
        private UIController? _ui;

        // ★★★ 双助手架构 ★★★
        private readonly MainFormWinHelper _winHelper;
        private readonly MainFormBizHelper _bizHelper;
        private readonly IRenderScheduler _renderScheduler;
        private readonly int _wmTaskbarCreated;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int OpenSettingsReadyRetryDelayMs = 100;
        private const int OpenSettingsReadyMaxAttempts = 30;
        private CancellationTokenSource? _displayChangeCts;
        private readonly bool _openSettingsOnStartup;
        private readonly string? _startupSettingsPage;
        private bool _startupMarketRefreshRequested;
        private Bitmap? _layeredRenderBuffer;
        private Size _layeredRenderBufferSize;
        private string _lastLayeredRenderSignature = "";
        private bool _lastLayeredRenderSucceeded;
        private static readonly Color InteractiveHitSurface = Color.FromArgb(1, 0, 0, 0);

        private Point _dragOffset;
        // 防止 Win11 自动隐藏无边框 + 无任务栏窗口
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (_cfg?.ShowMainWindowInTaskbar == true)
                {
                    cp.ExStyle |= WS_EX_APPWINDOW;
                    cp.ExStyle &= ~WS_EX_TOOLWINDOW;
                }
                else
                {
                    cp.ExStyle |= WS_EX_TOOLWINDOW;
                    cp.ExStyle &= ~WS_EX_APPWINDOW;
                }

                if (_cfg != null && _cfg.TopMost)
                {
                    cp.ExStyle |= WS_EX_TOPMOST; // 防止句柄重建后丢失置顶样式
                }

                cp.ExStyle |= WS_EX_LAYERED; // 支持绘制层的背景/文字 alpha，不依赖整窗 Opacity。

                // [Fix] 启动时应用鼠标穿透配置，防止因句柄重建导致样式丢失
                if (_cfg != null && _cfg.ClickThrough)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }

                return cp;
            }
        }

        // ========== 代理方法 (保持兼容性) ==========
        public void SetClickThrough(bool enable)
        {
            _winHelper.SetClickThrough(enable);
            RequestLayeredRender();
        }
        public void SetWindowsTaskbarButton(bool enable)
        {
            bool wasVisible = Visible;
            var oldBounds = Bounds;
            var oldState = WindowState;

            _cfg.ShowMainWindowInTaskbar = enable;
            ApplyApplicationIcon();
            ShowInTaskbar = enable;

            if (IsHandleCreated)
            {
                RecreateHandle();
                if (wasVisible && !Visible) Show();
                if (oldState == FormWindowState.Normal) Bounds = oldBounds;
                WindowState = oldState;
            }

            _winHelper.RefreshTopMost(_cfg.TopMost, forceReinsert: true);
            _winHelper.ApplyRoundedCorners();
            _bizHelper?.RebuildMenus();
            RequestLayeredRender();
        }
        public void InitAutoHideTimer() => _bizHelper.StartTimer();
        public void StopAutoHideTimer(bool restoreHidden = false) => _bizHelper.StopTimer(restoreHidden);
        public void HideTrayIcon()
        {
            _cfg.HideTrayIcon = true;
            if (EnsureReachableEntryPoint("HideTrayIcon"))
            {
                _bizHelper.SetTrayVisible(true);
                return;
            }

            _bizHelper.SetTrayVisible(false);
        }
        public void ShowTrayIcon() => _bizHelper.SetTrayVisible(true);
        public void RebuildMenus() => _bizHelper.RebuildMenus();
        public void ShowNotification(string title, string text, ToolTipIcon icon) => _bizHelper.ShowNotification(title, text, icon);
        public bool TryShowNotification(string title, string text, ToolTipIcon icon) => _bizHelper.TryShowNotification(title, text, icon);

        // 供 Helper 调用
        public void ToggleLayoutMode() => _bizHelper.ToggleLayoutMode();
        public void EnsureVisibleAndSavePos() => _bizHelper.SavePos();
        public void ClampFloatingWindow(bool force = false) => _bizHelper.ClampToScreen(force);
        public void ApplyRoundedCorners() => _winHelper.ApplyRoundedCorners();
        public void RefreshTopMost(bool forceReinsert = false)
        {
            _winHelper.RefreshTopMost(_cfg.TopMost, forceReinsert);
        }

        // 供外部调用
        public void OpenTaskManager() => _bizHelper.OpenTaskManager();
        public void OpenSettings() => _bizHelper.OpenSettings();
        public void HandleForwardedStartupArgs(string[] args)
        {
            if (IsDisposed || Disposing) return;

            string? startupSettingsPage = ExtractStartupSettingsPage(args);
            bool openSettings = !string.IsNullOrWhiteSpace(startupSettingsPage)
                || args.Any(arg => string.Equals(arg, "--open-settings", StringComparison.OrdinalIgnoreCase));

            DiagnosticsLogger.Info(
                "Startup",
                $"Received forwarded startup args. OpenSettings={openSettings}; Page={DiagnosticsLogger.Redact(startupSettingsPage ?? string.Empty)}");

            if (openSettings)
            {
                QueueOpenCommandLineSettingsPage(startupSettingsPage, "ForwardedStartupArgs");
                return;
            }

            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            _bizHelper.ForceShow(10.0);
            BringToFront();
            Activate();
        }

        public void ToggleMainWindowFromEntryPoint() => _bizHelper.ToggleMainWindowFromTray();

        // ==== 任务栏显示 ====
        private TaskbarForm? _taskbar;

        public void ToggleTaskbar(bool show)
        {
            if (show)
            {
                if (_taskbar != null && !_taskbar.IsDisposed)
                {
                    if (_taskbar.TargetDevice != _cfg.TaskbarMonitorDevice)
                    {
                        _taskbar.Close();
                        _taskbar.Dispose();
                        _taskbar = null;
                    }
                }

                if (_taskbar == null || _taskbar.IsDisposed)
                {
                    if (_ui != null)
                    {
                        _taskbar = new TaskbarForm(_cfg, _ui, this, _renderScheduler);
                        _taskbar.Show();
                    }
                }
                else
                {
                    if (!_taskbar.Visible)
                    {
                        _taskbar.Show();
                        _taskbar.ReloadLayout();
                    }
                }
            }
            else
            {
                if (_taskbar != null)
                {
                    _taskbar.Close();
                    _taskbar.Dispose();
                    _taskbar = null;
                }
            }
        }

        // ========== 构造函数 ==========
        public MainForm(bool openSettingsOnStartup = false, string? startupSettingsPage = null)
            : this(openSettingsOnStartup, startupSettingsPage, MainFormRuntimeServices.Resolve())
        {
        }

        internal MainForm(bool openSettingsOnStartup, string? startupSettingsPage, MainFormRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _openSettingsOnStartup = openSettingsOnStartup;
            _startupSettingsPage = startupSettingsPage;
            _renderScheduler = runtimeServices.RenderScheduler;

            // 语言加载
            if (string.IsNullOrEmpty(_cfg.Language))
            {
                _cfg.Language = "zh";
            }
            LanguageManager.Load(_cfg.Language);
            _cfg.SyncToLanguage();
            EnsureReachableEntryPoint("Startup");
            ApplyApplicationIcon();

            // 1. 初始化业务
            // ★★★ Fix: 初始化全局 DPI 缩放系数，防止未打开设置面板时弹窗排版异常 ★★★
            UIUtils.UpdateScale(this.DeviceDpi / 96f, (float)_cfg.UIScale);

            // 轻量版只初始化市场监控与界面显示，不启动上游硬件/插件/历史模块。
            MarketDataSourceManager.Configure(_cfg, requestInitialRefresh: false);
            RequestStartupMarketRefresh();

            _ui = new UIController(_cfg, this);

            runtimeServices.YouPinInventory.Configure(_cfg);
            runtimeServices.YouPinSaleReminders.Configure(_cfg);
            runtimeServices.YouPinLandlordAutomation.Configure(_cfg);

            // 5. 设置背景色 (这是关键！解耦时漏掉了这行，导致背景是系统默认色而非透明或皮肤色)
            BackColor = ThemeManager.ParseColor(string.IsNullOrWhiteSpace(_cfg.PanelBackgroundColor)
                ? ThemeManager.Current.Color.Background
                : _cfg.PanelBackgroundColor);

            // 2. 初始化双助手
            _winHelper = new MainFormWinHelper(this);

            //资源管理器重启监听
            _wmTaskbarCreated = MainFormWinHelper.RegisterTaskbarCreatedMessage();

            // ★★★ 关键修复：补全 SetStyle 调用，开启透明支持 ★★★
            // 原始代码中这里调用了 SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            // 解耦时漏掉了这一行，导致背景无法透明，显示为黑色或系统默认色
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _winHelper.InitializeStyle(_cfg.TopMost, _cfg.ClickThrough, _cfg.ShowMainWindowInTaskbar);

            // 窗体整体透明度不再用 Form.Opacity 控制；背景/文字透明度由 UpdateLayeredWindow 的像素 alpha 控制。
            // Form.Opacity 会调用 SetLayeredWindowAttributes，之后会干扰 per-pixel alpha 更新。

            _bizHelper = new MainFormBizHelper(this, _cfg, _ui, _winHelper);
            _bizHelper.Initialize();

            // 3. 事件绑定
            BindEvents();
        }

        private void ApplyApplicationIcon()
        {
            try
            {
                var icon = global::CS2TradeMonitor.Properties.Resources.AppIcon;
                if (icon != null)
                    Icon = (Icon)icon.Clone();
            }
            catch
            {
                // Keep the designer/default icon if the embedded resource cannot be loaded.
            }
        }

        private void BindEvents()
        {
            // 拖拽
            MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && !_cfg.LockPosition)
                {
                    _ui?.SetDragging(true);
                    _bizHelper.IsDragging = true;
                    _dragOffset = e.Location;
                }
            };
            MouseMove += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && !_cfg.LockPosition)
                {
                    if (Math.Abs(e.X - _dragOffset.X) + Math.Abs(e.Y - _dragOffset.Y) < 1) return;
                    Location = new Point(Left + e.X - _dragOffset.X, Top + e.Y - _dragOffset.Y);
                }
            };
            MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left && _bizHelper.IsDragging)
                {
                    _ui?.SetDragging(false);
                    _bizHelper.IsDragging = false;
                    _bizHelper.ClampToScreen();
                    _bizHelper.SavePos();
                }
            };

            // 双击
            this.DoubleClick += (_, __) => _bizHelper.HandleDoubleClick();

            // DPI / Resize
            this.Resize += (_, __) =>
            {
                _winHelper.ApplyRoundedCorners();
                RequestLayeredRender();
            };
            this.VisibleChanged += (_, __) => { if (Visible) _winHelper.RefreshTopMost(_cfg.TopMost, forceReinsert: true); };
            this.HandleCreated += (_, __) => _winHelper.RefreshTopMost(_cfg.TopMost, forceReinsert: true);
        }

        public void ShowMainWindow()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
            _cfg.HideMainForm = false;
            _cfg.Save();

            _bizHelper.ForceShow(30.0);
            _winHelper.RefreshTopMost(_cfg.TopMost, forceReinsert: true);
            _winHelper.ApplyRoundedCorners();
            _bizHelper.RebuildMenus();
            RequestLayeredRender();
        }

        public void HideMainWindow()
        {
            _cfg.HideMainForm = true;
            bool keptTray = EnsureReachableEntryPoint("HideMainWindow");
            if (keptTray)
            {
                _bizHelper.SetTrayVisible(true);
            }

            this.Hide();
            _cfg.Save();
            _bizHelper.RebuildMenus();
        }

        private bool EnsureReachableEntryPoint(string source)
        {
            if (Settings.HasNoInteractiveEntry(_cfg))
            {
                _cfg.HideTrayIcon = false;
                _cfg.Save();
                DiagnosticsLogger.Info("Visibility", $"{source}: unsafe hidden state corrected by keeping tray icon visible.");
                return true;
            }

            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDBLCLK)
            {
                DiagnosticsLogger.Info("Input", "Main form double-click message received.");
                _bizHelper?.HandleDoubleClick();
                return;
            }

            if (m.Msg == WM_CONTEXTMENU || m.Msg == WM_RBUTTONUP)
            {
                DiagnosticsLogger.Info("Input", "Main form context menu message received.");
                ShowContextMenuAtCursor();
                return;
            }

            if (m.Msg == _wmTaskbarCreated && _wmTaskbarCreated != 0)
            {
                // [Fix] Explorer 重启后，子窗口 TaskbarForm 会被销毁或失效。
                // 必须由主窗口感知并重建 TaskbarForm，才能恢复显示。
                if (_cfg.ShowTaskbar)
                {
                    // 延迟执行，确保 Explorer 初始化基本完成，同时避免阻塞消息泵
                    this.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            await Task.Delay(3000); // 等待3秒让Explorer缓口气
                            ToggleTaskbar(false);   // 彻底销毁旧实例
                            ToggleTaskbar(true);    // 创建新实例(新实例自带重试机制)
                        }
                        catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
                    }));
                }
            }

            if (m.Msg == WM_DISPLAYCHANGE)
            {
                // [Fix #288] 分辨率改变后，延迟执行位置恢复，确保 Screen.AllScreens 已完全更新
                // 增加防抖机制，避免短时间内多次触发
                _displayChangeCts?.Cancel();
                _displayChangeCts = new System.Threading.CancellationTokenSource();
                var token = _displayChangeCts.Token;

                Task.Delay(500, token).ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                    {
                        this.BeginInvoke(new Action(() => _bizHelper?.RestorePos()));
                    }
                });
            }

            base.WndProc(ref m);
        }

        private void ShowContextMenuAtCursor()
        {
            try
            {
                if (ContextMenuStrip == null || ContextMenuStrip.IsDisposed)
                {
                    RebuildMenus();
                }

                if (ContextMenuStrip == null || ContextMenuStrip.IsDisposed) return;

                MainFormWinHelper.ActivateWindow(Handle);
                ContextMenuStrip.Show(Cursor.Position);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Menu", "Main form context menu failed.", ex);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (UiJankProfiler.Measure("MainForm.OnPaint", $"Size={ClientSize.Width}x{ClientSize.Height}", thresholdMs: 8))
            {
                if (TryRenderLayeredWindow())
                    return;

                base.OnPaint(e);
                _ui?.Render(e.Graphics);
            }
        }

        public void RequestLayeredRender()
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(RequestLayeredRender)); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
                return;
            }

            if (!IsHandleCreated)
                return;

            _renderScheduler.RequestRender(this);
        }

        public void CommitLayeredFrame()
        {
            TryRenderLayeredWindow();
        }

        private bool TryRenderLayeredWindow()
        {
            if (_ui == null || !IsHandleCreated || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return false;

            try
            {
                string signature = BuildLayeredRenderSignature();
                if (_lastLayeredRenderSucceeded && string.Equals(signature, _lastLayeredRenderSignature, StringComparison.Ordinal))
                    return true;

                using (UiJankProfiler.Measure("MainForm.LayeredRender", $"Size={ClientSize.Width}x{ClientSize.Height}; Version={_ui.RenderVersion}", thresholdMs: 8))
                {
                    Bitmap bitmap = GetLayeredRenderBuffer();
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.Transparent);
                        _ui.Render(g);
                        ApplyInteractiveHitSurface(g, bitmap.Size, _cfg.ClickThrough);
                    }

                    bool updated = _winHelper.UpdateLayeredBitmap(bitmap, Location);
                    _lastLayeredRenderSucceeded = updated;
                    if (updated)
                        _lastLayeredRenderSignature = signature;
                    return updated;
                }
            }
            catch
            {
                _lastLayeredRenderSucceeded = false;
                return false;
            }
        }

        private string BuildLayeredRenderSignature()
        {
            long renderVersion = _ui?.RenderVersion ?? 0;
            return string.Concat(
                Handle.ToInt64().ToString(),
                "|",
                ClientSize.Width.ToString(),
                "x",
                ClientSize.Height.ToString(),
                "|",
                Location.X.ToString(),
                ",",
                Location.Y.ToString(),
                "|",
                _cfg.ClickThrough ? "1" : "0",
                "|",
                renderVersion.ToString());
        }

        private static void ApplyInteractiveHitSurface(Graphics graphics, Size size, bool clickThrough)
        {
            if (clickThrough || size.Width <= 0 || size.Height <= 0)
                return;

            CompositingMode previousMode = graphics.CompositingMode;
            graphics.CompositingMode = CompositingMode.SourceOver;
            using var brush = new SolidBrush(InteractiveHitSurface);
            graphics.FillRectangle(brush, 0, 0, size.Width, size.Height);
            graphics.CompositingMode = previousMode;
        }

        private Bitmap GetLayeredRenderBuffer()
        {
            if (_layeredRenderBuffer == null || _layeredRenderBufferSize != ClientSize)
            {
                _layeredRenderBuffer?.Dispose();
                _layeredRenderBuffer = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
                _layeredRenderBufferSize = ClientSize;
                _lastLayeredRenderSucceeded = false;
                _lastLayeredRenderSignature = "";
            }

            return _layeredRenderBuffer;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            UIUtils.UpdateScale(this.DeviceDpi / 96f, (float)_cfg.UIScale);
            _ui?.ApplyTheme(_cfg.Skin);
            _winHelper.ApplyRoundedCorners();
            RequestLayeredRender();
        }

        protected override void OnShown(EventArgs e)
        {
            using (UiJankProfiler.Measure("MainForm.OnShown", thresholdMs: 1))
            {
                base.OnShown(e);

                RequestStartupMarketRefresh();

                // 恢复可见性
                if (_cfg.HideMainForm) this.Hide();

                RequestLayeredRender();

                // 恢复位置
                _bizHelper.RestorePos();

                // 确保渲染尺寸正确 (横屏模式)
                if (_cfg.HorizontalMode && _ui != null)
                {
                    this.Size = new Size(this.Width, this.Height);
                }

                // per-pixel layered window 需要直接提交带 alpha 的位图，不能再走 Form.Opacity 渐入。
                _winHelper.ApplyRoundedCorners();
                RequestLayeredRender();
                _bizHelper.KeepVisible(3.0); // 启动保护期

                if (_cfg.ShowTaskbar) ToggleTaskbar(true);

                // 这样既检查了驱动，也检查了更新，以及置顶 透明度 穿透 等，而且时机完美（窗口显示后）
                if (_bizHelper != null)
                {
                    _ = _bizHelper.RunStartupChecksAsync();
                }
                // [Fix] 强制置顶刷新，增加重试机制确保在某些系统环境下依然生效
                if (_cfg.TopMost)
                {
                    this.BeginInvoke(new Action(async () =>
                    {
                        await Task.Delay(3000);
                        if (!CS2TradeMonitor.src.Core.Actions.AppActions.HasOpenSettingsWindow())
                        {
                            _winHelper.RefreshTopMost(true, forceReinsert: true);
                        }
                    }));
                }

                if (_openSettingsOnStartup)
                {
                    QueueOpenCommandLineSettingsPage(_startupSettingsPage, "StartupArgs");
                }
            }
        }

        private void RequestStartupMarketRefresh()
        {
            if (_startupMarketRefreshRequested)
                return;

            _startupMarketRefreshRequested = true;

            try
            {
                _ = MarketDataSourceManager.RequestStartupMarketIndexRefreshAsync()
                    .ContinueWith(
                        task =>
                        {
                            if (task.Exception != null)
                                DiagnosticsLogger.Error("Startup", "Startup market index refresh failed.", task.Exception);
                        },
                        TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Startup", "Startup market index refresh failed.", ex);
            }
        }

        private void QueueOpenCommandLineSettingsPage(string? settingsPage, string reason)
        {
            if (IsDisposed || Disposing)
                return;

            void Schedule()
            {
                if (!IsDisposed && !Disposing)
                    BeginInvoke(new Action(async () => await OpenCommandLineSettingsPageWhenReadyAsync(settingsPage, reason)));
            }

            if (IsHandleCreated)
            {
                Schedule();
                return;
            }

            EventHandler? handler = null;
            handler = (_, __) =>
            {
                HandleCreated -= handler;
                Schedule();
            };
            HandleCreated += handler;
        }

        private async Task OpenCommandLineSettingsPageWhenReadyAsync(string? settingsPage, string reason)
        {
            string normalizedPage = SettingsPageRegistry.NormalizeKey(settingsPage ?? "");
            for (int attempt = 1; attempt <= OpenSettingsReadyMaxAttempts; attempt++)
            {
                if (IsDisposed || Disposing)
                    return;

                if (IsHandleCreated && _ui != null)
                {
                    DiagnosticsLogger.Info(
                        "Settings",
                        $"Opening command line settings page. Reason={DiagnosticsLogger.Redact(reason)}; Page={DiagnosticsLogger.Redact(normalizedPage)}; Attempt={attempt}");
                    OpenCommandLineSettingsPage(settingsPage);
                    return;
                }

                await Task.Delay(OpenSettingsReadyRetryDelayMs);
            }

            DiagnosticsLogger.Error(
                "Settings",
                $"Command line settings page open timed out. Reason={DiagnosticsLogger.Redact(reason)}; Page={DiagnosticsLogger.Redact(normalizedPage)}");
        }

        private void OpenCommandLineSettingsPage(string? settingsPage)
        {
            string page = SettingsPageRegistry.NormalizeKey(settingsPage ?? "");
            if (SettingsPageRegistry.IsKnownRoute(page))
            {
                CS2TradeMonitor.src.Core.Actions.AppActions.ShowSettingsPage(_cfg, _ui, this, page, modal: false);
                return;
            }

            if (page.Equals("Taskbar", StringComparison.OrdinalIgnoreCase)
                || page.Equals("Style", StringComparison.OrdinalIgnoreCase)
                || page.Equals("Appearance", StringComparison.OrdinalIgnoreCase)
                || page.Equals("InventoryTrend", StringComparison.OrdinalIgnoreCase)
                || page.Equals("MainPanelItemMonitor", StringComparison.OrdinalIgnoreCase)
                || page.Equals("MainPanelInventoryTrend", StringComparison.OrdinalIgnoreCase)
                || page.Equals("Float", StringComparison.OrdinalIgnoreCase))
            {
                string tab = page.Equals("Taskbar", StringComparison.OrdinalIgnoreCase)
                    ? CS2TradeMonitor.src.Core.Actions.AppActions.MainPanelTaskbarTab
                    : page.Equals("Style", StringComparison.OrdinalIgnoreCase) || page.Equals("Appearance", StringComparison.OrdinalIgnoreCase)
                        ? "Style"
                        : page.Equals("MainPanelItemMonitor", StringComparison.OrdinalIgnoreCase)
                            ? "ItemMonitor"
                            : page.Equals("InventoryTrend", StringComparison.OrdinalIgnoreCase)
                                || page.Equals("MainPanelInventoryTrend", StringComparison.OrdinalIgnoreCase)
                                ? "InventoryTrend"
                                : CS2TradeMonitor.src.Core.Actions.AppActions.MainPanelFloatTab;
                CS2TradeMonitor.src.Core.Actions.AppActions.ShowInterfaceSettings(
                    _cfg,
                    _ui,
                    this,
                    tab,
                    modal: false);

                CS2TradeMonitor.src.Core.Actions.AppActions.TrySwitchOpenSettingsMainPanelTab(tab);
                return;
            }

            OpenSettings();
        }

        private static string? ExtractStartupSettingsPage(string[] args)
        {
            return args
                .FirstOrDefault(arg => arg.StartsWith("--open-settings=", StringComparison.OrdinalIgnoreCase))
                ?.Split('=', 2)[1];
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _cfg.Save();
            // 轻量版无上游硬件/流量历史后台模块需要释放。

            base.OnFormClosed(e);

            _layeredRenderBuffer?.Dispose();
            _layeredRenderBuffer = null;
            _ui?.Dispose();
            _bizHelper.Dispose();
        }
    }
}
