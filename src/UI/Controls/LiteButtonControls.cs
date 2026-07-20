using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core; // 确保引用了 UIUtils
using CS2TradeMonitor.src.UI.SettingsPage;
using CS2TradeMonitor.Infrastructure.Diagnostics;


namespace CS2TradeMonitor.src.UI.Controls
{
    public class LiteLink : Label
    {
        private Color _normalColor = UIColors.Link;
        private Color _hoverColor = UIColors.LinkHover;

        public LiteLink(string text, Action? onClick = null)
        {
            this.Text = text;
            this.AutoSize = true;
            this.Cursor = Cursors.Hand;
            this.ForeColor = _normalColor;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Underline);

            if (onClick != null)
                this.Click += (s, e) => { if (this.Enabled) onClick(); };

            this.MouseEnter += (s, e) => { if (this.Enabled) this.ForeColor = _hoverColor; };
            this.MouseLeave += (s, e) => { if (this.Enabled) this.ForeColor = _normalColor; };
        }

        public void SetColor(Color normal, Color hover)
        {
            _normalColor = normal;
            _hoverColor = hover;
            if (Enabled) this.ForeColor = normal;
        }

        public new bool Enabled
        {
            get => base.Enabled;
            set
            {
                base.Enabled = value;
                this.Cursor = value ? Cursors.Hand : Cursors.Default;
                this.ForeColor = value ? _normalColor : UIColors.TextDisabled;
            }
        }
    }


    // =======================================================================
    // 3. 组合/高级组件 (New Standard Components)
    // =======================================================================

    internal static class LiteControlSurfaceResolver
    {
        public static Color ResolveParentBackColor(Control? parent, Color fallback)
        {
            Control? current = parent;
            while (current != null)
            {
                if (TryGetFillOverride(current, out Color fillOverride))
                    return fillOverride;

                if (current.BackColor != Color.Transparent)
                    return current.BackColor;

                current = current.Parent;
            }

            return fallback;
        }

        private static bool TryGetFillOverride(Control control, out Color color)
        {
            object? value = control.GetType().GetProperty("FillOverride")?.GetValue(control);
            if (value is Color fillOverride)
            {
                color = fillOverride;
                return true;
            }

            color = Color.Empty;
            return false;
        }
    }

    public class LiteCheck : CheckBox
    {
        private bool _hover;

        public LiteCheck(bool val, string text = "")
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Checked = val;
            AutoSize = false;
            Cursor = Cursors.Hand;
            Text = text;
            Padding = UIUtils.S(new Padding(2));
            ForeColor = UIColors.TextSub;
            Font = new Font("Microsoft YaHei UI", 9F);
            Height = UIUtils.S(22);
            Width = Math.Max(UIUtils.S(34), UIUtils.S(28) + TextRenderer.MeasureText(text, Font).Width);
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.Transparent;
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

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color bg = ResolveParentBackColor();
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, ClientRectangle);

            int box = UIUtils.S(14);
            var boxRect = new Rectangle(UIUtils.S(2), Math.Max(0, (Height - box) / 2), box, box);
            Color boxBg;
            Color border;
            if (!Enabled)
            {
                boxBg = UIColors.ControlDisabledBg;
                border = UIColors.Border;
            }
            else if (Checked)
            {
                boxBg = UIColors.Primary;
                border = UIColors.Primary;
            }
            else
            {
                boxBg = UIColors.InputBg;
                border = _hover ? UIColors.Primary : UIColors.Border;
            }

            using (var b = new SolidBrush(boxBg))
                e.Graphics.FillRectangle(b, boxRect);
            using (var p = new Pen(border))
                e.Graphics.DrawRectangle(p, boxRect.X, boxRect.Y, boxRect.Width - 1, boxRect.Height - 1);

            if (Checked)
            {
                using var p = new Pen(Color.White, Math.Max(1.6f, UIUtils.ScaleFactor));
                p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(p, new[]
                {
                    new Point(boxRect.Left + UIUtils.S(3), boxRect.Top + UIUtils.S(7)),
                    new Point(boxRect.Left + UIUtils.S(6), boxRect.Top + UIUtils.S(10)),
                    new Point(boxRect.Right - UIUtils.S(3), boxRect.Top + UIUtils.S(4))
                });
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            }

            var textRect = new Rectangle(boxRect.Right + UIUtils.S(6), 0, Math.Max(1, Width - boxRect.Right - UIUtils.S(8)), Height);
            Color textColor = Enabled ? UIColors.TextSub : UIColors.TextDisabled;
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private Color ResolveParentBackColor()
        {
            return LiteControlSurfaceResolver.ResolveParentBackColor(Parent, UIColors.CardBg);
        }
    }


    public class LiteButton : Button
    {
        private readonly bool _primary;
        private readonly bool _dashed;
        private bool _hover;
        private bool _pressed;
        private bool _isActive;
        private Color? _fillColorOverride;
        private bool _showKeyboardFocusCue;

        public LiteButton(string t, bool p = false, bool dashed = false)
        {
            Text = t;
            _primary = p;
            _dashed = dashed;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = GetAutoSizeForText(t);
            FlatAppearance.BorderSize = 0;
            RefreshTheme();
        }

        public bool IsPrimary => _primary;

        public Color? BorderColorOverride { get; set; }

        public Color? FillColorOverride
        {
            get => _fillColorOverride;
            set
            {
                if (_fillColorOverride == value)
                    return;

                _fillColorOverride = value;
                Invalidate();
            }
        }

        public bool ShowKeyboardFocusCue
        {
            get => _showKeyboardFocusCue;
            set
            {
                if (_showKeyboardFocusCue == value)
                    return;

                _showKeyboardFocusCue = value;
                Invalidate();
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;

                _isActive = value;
                Invalidate();
            }
        }

        public void RefreshTheme()
        {
            bool keepSemanticText = !_primary &&
                (ForeColor.ToArgb() == UIColors.TextWarn.ToArgb() ||
                 ForeColor.ToArgb() == UIColors.TextCrit.ToArgb());

            BackColor = _primary ? UIColors.Primary : UIColors.ControlBg;
            ForeColor = _primary ? Color.White : (keepSemanticText ? ForeColor : UIColors.TextMain);
            FlatAppearance.BorderColor = UIColors.Border;
            FlatAppearance.MouseOverBackColor = UIColors.ControlHover;
            FlatAppearance.MouseDownBackColor = UIColors.ControlPressed;
            Invalidate();
        }

        private Size GetAutoSizeForText(string text)
        {
            int minWidth = UIUtils.S(80);
            int textWidth = TextRenderer.MeasureText(text ?? "", Font).Width + UIUtils.S(28);
            return new Size(Math.Max(minWidth, textWidth), UIUtils.S(32));
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
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            if (IsDisposed || Disposing)
                return;

            _pressed = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            string? operationId = null;
            if (DetailedDiagnosticsRuntime.IsEnabled)
            {
                operationId = Guid.NewGuid().ToString("N");
                string actionId = ResolveDiagnosticActionId();
                DetailedDiagnosticsRuntime.Record(
                    "Information",
                    "UI",
                    "ButtonAction",
                    new Dictionary<string, object?>
                    {
                        ["actionId"] = actionId,
                        ["controlType"] = GetType().Name,
                        ["pageId"] = FindDiagnosticPageId(),
                        ["activationSource"] = "Click"
                    },
                    operationId);
            }
            using IDisposable? scope = operationId is null
                ? null
                : DetailedDiagnosticOperationContext.Begin(operationId);
            base.OnClick(e);
        }

        private string ResolveDiagnosticActionId()
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name.Length <= 80 ? Name : Name[..80];
            if (!string.IsNullOrWhiteSpace(AccessibleName))
                return AccessibleName.Length <= 80 ? AccessibleName : AccessibleName[..80];
            string display = Text ?? string.Empty;
            return DetailedDiagnosticsRuntime.Current?.Correlate("uiAction", display)
                ?? GetType().Name;
        }

        private string FindDiagnosticPageId()
        {
            for (Control? current = Parent; current is not null; current = current.Parent)
            {
                string typeName = current.GetType().Name;
                if (typeName.EndsWith("Page", StringComparison.Ordinal)
                    || typeName.EndsWith("Form", StringComparison.Ordinal))
                {
                    return typeName;
                }
            }
            return FindForm()?.GetType().Name ?? "UnknownPage";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var parentBrush = new SolidBrush(ResolveParentBackColor()))
                e.Graphics.FillRectangle(parentBrush, ClientRectangle);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = ResolveFillColor();
            Color border = ResolveBorderColor();
            Color text = ResolveTextColor();
            int radius = UIUtils.S(3);

            using (var path = CreateRoundedRect(rect, radius))
            using (var fillBrush = new SolidBrush(fill))
                e.Graphics.FillPath(fillBrush, path);

            using (var path = CreateRoundedRect(rect, radius))
            using (var pen = new Pen(border, 1F))
            {
                if (_dashed) pen.DashStyle = DashStyle.Dash;
                e.Graphics.DrawPath(pen, path);
            }

            var textRect = new Rectangle(UIUtils.S(8), 0, Math.Max(1, Width - UIUtils.S(16)), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (_dashed)
            {
                using var innerPen = new Pen(Color.FromArgb(50, border), 1F) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(innerPen, 2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
            }

            if (_showKeyboardFocusCue && Focused && ShowFocusCues)
            {
                Rectangle focusRect = Rectangle.Inflate(rect, -UIUtils.S(3), -UIUtils.S(3));
                ControlPaint.DrawFocusRectangle(e.Graphics, focusRect);
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        private Color ResolveFillColor()
        {
            if (!Enabled) return UIColors.ControlDisabledBg;
            if (_primary || _isActive)
            {
                if (_fillColorOverride.HasValue)
                {
                    Color fill = _fillColorOverride.Value;
                    if (_pressed) return ControlPaint.Dark(fill, 0.12F);
                    if (_hover) return ControlPaint.Dark(fill, 0.04F);
                    return fill;
                }
                if (_pressed) return UIColors.IsDark ? Color.FromArgb(0, 103, 205) : Color.FromArgb(0, 100, 190);
                if (_hover) return UIColors.IsDark ? Color.FromArgb(24, 144, 255) : Color.FromArgb(0, 132, 235);
                return UIColors.Primary;
            }

            if (_pressed) return UIColors.ControlPressed;
            if (_hover) return UIColors.ControlHover;
            return UIColors.ControlBg;
        }

        private Color ResolveBorderColor()
        {
            if (!Enabled) return UIColors.Border;
            if (BorderColorOverride.HasValue) return BorderColorOverride.Value;
            if ((_primary || _isActive) && _fillColorOverride.HasValue) return _fillColorOverride.Value;
            if (_primary || _isActive) return UIColors.Primary;
            if (_hover || Focused) return UIColors.Primary;
            return UIColors.Border;
        }

        private Color ResolveTextColor()
        {
            if (!Enabled) return UIColors.TextDisabled;
            if (_primary || _isActive) return Color.White;
            if (ForeColor.ToArgb() == UIColors.TextWarn.ToArgb() ||
                ForeColor.ToArgb() == UIColors.TextCrit.ToArgb())
            {
                return ForeColor;
            }
            return UIColors.TextMain;
        }

        private Color ResolveParentBackColor()
        {
            return LiteControlSurfaceResolver.ResolveParentBackColor(Parent, UIColors.MainBg);
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (radius <= 1)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class LiteNavBtn : Button
    {
        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; Invalidate(); }
        }
        public LiteNavBtn(string text)
        {
            Text = "  " + text;
            Size = new Size(UIUtils.S(190), UIUtils.S(40));
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleLeft;
            Font = UIUtils.GetFont("Microsoft YaHei UI", 10F, false);
            Cursor = Cursors.Hand;
            Margin = UIUtils.S(new Padding(5, 2, 5, 2));
            BackColor = UIColors.SidebarBg;
            ForeColor = UIColors.TextMain;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color bg = _isActive ? UIColors.NavSelected : (ClientRectangle.Contains(PointToClient(Cursor.Position)) ? UIColors.NavHover : UIColors.SidebarBg);
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, ClientRectangle);
            if (_isActive)
            {
                using (var b = new SolidBrush(UIColors.Primary))
                    e.Graphics.FillRectangle(b, 0, UIUtils.S(8), UIUtils.S(3), Height - UIUtils.S(16));
            }
            Font drawFont = UIUtils.GetFont("Microsoft YaHei UI", 10F, _isActive);
            TextRenderer.DrawText(e.Graphics, Text, drawFont, new Point(UIUtils.S(12), UIUtils.S(9)), UIColors.TextMain);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    }

    public class LiteSortBtn : Button
    {
        private bool _hover;
        private bool _pressed;

        public LiteSortBtn(string txt)
        {
            Text = txt;
            Size = new Size(UIUtils.S(24), UIUtils.S(24));
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
            BackColor = UIColors.ControlBg;
            ForeColor = UIColors.IsDark ? UIColors.TextSub : Color.DimGray;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold);
            Margin = new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs mevent) { base.OnMouseDown(mevent); if (mevent.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs mevent) { base.OnMouseUp(mevent); _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color bg;
            Color fg;
            if (!Enabled)
            {
                bg = UIColors.ControlDisabledBg;
                fg = UIColors.TextDisabled;
            }
            else if (_pressed)
            {
                bg = UIColors.ControlPressed;
                fg = UIColors.TextMain;
            }
            else if (_hover)
            {
                bg = UIColors.ControlHover;
                fg = UIColors.TextMain;
            }
            else
            {
                bg = UIColors.ControlBg;
                fg = UIColors.IsDark ? UIColors.TextSub : Color.DimGray;
            }

            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(UIColors.Border))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }


    // ★★★ New: Header Button (Variable Width) ★★★
    public class LiteHeaderBtn : Button
    {
        public LiteHeaderBtn(string txt)
        {
            Text = txt;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.FromArgb(225, 225, 225);
            ForeColor = Color.DimGray;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            Margin = new Padding(0);

            // Auto Width
            int w = TextRenderer.MeasureText(txt, Font).Width + UIUtils.S(16);
            Size = new Size(w, UIUtils.S(24));
        }
        public void SetColor(Color c) { ForeColor = c; }
    }



    /// <summary>
    /// 终极防闪烁面板
    /// 1. 开启双缓冲合成
    /// 2. 拦截背景擦除 (消除白屏闪烁的关键)
    /// </summary>
    public class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            // 开启所有标准的双缓冲标志
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.ContainerControl |
                          ControlStyles.Selectable, true); // 确保它作为容器被优化
            this.TabStop = true;
            this.UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UIColors.ApplyNativeTheme(this);
        }

        // ★★★ 核心修复：拦截 WM_ERASEBKGND ★★★
        // Windows 默认会先用背景色清除窗口，这会导致一瞬间的“白屏”或“黑屏”。
        // 我们直接返回 1 (true)，告诉 Windows“我已经擦除过了，你别管”，从而消灭闪烁。
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014) // WM_ERASEBKGND
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

}
