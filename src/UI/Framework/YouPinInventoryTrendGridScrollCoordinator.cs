using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryTrendGridScrollCoordinator
    {
        private readonly Func<DataGridView?> _getGrid;
        private readonly Func<ThemedVerticalScrollBar?> _getScrollBar;
        private readonly Func<bool> _isDisposed;
        private readonly Func<bool> _isHandleCreated;
        private readonly Action<Action> _postToUi;
        private bool _syncingGridScroll;
        private bool _gridScrollBarUpdateQueued;

        public YouPinInventoryTrendGridScrollCoordinator(
            Func<DataGridView?> getGrid,
            Func<ThemedVerticalScrollBar?> getScrollBar,
            Func<bool> isDisposed,
            Func<bool> isHandleCreated,
            Action<Action> postToUi)
        {
            _getGrid = getGrid ?? throw new ArgumentNullException(nameof(getGrid));
            _getScrollBar = getScrollBar ?? throw new ArgumentNullException(nameof(getScrollBar));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _isHandleCreated = isHandleCreated ?? throw new ArgumentNullException(nameof(isHandleCreated));
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        }

        public void HandleMouseWheel(object? sender, MouseEventArgs e)
        {
            DataGridView? grid = _getGrid();
            using (UiJankProfiler.Measure("YouPinInventoryTrend.GridMouseWheel", $"Rows={grid?.RowCount ?? 0}", thresholdMs: 1))
            {
                if (grid == null || grid.RowCount == 0)
                    return;

                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines <= 0 || lines > 12)
                    lines = 3;

                int direction = e.Delta < 0 ? 1 : -1;
                ScrollToFirstDisplayedRow(GetFirstDisplayedRow() + direction * lines);
            }
        }

        public void ScrollToCustomBarValue()
        {
            if (_syncingGridScroll)
                return;

            ThemedVerticalScrollBar? scrollBar = _getScrollBar();
            if (scrollBar == null)
                return;

            ScrollToFirstDisplayedRow(scrollBar.Value);
        }

        public void ScrollToFirstDisplayedRow(int rowIndex)
        {
            DataGridView? grid = _getGrid();
            using (UiJankProfiler.Measure("YouPinInventoryTrend.GridScroll", $"Target={rowIndex}; Rows={grid?.RowCount ?? 0}", thresholdMs: 1))
            {
                if (grid == null || grid.RowCount == 0)
                    return;

                int target = YouPinInventoryTrendGridScrollModel.NormalizeFirstDisplayedRow(
                    grid.RowCount,
                    GetDisplayedRowCount(),
                    rowIndex);

                try
                {
                    _syncingGridScroll = true;
                    if (grid.FirstDisplayedScrollingRowIndex != target)
                        grid.FirstDisplayedScrollingRowIndex = target;
                }
                catch
                {
                    // 滚动同步期间行可能被刷新重建，失败时等待下一次滚动事件校正。
                }
                finally
                {
                    _syncingGridScroll = false;
                }

                UpdateScrollBar();
            }
        }

        public void UpdateScrollBar()
        {
            DataGridView? grid = _getGrid();
            ThemedVerticalScrollBar? scrollBar = _getScrollBar();
            if (grid == null || scrollBar == null)
                return;

            int totalRows = Math.Max(0, grid.RowCount);
            int displayedRows = totalRows == 0 ? 0 : GetDisplayedRowCount();
            int firstRow = totalRows == 0 ? 0 : GetFirstDisplayedRow();

            scrollBar.SetRange(totalRows, displayedRows, firstRow);
        }

        public void QueueUpdate()
        {
            if (_gridScrollBarUpdateQueued || _isDisposed())
                return;

            if (!_isHandleCreated())
            {
                UpdateScrollBar();
                return;
            }

            _gridScrollBarUpdateQueued = true;
            try
            {
                _postToUi(() =>
                {
                    _gridScrollBarUpdateQueued = false;
                    if (!_isDisposed())
                        UpdateScrollBar();
                });
            }
            catch
            {
                _gridScrollBarUpdateQueued = false;
            }
        }

        private int GetDisplayedRowCount()
        {
            DataGridView? grid = _getGrid();
            if (grid == null || grid.RowCount == 0)
                return 0;

            try
            {
                int displayed = grid.DisplayedRowCount(false);
                if (displayed > 0)
                    return displayed;
            }
            catch
            {
                // 网格句柄或行模板暂不可用时使用估算高度兜底。
            }

            int bodyHeight = Math.Max(1, grid.ClientSize.Height - grid.ColumnHeadersHeight);
            int rowHeight = Math.Max(1, grid.RowTemplate.Height);
            return YouPinInventoryTrendGridScrollModel.BuildFallbackDisplayedRowCount(bodyHeight, rowHeight);
        }

        private int GetFirstDisplayedRow()
        {
            DataGridView? grid = _getGrid();
            if (grid == null || grid.RowCount == 0)
                return 0;

            try
            {
                return Math.Max(0, grid.FirstDisplayedScrollingRowIndex);
            }
            catch
            {
                return 0;
            }
        }
    }

    internal static class YouPinInventoryTrendGridScrollModel
    {
        public static int NormalizeFirstDisplayedRow(int totalRows, int displayedRows, int requestedRow)
        {
            int maxFirstRow = Math.Max(0, Math.Max(0, totalRows) - Math.Max(1, displayedRows));
            return Math.Max(0, Math.Min(requestedRow, maxFirstRow));
        }

        public static int BuildFallbackDisplayedRowCount(int bodyHeight, int rowHeight)
        {
            return Math.Max(1, Math.Max(1, bodyHeight) / Math.Max(1, rowHeight));
        }
    }
}
