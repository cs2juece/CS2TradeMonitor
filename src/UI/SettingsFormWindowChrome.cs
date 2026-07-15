using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsFormWindowChrome
    {
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_NCMOUSEMOVE = 0x00A0;
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int WM_NCLBUTTONUP = 0x00A2;
        internal const int HTCLIENT = 0x0001;
        internal const int HTCAPTION = 0x0002;
        internal const int HTLEFT = 0x000A;
        internal const int HTRIGHT = 0x000B;
        internal const int HTTOP = 0x000C;
        internal const int HTTOPLEFT = 0x000D;
        internal const int HTTOPRIGHT = 0x000E;
        internal const int HTBOTTOM = 0x000F;
        internal const int HTBOTTOMLEFT = 0x0010;
        internal const int HTBOTTOMRIGHT = 0x0011;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        internal static void ApplyWindowIcon(Form target, Form? fallbackForm)
        {
            ArgumentNullException.ThrowIfNull(target);

            try
            {
                var icon = global::CS2TradeMonitor.Properties.Resources.AppIcon;
                if (icon != null)
                {
                    target.Icon = (Icon)icon.Clone();
                    return;
                }
            }
            catch
            {
                // Icon loading must not block opening settings.
            }

            try
            {
                if (fallbackForm?.Icon != null)
                    target.Icon = (Icon)fallbackForm.Icon.Clone();
            }
            catch
            {
                // Keep default icon as the last fallback.
            }
        }

        internal static void BeginWindowDrag(Form form, MouseButtons button)
        {
            ArgumentNullException.ThrowIfNull(form);
            if (!ShouldBeginWindowDrag(button))
                return;

            ReleaseCapture();
            SendMessage(form.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), CurrentCursorLParam());
        }

        internal static void BeginResizeDrag(Form form, int hitTest)
        {
            ArgumentNullException.ThrowIfNull(form);
            if (hitTest == HTCLIENT || hitTest == HTCAPTION)
                return;

            ReleaseCapture();
            SendMessage(form.Handle, WM_NCLBUTTONDOWN, new IntPtr(hitTest), CurrentCursorLParam());
        }

        internal static bool ShouldBeginWindowDrag(MouseButtons button)
        {
            return button == MouseButtons.Left;
        }

        internal static bool IsResizeHitTest(int hitTest)
        {
            return hitTest is HTLEFT
                or HTRIGHT
                or HTTOP
                or HTTOPLEFT
                or HTTOPRIGHT
                or HTBOTTOM
                or HTBOTTOMLEFT
                or HTBOTTOMRIGHT;
        }

        internal static int ResolveResizeHitTest(Size clientSize, Point clientPoint, int gripSize, bool allowResize)
        {
            if (!allowResize || clientSize.Width <= 0 || clientSize.Height <= 0)
                return HTCLIENT;

            int grip = Math.Max(1, gripSize);
            bool left = clientPoint.X >= 0 && clientPoint.X < grip;
            bool right = clientPoint.X <= clientSize.Width && clientPoint.X >= clientSize.Width - grip;
            bool top = clientPoint.Y >= 0 && clientPoint.Y < grip;
            bool bottom = clientPoint.Y <= clientSize.Height && clientPoint.Y >= clientSize.Height - grip;

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;
            return HTCLIENT;
        }

        internal static Point PointFromLParam(IntPtr lParam)
        {
            long value = lParam.ToInt64();
            return new Point((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
        }

        private static IntPtr CurrentCursorLParam()
        {
            if (!GetCursorPos(out NativePoint point))
                return IntPtr.Zero;

            return PackPointToLParam(point.X, point.Y);
        }

        private static IntPtr PackPointToLParam(int x, int y)
        {
            int packed = (y & 0xFFFF) << 16 | (x & 0xFFFF);
            return new IntPtr(packed);
        }

        internal static Cursor CursorForResizeHitTest(int hitTest)
        {
            return hitTest switch
            {
                HTLEFT or HTRIGHT => Cursors.SizeWE,
                HTTOP or HTBOTTOM => Cursors.SizeNS,
                HTTOPLEFT or HTBOTTOMRIGHT => Cursors.SizeNWSE,
                HTTOPRIGHT or HTBOTTOMLEFT => Cursors.SizeNESW,
                _ => Cursors.Default
            };
        }

        internal static Rectangle BuildManualResizeBounds(
            Rectangle startBounds,
            Point startCursor,
            Point currentCursor,
            Size minimumSize,
            int hitTest)
        {
            int minWidth = Math.Max(1, minimumSize.Width);
            int minHeight = Math.Max(1, minimumSize.Height);
            int dx = currentCursor.X - startCursor.X;
            int dy = currentCursor.Y - startCursor.Y;

            Rectangle next = startBounds;

            if (hitTest is HTLEFT or HTTOPLEFT or HTBOTTOMLEFT)
            {
                int right = startBounds.Right;
                int requestedLeft = startBounds.Left + dx;
                next.X = Math.Min(requestedLeft, right - minWidth);
                next.Width = right - next.X;
            }
            else if (hitTest is HTRIGHT or HTTOPRIGHT or HTBOTTOMRIGHT)
            {
                next.Width = Math.Max(minWidth, startBounds.Width + dx);
            }

            if (hitTest is HTTOP or HTTOPLEFT or HTTOPRIGHT)
            {
                int bottom = startBounds.Bottom;
                int requestedTop = startBounds.Top + dy;
                next.Y = Math.Min(requestedTop, bottom - minHeight);
                next.Height = bottom - next.Y;
            }
            else if (hitTest is HTBOTTOM or HTBOTTOMLEFT or HTBOTTOMRIGHT)
            {
                next.Height = Math.Max(minHeight, startBounds.Height + dy);
            }

            return next;
        }

        internal sealed class ResizeMessageFilter : IMessageFilter, IDisposable
        {
            private readonly Form _form;
            private readonly Func<bool> _allowResize;
            private readonly Action? _resizeCompleted;
            private int _activeHitTest = HTCLIENT;
            private Point _resizeStartCursor;
            private Rectangle _resizeStartBounds;
            private bool _disposed;

            public ResizeMessageFilter(Form form, Func<bool> allowResize, Action? resizeCompleted = null)
            {
                _form = form ?? throw new ArgumentNullException(nameof(form));
                _allowResize = allowResize ?? throw new ArgumentNullException(nameof(allowResize));
                _resizeCompleted = resizeCompleted;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (_activeHitTest != HTCLIENT)
                    return HandleManualResizeMessage(m.Msg);

                if (_disposed
                    || _form.IsDisposed
                    || !_form.IsHandleCreated
                    || !_form.Visible
                    || !_allowResize()
                    || (m.Msg != WM_MOUSEMOVE && m.Msg != WM_LBUTTONDOWN && m.Msg != WM_LBUTTONUP)
                    || !BelongsToForm(m.HWnd))
                {
                    return false;
                }

                Point screenPoint = MouseMessageToScreenPoint(m.HWnd, m.LParam);
                Point clientPoint = _form.PointToClient(screenPoint);
                int hit = ResolveResizeHitTest(
                    _form.ClientSize,
                    clientPoint,
                    UIUtils.S(8),
                    allowResize: true);
                if (hit == HTCLIENT)
                    return false;

                Cursor.Current = CursorForResizeHitTest(hit);
                if (m.Msg == WM_LBUTTONDOWN)
                {
                    TryBeginManualResize(hit);
                    return true;
                }

                return false;
            }

            public void Dispose()
            {
                EndManualResize(invokeCompleted: false);
                _disposed = true;
            }

            public bool TryBeginManualResize(int hitTest)
            {
                if (!IsResizeHitTest(hitTest) || _activeHitTest != HTCLIENT)
                    return false;

                _activeHitTest = hitTest;
                _resizeStartCursor = Control.MousePosition;
                _resizeStartBounds = _form.Bounds;
                _form.Capture = true;
                Cursor.Current = CursorForResizeHitTest(hitTest);
                return true;
            }

            public bool IsManualResizeActive => _activeHitTest != HTCLIENT;

            public bool HandleManualResizeMessage(int message)
            {
                if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
                {
                    EndManualResize(invokeCompleted: false);
                    return false;
                }

                if (message == WM_LBUTTONUP
                    || message == WM_NCLBUTTONUP
                    || !_allowResize()
                    || (Control.MouseButtons & MouseButtons.Left) == 0)
                {
                    EndManualResize(invokeCompleted: true);
                    return true;
                }

                if (message != WM_MOUSEMOVE && message != WM_NCMOUSEMOVE)
                    return false;

                _form.Bounds = BuildManualResizeBounds(
                    _resizeStartBounds,
                    _resizeStartCursor,
                    Control.MousePosition,
                    _form.MinimumSize,
                    _activeHitTest);
                Cursor.Current = CursorForResizeHitTest(_activeHitTest);
                return true;
            }

            private void EndManualResize(bool invokeCompleted)
            {
                if (_activeHitTest == HTCLIENT)
                    return;

                _activeHitTest = HTCLIENT;
                if (!_form.IsDisposed && _form.IsHandleCreated)
                    _form.Capture = false;

                Cursor.Current = Cursors.Default;
                if (invokeCompleted)
                    _resizeCompleted?.Invoke();
            }

            private bool BelongsToForm(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero)
                    return false;
                if (hwnd == _form.Handle)
                    return true;

                Control? control = Control.FromHandle(hwnd);
                while (control != null)
                {
                    if (ReferenceEquals(control, _form))
                        return true;
                    control = control.Parent;
                }

                return IsChild(_form.Handle, hwnd);
            }

            private static Point MouseMessageToScreenPoint(IntPtr hwnd, IntPtr lParam)
            {
                Point clientPoint = PointFromLParam(lParam);
                var nativePoint = new NativePoint(clientPoint.X, clientPoint.Y);
                ClientToScreen(hwnd, ref nativePoint);
                return new Point(nativePoint.X, nativePoint.Y);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;

            public NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }
    }
}
