using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal sealed record SettingsFormTitleBarChrome(
        Panel TitleBar,
        Label TitleLabel,
        Button MinimizeButton,
        Button MaximizeButton,
        Button CloseButton);

    internal readonly record struct SettingsFormTitleBarScaleMetrics(
        int Height,
        Padding Padding,
        int TitleWidth,
        int ButtonWidth);

    internal static class SettingsFormChromeControls
    {
        public static SettingsFormTitleBarScaleMetrics BuildTitleBarScaleMetrics()
        {
            return new SettingsFormTitleBarScaleMetrics(
                Height: UIUtils.S(34),
                Padding: UIUtils.S(new Padding(12, 0, 0, 0)),
                TitleWidth: UIUtils.S(260),
                ButtonWidth: UIUtils.S(46));
        }

        public static Size BuildThemeSwitchSize()
        {
            return UIUtils.S(new Size(66, 28));
        }

        public static SettingsFormTitleBarChrome CreateTitleBar(
            string title,
            Action close,
            Action minimize,
            Action maximize,
            MouseEventHandler dragHandler)
        {
            ArgumentNullException.ThrowIfNull(close);
            ArgumentNullException.ThrowIfNull(minimize);
            ArgumentNullException.ThrowIfNull(maximize);
            ArgumentNullException.ThrowIfNull(dragHandler);

            SettingsFormTitleBarScaleMetrics metrics = BuildTitleBarScaleMetrics();
            var titleBar = new Panel
            {
                Dock = DockStyle.Fill,
                Height = metrics.Height,
                BackColor = UIColors.SidebarBg,
                Padding = metrics.Padding
            };
            var titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Left,
                Width = metrics.TitleWidth,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UIUtils.GetFont("Microsoft YaHei UI", 9F, false),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };
            var closeButton = CreateTitleButton("×", close, isClose: true);
            var maxButton = CreateTitleButton("□", maximize, isClose: false);
            var minButton = CreateTitleButton("−", minimize, isClose: false);

            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(minButton);
            titleBar.Controls.Add(maxButton);
            titleBar.Controls.Add(closeButton);
            titleBar.MouseDown += dragHandler;
            titleLabel.MouseDown += dragHandler;
            titleBar.Paint += (_, e) =>
            {
                using var p = new Pen(UIColors.Border);
                e.Graphics.DrawLine(p, 0, titleBar.Height - 1, Math.Max(0, titleBar.Width - 1), titleBar.Height - 1);
            };

            ApplyTitleBarTheme(titleBar, titleLabel, minButton, maxButton, closeButton);
            return new SettingsFormTitleBarChrome(titleBar, titleLabel, minButton, maxButton, closeButton);
        }

        public static void ApplyTitleBarScale(
            Panel titleBar,
            Label titleLabel,
            Button minButton,
            Button maxButton,
            Button closeButton)
        {
            SettingsFormTitleBarScaleMetrics metrics = BuildTitleBarScaleMetrics();
            titleBar.Height = metrics.Height;
            titleBar.Padding = metrics.Padding;
            titleLabel.Width = metrics.TitleWidth;
            minButton.Width = metrics.ButtonWidth;
            maxButton.Width = metrics.ButtonWidth;
            closeButton.Width = metrics.ButtonWidth;
        }

        public static void ApplyTitleBarTheme(
            Panel titleBar,
            Label titleLabel,
            Button minButton,
            Button maxButton,
            Button closeButton)
        {
            titleBar.BackColor = UIColors.SidebarBg;
            titleLabel.ForeColor = UIColors.TextMain;
            minButton.BackColor = UIColors.SidebarBg;
            maxButton.BackColor = UIColors.SidebarBg;
            closeButton.BackColor = UIColors.SidebarBg;
            minButton.ForeColor = UIColors.TextSub;
            maxButton.ForeColor = UIColors.TextSub;
            closeButton.ForeColor = UIColors.TextSub;
            minButton.FlatAppearance.MouseOverBackColor = UIColors.ControlHover;
            minButton.FlatAppearance.MouseDownBackColor = UIColors.ControlPressed;
            maxButton.FlatAppearance.MouseOverBackColor = UIColors.ControlHover;
            maxButton.FlatAppearance.MouseDownBackColor = UIColors.ControlPressed;
            closeButton.FlatAppearance.MouseOverBackColor = UIColors.Negative;
            closeButton.FlatAppearance.MouseDownBackColor = UIColors.TextCrit;
            minButton.Invalidate();
            maxButton.Invalidate();
            closeButton.Invalidate();
            titleBar.Invalidate();
        }

        public static void UpdateMaximizeButton(Button button, bool maximized)
        {
            button.Text = maximized ? "❐" : "□";
            button.Invalidate();
        }

        public static ThemeModeSwitch CreateThemeSwitchButton(bool darkMode, EventHandler clickHandler)
        {
            ArgumentNullException.ThrowIfNull(clickHandler);

            var button = new ThemeModeSwitch
            {
                Dock = DockStyle.None,
                Cursor = Cursors.Hand,
                TabStop = true,
                Size = BuildThemeSwitchSize()
            };
            button.Click += clickHandler;
            UpdateThemeSwitchButton(button, darkMode);
            return button;
        }

        public static void UpdateThemeSwitchButton(ThemeModeSwitch button, bool darkMode)
        {
            button.SetDarkMode(darkMode);
            button.Invalidate();
        }

        private static Button CreateTitleButton(string text, Action action, bool isClose)
        {
            SettingsFormTitleBarScaleMetrics metrics = BuildTitleBarScaleMetrics();
            var button = new TitleBarButton(isClose)
            {
                Dock = DockStyle.Right,
                Width = metrics.ButtonWidth,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                Font = UIUtils.GetFont("Microsoft YaHei UI", 10F, false),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0),
                Padding = new Padding(0),
                TabStop = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (_, __) => action();
            return button;
        }
    }

    internal sealed class TitleBarButton : Button
    {
        private readonly bool _isClose;
        private bool _hover;
        private bool _down;

        public TitleBarButton(bool isClose)
        {
            _isClose = isClose;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            var g = pevent.Graphics;
            Color bg = UIColors.SidebarBg;
            Color fg = UIColors.TextSub;

            if (_isClose && _hover)
            {
                bg = _down ? Color.FromArgb(160, 32, 24) : Color.FromArgb(196, 43, 28);
                fg = Color.White;
            }
            else if (_hover)
            {
                bg = _down ? UIColors.ControlPressed : UIColors.ControlHover;
                fg = UIColors.TextMain;
            }

            using (var b = new SolidBrush(bg))
                g.FillRectangle(b, ClientRectangle);

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            using var borderPen = new Pen(UIColors.Border);
            g.DrawLine(borderPen, 0, Height - 1, Math.Max(0, Width - 1), Height - 1);
        }
    }

    internal sealed class ThemeModeSwitch : Control
    {
        private bool _darkMode;
        private bool _hovered;
        private bool _pressed;

        public ThemeModeSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable
                | ControlStyles.UserPaint, true);
            BackColor = UIColors.SidebarBg;
            ForeColor = Color.Black;
            MinimumSize = UIUtils.S(new Size(150, 34));
        }

        public void SetDarkMode(bool darkMode)
        {
            _darkMode = darkMode;
            Text = darkMode ? "深色模式" : "浅色模式";
            AccessibleName = Text;
            AccessibleDescription = "切换设置面板深色或浅色模式";
            BackColor = UIColors.SidebarBg;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                e.Handled = true;
                OnClick(EventArgs.Empty);
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var back = new SolidBrush(UIColors.SidebarBg))
                e.Graphics.FillRectangle(back, ClientRectangle);

            float outerPad = Math.Max(2f, UIUtils.S(3));
            float borderWidth = Math.Max(1.2f, UIUtils.S(1));
            float trackHeight = Math.Min(Height - outerPad * 2f, UIUtils.S(30));
            float trackWidth = Math.Max(UIUtils.S(140), Width - outerPad * 2f);
            var track = new RectangleF(
                (Width - trackWidth) / 2f,
                (Height - trackHeight) / 2f,
                trackWidth,
                trackHeight);

            Color fillColor;
            Color borderColor;
            Color iconColor;
            if (_darkMode)
            {
                fillColor = _pressed
                    ? UIColors.ControlPressed
                    : _hovered
                        ? UIColors.ControlHover
                        : UIColors.ControlBg;
                borderColor = Enabled ? UIColors.Border : UIColors.ControlDisabledBg;
                iconColor = Enabled ? Color.FromArgb(142, 196, 255) : UIColors.TextDisabled;
            }
            else
            {
                fillColor = _pressed
                    ? UIColors.ControlPressed
                    : _hovered
                        ? UIColors.ControlHover
                        : UIColors.ControlBg;
                borderColor = Enabled ? UIColors.Border : UIColors.ControlDisabledBg;
                iconColor = Enabled ? UIColors.TextMain : UIColors.TextDisabled;
            }

            using (var trackPath = CreateRoundedRectPath(track, track.Height / 2f))
            using (var fillBrush = new SolidBrush(fillColor))
            {
                e.Graphics.FillPath(fillBrush, trackPath);
            }

            var borderRect = track;
            borderRect.Inflate(-borderWidth / 2f, -borderWidth / 2f);
            using (var borderPath = CreateRoundedRectPath(borderRect, borderRect.Height / 2f))
            using (var borderPen = new Pen(borderColor, borderWidth))
            {
                e.Graphics.DrawPath(borderPen, borderPath);
            }

            float thumbSize = Math.Max(UIUtils.S(24), track.Height - UIUtils.S(6));
            float thumbInset = Math.Max(UIUtils.S(3), (track.Height - thumbSize) / 2f);
            float centerX = _darkMode
                ? track.Right - thumbInset - thumbSize / 2f
                : track.Left + thumbInset + thumbSize / 2f;
            float centerY = track.Top + track.Height / 2f;
            var thumbRect = new RectangleF(centerX - thumbSize / 2f, centerY - thumbSize / 2f, thumbSize, thumbSize);

            using (var thumbBrush = new SolidBrush(_darkMode ? Color.FromArgb(31, 42, 56) : Color.FromArgb(245, 248, 252)))
                e.Graphics.FillEllipse(thumbBrush, thumbRect);
            using (var thumbPen = new Pen(UIColors.Border, Math.Max(1f, UIUtils.S(1))))
                e.Graphics.DrawEllipse(thumbPen, thumbRect);

            float iconSize = Math.Min(thumbSize * 0.58f, UIUtils.S(15));
            if (_darkMode)
                DrawMoon(e.Graphics, new PointF(centerX, centerY), iconSize, iconColor, _darkMode ? Color.FromArgb(31, 42, 56) : fillColor);
            else
                DrawSun(e.Graphics, new PointF(centerX, centerY), iconSize, iconColor);

            var textRect = _darkMode
                ? RectangleF.FromLTRB(track.Left + UIUtils.S(14), track.Top, thumbRect.Left - UIUtils.S(8), track.Bottom)
                : RectangleF.FromLTRB(thumbRect.Right + UIUtils.S(8), track.Top, track.Right - UIUtils.S(14), track.Bottom);
            if (textRect.Width > UIUtils.S(42))
            {
                using var textBrush = new SolidBrush(Enabled ? UIColors.TextMain : UIColors.TextDisabled);
                using var textFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                using var textFont = new Font(Font.FontFamily, 9F, FontStyle.Bold);
                e.Graphics.DrawString(Text, textFont, textBrush, textRect, textFormat);
            }

            if (Focused && Enabled)
            {
                var focusRect = track;
                focusRect.Inflate(-borderWidth - UIUtils.S(2), -borderWidth - UIUtils.S(2));
                using var focusPath = CreateRoundedRectPath(focusRect, focusRect.Height / 2f);
                using var focusPen = new Pen(UIColors.Primary, Math.Max(1f, UIUtils.S(1)));
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }

        private static void DrawSun(Graphics g, PointF center, float size, Color color)
        {
            float radius = size * 0.27f;
            float innerRay = size * 0.42f;
            float outerRay = size * 0.56f;

            using var rayPen = new Pen(color, Math.Max(2.3f, size * 0.09f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            for (int i = 0; i < 8; i++)
            {
                double angle = Math.PI * 2d * i / 8d;
                float x1 = center.X + (float)Math.Cos(angle) * innerRay;
                float y1 = center.Y + (float)Math.Sin(angle) * innerRay;
                float x2 = center.X + (float)Math.Cos(angle) * outerRay;
                float y2 = center.Y + (float)Math.Sin(angle) * outerRay;
                g.DrawLine(rayPen, x1, y1, x2, y2);
            }

            using var sunBrush = new SolidBrush(color);
            g.FillEllipse(sunBrush, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
        }

        private static void DrawMoon(Graphics g, PointF center, float size, Color color, Color cutoutColor)
        {
            float radius = size * 0.42f;
            using var moonBrush = new SolidBrush(color);
            using var cutoutBrush = new SolidBrush(cutoutColor);

            g.FillEllipse(moonBrush, center.X - radius, center.Y - radius, radius * 2f, radius * 2f);
            float cutoutRadius = radius * 0.92f;
            g.FillEllipse(cutoutBrush,
                center.X - radius * 0.08f,
                center.Y - cutoutRadius * 1.06f,
                cutoutRadius * 2f,
                cutoutRadius * 2f);
        }

        private static GraphicsPath CreateRoundedRectPath(RectangleF rect, float radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
