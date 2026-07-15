using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor
{
    internal sealed class MarketAlertToastForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int CS_DROPSHADOW = 0x00020000;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private static readonly IntPtr HwndTopmost = new(-1);
        private static readonly List<MarketAlertToastForm> ActiveToasts = new();

        private readonly System.Windows.Forms.Timer _closeTimer;
        private readonly System.Windows.Forms.Timer _fadeTimer;
        private readonly Panel _border;
        private readonly Panel _body;
        private readonly Label _closeLabel;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        private MarketAlertToastForm(string title, string message, ToolTipIcon icon)
        {
            Name = "MarketAlertToast";
            Text = title;
            AccessibleName = title;
            AccessibleDescription = message;
            AccessibleRole = AccessibleRole.Alert;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.White;
            Padding = new Padding(0);
            Size = new Size(UIUtils.S(392), UIUtils.S(128));
            Opacity = 0;

            _border = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = GetAccentColor(icon),
                Padding = UIUtils.S(new Padding(4, 0, 0, 0))
            };
            Controls.Add(_border);

            _body = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(253, 254, 255),
                Padding = UIUtils.S(new Padding(16, 14, 14, 14))
            };
            _border.Controls.Add(_body);

            var statusIcon = new MarketAlertAppIcon(icon);
            _body.Controls.Add(statusIcon);

            _closeLabel = new Label
            {
                Text = "×",
                AutoSize = false,
                Width = UIUtils.S(24),
                Height = UIUtils.S(24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(130, 136, 144),
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = "关闭预警弹窗"
            };
            _closeLabel.Click += (_, __) => Close();
            _closeLabel.MouseEnter += (_, __) => _closeLabel.ForeColor = Color.FromArgb(210, 60, 50);
            _closeLabel.MouseLeave += (_, __) => _closeLabel.ForeColor = Color.FromArgb(130, 136, 144);
            _body.Controls.Add(_closeLabel);

            var textPanel = new Panel
            {
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            _body.Controls.Add(textPanel);

            var titleLabel = new Label
            {
                Text = title,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(24, 32, 42),
                TextAlign = ContentAlignment.MiddleLeft
            };
            textPanel.Controls.Add(titleLabel);

            var messageLabel = new Label
            {
                Text = message,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(68, 78, 90),
                TextAlign = ContentAlignment.TopLeft
            };
            textPanel.Controls.Add(messageLabel);
            _body.Layout += (_, __) =>
            {
                var pad = _body.Padding;
                int iconWidth = UIUtils.S(42);
                int gap = UIUtils.S(14);
                int closeGap = UIUtils.S(6);
                int contentLeft = pad.Left;
                int contentTop = pad.Top;
                int contentRight = Math.Max(contentLeft, _body.ClientSize.Width - pad.Right);
                int contentBottom = Math.Max(contentTop, _body.ClientSize.Height - pad.Bottom);

                _closeLabel.Location = new Point(
                    Math.Max(contentLeft, contentRight - _closeLabel.Width),
                    contentTop - UIUtils.S(2));

                statusIcon.SetBounds(
                    contentLeft,
                    contentTop + UIUtils.S(3),
                    iconWidth,
                    iconWidth);

                int textLeft = statusIcon.Right + gap;
                int textRight = Math.Max(textLeft + UIUtils.S(40), _closeLabel.Left - closeGap);
                textPanel.SetBounds(
                    textLeft,
                    contentTop,
                    Math.Max(1, textRight - textLeft),
                    Math.Max(1, contentBottom - contentTop));

                _closeLabel.BringToFront();
            };
            textPanel.Layout += (_, __) =>
            {
                int titleHeight = UIUtils.S(28);
                int gap = UIUtils.S(7);
                titleLabel.SetBounds(0, 0, textPanel.ClientSize.Width, titleHeight);
                messageLabel.SetBounds(0, titleHeight + gap, textPanel.ClientSize.Width, Math.Max(0, textPanel.ClientSize.Height - titleHeight - gap));
            };

            HookHoverPause(this);
            HookHoverPause(_border);
            HookHoverPause(_body);
            HookHoverPause(statusIcon);
            HookHoverPause(textPanel);
            HookHoverPause(titleLabel);
            HookHoverPause(messageLabel);
            HookHoverPause(_closeLabel);

            _closeTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _closeTimer.Tick += (_, __) =>
            {
                _closeTimer.Stop();
                Close();
            };

            _fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _fadeTimer.Tick += (_, __) =>
            {
                Opacity = Math.Min(1.0, Opacity + 0.12);
                if (Opacity >= 1.0)
                    _fadeTimer.Stop();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryEnableDwmRoundCorners();
            ApplyRoundedRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRoundedRegion();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _closeTimer.Start();
            _fadeTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closeTimer.Dispose();
                _fadeTimer.Dispose();
                Region?.Dispose();
                Region = null;
            }

            base.Dispose(disposing);
        }

        private void HookHoverPause(Control control)
        {
            control.MouseEnter += (_, __) =>
            {
                _closeTimer.Stop();
            };

            control.MouseLeave += (_, __) =>
            {
                if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
                    return;

                if (Visible && !IsDisposed)
                    _closeTimer.Start();
            };
        }

        private void TryEnableDwmRoundCorners()
        {
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // DWM rounded corners are best-effort; Win10 and restricted desktops can ignore them.
            }
        }

        private void ApplyRoundedRegion()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;

            using var path = UIUtils.RoundRect(new Rectangle(0, 0, Width, Height), UIUtils.S(8));
            Region?.Dispose();
            Region = new Region(path);
        }

        public static bool ShowToast(string title, string message, ToolTipIcon icon)
        {
            return ShowDesktopToast(title, message, icon);
        }

        public static bool ShowDesktopToast(string title, string message, ToolTipIcon icon)
        {
            return GlobalPromptService.Notify(
                title,
                message,
                GlobalPromptService.MapToolTipIcon(icon),
                source: "系统通知",
                dedupKey: "LegacyToast:" + title + "|" + message,
                respectDoNotDisturb: true);
        }

        public static bool ShowBottomLeftToast(string title, string message, ToolTipIcon icon)
        {
            return GlobalPromptService.Notify(
                title,
                message,
                GlobalPromptService.MapToolTipIcon(icon),
                source: "系统通知",
                dedupKey: "LegacyToast:" + title + "|" + message,
                placement: AppNotificationPlacement.BottomLeft,
                respectDoNotDisturb: true);
        }

        private static bool ShowToastCore(string title, string message, ToolTipIcon icon, bool avoidAppWindows, bool bottomLeft = false)
        {
            if (SettingsHelperRuntimeServices.Resolve().AppConfigState.Notifications.DoNotDisturbEnabled)
            {
                return false;
            }
            try
            {
                var toast = new MarketAlertToastForm(title, message, icon);
                toast.FormClosed += (_, __) =>
                {
                    lock (ActiveToasts)
                    {
                        ActiveToasts.Remove(toast);
                    }
                };
                lock (ActiveToasts)
                {
                    ActiveToasts.Add(toast);
                }

                toast.Location = CalculateLocation(toast.Size, avoidAppWindows, bottomLeft);
                toast.Show();
                toast.TopMost = true;
                SetWindowPos(toast.Handle, HwndTopmost, toast.Left, toast.Top, toast.Width, toast.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return !toast.IsDisposed && toast.Visible;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("MarketAlert", "Showing in-app alert toast failed.", ex);
                return false;
            }
        }

        private static Point CalculateLocation(Size toastSize, bool avoidAppWindows, bool bottomLeft = false)
        {
            var screen = Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;
            int margin = UIUtils.S(18);
            int x = bottomLeft ? area.Left + margin : area.Right - toastSize.Width - margin;
            int y = area.Bottom - toastSize.Height - margin;

            MarketAlertToastForm[] existing;
            lock (ActiveToasts)
            {
                existing = ActiveToasts.Where(t => t.Visible && !t.IsDisposed).ToArray();
            }

            foreach (var active in existing)
            {
                if (bottomLeft)
                {
                    bool sameSide = active.Left < area.Left + toastSize.Width + margin * 3;
                    if (sameSide) y = Math.Min(y, active.Top - toastSize.Height - margin);
                }
                else
                {
                    y = Math.Min(y, active.Top - toastSize.Height - margin);
                }
            }

            if (avoidAppWindows)
            {
                var target = new Rectangle(x, y, toastSize.Width, toastSize.Height);
                foreach (Form form in System.Windows.Forms.Application.OpenForms)
                {
                    if (!form.Visible || form is MarketAlertToastForm)
                        continue;

                    if (form is not MainForm && form is not TaskbarForm)
                        continue;

                    if (!target.IntersectsWith(form.Bounds))
                        continue;

                    y = form.Top - toastSize.Height - margin;
                    target = new Rectangle(x, y, toastSize.Width, toastSize.Height);
                }
            }

            if (y < area.Top + margin)
                y = area.Bottom - toastSize.Height - margin;

            x = Math.Max(area.Left + margin, Math.Min(x, area.Right - toastSize.Width - margin));
            y = Math.Max(area.Top + margin, Math.Min(y, area.Bottom - toastSize.Height - margin));
            return new Point(x, y);
        }

        private static Color GetAccentColor(ToolTipIcon icon)
        {
            return icon switch
            {
                ToolTipIcon.Error => Color.FromArgb(220, 64, 52),
                ToolTipIcon.Warning => Color.FromArgb(224, 138, 24),
                ToolTipIcon.Info => Color.FromArgb(0, 120, 215),
                _ => Color.FromArgb(0, 120, 215)
            };
        }

        private sealed class MarketAlertAppIcon : Control
        {
            private readonly ToolTipIcon _icon;
            private readonly Bitmap? _bitmap;

            public MarketAlertAppIcon(ToolTipIcon icon)
            {
                _icon = icon;
                _bitmap = Properties.Resources.AppIcon?.ToBitmap();
                BackColor = Color.FromArgb(253, 254, 255);
                AccessibleName = "预警应用图标";
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw |
                    ControlStyles.UserPaint,
                    true);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _bitmap?.Dispose();
                }

                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float size = Math.Max(1, Math.Min(ClientSize.Width, ClientSize.Height));
                float x = (ClientSize.Width - size) / 2f;
                float y = (ClientSize.Height - size) / 2f;
                var box = new RectangleF(x, y, size, size);
                var color = GetAccentColor(_icon);

                using (var fill = new SolidBrush(Color.FromArgb(246, 250, 255)))
                using (var path = RoundRect(box, size * 0.22f))
                {
                    g.FillPath(fill, path);
                }

                using (var border = new Pen(Color.FromArgb(212, 224, 238), Math.Max(1f, size * 0.035f)))
                using (var path = RoundRect(box, size * 0.22f))
                {
                    g.DrawPath(border, path);
                }

                if (_bitmap != null)
                {
                    float inset = size * 0.18f;
                    var iconRect = Rectangle.Round(new RectangleF(
                        box.X + inset,
                        box.Y + inset,
                        size - inset * 2f,
                        size - inset * 2f));
                    g.DrawImage(_bitmap, iconRect);
                }
                else
                {
                    using var pen = new Pen(color, Math.Max(1.5f, size * 0.06f));
                    pen.LineJoin = LineJoin.Round;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    var fallback = new RectangleF(
                        box.X + size * 0.24f,
                        box.Y + size * 0.30f,
                        size * 0.52f,
                        size * 0.32f);
                    using var fallbackPath = RoundRect(fallback, size * 0.04f);
                    g.DrawPath(pen, fallbackPath);
                    g.DrawLine(pen, box.X + size * 0.42f, box.Y + size * 0.72f, box.X + size * 0.58f, box.Y + size * 0.72f);
                    g.DrawLine(pen, box.X + size * 0.50f, box.Y + size * 0.62f, box.X + size * 0.50f, box.Y + size * 0.78f);
                }

                DrawBadge(g, box, color, GetBadgeText(_icon));
            }

            private static void DrawBadge(Graphics g, RectangleF box, Color color, string text)
            {
                float badge = box.Width * 0.34f;
                var rect = new RectangleF(
                    box.Right - badge - box.Width * 0.02f,
                    box.Bottom - badge - box.Height * 0.02f,
                    badge,
                    badge);

                using (var shadow = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
                {
                    g.FillEllipse(shadow, rect.X, rect.Y + 1, rect.Width, rect.Height);
                }

                using (var fill = new SolidBrush(color))
                {
                    g.FillEllipse(fill, rect);
                }

                using (var stroke = new Pen(Color.White, Math.Max(1f, box.Width * 0.045f)))
                {
                    g.DrawEllipse(stroke, rect);
                }

                using var font = new Font("Segoe UI", Math.Max(6f, box.Width * 0.22f), FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Color.White);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(text, font, brush, rect, format);
            }

            private static string GetBadgeText(ToolTipIcon icon)
            {
                return icon switch
                {
                    ToolTipIcon.Error => "×",
                    ToolTipIcon.Warning => "!",
                    ToolTipIcon.Info => "i",
                    _ => "i"
                };
            }

            private static GraphicsPath RoundRect(RectangleF rect, float radius)
            {
                var path = new GraphicsPath();
                if (rect.Width <= 0 || rect.Height <= 0)
                    return path;

                float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
                if (diameter <= 0)
                {
                    path.AddRectangle(rect);
                    return path;
                }

                path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
                path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

    }
}
