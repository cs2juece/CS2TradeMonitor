using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class MainPanelLayoutHelper
    {
        public static int LayoutTabPanel(Panel panel, int width)
        {
            int y = 0;
            foreach (Control wrapper in panel.Controls.Cast<Control>())
            {
                if (wrapper.Tag is GroupLayoutCache cache && cache.Width == width)
                {
                    wrapper.Height = cache.Height;
                }
                else if (wrapper.Controls.Count > 0)
                {
                    Control group = wrapper.Controls[0];
                    group.MaximumSize = new Size(width, 0);
                    if (group.Width != width)
                        group.Width = width;
                    int groupHeight = MeasureDescendantBottomIterative(group);
                    groupHeight = Math.Max(UIUtils.S(48), groupHeight);
                    LiteLayoutHelpers.SetBoundsIfChanged(group, 0, 0, width, groupHeight);
                    int wrapperHeight = Math.Max(UIUtils.S(48), groupHeight + wrapper.Padding.Vertical);
                    if (wrapper.Height != wrapperHeight)
                        wrapper.Height = wrapperHeight;
                    wrapper.Tag = new GroupLayoutCache(width, wrapper.Height);
                }

                LiteLayoutHelpers.SetBoundsIfChanged(wrapper, 0, y, width, Math.Max(UIUtils.S(1), wrapper.Height));
                y = wrapper.Bottom;
            }

            return Math.Max(1, y);
        }

        public static int MeasureDescendantBottomIterative(Control root)
        {
            int bottom = root.Padding.Top;
            var stack = new Stack<(Control Parent, int OffsetTop)>();
            var visited = new HashSet<Control>();
            stack.Push((root, 0));

            while (stack.Count > 0 && visited.Count < 2000)
            {
                var current = stack.Pop();
                if (!visited.Add(current.Parent))
                    continue;

                foreach (Control child in current.Parent.Controls)
                {
                    int childTop = current.OffsetTop + child.Top;
                    int childBottom = childTop + child.Height + child.Margin.Bottom;
                    bottom = Math.Max(bottom, childBottom);

                    if (child.HasChildren)
                        stack.Push((child, childTop));
                }
            }

            return bottom + root.Padding.Bottom;
        }
    }
}
