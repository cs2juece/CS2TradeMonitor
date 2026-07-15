using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public class VirtualListPanel<T> : Panel
    {
        private readonly Dictionary<int, Control> _visibleRows = new Dictionary<int, Control>();
        private readonly Stack<Control> _rowPool = new Stack<Control>();
        private readonly ThemedVerticalScrollBar _ownedScrollBar = new ThemedVerticalScrollBar();
        private readonly IRenderScheduler _renderScheduler;
        private readonly VirtualListPanelRenderQueue _renderQueue = new VirtualListPanelRenderQueue();

        private IReadOnlyList<T> _items = Array.Empty<T>();
        private ThemedVerticalScrollBar _scrollBar;
        private bool _ownsScrollBar;
        private bool _updatingScrollBar;
        private bool _renderPending;
        private bool _contentDirty = true;
        private readonly HashSet<int> _dirtyRows = new HashSet<int>();
        private int _verticalOffset;
        private int _rowHeight = 32;
        private int _overscanRowCount = 1;
        private int _maxNewRowsPerPass = int.MaxValue;
        private int _lastRealizedOffset = -1;
        private int _lastRealizedFirstIndex = -1;
        private int _lastRealizedLastIndex = -1;
        private Size _lastRealizedViewportSize = Size.Empty;

        public VirtualListPanel()
            : this(UIFrameworkRuntimeServices.ResolveRenderScheduler())
        {
        }

        public VirtualListPanel(IRenderScheduler renderScheduler)
        {
            _renderScheduler = renderScheduler ?? throw new ArgumentNullException(nameof(renderScheduler));
            DoubleBuffered = true;
            TabStop = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.ContainerControl
                | ControlStyles.Selectable,
                true);

            _scrollBar = _ownedScrollBar;
            AttachScrollBar(null);
        }

        public VirtualListPanel(IReadOnlyList<T> items)
            : this()
        {
            SetItems(items);
        }

        public VirtualListPanel(IReadOnlyList<T> items, IRenderScheduler renderScheduler)
            : this(renderScheduler)
        {
            SetItems(items);
        }

        public IReadOnlyList<T> Items
        {
            get { return _items; }
        }

        public int RowHeight
        {
            get { return _rowHeight; }
            set
            {
                int normalized = VirtualListPanelLayoutModel.NormalizeRowHeight(value);
                if (_rowHeight == normalized)
                {
                    return;
                }

                _rowHeight = normalized;
                InvalidateRealizationCache();
                ClampScrollOffset();
                UpdateScrollBar();
                RequestVirtualRender();
            }
        }

        public int OverscanRowCount
        {
            get { return _overscanRowCount; }
            set
            {
                int normalized = VirtualListPanelLayoutModel.NormalizeOverscanRowCount(value);
                if (_overscanRowCount == normalized)
                {
                    return;
                }

                _overscanRowCount = normalized;
                InvalidateRealizationCache();
                RequestVirtualRender();
            }
        }

        public int MaxNewRowsPerPass
        {
            get { return _maxNewRowsPerPass; }
            set { _maxNewRowsPerPass = VirtualListPanelLayoutModel.NormalizeMaxNewRowsPerPass(value); }
        }

        public int VerticalOffset
        {
            get { return _verticalOffset; }
        }

        public int FirstVisibleIndex
        {
            get
            {
                return VirtualListPanelLayoutModel.GetFirstVisibleIndex(_items.Count, _verticalOffset, _rowHeight);
            }
        }

        public int LastVisibleIndex
        {
            get
            {
                Rectangle viewport = GetViewportBounds();
                return VirtualListPanelLayoutModel.GetLastVisibleIndex(
                    _items.Count,
                    _verticalOffset,
                    _rowHeight,
                    viewport.Height);
            }
        }

        public ThemedVerticalScrollBar LinkedScrollBar
        {
            get { return _scrollBar; }
        }

        public void SetItems(IReadOnlyList<T>? items)
        {
            _items = items ?? Array.Empty<T>();
            _contentDirty = true;
            _dirtyRows.Clear();
            InvalidateRealizationCache();
            RecycleAllVisibleRows();
            ClampScrollOffset();
            UpdateScrollBar();
            RequestVirtualRender();
        }

        public void SetItemsIncremental(IReadOnlyList<T>? items, Func<T, T, bool>? areEquivalent)
        {
            IReadOnlyList<T> nextItems = items ?? Array.Empty<T>();
            IReadOnlyList<T> previousItems = _items;
            var rowsToRecycle = new List<int>();

            foreach (KeyValuePair<int, Control> pair in _visibleRows)
            {
                int index = pair.Key;
                if (index >= nextItems.Count)
                {
                    rowsToRecycle.Add(index);
                    continue;
                }

                if (index >= previousItems.Count
                    || areEquivalent == null
                    || !areEquivalent(previousItems[index], nextItems[index]))
                {
                    _dirtyRows.Add(index);
                }
            }

            _items = nextItems;
            if (areEquivalent == null)
                _contentDirty = true;
            InvalidateRealizationCache();

            foreach (int index in rowsToRecycle)
                RecycleRow(index);

            ClampScrollOffset();
            UpdateScrollBar();
            RequestVirtualRender();
        }

        public void RequestRowsRefresh()
        {
            _contentDirty = true;
            _dirtyRows.Clear();
            InvalidateRealizationCache();
            RequestVirtualRender();
        }

        public void PrewarmRowPool(int count)
        {
            if (count <= 0 || IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => PrewarmRowPool(count)));
                }
                catch
                {
                    // 控件释放期间预热行池失败可忽略，后续可见行会按需创建。
                }

                return;
            }

            if (!IsHandleCreated)
            {
                return;
            }

            SuspendLayout();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Control row = CreateRowControl();
                    row.Margin = Padding.Empty;
                    row.Anchor = AnchorStyles.None;
                    row.Visible = false;
                    Controls.Add(row);
                    _rowPool.Push(row);
                }
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        public void RenderNow()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(RenderNow));
                }
                catch
                {
                    // 控件释放期间渲染投递失败可忽略。
                }

                return;
            }

            if (!IsHandleCreated)
            {
                return;
            }

            _renderPending = false;
            RealizeVisibleRows();
            Invalidate();
        }

        public void AttachScrollBar(ThemedVerticalScrollBar? scrollBar)
        {
            if (_scrollBar != null)
            {
                _scrollBar.ValueChanged -= ScrollBar_ValueChanged;

                if (_ownsScrollBar && _scrollBar.Parent == this)
                {
                    Controls.Remove(_scrollBar);
                }
            }

            _ownsScrollBar = scrollBar == null;
            _scrollBar = scrollBar ?? _ownedScrollBar;
            _scrollBar.ValueChanged += ScrollBar_ValueChanged;

            if (_ownsScrollBar)
            {
                _scrollBar.Dock = DockStyle.Right;
                _scrollBar.TabStop = false;
                if (_scrollBar.Parent != this)
                {
                    Controls.Add(_scrollBar);
                }
            }

            _scrollBar.RefreshTheme();

            UpdateScrollBar();
            RequestVirtualRender();
        }

        public void ScrollToIndex(int index)
        {
            SetVerticalOffset(VirtualListPanelLayoutModel.GetScrollToIndexOffset(_items.Count, index, _rowHeight));
        }

        public void ScrollToOffset(int offset)
        {
            SetVerticalOffset(offset);
        }

        protected virtual Control CreateRowControl()
        {
            var row = new Panel
            {
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };

            var label = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(6, 0, 6, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            row.Controls.Add(label);
            return row;
        }

        protected virtual void OnRenderRow(Control rowControl, T item, int index)
        {
            Label? label = rowControl as Label;
            if (label == null)
            {
                foreach (Control child in rowControl.Controls)
                {
                    if (child is Label childLabel)
                    {
                        label = childLabel;
                        break;
                    }
                }
            }

            if (label != null)
            {
                label.Text = item?.ToString() ?? string.Empty;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _scrollBar.RefreshTheme();
            UpdateScrollBar();
            RequestVirtualRender();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            ClampScrollOffset();
            UpdateScrollBar();
            RequestVirtualRender();
        }

        protected override void OnPaddingChanged(EventArgs e)
        {
            base.OnPaddingChanged(e);
            ClampScrollOffset();
            UpdateScrollBar();
            RequestVirtualRender();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (MaximumScrollOffset <= 0)
            {
                base.OnMouseWheel(e);
                return;
            }

            int wheelDelta = SystemInformation.MouseWheelScrollDelta;
            int notches = e.Delta / wheelDelta;
            if (notches == 0)
            {
                notches = Math.Sign(e.Delta);
            }

            int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            SetVerticalOffset(_verticalOffset - notches * lines * _rowHeight);

            if (e is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int pageStep = Math.Max(_rowHeight, GetViewportBounds().Height);

            switch (e.KeyCode)
            {
                case Keys.Home:
                    SetVerticalOffset(0);
                    e.Handled = true;
                    break;
                case Keys.End:
                    SetVerticalOffset(MaximumScrollOffset);
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    SetVerticalOffset(_verticalOffset - pageStep);
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    SetVerticalOffset(_verticalOffset + pageStep);
                    e.Handled = true;
                    break;
                case Keys.Up:
                    SetVerticalOffset(_verticalOffset - _rowHeight);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    SetVerticalOffset(_verticalOffset + _rowHeight);
                    e.Handled = true;
                    break;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_renderPending)
            {
                QueueRenderNow();
            }

            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _scrollBar.ValueChanged -= ScrollBar_ValueChanged;
                RecycleAllVisibleRows();

                while (_rowPool.Count > 0)
                {
                    Control row = _rowPool.Pop();
                    if (row.Parent == this)
                        Controls.Remove(row);
                    row.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private int MaximumScrollOffset
        {
            get
            {
                return VirtualListPanelLayoutModel.GetMaximumScrollOffset(
                    _items.Count,
                    _rowHeight,
                    GetViewportBounds().Height);
            }
        }

        private void RequestVirtualRender()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            _renderPending = true;

            if (IsHandleCreated)
            {
                QueueRenderNow();
                _renderScheduler.RequestRender(this);
            }
        }

        private void RealizeVisibleRows()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            Rectangle viewport = GetViewportBounds();
            using (UiJankProfiler.Measure(
                "VirtualList.RealizeVisibleRows",
                $"Type={typeof(T).Name}; Count={_items.Count}; Offset={_verticalOffset}; View={viewport.Width}x{viewport.Height}",
                thresholdMs: 1))
            {
                bool contentDirty = _contentDirty;
                _contentDirty = false;

                if (_items.Count == 0 || viewport.Width <= 0 || viewport.Height <= 0)
                {
                    RecycleAllVisibleRows();
                    _dirtyRows.Clear();
                    return;
                }

                var range = VirtualListPanelLayoutModel.BuildRealizationRange(
                    _items.Count,
                    _verticalOffset,
                    _rowHeight,
                    viewport.Height,
                    _overscanRowCount);
                if (!range.HasRows)
                {
                    RecycleAllVisibleRows();
                    _dirtyRows.Clear();
                    return;
                }

                int firstIndex = range.FirstIndex;
                int lastIndex = range.LastIndex;

                bool allRowsAlreadyRealized = true;
                for (int index = firstIndex; index <= lastIndex; index++)
                {
                    if (!_visibleRows.ContainsKey(index))
                    {
                        allRowsAlreadyRealized = false;
                        break;
                    }
                }

                if (VirtualListPanelLayoutModel.CanReuseRealization(
                    contentDirty,
                    _dirtyRows.Count,
                    allRowsAlreadyRealized,
                    _verticalOffset,
                    _lastRealizedOffset,
                    firstIndex,
                    _lastRealizedFirstIndex,
                    lastIndex,
                    _lastRealizedLastIndex,
                    viewport.Size,
                    _lastRealizedViewportSize))
                {
                    return;
                }

                var indexesToRecycle = new List<int>();
                foreach (KeyValuePair<int, Control> pair in _visibleRows)
                {
                    if (pair.Key < firstIndex || pair.Key > lastIndex)
                    {
                        indexesToRecycle.Add(pair.Key);
                    }
                }

                int renderedRows = 0;
                int newlyRealizedRows = 0;
                SuspendLayout();
                try
                {
                    foreach (int index in indexesToRecycle)
                    {
                        RecycleRow(index);
                    }

                    bool hasMoreRowsToRealize = false;
                    for (int index = firstIndex; index <= lastIndex; index++)
                    {
                        if (!_visibleRows.ContainsKey(index) && newlyRealizedRows >= _maxNewRowsPerPass)
                        {
                            hasMoreRowsToRealize = true;
                            break;
                        }

                        Control row = GetOrCreateRow(index, out bool isNewlyRealized);
                        if (isNewlyRealized)
                            newlyRealizedRows++;

                        row.Bounds = VirtualListPanelLayoutModel.BuildRowBounds(viewport, index, _rowHeight, _verticalOffset);
                        row.Visible = true;
                        if (contentDirty || isNewlyRealized || _dirtyRows.Remove(index))
                        {
                            renderedRows++;
                            OnRenderRow(row, _items[index], index);
                        }
                    }

                    if (_ownsScrollBar)
                    {
                        _scrollBar.BringToFront();
                    }

                    if (hasMoreRowsToRealize)
                        _renderPending = true;
                }
                finally
                {
                    ResumeLayout(false);
                }

                if (UiJankProfiler.Enabled)
                {
                    UiJankProfiler.Log(
                        "VirtualList.RealizeVisibleRows.Rows",
                        $"Type={typeof(T).Name}; Range={firstIndex}-{lastIndex}; Rendered={renderedRows}; New={newlyRealizedRows}; Recycled={indexesToRecycle.Count}; Dirty={_dirtyRows.Count}; ContentDirty={contentDirty}");
                }

                _lastRealizedOffset = _verticalOffset;
                _lastRealizedFirstIndex = firstIndex;
                _lastRealizedLastIndex = lastIndex;
                _lastRealizedViewportSize = viewport.Size;

                if (_renderPending)
                    QueueRenderNow();
            }
        }

        private Control GetOrCreateRow(int index, out bool isNewlyRealized)
        {
            if (_visibleRows.TryGetValue(index, out Control? existing))
            {
                isNewlyRealized = false;
                return existing;
            }

            isNewlyRealized = true;
            Control row = _rowPool.Count > 0 ? _rowPool.Pop() : CreateRowControl();
            row.Margin = Padding.Empty;
            row.Anchor = AnchorStyles.None;
            row.Visible = false;
            if (row.Parent != this)
                Controls.Add(row);
            _visibleRows[index] = row;
            return row;
        }

        private void QueueRenderNow()
        {
            _renderQueue.Queue(this, () => IsDisposed || Disposing, () => _renderPending, RenderNow);
        }

        private void RecycleRow(int index)
        {
            if (!_visibleRows.TryGetValue(index, out Control? row))
            {
                return;
            }

            _visibleRows.Remove(index);
            row.Visible = false;
            _rowPool.Push(row);
        }

        private void RecycleAllVisibleRows()
        {
            foreach (Control row in _visibleRows.Values)
            {
                row.Visible = false;
                _rowPool.Push(row);
            }

            _visibleRows.Clear();
            _dirtyRows.Clear();
            InvalidateRealizationCache();
        }

        private void SetVerticalOffset(int offset)
        {
            int normalized = VirtualListPanelLayoutModel.ClampOffset(offset, MaximumScrollOffset);
            if (_verticalOffset == normalized)
            {
                return;
            }

            _verticalOffset = normalized;
            UpdateScrollBarValue();
            RequestVirtualRender();
        }

        private void ClampScrollOffset()
        {
            _verticalOffset = VirtualListPanelLayoutModel.ClampOffset(_verticalOffset, MaximumScrollOffset);
        }

        private void InvalidateRealizationCache()
        {
            _lastRealizedOffset = -1;
            _lastRealizedFirstIndex = -1;
            _lastRealizedLastIndex = -1;
            _lastRealizedViewportSize = Size.Empty;
        }

        private void UpdateScrollBar()
        {
            if (_scrollBar == null || _scrollBar.IsDisposed)
            {
                return;
            }

            int viewportHeight = Math.Max(1, GetViewportBounds(ignoreOwnedScrollBar: true).Height);
            int maxOffset = MaximumScrollOffset;
            bool hasOverflow = maxOffset > 0;

            _updatingScrollBar = true;
            try
            {
                _scrollBar.Minimum = 0;
                _scrollBar.SmallChange = Math.Max(1, _rowHeight);
                _scrollBar.LargeChange = Math.Max(1, viewportHeight);
                _scrollBar.Maximum = maxOffset;
                _scrollBar.Enabled = hasOverflow;
                if (_ownsScrollBar)
                {
                    _scrollBar.Visible = hasOverflow;
                }

                _scrollBar.Value = Math.Max(_scrollBar.Minimum, Math.Min(_verticalOffset, _scrollBar.Maximum));
            }
            finally
            {
                _updatingScrollBar = false;
            }
        }

        private void UpdateScrollBarValue()
        {
            if (_scrollBar == null || _scrollBar.IsDisposed)
            {
                return;
            }

            int value = Math.Max(_scrollBar.Minimum, Math.Min(_verticalOffset, _scrollBar.Maximum));

            if (_scrollBar.Value == value)
            {
                return;
            }

            _updatingScrollBar = true;
            try
            {
                _scrollBar.Value = value;
            }
            finally
            {
                _updatingScrollBar = false;
            }
        }

        private Rectangle GetViewportBounds(bool ignoreOwnedScrollBar = false)
        {
            return VirtualListPanelLayoutModel.BuildViewportBounds(
                ClientSize,
                Padding,
                _ownsScrollBar,
                _scrollBar.Visible,
                _scrollBar.Parent == this,
                _scrollBar.Width,
                ignoreOwnedScrollBar);
        }

        private void ScrollBar_ValueChanged(object? sender, EventArgs e)
        {
            if (_updatingScrollBar)
            {
                return;
            }

            SetVerticalOffset(_scrollBar.Value);
        }
    }
}
