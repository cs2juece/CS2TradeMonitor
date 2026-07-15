using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI;

namespace CS2TradeMonitor.src.UI.Helpers
{
    /// <summary>
    /// 主窗口业务助手 (Business Helper)
    /// 职责：自动隐藏、托盘交互、快捷动作、启动流程、布局切换
    /// </summary>
    public class MainFormBizHelper : IDisposable
    {
        private readonly Form _form;
        private readonly Settings _cfg;
        private readonly UIController _ui;
        private readonly MainFormWinHelper _winHelper;
        private readonly NotifyIcon _tray;

        // 自动隐藏相关
        private System.Windows.Forms.Timer? _autoHideTimer;
        private System.Windows.Forms.Timer? _topMostTimer;
        private bool _isHidden = false;
        private readonly int _hideWidth = 4;
        private readonly int _hideThreshold = 10;
        private enum DockEdge { None, Left, Right, Top, Bottom }
        private DockEdge _dock = DockEdge.None;
        private DateTime _keepVisibleUntil = DateTime.MinValue;
        private DateTime _lastAutoHideErrorLog = DateTime.MinValue;

        public bool IsHidden => _isHidden;
        public bool IsDragging { get; set; } = false;

        public MainFormBizHelper(Form form, Settings cfg, UIController ui, MainFormWinHelper winHelper)
        {
            _form = form;
            _cfg = cfg;
            _ui = ui;
            _winHelper = winHelper;
            _tray = new NotifyIcon();
        }

        public void Initialize()
        {
            InitTray();
            if (_cfg.AutoHide) StartTimer();
            StartTopMostTimer();
        }

        // =================================================================
        // 自动隐藏逻辑
        // =================================================================
        public void StartTimer()
        {
            _autoHideTimer ??= new System.Windows.Forms.Timer { Interval = 250 };
            _autoHideTimer.Tick -= AutoHideTick;
            _autoHideTimer.Tick += AutoHideTick;
            _autoHideTimer.Start();
        }

        public void StopTimer(bool restoreHidden = false)
        {
            _autoHideTimer?.Stop();
            if (restoreHidden && _isHidden)
            {
                RestorePos();
            }
        }

        public void KeepVisible(double seconds) => _keepVisibleUntil = DateTime.Now.AddSeconds(seconds);

        public void ForceShow(double seconds = 15.0)
        {
            if (!_form.Visible)
            {
                _form.Show();
            }

            if (_form.WindowState == FormWindowState.Minimized)
            {
                _form.WindowState = FormWindowState.Normal;
            }

            _isHidden = false;
            _dock = DockEdge.None;
            ClampToScreen(force: true); // 强制拉回
            KeepVisible(seconds);
        }

        private void AutoHideTick(object? sender, EventArgs e)
        {
            try
            {
                CheckAutoHide();
            }
            catch (Exception ex)
            {
                if (DateTime.Now - _lastAutoHideErrorLog > TimeSpan.FromSeconds(30))
                {
                    _lastAutoHideErrorLog = DateTime.Now;
                    DiagnosticsLogger.Error("AutoHide", "Auto-hide tick failed.", ex);
                }
            }
        }

        private void StartTopMostTimer()
        {
            _topMostTimer ??= new System.Windows.Forms.Timer { Interval = 10000 };
            _topMostTimer.Tick -= TopMostTick;
            _topMostTimer.Tick += TopMostTick;
            _topMostTimer.Start();
        }

        private void TopMostTick(object? sender, EventArgs e)
        {
            UpdateTrayTooltip();
            if (!_form.Visible) return;
            if (CS2TradeMonitor.src.Core.Actions.AppActions.HasOpenSettingsWindow()) return;

            if (_cfg.TopMost)
            {
                if (!_winHelper.IsTopMostStyleApplied())
                {
                    _winHelper.RefreshTopMost(true, forceReinsert: true);
                }
            }
            else if (_winHelper.IsTopMostStyleApplied())
            {
                _winHelper.RefreshTopMost(false);
            }
        }

