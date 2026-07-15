using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class FrameworkSettingsPageLayoutHelper
    {
        private const int SB_HORZ = 0;
        internal const int DefaultPageHorizontalPadding = 22;
        internal const int DefaultPageTopPadding = 18;
        internal const int DefaultPageBottomPadding = 72;
        internal const int DefaultContentMinimumWidth = 1;
        internal const int CompactContentMinimumWidth = 720;
        internal const int StandardContentMinimumWidth = 820;
        internal const int ItemMonitorContentMinimumWidth = 860;
        internal const int WideContentMinimumWidth = 900;
        internal const int DefaultContentExtraRightPadding = 4;

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        internal static void HideHorizontalScroll(ScrollableControl control)
        {
            try
            {
                control.HorizontalScroll.Enabled = false;
                control.HorizontalScroll.Visible = false;
                control.HorizontalScroll.Maximum = 0;
                control.AutoScrollMinSize = new Size(0, control.AutoScrollMinSize.Height);
                if (control.IsHandleCreated)
                    ShowScrollBar(control.Handle, SB_HORZ, false);
            }
            catch
            {
                // Visual-only guard; layout must continue even if the HWND is closing.
            }
        }

        internal static void AttachAutoWidth(LiteComboBox combo)
        {
            combo.Inner.DropDown += (_, __) =>
            {
                int maxWidth = combo.Inner.Width;
                int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
                foreach (object? item in combo.Inner.Items)
                {
                    if (item == null)
                        continue;

                    string text = item.ToString() ?? "";
                    int width = TextRenderer.MeasureText(text, combo.Inner.Font).Width + scrollBarWidth + UIUtils.S(12);
                    if (width > maxWidth)
                        maxWidth = width;
                }

                combo.Inner.DropDownWidth = maxWidth;
            };
        }

        internal static Padding CreateDefaultPagePadding(int bottomPadding = DefaultPageBottomPadding)
        {
            return UIUtils.S(new Padding(
                DefaultPageHorizontalPadding,
                DefaultPageTopPadding,
                DefaultPageHorizontalPadding,
                bottomPadding));
        }

        internal static Rectangle CalculateDefaultContentBounds(
            ScrollableControl container,
            int minimumWidth = DefaultContentMinimumWidth,
            int extraRightPadding = DefaultContentExtraRightPadding,
            int scrollBarWidth = -1)
        {
            return CalculateDefaultContentBounds(
                container.ClientSize.Width,
                container.Padding,
                minimumWidth,
                extraRightPadding,
                scrollBarWidth);
        }

        internal static Rectangle CalculateDefaultContentBounds(
            int containerClientWidth,
            Padding containerPadding,
            int minimumWidth = DefaultContentMinimumWidth,
            int extraRightPadding = DefaultContentExtraRightPadding,
            int scrollBarWidth = -1)
        {
            Rectangle bounds = CalculateCenteredContentBounds(
                containerClientWidth,
                containerPadding,
                minimumWidth,
                extraRightPadding,
                subtractHorizontalPadding: true,
                scrollBarWidth);
            return new Rectangle(bounds.Left, containerPadding.Top, bounds.Width, bounds.Height);
        }

        internal static int CalculateVisibleWidthWithinForm(Control control)
        {
            int clientWidth = Math.Max(1, control.ClientSize.Width);
            if (!control.IsHandleCreated)
                return clientWidth;

            try
            {
                Form? form = control.FindForm();
                int visibleRight = form is { IsHandleCreated: true }
                    ? form.PointToScreen(new Point(form.ClientSize.Width, 0)).X
                    : Screen.FromControl(control).Bounds.Right;
                int controlLeft = control.PointToScreen(Point.Empty).X;
                int visibleWidth = visibleRight - controlLeft;
                if (visibleWidth > 0)
                    return Math.Min(clientWidth, visibleWidth);
            }
            catch
            {
                // Layout should keep working while a window is being created or disposed.
            }

            return clientWidth;
        }

        internal static int CalculateGroupWidth(
            int wrapperClientWidth,
            int parentClientWidth,
            int parentWidth,
            Padding parentPadding,
            bool parentIsScrollable,
            bool parentVerticalScrollVisible,
            int scrollBarWidth,
            int scrollBarInset,
            int minimumPreferredWidth)
        {
            int width = wrapperClientWidth;
            if (parentIsScrollable && parentVerticalScrollVisible && width > scrollBarWidth)
                width -= scrollBarWidth + scrollBarInset;

            if (width < minimumPreferredWidth)
            {
                int availableParentWidth = parentClientWidth > 0 ? parentClientWidth : parentWidth;
                availableParentWidth -= parentPadding.Horizontal;
                if (parentIsScrollable && availableParentWidth > scrollBarWidth)
                    availableParentWidth -= scrollBarWidth;

                width = Math.Max(width, availableParentWidth);
            }

            return Math.Max(1, width);
        }

        internal static int CalculateStableContentWidth(
            int containerClientWidth,
            Padding containerPadding,
            int minimumWidth,
            int extraRightPadding,
            bool subtractHorizontalPadding,
            int scrollBarWidth = -1)
        {
            int available = containerClientWidth;
            if (subtractHorizontalPadding)
                available -= containerPadding.Horizontal;

            int effectiveScrollBarWidth = scrollBarWidth >= 0
                ? scrollBarWidth
                : SystemInformation.VerticalScrollBarWidth;
            available -= effectiveScrollBarWidth;
            available -= UIUtils.S(extraRightPadding);

            int scaledMinimum = UIUtils.S(minimumWidth);
            if (available >= scaledMinimum)
                return available;

            int adaptiveMinimum = UIUtils.S(Math.Min(minimumWidth, 640));
            return Math.Max(Math.Max(1, available), adaptiveMinimum);
        }

        internal static Rectangle CalculateCenteredContentBounds(
            int containerClientWidth,
            Padding containerPadding,
            int minimumWidth,
            int extraRightPadding,
            bool subtractHorizontalPadding,
            int scrollBarWidth = -1)
        {
            int width = CalculateStableContentWidth(
                containerClientWidth,
                containerPadding,
                minimumWidth,
                extraRightPadding,
                subtractHorizontalPadding,
                scrollBarWidth);

            return CalculateCenteredContentBounds(containerClientWidth, width, scrollBarWidth);
        }

        internal static Rectangle CalculateCenteredContentBounds(
            int containerClientWidth,
            int contentWidth,
            int scrollBarWidth = -1)
        {
            int effectiveScrollBarWidth = scrollBarWidth >= 0
                ? scrollBarWidth
                : SystemInformation.VerticalScrollBarWidth;
            int viewportWidth = Math.Max(1, containerClientWidth - effectiveScrollBarWidth);
            int width = Math.Max(1, contentWidth);
            int left = Math.Max(0, (viewportWidth - width) / 2);
            return new Rectangle(left, 0, width, 0);
        }

        internal static Rectangle CalculateTopLevelContentBounds(ScrollableControl container, int contentWidth)
        {
            Rectangle bounds = CalculateCenteredContentBounds(container.ClientSize.Width, contentWidth);
            return new Rectangle(bounds.Left, container.Padding.Top, bounds.Width, bounds.Height);
        }

        internal static void RelayoutGroupWrapper(Control wrapper, Control group)
        {
            ClampGroupWidth(wrapper, group);
            int groupHeight = Math.Max(UIUtils.S(48), group.PreferredSize.Height);
            int groupWidth = Math.Max(1, group.Width);
            int groupLeft = Math.Max(0, (wrapper.ClientSize.Width - groupWidth) / 2);
            if (group.Left != groupLeft || group.Top != 0 || group.Width != groupWidth || group.Height != groupHeight)
                group.SetBounds(groupLeft, 0, groupWidth, groupHeight);

            wrapper.Height = groupHeight + wrapper.Padding.Vertical;
        }

        internal static void StretchTopLevelContent(ScrollableControl container, int width)
        {
            StretchTopLevelContent(container, CalculateTopLevelContentBounds(container, width));
        }

        internal static void StretchTopLevelContent(ScrollableControl container, Rectangle bounds)
        {
            if (container.IsDisposed)
                return;

            int left = Math.Max(0, bounds.Left);
            int top = Math.Max(0, bounds.Top);
            int width = Math.Max(1, bounds.Width);

            foreach (Control child in container.Controls)
            {
                if (child.IsDisposed || child.Dock == DockStyle.Fill)
                    continue;

                if (child.Dock == DockStyle.Top || child.Anchor.HasFlag(AnchorStyles.Right) || child is TableLayoutPanel)
                {
                    if (child.Left != left || child.Top != top || child.Width != width)
                        child.SetBounds(left, top, width, child.Height);
                }

                if (child is TableLayoutPanel table)
                {
                    table.PerformLayout();
                    int preferredHeight = Math.Max(UIUtils.S(1), table.GetPreferredSize(new Size(width, 0)).Height);
                    if (table.Height != preferredHeight)
                        table.Height = preferredHeight;
                }

                child.PerformLayout();
            }
        }

        internal static void RefreshTheme(Control root)
        {
            foreach (Control child in root.Controls)
            {
                UIColors.ApplyNativeTheme(child);
                switch (child)
                {
                    case RedesignCardPanel card:
                        card.RefreshTheme();
                        break;
                    case YouPinCcRoundedPanel roundedPanel:
                        roundedPanel.Invalidate();
                        break;
                    case LiteButton button:
                        button.RefreshTheme();
                        break;
                    case LiteComboBox combo:
                        combo.RefreshTheme();
                        break;
                    case LiteUnderlineInput input:
                        input.RefreshTheme();
                        break;
                    case LiteColorInput colorInput:
                        colorInput.Input.RefreshTheme();
                        break;
                }

                if (child.HasChildren)
                    RefreshTheme(child);
            }
        }

        private static void ClampGroupWidth(Control wrapper, Control group)
        {
            Control? parent = wrapper.Parent;
            bool parentIsScrollable = parent is ScrollableControl;
            int parentClientWidth = parent?.ClientSize.Width ?? 0;
            int parentWidth = parent?.Width ?? 0;
            Padding parentPadding = parent?.Padding ?? Padding.Empty;
            int scrollBarWidth = SystemInformation.VerticalScrollBarWidth;
            int width = CalculateGroupWidth(
                wrapper.ClientSize.Width,
                parentClientWidth,
                parentWidth,
                parentPadding,
                parentIsScrollable,
                parentIsScrollable,
                scrollBarWidth,
                UIUtils.S(2),
                UIUtils.S(560));

            group.MaximumSize = new Size(width, 0);
            group.Width = width;
        }
    }
}
