using System.Drawing.Drawing2D;
using System.Diagnostics.CodeAnalysis;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.src.UI.Controls;

internal static class LiteCorners
{
    public const int Radius = 6;

    public static GraphicsPath Path(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter < 2) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Clip(Control control, int radius = Radius)
    {
        void Update()
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            using var path = Path(control.ClientRectangle, UIUtils.S(radius));
            Region? previous = control.Region;
            control.Region = new Region(path);
            previous?.Dispose();
        }
        control.SizeChanged += (_, _) => Update();
        control.DpiChangedAfterParent += (_, _) => Update();
        Update();
    }
}

public sealed class LiteTextBox : Panel
{
    private readonly TextBox _input = new() { BorderStyle = BorderStyle.None };

    public LiteTextBox()
    {
        DoubleBuffered = true;
        BackColor = UIColors.InputBg;
        ForeColor = UIColors.TextMain;
        Controls.Add(_input);
        LiteCorners.Clip(this);
        _input.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _input.GotFocus += (_, _) => Invalidate();
        _input.LostFocus += (_, _) => Invalidate();
        Click += (_, _) => _input.Focus();
        RefreshTheme();
    }

    [AllowNull]
    public override string Text { get => _input.Text; set => _input.Text = value ?? string.Empty; }
    public string PlaceholderText { get => _input.PlaceholderText; set => _input.PlaceholderText = value; }
    public bool ReadOnly { get => _input.ReadOnly; set => _input.ReadOnly = value; }
    public bool Multiline { get => _input.Multiline; set { _input.Multiline = value; PerformLayout(); } }
    public ScrollBars ScrollBars { get => _input.ScrollBars; set => _input.ScrollBars = value; }
    public bool WordWrap { get => _input.WordWrap; set => _input.WordWrap = value; }
    public bool AcceptsReturn { get => _input.AcceptsReturn; set => _input.AcceptsReturn = value; }
    public bool AcceptsTab { get => _input.AcceptsTab; set => _input.AcceptsTab = value; }
    public int MaxLength { get => _input.MaxLength; set => _input.MaxLength = value; }
    public void Clear() => _input.Clear();

    public void RefreshTheme()
    {
        BackColor = _input.BackColor = UIColors.InputBg;
        ForeColor = _input.ForeColor = UIColors.TextMain;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int inset = UIUtils.S(8);
        _input.Font = Font;
        _input.AccessibleName = AccessibleName;
        int height = Multiline ? Math.Max(1, Height - inset * 2) : _input.PreferredHeight;
        _input.SetBounds(inset, Math.Max(inset / 2, (Height - height) / 2), Math.Max(1, Width - inset * 2), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = LiteCorners.Path(new Rectangle(0, 0, Width - 1, Height - 1), UIUtils.S(LiteCorners.Radius));
        using var pen = new Pen(_input.Focused ? UIColors.Primary : UIColors.Border);
        e.Graphics.DrawPath(pen, path);
    }
}