        private void CheckAutoHide()
        {
            if (!_cfg.AutoHide) return;
            if (!_form.Visible) return;
            if (IsDragging || _form.ContextMenuStrip?.Visible == true) return;
            if (DateTime.Now < _keepVisibleUntil) return;

            var center = new Point(_form.Left + _form.Width / 2, _form.Top + _form.Height / 2);
            var screen = Screen.FromPoint(center);
            var area = screen.WorkingArea;
            var cursor = Cursor.Position;

            bool nearLeft = _form.Left <= area.Left + _hideThreshold;
            bool nearRight = area.Right - _form.Right <= _hideThreshold;
            bool nearTop = _form.Top <= area.Top + _hideThreshold;

            bool shouldHide = nearLeft || nearRight || nearTop;

            // 靠边 -> 隐藏
            if (!_isHidden && shouldHide && !_form.Bounds.Contains(cursor))
            {
                if (nearRight) { _form.Left = area.Right - _hideWidth; _dock = DockEdge.Right; }
                else if (nearLeft) { _form.Left = area.Left - (_form.Width - _hideWidth); _dock = DockEdge.Left; }
                else if (nearTop) { _form.Top = area.Top - (_form.Height - _hideWidth); _dock = DockEdge.Top; }
                _isHidden = true;
                return;
            }

            // 鼠标靠近 -> 弹出
            if (_isHidden)
            {
                const int hoverBand = 30;
                bool isMouseOnHiddenPanel = false;

                if (_dock == DockEdge.Right) isMouseOnHiddenPanel = cursor.X >= area.Right - _hideWidth && cursor.Y >= _form.Top && cursor.Y <= _form.Top + _form.Height;
                else if (_dock == DockEdge.Left) isMouseOnHiddenPanel = cursor.X <= area.Left + _hideWidth && cursor.Y >= _form.Top && cursor.Y <= _form.Top + _form.Height;
                else if (_dock == DockEdge.Top) isMouseOnHiddenPanel = cursor.Y <= area.Top + _hideWidth && cursor.X >= _form.Left && cursor.X <= _form.Left + _form.Width;

                if (isMouseOnHiddenPanel)
                {
                    if (_dock == DockEdge.Right && cursor.X >= area.Right - hoverBand) { _form.Left = area.Right - _form.Width; _isHidden = false; _dock = DockEdge.None; }
                    else if (_dock == DockEdge.Left && cursor.X <= area.Left + hoverBand) { _form.Left = area.Left; _isHidden = false; _dock = DockEdge.None; }
                    else if (_dock == DockEdge.Top && cursor.Y <= area.Top + hoverBand) { _form.Top = area.Top; _isHidden = false; _dock = DockEdge.None; }
                }
            }
        }

        // =================================================================
        // 托盘管理
        // =================================================================
        private void InitTray()
        {
            try { _tray.Icon = Properties.Resources.AppIcon ?? _form.Icon; } catch { _tray.Icon = _form.Icon; }
            UpdateTrayTooltip();
            _tray.Visible = !_cfg.HideTrayIcon;

            RebuildMenus();

            _tray.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    MainFormWinHelper.ActivateWindow(_form.Handle);
                    _form.ContextMenuStrip?.Show(Cursor.Position);
                }
            };

