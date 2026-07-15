using System;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Controls
{
    public sealed class ThemedVerticalScrollBar : Control
    {
        private int _minimum;
        private int _maximum;
        private int _largeChange = 1;
        private int _smallChange = 20;
        private int _value;
        private bool _dragging;
        private bool _thumbHot;
        private int _dragStartY;
        private int _dragStartValue;

        public event EventHandler? ValueChanged;

        public ThemedVerticalScrollBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.Selectable,
                true);

            TabStop = false;
            Width = UIUtils.S(14);
            BackColor = UIColors.CardBg;
            Cursor = Cursors.Hand;
        }

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_maximum < _minimum)
                    _maximum = _minimum;
                SetValue(_value, raise: false);
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(_minimum, value);
                SetValue(_value, raise: false);
                Invalidate();
            }
        }

        public int LargeChange
        {
            get => _largeChange;
            set
            {
                _largeChange = Math.Max(1, value);
                Invalidate();
            }
        }

        public int SmallChange
        {
            get => _smallChange;
            set => _smallChange = Math.Max(1, value);
        }

        public int Value
        {
            get => _value;
            set => SetValue(value, raise: true);
        }

        public void SetRange(int totalItems, int displayedItems, int firstItem)
        {
            int viewport = Math.Max(1, displayedItems);
            Minimum = 0;
            Maximum = Math.Max(0, totalItems - viewport);
            LargeChange = viewport;
            SmallChange = 1;
            SetValue(Math.Min(firstItem, _maximum), raise: false);
            Visible = totalItems > viewport;
            Invalidate();
        }

        public void RefreshTheme()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(UIColors.CardBg);

            using (var trackBrush = new SolidBrush(UIColors.IsDark ? UIColors.InputBg : UIColors.ControlBg))
                e.Graphics.FillRectangle(trackBrush, ClientRectangle);

            using (var borderPen = new Pen(UIColors.Border))
                e.Graphics.DrawLine(borderPen, 0, 0, 0, Height);

            if (!Enabled || _maximum <= _minimum || Height <= 0 || Width <= 0)
                return;

            Rectangle thumb = GetThumbBounds();
            Color thumbColor = _dragging
                ? (UIColors.IsDark ? Color.FromArgb(88, 101, 118) : Color.FromArgb(150, 158, 168))
                : _thumbHot
                    ? (UIColors.IsDark ? Color.FromArgb(72, 84, 99) : Color.FromArgb(165, 173, 184))
                    : (UIColors.IsDark ? Color.FromArgb(55, 66, 80) : Color.FromArgb(184, 190, 198));

            using var thumbBrush = new SolidBrush(thumbColor);
            e.Graphics.FillRectangle(thumbBrush, thumb);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled || _maximum <= _minimum)
                return;

            Rectangle thumb = GetThumbBounds();
            if (!thumb.Contains(e.Location))
            {
                SetValue(_value + (e.Y < thumb.Top ? -_largeChange : _largeChange), raise: true);
                return;
            }

            _dragging = true;
            _dragStartY = e.Y;
            _dragStartValue = _value;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Enabled || _maximum <= _minimum)
                return;

            Rectangle thumb = GetThumbBounds();
            if (_dragging)
            {
                int trackRange = Math.Max(1, Height - thumb.Height);
                int delta = e.Y - _dragStartY;
                int range = Math.Max(1, _maximum - _minimum);
                int next = _dragStartValue + (int)Math.Round(delta * (range / (double)trackRange));
                SetValue(next, raise: true);
                return;
            }

            bool hot = thumb.Contains(e.Location);
            if (_thumbHot != hot)
            {
                _thumbHot = hot;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_thumbHot)
            {
                _thumbHot = false;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
                return;

            _dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled)
                return;

            int delta = e.Delta < 0 ? _smallChange : -_smallChange;
            SetValue(_value + delta, raise: true);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        private Rectangle GetThumbBounds()
        {
            int range = Math.Max(1, _maximum - _minimum);
            int thumbHeight = Math.Max(UIUtils.S(28), (int)Math.Round(Height * (_largeChange / (double)(range + _largeChange))));
            thumbHeight = Math.Min(Math.Max(1, Height), thumbHeight);

            int trackRange = Math.Max(0, Height - thumbHeight);
            int top = range <= 0 ? 0 : (int)Math.Round(trackRange * ((_value - _minimum) / (double)range));
            int inset = Math.Min(UIUtils.S(3), Math.Max(1, Width / 4));
            return new Rectangle(inset, top, Math.Max(1, Width - inset * 2), thumbHeight);
        }

        private void SetValue(int value, bool raise)
        {
            int normalized = Math.Max(_minimum, Math.Min(value, _maximum));
            if (_value == normalized)
                return;

            _value = normalized;
            Invalidate();
            if (raise)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
