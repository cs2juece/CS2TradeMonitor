using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class RedesignCardPanel : Panel
    {
        private readonly RedesignCardFillRole _fillRole;

        public RedesignCardPanel(Color? fill = null, int radius = 8)
        {
            _fillRole = ResolveFillRole(fill);
            FillColor = fill ?? UIColors.CardBg;
            Radius = radius;
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Padding = UIUtils.S(new Padding(18));
        }

        public Color FillColor { get; set; }
        public Color BorderColor { get; set; } = UIColors.Border;
        public int Radius { get; set; }
        public bool DrawBorder { get; set; } = true;

        public void RefreshTheme()
        {
            FillColor = _fillRole == RedesignCardFillRole.Input
                ? UIColors.InputBg
                : UIColors.CardBg;
            BorderColor = UIColors.Border;
            Invalidate();
        }

        private static RedesignCardFillRole ResolveFillRole(Color? fill)
        {
            if (!fill.HasValue)
                return RedesignCardFillRole.Card;

            int argb = fill.Value.ToArgb();
            return argb == UIColors.InputBg.ToArgb()
                || argb == Color.FromArgb(15, 20, 27).ToArgb()
                    ? RedesignCardFillRole.Input
                    : RedesignCardFillRole.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            using GraphicsPath path = CreateRoundPath(rect, UIUtils.S(Radius));
            using (var brush = new SolidBrush(FillColor))
                e.Graphics.FillPath(brush, path);
            if (DrawBorder)
            {
                using var pen = new Pen(BorderColor);
                e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }

        private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            int r = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class RedesignSwitch : Control
    {
        private bool _checked;
        private bool _hover;

        public RedesignSwitch()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = UIUtils.S(new Size(44, 24));
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? CheckedChanged;

        public void RefreshTheme()
        {
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled)
                Checked = !Checked;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color surface = LiteControlSurfaceResolver.ResolveParentBackColor(Parent, UIColors.CardBg);
            using (var background = new SolidBrush(surface))
                e.Graphics.FillRectangle(background, ClientRectangle);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color track = !Enabled
                ? UIColors.ControlDisabledBg
                : Checked
                    ? UIColors.Primary
                    : (_hover ? Color.FromArgb(70, 82, 98) : Color.FromArgb(58, 68, 82));
            Color thumb = Enabled ? Color.FromArgb(230, 236, 244) : UIColors.TextDisabled;
            Rectangle trackRect = new(0, UIUtils.S(2), Width - 1, Height - UIUtils.S(4));
            using (GraphicsPath path = CreateRoundPath(trackRect, trackRect.Height / 2))
            using (var brush = new SolidBrush(track))
                e.Graphics.FillPath(brush, path);

            int d = UIUtils.S(18);
            int x = Checked ? Width - d - UIUtils.S(3) : UIUtils.S(3);
            var thumbRect = new Rectangle(x, (Height - d) / 2, d, d);
            using var thumbBrush = new SolidBrush(thumb);
            e.Graphics.FillEllipse(thumbBrush, thumbRect);
        }

        private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            int r = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class RedesignSegmentedControl : Control
    {
        private readonly string[] _items;
        private int _selectedIndex;

        public RedesignSegmentedControl(params string[] items)
        {
            _items = items.Length == 0 ? new[] { "默认" } : items;
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = UIFonts.Bold(9f);
            Height = UIUtils.S(32);
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                int normalized = Math.Clamp(value, 0, _items.Length - 1);
                if (_selectedIndex == normalized) return;
                _selectedIndex = normalized;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? SelectedIndexChanged;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || _items.Length == 0) return;
            int width = Math.Max(1, Width / _items.Length);
            SelectedIndex = Math.Clamp(e.X / width, 0, _items.Length - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath outer = CreateRoundPath(rect, UIUtils.S(6)))
            using (var bg = new SolidBrush(UIColors.InputBg))
            using (var border = new Pen(UIColors.Border))
            {
                e.Graphics.FillPath(bg, outer);
                e.Graphics.DrawPath(border, outer);
            }

            int itemWidth = Math.Max(1, Width / _items.Length);
            var selectedRect = new Rectangle(
                SelectedIndex * itemWidth,
                0,
                SelectedIndex == _items.Length - 1 ? Width - SelectedIndex * itemWidth - 1 : itemWidth,
                Height - 1);
            using (GraphicsPath selected = CreateRoundPath(selectedRect, UIUtils.S(6)))
            using (var brush = new SolidBrush(Color.FromArgb(30, 111, 220)))
            using (var pen = new Pen(UIColors.Primary))
            {
                e.Graphics.FillPath(brush, selected);
                e.Graphics.DrawPath(pen, selected);
            }

            for (int i = 0; i < _items.Length; i++)
            {
                var textRect = new Rectangle(i * itemWidth, 0, i == _items.Length - 1 ? Width - i * itemWidth : itemWidth, Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    _items[i],
                    Font,
                    textRect,
                    i == SelectedIndex ? Color.White : UIColors.TextSub,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
        }

        private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            int r = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum RedesignCardFillRole
    {
        Card,
        Input
    }

    internal sealed class RedesignSlider : Control
    {
        private bool _dragging;
        private int _value;

        public RedesignSlider()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = UIUtils.S(28);
            Minimum = 0;
            Maximum = 100;
        }

        public int Minimum { get; set; }
        public int Maximum { get; set; }

        public int Value
        {
            get => _value;
            set
            {
                int normalized = Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
                if (_value == normalized) return;
                _value = normalized;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? ValueChanged;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            _dragging = true;
            Capture = true;
            UpdateValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
                UpdateValueFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int y = Height / 2;
            int pad = UIUtils.S(8);
            int w = Math.Max(1, Width - pad * 2);
            double ratio = Maximum == Minimum ? 0 : (Value - Minimum) / (double)(Maximum - Minimum);
            int activeWidth = (int)Math.Round(w * ratio);
            var track = new Rectangle(pad, y - UIUtils.S(3), w, UIUtils.S(6));
            var active = new Rectangle(pad, y - UIUtils.S(3), activeWidth, UIUtils.S(6));
            using (var bg = new SolidBrush(Color.FromArgb(35, 43, 54)))
                e.Graphics.FillRoundedRectangle(bg, track, UIUtils.S(3));
            using (var fg = new SolidBrush(UIColors.Primary))
                e.Graphics.FillRoundedRectangle(fg, active, UIUtils.S(3));

            int d = UIUtils.S(14);
            int cx = pad + activeWidth;
            using var thumb = new SolidBrush(Color.FromArgb(220, 228, 238));
            e.Graphics.FillEllipse(thumb, cx - d / 2, y - d / 2, d, d);
        }

        private void UpdateValueFromMouse(int x)
        {
            int pad = UIUtils.S(8);
            int w = Math.Max(1, Width - pad * 2);
            double ratio = Math.Clamp((x - pad) / (double)w, 0, 1);
            Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
        }
    }

    internal static class RedesignGraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rect, int radius)
        {
            using GraphicsPath path = CreateRoundPath(rect, radius);
            graphics.FillPath(brush, path);
        }

        private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            int r = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Top, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