            _tray.MouseDoubleClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleMainWindowFromTray();
                }
            };
        }

        public void ToggleMainWindowFromTray()
        {
            try
            {
                var mainForm = (MainForm)_form;

                if (_isHidden)
                {
                    mainForm.ShowMainWindow();
                    return;
                }

                if (_form.Visible) mainForm.HideMainWindow();
                else mainForm.ShowMainWindow();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Tray", "Tray double-click toggle failed.", ex);
            }
        }

        public void RebuildMenus()
        {
            UpdateTrayTooltip();
            ContextMenuStrip? oldMenu = _form.ContextMenuStrip;
            ContextMenuStrip newMenu = MenuManager.Build((MainForm)_form, _cfg, _ui);
            _form.ContextMenuStrip = newMenu;
            MenuLifetime.DisposeLater(oldMenu, _form, "MainFormBizHelper.RebuildMenus");
            UIUtils.ClearBrushCache();
        }

        private void UpdateTrayTooltip()
        {
            try
            {
                var summary = AppActions.GetDataSourceSummary(_cfg);
                string qaqText = summary.QaqTrayText;
                string dtText = summary.SteamDtTrayText;
                string text = $"CS2交易监控\n{qaqText}\n{dtText}";

                if (text.Length > 127)
                {
                    text = text.Substring(0, 124) + "...";
                }
                try
                {
                    _tray.Text = text;
                }
                catch (ArgumentException)
                {
                    if (text.Length > 63)
                    {
                        text = text.Substring(0, 60) + "...";
                    }
                    _tray.Text = text;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Tray", "Setting tray text failed.", ex);
            }
        }

        public void ShowNotification(string title, string text, ToolTipIcon icon)
        {
            if (!GlobalPromptService.Notify(
                    title,
                    text,
                    GlobalPromptService.MapToolTipIcon(icon),
                    source: "系统通知",
                    dedupKey: "MainForm:" + title + "|" + text,
                    owner: _form))
            {
                TryShowNotification(title, text, icon);
            }
        }

        public bool TryShowNotification(string title, string text, ToolTipIcon icon)
        {
            if (_cfg.DoNotDisturbEnabled) return false;
            if (!_tray.Visible) return false;
            _tray.ShowBalloonTip(5000, title, text, icon);
            return true;
        }

        public void SetTrayVisible(bool visible) => _tray.Visible = visible;

        // =================================================================
        // 布局切换与位置管理
        // =================================================================
        public void ToggleLayoutMode()
        {
            _form.SuspendLayout();
            try
            {
                // 记录旧模式
                bool oldMode = _cfg.HorizontalMode;

                _cfg.HorizontalMode = !oldMode;
                _cfg.Save();

                // ★ 统一使用 AppActions，传入旧模式以触发自动居中
                Core.Actions.AppActions.ApplyThemeAndLayout(_cfg, _ui, (MainForm)_form, oldMode);
            }
            finally
            {
                _form.ResumeLayout(true);
            }
        }

        public void SavePos()
        {
            ClampToScreen(force: false);
            var center = new Point(_form.Left + _form.Width / 2, _form.Top + _form.Height / 2);
            var scr = Screen.FromPoint(center);

            _cfg.ScreenDevice = scr.DeviceName;
            _cfg.Position = new Point(_form.Left, _form.Top);
            _cfg.Save();
        }

        public void RestorePos()
        {
            var screen = ResolveSavedScreen() ?? Screen.FromControl(_form);
            var area = screen.WorkingArea;
            var savedPosition = _cfg.Position;

            if (IsSavedLocationUsable(area, savedPosition))
            {
                SetSafeLocation(area, savedPosition.X, savedPosition.Y);
            }
            else
            {
                SetDefaultFloatingLocation(area);
            }

            // 恢复位置后重置隐藏状态，避免旧的贴边隐藏状态继续影响新位置。
            _isHidden = false;
            _dock = DockEdge.None;
            KeepVisible(0.5);
        }

        private Screen? ResolveSavedScreen()
        {
            if (string.IsNullOrEmpty(_cfg.ScreenDevice)) return null;
            return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _cfg.ScreenDevice);
        }

        private bool IsSavedLocationUsable(Rectangle area, Point position)
        {
            if (position.X < 0 || position.Y < 0) return false;

            int width = Math.Max(1, _form.Width);
            int height = Math.Max(1, _form.Height);
            var windowBounds = new Rectangle(position.X, position.Y, width, height);
            return area.Contains(windowBounds);
        }

        private void SetDefaultFloatingLocation(Rectangle area)
        {
            int width = Math.Max(1, _form.Width);
            int height = Math.Max(1, _form.Height);
            int leftPadding = Math.Min(Math.Max(24, area.Width / 40), Math.Max(0, area.Width / 5));
            int rightMargin = Math.Min(Math.Max(56, area.Width / 24), Math.Max(0, area.Width / 4));
            int topMargin = Math.Min(Math.Max(56, area.Height / 12), Math.Max(0, area.Height / 3));
            int bottomMargin = Math.Min(Math.Max(24, area.Height / 40), Math.Max(0, area.Height / 5));

            int minX = area.Left + leftPadding;
            int maxX = Math.Max(minX, area.Right - width - rightMargin);
            int preferredX = area.Left + (int)Math.Round((area.Width - width) * 0.72);

            int minY = area.Top + topMargin;
            int maxY = Math.Max(minY, area.Bottom - height - bottomMargin);
            int preferredY = area.Top + (int)Math.Round((area.Height - height) * 0.24);

            int x = Clamp(preferredX, minX, maxX);
            int y = Clamp(preferredY, minY, maxY);
            SetSafeLocation(area, x, y);
        }

        public void ClampToScreen(bool force = false)
        {
            if (!_cfg.ClampToScreen && !force) return;

            var area = Screen.FromControl(_form).WorkingArea;
            int x = _form.Left;
            int y = _form.Top;

            // 修复逻辑：
            // 如果启用了自动隐藏，允许窗口稍微贴边，而不是强制弹开
            // 只有当窗口完全跑出屏幕外时，才强制拉回
            // 之前的 margin = _hideThreshold + 1 逻辑会导致：用户刚拖到边缘想隐藏，就被弹回来了

            if (x < area.Left) x = area.Left;
            if (x + _form.Width > area.Right) x = area.Right - _form.Width;
            if (y < area.Top) y = area.Top;
            if (y + _form.Height > area.Bottom) y = area.Bottom - _form.Height;

            _form.Location = new Point(x, y);
        }

        private void SetSafeLocation(Rectangle area, int x, int y)
        {
            int width = Math.Max(1, _form.Width);
            int height = Math.Max(1, _form.Height);
            int maxX = Math.Max(area.Left, area.Right - width);
            int maxY = Math.Max(area.Top, area.Bottom - height);

            x = Clamp(x, area.Left, maxX);
            y = Clamp(y, area.Top, maxY);
            _form.Location = new Point(x, y);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // =================================================================
        // 快捷动作
        // =================================================================
        public void HandleDoubleClick()
        {
            switch (_cfg.MainFormDoubleClickAction)
            {
                case 1: OpenTaskManager(); break;
                case 2: OpenSettings(); break;
                case 0: default: ToggleLayoutMode(); break;
            }
        }

        public void OpenTaskManager()
        {
            try { Process.Start(new ProcessStartInfo("taskmgr") { UseShellExecute = true }); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
        }

        public void OpenSettings()
        {
            DiagnosticsLogger.Info("Settings", "Opening interface settings from main form.");
            AppActions.ShowInterfaceSettings(_cfg, _ui, (MainForm)_form, AppActions.MainPanelFloatTab, modal: false);
        }


        // =================================================================
        // 启动流程
        // =================================================================
        public async Task RunStartupChecksAsync()
        {
            try
            {
                await Task.Yield();

                // 启动后再次确认窗口属性（置顶、穿透），确保功能与 UI 勾选一致。
                _form.BeginInvoke(new Action(() =>
                {
                    Core.Actions.AppActions.ApplyWindowAttributes(_cfg, (MainForm)_form);
                }));

                // 配置里要求开机启动时，启动后顺手校验计划任务是否真的指向当前 exe。
                // 这能修复旧版本任务名/旧路径残留，避免复选框已勾选但 Windows 实际没有启动当前程序。
                if (_cfg.AutoStart)
                {
                    await Task.Run(() => AutoStart.RepairIfNeeded(enabled: true, showErrorMessage: false));
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Startup", "Startup checks failed.", ex);
            }
        }

        public void Dispose()
        {
            _tray.Visible = false;
            _tray.Dispose();
            _autoHideTimer?.Stop();
            _autoHideTimer?.Dispose();
            _topMostTimer?.Stop();
            _topMostTimer?.Dispose();
        }
    }
}
