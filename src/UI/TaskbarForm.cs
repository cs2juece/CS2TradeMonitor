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
        private readonly System.Windows.Forms.Timer _maintenanceTimer = new();

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
        private bool? _lastAppliedClickThrough;
        private readonly TaskbarTooltipHelper _tooltipHelper;

        // 公开属性
        public string TargetDevice { get; private set; } = "";

        // 判断菜单是否打开
        public bool IsMenuOpen => _currentMenu != null && !_currentMenu.IsDisposed && _currentMenu.Visible;

        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private bool _isOpeningContextMenu;

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
            ApplyInteractionMode(force: true);

            _ui.RefreshSnapshotApplied += ApplyUiSnapshot;
            _maintenanceTimer.Interval = 1000;
            _maintenanceTimer.Tick += OnMaintenanceTick;
            _maintenanceTimer.Start();
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
            _bizHelper.CheckTheme(true);

            // 更新悬浮窗模式 (支持热切换)
            ApplyInteractionMode(force: true);

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
                _maintenanceTimer.Stop();
                _maintenanceTimer.Tick -= OnMaintenanceTick;
                _maintenanceTimer.Dispose();
                _winHelper?.RestoreTaskbar();
                _currentMenu?.Dispose();
                _currentMenu = null;
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
            if (IsDisposed || Disposing || !IsHandleCreated || _isOpeningContextMenu)
            {
                return;
            }

            _isOpeningContextMenu = true;
            try
            {
                _currentMenu?.Dispose();
                _currentMenu = MenuManager.Build(_mainForm, _cfg, _ui, "Taskbar");

                TaskbarWinHelper.ActivateWindow(Handle);
                _currentMenu.Show(Cursor.Position);
            }
            catch (ObjectDisposedException)
            {
                _currentMenu = null;
            }
            finally
            {
                _isOpeningContextMenu = false;
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
            ApplyInteractionMode();

            if (snapshot.ForceLayoutRebuild)
            {
                _lastLayoutSignature = "";
                _lastRenderSignature = "";
                _lastPlacementSignature = "";
            }

            bool handleRefreshed = RefreshHandlesIfNeeded();
            bool visualChanged = RefreshThemeIfDue();

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
            if (!_bizHelper.IsTaskbarValid())
                return;

            Rectangle oldTaskbarRect = _bizHelper.Rect;
            _bizHelper.UpdateTaskbarRect();
            bool geometryChanged = oldTaskbarRect != _bizHelper.Rect;
            bool layoutChanged = RefreshLayoutForCurrentGeometry(force: geometryChanged);
            visualChanged |= layoutChanged;
            UpdatePlacementIfNeeded(handleRefreshed || geometryChanged || layoutChanged);

            if (_cfg.TaskbarHoverShowAll) _tooltipHelper.UpdateContent();

            string renderSig = GetRenderSignature(_cols);
            if (snapshot.ForceRender || visualChanged || renderSig != _lastRenderSignature)
            {
                _lastRenderSignature = renderSig;
                _renderScheduler.RequestRender(this);
            }
        }

        private void OnMaintenanceTick(object? sender, EventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            ApplyInteractionMode();
            bool handleRefreshed = RefreshHandlesIfNeeded();
            if (!_bizHelper.IsTaskbarValid())
                return;

            bool visualChanged = RefreshThemeIfDue();
            Rectangle oldTaskbarRect = _bizHelper.Rect;
            _bizHelper.UpdateTaskbarRect();
            bool geometryChanged = oldTaskbarRect != _bizHelper.Rect;
            bool layoutChanged = RefreshLayoutForCurrentGeometry(force: geometryChanged);

            if (_cols != null && _cols.Count > 0)
                UpdatePlacementIfNeeded(handleRefreshed || geometryChanged || layoutChanged);
            if (visualChanged || layoutChanged)
                _renderScheduler.RequestRender(this);
        }

        private bool RefreshHandlesIfNeeded()
        {
            if (!_bizHelper.NeedsHandleRefresh())
                return false;

            DateTime now = DateTime.Now;
            if ((now - _lastFindHandleTime).TotalSeconds <= 2)
                return false;

            _bizHelper.FindHandles();
            _lastFindHandleTime = now;
            return _bizHelper.IsTaskbarValid();
        }

        private bool RefreshThemeIfDue()
        {
            DateTime now = DateTime.Now;
            if ((now - _lastThemeCheckTime).TotalSeconds < 5)
                return false;

            _lastThemeCheckTime = now;
            return _bizHelper.CheckTheme();
        }

        private bool RefreshLayoutForCurrentGeometry(bool force)
        {
            if (_cols == null || _cols.Count == 0)
                return false;

            if (_bizHelper.IsVertical())
            {
                string currentSignature = "vertical_" + _layout.GetLayoutSignature(_cols) + "_" + _bizHelper.Rect.Width;
                if (!force && currentSignature == _lastLayoutSignature)
                    return false;

                _bizHelper.BuildVerticalLayout(_cols);
                _lastLayoutSignature = currentSignature;
                return true;
            }

            bool isUninitialized = _cols[0].Bounds.IsEmpty;
            string layoutSignature = _layout.GetLayoutSignature(_cols) + "_" + _bizHelper.Height;
            if (!force && !isUninitialized && layoutSignature == _lastLayoutSignature)
                return false;

            _layout.Build(_cols, _bizHelper.Height);
            Width = _layout.PanelWidth;
            Height = _bizHelper.Height;
            _lastLayoutSignature = layoutSignature;
            return true;
        }

        private void UpdatePlacementIfNeeded(bool force)
        {
            bool isVertical = _bizHelper.IsVertical();
            string placementSignature = _bizHelper.GetPlacementSignature(Width);
            bool placementChanged = force
                || _lastPlacementWidth != Width
                || _lastPlacementHeight != Height
                || _lastPlacementRect != _bizHelper.Rect
                || _lastPlacementVertical != isVertical
                || _lastPlacementSignature != placementSignature;

            if (!placementChanged)
                return;

            _bizHelper.UpdatePlacement(Width);
            _lastPlacementWidth = Width;
            _lastPlacementHeight = Height;
            _lastPlacementRect = _bizHelper.Rect;
            _lastPlacementVertical = isVertical;
            _lastPlacementSignature = placementSignature;
        }

        private void ApplyInteractionMode(bool force = false)
        {
            bool clickThrough = _cfg.TaskbarClickThrough;
            if (!force && _lastAppliedClickThrough == clickThrough)
                return;

            _winHelper.ApplyLayeredStyle(_bizHelper.TransparentKey, clickThrough);
            _tooltipHelper.ReloadMode();
            _lastAppliedClickThrough = clickThrough;
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
                if (_cfg != null && _cfg.TaskbarClickThrough)
                {
                    cp.ExStyle |= WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }
    }
}
