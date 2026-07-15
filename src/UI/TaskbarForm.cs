using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.Helpers;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using static CS2TradeMonitor.src.UI.Helpers.NativeMethods;

namespace CS2TradeMonitor
{
    public class TaskbarForm : Form
    {
        private readonly Settings _cfg;
        private readonly UIController _ui;
        private readonly MainForm _mainForm;
        private readonly IRenderScheduler _renderScheduler;

        // ★★★ 双助手架构 ★★★
        private readonly TaskbarWinHelper _winHelper;
        private readonly TaskbarBizHelper _bizHelper;

        private HorizontalLayout _layout = null!;
        private List<Column>? _cols;
        private ContextMenuStrip? _currentMenu;
        private DateTime _lastFindHandleTime = DateTime.MinValue;
        private DateTime _lastThemeCheckTime = DateTime.MinValue;
        private string _lastLayoutSignature = "";
        private string _lastRenderSignature = "";
        private Rectangle _lastPlacementRect = Rectangle.Empty;
        private int _lastPlacementWidth = -1;
        private int _lastPlacementHeight = -1;
        private bool _lastPlacementVertical = false;
        private string _lastPlacementSignature = "";
        private readonly TaskbarTooltipHelper _tooltipHelper;

        // 公开属性
        public string TargetDevice { get; private set; } = "";

        // 判断菜单是否打开
        public bool IsMenuOpen => _currentMenu != null && !_currentMenu.IsDisposed && _currentMenu.Visible;

        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private bool _isWin11;

        public TaskbarForm(Settings cfg, UIController ui, MainForm mainForm)
            : this(cfg, ui, mainForm, UIFrameworkRuntimeServices.ResolveRenderScheduler())
        {
        }

        internal TaskbarForm(Settings cfg, UIController ui, MainForm mainForm, IRenderScheduler renderScheduler)
        {
            _cfg = cfg;
            _ui = ui;
            _mainForm = mainForm;
            _renderScheduler = renderScheduler ?? throw new ArgumentNullException(nameof(renderScheduler));
            TargetDevice = _cfg.TaskbarMonitorDevice;

            _isWin11 = Environment.OSVersion.Version >= new Version(10, 0, 22000);

            // 初始化组件
            _winHelper = new TaskbarWinHelper(this, _renderScheduler);
            _bizHelper = new TaskbarBizHelper(this, _cfg, _winHelper);

            // 窗体属性
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ControlBox = false;
            TopMost = false;
            DoubleBuffered = true;

            // 鼠标悬浮提示初始化
            _tooltipHelper = new TaskbarTooltipHelper(this, _cfg, _ui);

            ReloadLayout();

            _bizHelper.CheckTheme(true);
            _bizHelper.FindHandles();

            _bizHelper.AttachToTaskbar();
            _lastRenderSignature = "";
            _lastPlacementRect = Rectangle.Empty;
            _lastPlacementWidth = -1;
            _lastPlacementHeight = -1;
            _lastPlacementSignature = "";
            _cfg.TaskbarClickThrough = false;
            _winHelper.ApplyLayeredStyle(_bizHelper.TransparentKey, clickThrough: false);

            _ui.RefreshSnapshotApplied += ApplyUiSnapshot;
            ApplyUiSnapshot(UiSnapshot.Empty);
        }

