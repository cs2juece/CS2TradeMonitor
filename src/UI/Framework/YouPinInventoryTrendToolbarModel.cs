using System;
using System.Drawing;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinInventoryTrendToolbarModel
    {
        public static YouPinInventoryTrendToolbarLayout BuildLayout(
            Size clientSize,
            int rowHeight,
            int searchHeight,
            int filterWidth,
            int filterHeight,
            int clearWidth,
            int clearHeight)
        {
            int gap = UIUtils.S(16);
            int left = UIUtils.S(12);
            int mid = Math.Max(1, rowHeight) / 2;
            int available = Math.Max(1, clientSize.Width - left * 2);
            int rightWidth = Math.Max(1, filterWidth) + Math.Max(1, clearWidth) + gap * 2;
            int searchWidth = Math.Min(UIUtils.S(320), Math.Max(UIUtils.S(170), available - rightWidth));

            var searchBounds = new Rectangle(
                left,
                mid - Math.Max(1, searchHeight) / 2,
                searchWidth,
                Math.Max(1, searchHeight));
            var filterBounds = new Rectangle(
                searchBounds.Right + gap,
                mid - Math.Max(1, filterHeight) / 2,
                Math.Max(1, filterWidth),
                Math.Max(1, filterHeight));
            var clearBounds = new Rectangle(
                filterBounds.Right + gap,
                mid - Math.Max(1, clearHeight) / 2,
                Math.Max(1, clearWidth),
                Math.Max(1, clearHeight));

            return new YouPinInventoryTrendToolbarLayout(searchBounds, filterBounds, clearBounds);
        }
    }

    internal readonly record struct YouPinInventoryTrendToolbarLayout(
        Rectangle SearchBounds,
        Rectangle FilterBounds,
        Rectangle ClearBounds);
}
