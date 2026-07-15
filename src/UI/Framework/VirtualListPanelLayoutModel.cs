using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class VirtualListPanelLayoutModel
    {
        internal static int NormalizeRowHeight(int rowHeight)
        {
            return Math.Max(1, rowHeight);
        }

        internal static int NormalizeOverscanRowCount(int overscanRowCount)
        {
            return Math.Max(0, overscanRowCount);
        }

        internal static int NormalizeMaxNewRowsPerPass(int maxNewRowsPerPass)
        {
            return maxNewRowsPerPass <= 0 ? int.MaxValue : maxNewRowsPerPass;
        }

        internal static int GetTotalContentHeight(int itemCount, int rowHeight)
        {
            if (itemCount <= 0)
                return 0;

            long total = (long)itemCount * NormalizeRowHeight(rowHeight);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        internal static int GetMaximumScrollOffset(int itemCount, int rowHeight, int viewportHeight)
        {
            return Math.Max(0, GetTotalContentHeight(itemCount, rowHeight) - Math.Max(0, viewportHeight));
        }

        internal static int ClampOffset(int offset, int maximumOffset)
        {
            return Math.Max(0, Math.Min(offset, Math.Max(0, maximumOffset)));
        }

        internal static int GetFirstVisibleIndex(int itemCount, int verticalOffset, int rowHeight)
        {
            if (itemCount <= 0)
                return -1;

            return Math.Min(itemCount - 1, Math.Max(0, verticalOffset / NormalizeRowHeight(rowHeight)));
        }

        internal static int GetLastVisibleIndex(int itemCount, int verticalOffset, int rowHeight, int viewportHeight)
        {
            if (itemCount <= 0)
                return -1;

            int last = (verticalOffset + Math.Max(0, viewportHeight - 1)) / NormalizeRowHeight(rowHeight);
            return Math.Min(itemCount - 1, Math.Max(0, last));
        }

        internal static int GetScrollToIndexOffset(int itemCount, int index, int rowHeight)
        {
            if (itemCount <= 0)
                return 0;

            int normalized = Math.Max(0, Math.Min(index, itemCount - 1));
            long offset = (long)normalized * NormalizeRowHeight(rowHeight);
            return offset > int.MaxValue ? int.MaxValue : (int)offset;
        }

        internal static Rectangle BuildViewportBounds(
            Size clientSize,
            Padding padding,
            bool ownsScrollBar,
            bool scrollBarVisible,
            bool scrollBarParented,
            int scrollBarWidth,
            bool ignoreOwnedScrollBar)
        {
            int effectiveScrollBarWidth = 0;
            if (!ignoreOwnedScrollBar && ownsScrollBar && scrollBarVisible && scrollBarParented)
            {
                effectiveScrollBarWidth = Math.Max(0, scrollBarWidth);
            }

            int width = Math.Max(0, clientSize.Width - padding.Horizontal - effectiveScrollBarWidth);
            int height = Math.Max(0, clientSize.Height - padding.Vertical);
            return new Rectangle(padding.Left, padding.Top, width, height);
        }

        internal static VirtualListPanelRealizationRange BuildRealizationRange(
            int itemCount,
            int verticalOffset,
            int rowHeight,
            int viewportHeight,
            int overscanRowCount)
        {
            if (itemCount <= 0 || viewportHeight <= 0)
                return VirtualListPanelRealizationRange.Empty;

            int normalizedRowHeight = NormalizeRowHeight(rowHeight);
            int normalizedOverscan = NormalizeOverscanRowCount(overscanRowCount);
            int firstIndex = Math.Max(0, verticalOffset / normalizedRowHeight - normalizedOverscan);
            int lastIndex = Math.Min(
                itemCount - 1,
                (verticalOffset + viewportHeight - 1) / normalizedRowHeight + normalizedOverscan);

            return new VirtualListPanelRealizationRange(firstIndex, lastIndex);
        }

        internal static Rectangle BuildRowBounds(Rectangle viewport, int index, int rowHeight, int verticalOffset)
        {
            int normalizedRowHeight = NormalizeRowHeight(rowHeight);
            int y = viewport.Top + index * normalizedRowHeight - verticalOffset;
            return new Rectangle(viewport.Left, y, viewport.Width, normalizedRowHeight);
        }

        internal static bool CanReuseRealization(
            bool contentDirty,
            int dirtyRowCount,
            bool allRowsAlreadyRealized,
            int verticalOffset,
            int lastRealizedOffset,
            int firstIndex,
            int lastRealizedFirstIndex,
            int lastIndex,
            int lastRealizedLastIndex,
            Size viewportSize,
            Size lastRealizedViewportSize)
        {
            return !contentDirty
                && dirtyRowCount == 0
                && allRowsAlreadyRealized
                && verticalOffset == lastRealizedOffset
                && firstIndex == lastRealizedFirstIndex
                && lastIndex == lastRealizedLastIndex
                && viewportSize == lastRealizedViewportSize;
        }
    }

    internal readonly record struct VirtualListPanelRealizationRange(int FirstIndex, int LastIndex)
    {
        public static readonly VirtualListPanelRealizationRange Empty = new(-1, -1);

        public bool HasRows => FirstIndex >= 0 && LastIndex >= FirstIndex;
    }
}
