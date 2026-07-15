using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class ScopeSegmentControl : Control
    {
        private bool _onlySpecified;

        public ScopeSegmentControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UIColors.ControlBg;
            Cursor = Cursors.Hand;
            Font = UIFonts.Bold(9F);
        }

        public event Action<bool>? ScopeChanged;

        public bool OnlySpecified
        {
            get => _onlySpecified;
            set
            {
                if (_onlySpecified == value)
                    return;

                _onlySpecified = value;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            bool next = e.X >= Width / 2;
            if (next == _onlySpecified)
                return;

            _onlySpecified = next;
            Invalidate();
            ScopeChanged?.Invoke(next);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parentBrush = new SolidBrush(ResolveParentBackColor());
            e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            var outer = new Rectangle(0, 0, Width - 1, Height - 1);
            int half = Width / 2;
            using var outerPath = UIUtils.RoundRect(outer, UIUtils.S(4));
            using var baseBrush = new SolidBrush(UIColors.ControlBg);
            e.Graphics.FillPath(baseBrush, outerPath);

            var activeRect = _onlySpecified
                ? new Rectangle(half, 0, Width - half, Height)
                : new Rectangle(0, 0, half + 1, Height);
            using (var activeBrush = new SolidBrush(UIColors.Primary))
                e.Graphics.FillRectangle(activeBrush, activeRect);

            using var border = new Pen(UIColors.Border);
            e.Graphics.DrawPath(border, outerPath);
            using var divider = new Pen(UIColors.Border);
            e.Graphics.DrawLine(divider, half, UIUtils.S(4), half, Height - UIUtils.S(4));

            DrawSegmentText(e.Graphics, new Rectangle(0, 0, half, Height), "整个库存", !_onlySpecified);
            DrawSegmentText(e.Graphics, new Rectangle(half, 0, Width - half, Height), "指定单品", _onlySpecified);
        }

        private void DrawSegmentText(Graphics graphics, Rectangle bounds, string text, bool active)
        {
            TextRenderer.DrawText(
                graphics,
                text,
                Font,
                bounds,
                active ? Color.White : UIColors.TextMain,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private Color ResolveParentBackColor()
        {
            Control? current = Parent;
            while (current != null && current.BackColor == Color.Transparent)
                current = current.Parent;
            return current?.BackColor ?? UIColors.CardBg;
        }
    }
}
