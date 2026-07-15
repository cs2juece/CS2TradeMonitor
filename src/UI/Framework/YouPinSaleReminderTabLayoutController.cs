using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum YouPinSaleReminderTabWrapperRole
    {
        WaitDeliver,
        AutoDelivery,
        TodoSettings,
        MsgSettings,
        MsgCenter,
        AccountTools
    }

    internal static class YouPinSaleReminderTabLayoutModel
    {
        public static IReadOnlyList<YouPinSaleReminderTabWrapperRole> WrapperOrder { get; } =
            Array.AsReadOnly(new[]
            {
                YouPinSaleReminderTabWrapperRole.WaitDeliver,
                YouPinSaleReminderTabWrapperRole.TodoSettings,
                YouPinSaleReminderTabWrapperRole.MsgSettings,
                YouPinSaleReminderTabWrapperRole.MsgCenter,
                YouPinSaleReminderTabWrapperRole.AutoDelivery,
                YouPinSaleReminderTabWrapperRole.AccountTools
            });
    }

    internal sealed class YouPinSaleReminderTabWrapperSet
    {
        private readonly IReadOnlyDictionary<YouPinSaleReminderTabWrapperRole, Control?> _wrappers;

        public YouPinSaleReminderTabWrapperSet(
            Control? waitDeliver,
            Control? autoDelivery,
            Control? todoSettings,
            Control? msgSettings,
            Control? msgCenter,
            Control? accountTools = null)
        {
            _wrappers = new Dictionary<YouPinSaleReminderTabWrapperRole, Control?>
            {
                [YouPinSaleReminderTabWrapperRole.WaitDeliver] = waitDeliver,
                [YouPinSaleReminderTabWrapperRole.AutoDelivery] = autoDelivery,
                [YouPinSaleReminderTabWrapperRole.TodoSettings] = todoSettings,
                [YouPinSaleReminderTabWrapperRole.MsgSettings] = msgSettings,
                [YouPinSaleReminderTabWrapperRole.MsgCenter] = msgCenter,
                [YouPinSaleReminderTabWrapperRole.AccountTools] = accountTools
            };
        }

        public IEnumerable<Control> GetTopDownControls(Control container)
        {
            ArgumentNullException.ThrowIfNull(container);

            return YouPinSaleReminderTabLayoutModel.WrapperOrder
                .Select(role => _wrappers[role])
                .Where(control => control != null && container.Controls.Contains(control))
                .Cast<Control>()
                .Distinct();
        }
    }

    internal sealed class YouPinSaleReminderTabLayoutController
    {
        private const int SB_HORZ = 0;

        private readonly ScrollableControl _container;
        private readonly Func<bool> _isDisposed;
        private readonly Func<bool> _isHandleCreated;
        private readonly Action<Action> _beginInvoke;
        private readonly Func<YouPinSaleReminderTabWrapperSet> _getWrappers;
        private bool _stabilizing;
        private bool _retryQueued;
        private bool _wrappersReordered;
        private string _lastLayoutSignature = string.Empty;

        public YouPinSaleReminderTabLayoutController(
            ScrollableControl container,
            Func<bool> isDisposed,
            Func<bool> isHandleCreated,
            Action<Action> beginInvoke,
            Func<YouPinSaleReminderTabWrapperSet> getWrappers)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _isHandleCreated = isHandleCreated ?? throw new ArgumentNullException(nameof(isHandleCreated));
            _beginInvoke = beginInvoke ?? throw new ArgumentNullException(nameof(beginInvoke));
            _getWrappers = getWrappers ?? throw new ArgumentNullException(nameof(getWrappers));
        }

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        public void Stabilize(bool deferIfNotReady)
        {
            if (_isDisposed() || _stabilizing)
                return;

            if (_container.ClientSize.Width <= 0 || _container.ClientSize.Height <= 0)
            {
                if (deferIfNotReady)
                    QueueRetry();
                return;
            }

            _stabilizing = true;
            try
            {
                List<Control> controls = GetTopDownControls().ToList();
                Rectangle bounds = FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(_container);
                string signature = BuildLayoutSignature(bounds, controls);
                if (string.Equals(signature, _lastLayoutSignature, StringComparison.Ordinal))
                    return;

                LayoutWrappersInDocumentFlow(bounds, controls);
                _lastLayoutSignature = signature;
                _container.Invalidate();
            }
            finally
            {
                _stabilizing = false;
            }

            if (deferIfNotReady)
                QueueRetry();
        }

        public void QueueRetry()
        {
            if (_retryQueued || _isDisposed() || !_isHandleCreated())
                return;

            _retryQueued = true;
            try
            {
                _beginInvoke(() =>
                {
                    _retryQueued = false;
                    Stabilize(deferIfNotReady: false);
                });
            }
            catch
            {
                _retryQueued = false;
            }
        }

        public void ReorderWrappers()
        {
            if (_wrappersReordered)
                return;

            List<Control> ordered = GetTopDownControls().ToList();
            if (ordered.Count == 0)
                return;

            _container.SuspendLayout();
            try
            {
                foreach (Control control in ordered)
                    _container.Controls.Remove(control);

                for (int i = ordered.Count - 1; i >= 0; i--)
                    _container.Controls.Add(ordered[i]);
            }
            finally
            {
                _container.ResumeLayout(true);
            }

            _wrappersReordered = true;
            _lastLayoutSignature = string.Empty;
        }

        private void LayoutWrappersInDocumentFlow(Rectangle bounds, IReadOnlyList<Control> controls)
        {
            int x = bounds.Left;
            int y = bounds.Top;
            int width = bounds.Width;

            foreach (Control control in controls)
            {
                if (control.IsDisposed)
                    continue;

                control.Dock = DockStyle.None;
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                control.Margin = Padding.Empty;
                if (control.Width != width)
                    control.Width = width;

                int height = Math.Max(1, control.Height);
                if (control.Controls.Count > 0 && control.Controls[0] is LiteSettingsGroup group)
                    height = Math.Max(height, group.PreferredSize.Height + control.Padding.Vertical);

                control.SetBounds(x, y, width, height);
                if (control.Visible)
                    y += height;
            }

            _container.AutoScrollMinSize = new Size(0, y + _container.Padding.Bottom);
            HideHorizontalScroll();
        }

        private static string BuildLayoutSignature(Rectangle bounds, IReadOnlyList<Control> controls)
        {
            var builder = new StringBuilder();
            builder.Append(bounds.Left).Append('|')
                .Append(bounds.Top).Append('|')
                .Append(bounds.Width).Append('|')
                .Append(controls.Count);

            foreach (Control control in controls)
            {
                builder.Append('|')
                    .Append(control.GetHashCode()).Append(':')
                    .Append(control.Visible ? '1' : '0').Append(':')
                    .Append(control.Height);
                if (control.Controls.Count > 0 && control.Controls[0] is LiteSettingsGroup group)
                    builder.Append(':').Append(group.PreferredSize.Height);
            }

            return builder.ToString();
        }

        private IEnumerable<Control> GetTopDownControls()
        {
            return _getWrappers().GetTopDownControls(_container);
        }

        private void HideHorizontalScroll()
        {
            try
            {
                _container.HorizontalScroll.Enabled = false;
                _container.HorizontalScroll.Visible = false;
                _container.HorizontalScroll.Maximum = 0;
                _container.AutoScrollMinSize = new Size(0, _container.AutoScrollMinSize.Height);
                if (_container.IsHandleCreated)
                    ShowScrollBar(_container.Handle, SB_HORZ, false);
            }
            catch
            {
                // Visual-only guard; layout must continue even if the HWND is closing.
            }
        }
    }
}