        public void ReloadLayout()
        {
            _layout = new HorizontalLayout(ThemeManager.Current, 300, LayoutMode.Taskbar, _cfg);
            _lastRenderSignature = "";
            _lastPlacementRect = Rectangle.Empty;
            _lastPlacementWidth = -1;
            _lastPlacementHeight = -1;
            _lastPlacementSignature = "";
            _lastLayoutSignature = ""; // 重置签名，强制重算
            _winHelper.ApplyLayeredStyle(_bizHelper.TransparentKey, clickThrough: false);
            _bizHelper.CheckTheme(true);

            // 更新悬浮窗模式 (支持热切换)
            _tooltipHelper?.ReloadMode();

            // 注意：这里仍然可能因为 _cols 为空而暂时不 Build，
            // 但随后的 snapshot 刷新会在获取到新数据后自动 Build
            if (_cols != null && _cols.Count > 0)
            {
                _layout.Build(_cols, _bizHelper.Height);
                Width = _layout.PanelWidth;
                _bizHelper.UpdatePlacement(Width);
            }
            _renderScheduler.RequestRender(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ui.RefreshSnapshotApplied -= ApplyUiSnapshot;
                _winHelper?.RestoreTaskbar();
                ContextMenuStrip? menu = _currentMenu;
                _currentMenu = null;
                MenuLifetime.DisposeLater(menu, _mainForm, "TaskbarForm.Dispose");
                _tooltipHelper?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void WndProc(ref Message m)
        {
            // [Fix] 兼容性修复：在 Win11 25H2 + StartAllBack 环境下，
            // 右键事件会穿透到原生任务栏。
            // 因此不再区分系统版本，统一拦截右键按下和抬起消息。
            if (m.Msg == WM_RBUTTONDOWN)
            {
                return; // 吞掉按下事件，防止穿透
            }

            if (m.Msg == WM_RBUTTONUP)
            {
                this.BeginInvoke(new Action(ShowContextMenu));
                return;
            }

            // [Fix] 强制拦截双击事件
            // 当悬浮窗(Tooltip)显示时，WinForms 的标准双击事件可能因为焦点/激活状态的微妙变化而失效。
            // 这里直接在消息层处理 WM_LBUTTONDBLCLK，确保双击动作始终能被触发。
            if (m.Msg == WM_LBUTTONDBLCLK)
            {
                _bizHelper.HandleDoubleClick(_mainForm, _ui);
                return;
            }

            base.WndProc(ref m);
        }

        private void ShowContextMenu()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            ContextMenuStrip? oldMenu = _currentMenu;
            _currentMenu = MenuManager.Build(_mainForm, _cfg, _ui, "Taskbar");
            ContextMenuStrip menu = _currentMenu;
            menu.Closed += (_, __) =>
            {
                if (ReferenceEquals(_currentMenu, menu))
                {
                    _currentMenu = null;
                }

                MenuLifetime.DisposeLater(menu, this, "TaskbarForm.MenuClosed");
            };
            MenuLifetime.DisposeLater(oldMenu, this, "TaskbarForm.ShowContextMenu");

            try
            {
                IntPtr menuHandle = menu.Handle;
                int menuExStyle = GetWindowLong(menuHandle, GWL_EXSTYLE);
                menuExStyle |= WS_EX_TOOLWINDOW;
                menuExStyle &= ~WS_EX_APPWINDOW;
                SetWindowLong(menuHandle, GWL_EXSTYLE, menuExStyle);
                menu.Show(this, PointToClient(Cursor.Position));
            }
            catch (ObjectDisposedException)
            {
                _currentMenu = null;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                _bizHelper.HandleDoubleClick(_mainForm, _ui);
            }
        }

        private void ApplyUiSnapshot(UiSnapshot snapshot)
        {
            if (IsDisposed || Disposing) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => ApplyUiSnapshot(snapshot)));
                }
                catch
                {
                    // 窗口关闭期间 BeginInvoke 可能失败，忽略本次快照应用。
                }

                return;
            }

            RefreshFromSnapshot(snapshot);
        }

        private void RefreshFromSnapshot(UiSnapshot snapshot)
        {
            if (snapshot.ForceLayoutRebuild)
            {
                _lastLayoutSignature = "";
                _lastRenderSignature = "";
                _lastPlacementSignature = "";
            }

            // [Fix] 周期性检查句柄，防止 Explorer 重启后句柄失效
            // 优化：仅在重试期或句柄无效时调用 FindHandles，且限制调用频率
            bool isHandleInvalid = _bizHelper.NeedsHandleRefresh();

            // 如果处于重试期，或者句柄无效且距离上次查找超过2秒(防止无Explorer时高频空转)
            if (isHandleInvalid && (DateTime.Now - _lastFindHandleTime).TotalSeconds > 2)
            {
                _bizHelper.FindHandles();
                _lastFindHandleTime = DateTime.Now;
            }

            bool visualChanged = false;
            var now = DateTime.Now;
            if ((now - _lastThemeCheckTime).TotalSeconds >= 5)
            {
                visualChanged = _bizHelper.CheckTheme();
                _lastThemeCheckTime = now;
            }

            // [Fix Part 1] 防空数据保护
            // 使用临时变量接收，先判断数据有效性，再赋值给成员变量 _cols
            // 防止在 UI 重建期间(RebuildLayout)获取到空列表导致任务栏闪烁或清空
            var nextCols = _ui.GetTaskbarColumns();
            if (nextCols == null) return;

            if (nextCols.Count == 0)
            {
                if (_cols != null && _cols.Count > 0)
                {
                    _cols = nextCols;
                    _lastLayoutSignature = "";
                    _lastRenderSignature = "";
                    Width = 0;
                    _bizHelper.UpdatePlacement(0);
                    _renderScheduler.RequestRender(this);
                }
                return;
            }

            _cols = nextCols; // 确认有效后再更新引用

            Rectangle oldTaskbarRect = _bizHelper.Rect;
            _bizHelper.UpdateTaskbarRect();
            bool placementChanged = oldTaskbarRect != _bizHelper.Rect;

            // [Fix Part 2] 布局变更检测
            if (_bizHelper.IsVertical())
            {
                // 垂直模式逻辑简单且无测量开销，直接重算即可
                string currentSig = "vertical_" + _layout.GetLayoutSignature(_cols) + "_" + _bizHelper.Rect.Width;
                if (currentSig != _lastLayoutSignature)
                {
                    _bizHelper.BuildVerticalLayout(_cols);
                    _lastLayoutSignature = currentSig;
                    placementChanged = true;
                    visualChanged = true;
                }
            }
            else
            {
                // [优化] 智能判断更新条件
                // 1. 必须检查：如果列表是新生成的（坐标还没算过，Bounds为空），必须重算！
                // 这解决了“主界面显隐导致任务栏消失”的问题
                bool isUninitialized = (_cols.Count > 0 && _cols[0].Bounds.IsEmpty);

                // 2. 常规检查：如果内容长度/结构变了（签名变了），也要重算
                string currentSig = _layout.GetLayoutSignature(_cols) + "_" + _bizHelper.Height;
                bool isContentChanged = (currentSig != _lastLayoutSignature);

                if (isUninitialized || isContentChanged)
                {
                    _layout.Build(_cols, _bizHelper.Height);
                    Width = _layout.PanelWidth;
                    Height = _bizHelper.Height;

                    _lastLayoutSignature = currentSig;
                    placementChanged = true;
                    visualChanged = true;
                }
            }

            bool isVertical = _bizHelper.IsVertical();
            string placementSignature = _bizHelper.GetPlacementSignature(Width);
            placementChanged = placementChanged
                || _lastPlacementWidth != Width
                || _lastPlacementHeight != Height
                || _lastPlacementRect != _bizHelper.Rect
                || _lastPlacementVertical != isVertical
                || _lastPlacementSignature != placementSignature;

            if (placementChanged)
            {
                _bizHelper.UpdatePlacement(Width);
                _lastPlacementWidth = Width;
                _lastPlacementHeight = Height;
                _lastPlacementRect = _bizHelper.Rect;
                _lastPlacementVertical = isVertical;
                _lastPlacementSignature = placementSignature;
            }

            if (_cfg.TaskbarHoverShowAll) _tooltipHelper.UpdateContent();

            string renderSig = GetRenderSignature(_cols);
            if (snapshot.ForceRender || visualChanged || renderSig != _lastRenderSignature)
            {
                _lastRenderSignature = renderSig;
                _renderScheduler.RequestRender(this);
            }
        }

        private static string GetRenderSignature(List<Column> cols)
        {
            unchecked
            {
                int hash = 17;

                void AddItem(MetricItem? item)
                {
                    if (item == null) return;
                    string text = item.GetFormattedText(true);
                    hash = hash * 31 + item.Key.GetHashCode();
                    hash = hash * 31 + text.GetHashCode();
                    hash = hash * 31 + item.CachedColorState;
                }

                foreach (var col in cols)
                {
                    AddItem(col.Top);
                    AddItem(col.Bottom);
                }

                return hash.ToString();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(_bizHelper.TransparentKey);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (UiJankProfiler.Measure("TaskbarForm.OnPaint", $"Size={ClientSize.Width}x{ClientSize.Height}; Columns={_cols?.Count ?? 0}", thresholdMs: 8))
            {
                if (_cols == null) return;
                var g = e.Graphics;
                g.Clear(_bizHelper.TransparentKey);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                TaskbarRenderer.Render(g, _cols, _bizHelper.LastIsLightTheme);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (防止点击激活窗口，避免抢占焦点)
                return cp;
            }
        }
    }
}
