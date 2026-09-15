using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class ConsoleIconGlyphs
    {
        public const string Completed = "\uE930";
        public const string Refresh = "\uE72C";
        public const string Chart = "\uE9D2";
        public const string Tag = "\uE8EC";
        public const string Package = "\uE7B8";
        public const string Clock = "\uE823";
        public const string Link = "\uE71B";
        public const string Notification = "\uEA8F";
        public const string Info = "\uE946";
        public const string Warning = "\uE7BA";
        public const string Error = "\uEA39";

        public static string ForModule(string id)
        {
            return id switch
            {
                "market" => Chart,
                "item" => Tag,
                "youpin-inventory" => Package,
                "youpin-todo" => Clock,
                "steam-offers" => Link,
                "notification" => Notification,
                _ => Info
            };
        }
    }

    internal class ConsoleCardPanel : Panel
    {
        public ConsoleCardPanel()
        {
            BackColor = Color.Transparent;
            Radius = UIUtils.S(6);
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);
        }

        public int Radius { get; set; }

        public Color? FillColorOverride { get; set; }

        public Color? BorderColorOverride { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using GraphicsPath path = CreateRoundedRect(rect, Math.Max(1, Radius));
            using var fill = new SolidBrush(FillColorOverride ?? UIColors.CardBg);
            using var border = new Pen(BorderColorOverride ?? UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            base.OnPaint(e);
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ConsoleIconButton : Button
    {
        private readonly string _glyph;
        private bool _hover;
        private bool _pressed;

        public ConsoleIconButton(string glyph, string text)
        {
            _glyph = glyph;
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            AccessibleName = text;
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
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
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled
                ? UIColors.ControlDisabledBg
                : (_pressed ? Color.FromArgb(0, 103, 205) : (_hover ? Color.FromArgb(24, 144, 255) : UIColors.Primary));
            Rectangle rect = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using GraphicsPath path = CreateRoundedRect(rect, UIUtils.S(4));
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);

            Color textColor = Enabled ? Color.White : UIColors.TextDisabled;
            using var iconFont = new Font("Segoe MDL2 Assets", 11F, FontStyle.Regular);
            int iconWidth = UIUtils.S(28);
            Rectangle iconRect = new(UIUtils.S(12), 0, iconWidth, Height);
            Rectangle textRect = new(iconRect.Right, 0, Math.Max(1, Width - iconRect.Right - UIUtils.S(10)), Height);
            TextRenderer.DrawText(e.Graphics, _glyph, iconFont, iconRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(rect, -UIUtils.S(4), -UIUtils.S(4));
                ControlPaint.DrawFocusRectangle(e.Graphics, focus);
            }
        }

        private static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ConsoleHealthBanner : ConsoleCardPanel
    {
        private readonly Label _icon;
        private readonly Label _headline;
        private readonly Label _summary;

        public ConsoleHealthBanner()
        {
            _icon = ConsoleUi.IconLabel(ConsoleIconGlyphs.Completed, 38F, UIColors.Positive, ContentAlignment.MiddleCenter);
            _headline = ConsoleUi.Label("正在读取", 15F, FontStyle.Bold, UIColors.Positive);
            _summary = ConsoleUi.Label("正在汇总监控链路与提醒准备度。", 10.5F, FontStyle.Regular, UIColors.TextMain);
            Controls.AddRange(new Control[] { _icon, _headline, _summary });
            Layout += (_, __) => LayoutChildren();
            ApplyTheme(healthy: true);
        }

        public void Apply(MonitoringConsoleSnapshot snapshot)
        {
            bool healthy = snapshot.IsHealthy;
            _icon.Text = healthy ? ConsoleIconGlyphs.Completed : ConsoleIconGlyphs.Warning;
            _headline.Text = healthy ? "运行正常" : "需要关注";
            _summary.Text = ConsolePageModel.BuildHealthSummary(snapshot);
            ApplyTheme(healthy);
        }

        public void ApplyFailure()
        {
            _icon.Text = ConsoleIconGlyphs.Error;
            _headline.Text = "状态读取失败";
            _summary.Text = "暂时无法读取控制台快照，请稍后重新刷新。";
            ApplyTheme(healthy: false, critical: true);
        }

        private void ApplyTheme(bool healthy, bool critical = false)
        {
            Color accent = critical ? UIColors.TextCrit : (healthy ? UIColors.Positive : UIColors.TextWarn);
            _icon.ForeColor = accent;
            _headline.ForeColor = accent;
            _summary.ForeColor = UIColors.TextMain;
            FillColorOverride = UIColors.IsDark
                ? Color.FromArgb(18, 35, 31)
                : Color.FromArgb(237, 249, 243);
            BorderColorOverride = Color.FromArgb(105, accent);
            Invalidate();
        }

        private void LayoutChildren()
        {
            int pad = UIUtils.S(30);
            int iconSize = UIUtils.S(54);
            _icon.SetBounds(pad, (Height - iconSize) / 2, iconSize, iconSize);
            int textLeft = _icon.Right + UIUtils.S(16);
            int textWidth = Math.Max(1, Width - textLeft - pad);
            _headline.SetBounds(textLeft, UIUtils.S(25), textWidth, UIUtils.S(30));
            _summary.SetBounds(textLeft, _headline.Bottom - UIUtils.S(1), textWidth, UIUtils.S(26));
        }
    }

    internal sealed class ConsoleModuleTile : Panel
    {
        private readonly Label _icon;
        private readonly Label _name;
        private readonly ConsoleStatusDot _dot;
        private readonly Label _state;

        public ConsoleModuleTile(MonitorModuleDescriptor descriptor)
        {
            BackColor = Color.Transparent;
            _icon = ConsoleUi.IconLabel(ConsoleIconGlyphs.ForModule(descriptor.Id), 14F, UIColors.TextMain, ContentAlignment.MiddleCenter);
            _name = ConsoleUi.Label(descriptor.DisplayName, 11F, FontStyle.Bold, UIColors.TextMain);
            _dot = new ConsoleStatusDot();
            _state = ConsoleUi.Label("未读取", 9.5F, FontStyle.Regular, UIColors.TextSub);
            Controls.AddRange(new Control[] { _icon, _name, _dot, _state });
            Layout += (_, __) => LayoutChildren();
        }

        public void Apply(MonitorModuleHealth health)
        {
            Color color = ConsoleUi.ResolveToneColor(ConsolePageModel.ResolveModuleTone(health.State));
            _state.Text = SystemSettingsPageModel.FormatModuleState(health);
            _state.ForeColor = color;
            _dot.DotColor = color;
        }

        private void LayoutChildren()
        {
            int left = UIUtils.S(38);
            int iconSize = UIUtils.S(28);
            _icon.SetBounds(left, UIUtils.S(13), iconSize, iconSize);
            int textLeft = _icon.Right + UIUtils.S(8);
            _name.SetBounds(textLeft, UIUtils.S(7), Math.Max(1, Width - textLeft - UIUtils.S(8)), UIUtils.S(26));
            _dot.SetBounds(textLeft, UIUtils.S(37), UIUtils.S(8), UIUtils.S(8));
            _state.SetBounds(_dot.Right + UIUtils.S(5), UIUtils.S(29), Math.Max(1, Width - _dot.Right - UIUtils.S(8)), UIUtils.S(24));
        }
    }

    internal sealed class ConsoleActivityRow : Panel
    {
        private readonly Label _icon;
        private readonly Label _time;
        private readonly Label _source;
        private readonly Label _title;
        private readonly Label _detail;
        private bool _drawTopLine;
        private bool _drawBottomLine;

        public ConsoleActivityRow()
        {
            BackColor = Color.Transparent;
            _icon = ConsoleUi.IconLabel(ConsoleIconGlyphs.Completed, 14F, UIColors.Positive, ContentAlignment.MiddleCenter);
            _time = ConsoleUi.Label("--:--:--", 9.5F, FontStyle.Regular, UIColors.TextSub);
            _source = ConsoleUi.Label("系统", 9.5F, FontStyle.Regular, UIColors.TextSub);
            _title = ConsoleUi.Label("正在读取", 11.5F, FontStyle.Bold, UIColors.TextMain);
            _detail = ConsoleUi.Label("控制台正在汇总最新动态。", 9.5F, FontStyle.Regular, UIColors.TextSub);
            Controls.AddRange(new Control[] { _icon, _time, _source, _title, _detail });
            Layout += (_, __) => LayoutChildren();
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void Apply(ConsoleActivityItem? item, bool first, bool last)
        {
            _drawTopLine = !first;
            _drawBottomLine = !last;
            if (item == null)
            {
                _icon.Text = ConsoleIconGlyphs.Info;
                _icon.ForeColor = UIColors.TextSub;
                _time.Text = "--:--:--";
                _source.Text = "系统";
                _title.Text = "暂无实时动态";
                _detail.Text = "提醒和模块状态变化会显示在这里。";
                Invalidate();
                return;
            }

            Color color = ConsoleUi.ResolveToneColor(item.Tone);
            _icon.Text = ConsoleUi.ResolveToneGlyph(item.Tone);
            _icon.ForeColor = color;
            _time.Text = item.OccurredAt.LocalDateTime.ToString("HH:mm:ss");
            _source.Text = item.Source;
            _title.Text = item.Title;
            _detail.Text = item.Detail;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int x = UIUtils.S(18);
            int center = UIUtils.S(30);
            using var pen = new Pen(UIColors.Border);
            if (_drawTopLine)
                e.Graphics.DrawLine(pen, x, 0, x, center - UIUtils.S(12));
            if (_drawBottomLine)
                e.Graphics.DrawLine(pen, x, center + UIUtils.S(12), x, Height);
        }

        private void LayoutChildren()
        {
            int pad = UIUtils.S(12);
            int iconSize = UIUtils.S(34);
            _icon.SetBounds(UIUtils.S(1), UIUtils.S(16), iconSize, iconSize);
            _time.SetBounds(_icon.Right + UIUtils.S(13), UIUtils.S(15), UIUtils.S(78), UIUtils.S(26));
            int contentLeft = _time.Right + UIUtils.S(10);
            int contentWidth = Math.Max(1, Width - contentLeft - pad);
            _source.SetBounds(contentLeft, UIUtils.S(13), contentWidth, UIUtils.S(22));
            _title.SetBounds(contentLeft, UIUtils.S(30), contentWidth, UIUtils.S(28));
            _detail.SetBounds(contentLeft, UIUtils.S(62), contentWidth, UIUtils.S(24));
        }
    }

    internal sealed class ConsoleReadinessRow : Panel
    {
        private readonly string _pageKey;
        private readonly Label _icon;
        private readonly Label _name;
        private readonly ConsoleStatusDot _dot;
        private readonly Label _state;
        private readonly Label _detail;
        private readonly LinkLabel _navigate;

        public ConsoleReadinessRow(AlertReadinessSnapshot snapshot)
        {
            _pageKey = snapshot.PageKey;
            BackColor = Color.Transparent;
            _icon = ConsoleUi.IconLabel(ConsoleIconGlyphs.Info, 14F, UIColors.TextSub, ContentAlignment.MiddleCenter);
            _name = ConsoleUi.Label(snapshot.DisplayName, 10.5F, FontStyle.Bold, UIColors.TextMain);
            _dot = new ConsoleStatusDot();
            _state = ConsoleUi.Label("未读取", 9.5F, FontStyle.Regular, UIColors.TextSub);
            _detail = ConsoleUi.Label(string.Empty, 9.5F, FontStyle.Regular, UIColors.TextSub);
            _navigate = new LinkLabel
            {
                Text = "查看",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleRight,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Color.Transparent,
                LinkColor = UIColors.Link,
                ActiveLinkColor = UIColors.LinkHover,
                VisitedLinkColor = UIColors.Link,
                Cursor = Cursors.Hand,
                AccessibleName = "查看" + snapshot.DisplayName
            };
            _navigate.LinkClicked += (_, __) => NavigateRequested?.Invoke(_pageKey);
            Controls.AddRange(new Control[] { _icon, _name, _dot, _state, _detail, _navigate });
            Layout += (_, __) => LayoutChildren();
            Paint += (_, e) => ConsoleUi.DrawBottomDivider(e.Graphics, Width, Height);
            Apply(snapshot);
        }

        public event Action<string>? NavigateRequested;

        public void Apply(AlertReadinessSnapshot snapshot)
        {
            ConsoleVisualTone tone = ConsolePageModel.ResolveReadinessTone(snapshot.State);
            Color color = ConsoleUi.ResolveToneColor(tone);
            _icon.Text = ConsoleUi.ResolveToneGlyph(tone);
            _icon.ForeColor = color;
            _dot.DotColor = color;
            _state.Text = ConsoleUi.FormatReadiness(snapshot.State);
            _state.ForeColor = color;
            _detail.Text = snapshot.Message;
            _navigate.Visible = snapshot.State != AlertReadinessState.Ready;
        }

        private void LayoutChildren()
        {
            int pad = UIUtils.S(20);
            int iconSize = UIUtils.S(30);
            _icon.SetBounds(pad, (Height - iconSize) / 2, iconSize, iconSize);
            int nameLeft = _icon.Right + UIUtils.S(8);
            int nameWidth = Math.Min(UIUtils.S(118), Math.Max(UIUtils.S(92), Width / 4));
            _name.SetBounds(nameLeft, 0, nameWidth, Height);
            int stateLeft = _name.Right + UIUtils.S(8);
            _dot.SetBounds(stateLeft, (Height - UIUtils.S(8)) / 2, UIUtils.S(8), UIUtils.S(8));
            _state.SetBounds(_dot.Right + UIUtils.S(8), 0, UIUtils.S(76), Height);
            _navigate.SetBounds(Width - pad - UIUtils.S(42), 0, UIUtils.S(42), Height);
            int detailRight = _navigate.Visible ? _navigate.Left - UIUtils.S(8) : Width - pad;
            _detail.SetBounds(_state.Right + UIUtils.S(14), 0, Math.Max(1, detailRight - _state.Right - UIUtils.S(14)), Height);
        }
    }

    internal sealed class ConsoleUpdateRow : Panel
    {
        private readonly Label _time;
        private readonly Label _source;
        private readonly Label _title;
        private readonly LinkLabel _link;

        public ConsoleUpdateRow()
        {
            BackColor = Color.Transparent;
            _time = ConsoleUi.Label(string.Empty, 9.5F, FontStyle.Regular, UIColors.TextMain);
            _source = ConsoleUi.Label(string.Empty, 9.5F, FontStyle.Regular, UIColors.TextSub);
            _title = ConsoleUi.Label(string.Empty, 9.5F, FontStyle.Regular, UIColors.TextMain);
            _link = new LinkLabel
            {
                Text = "查看",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                TextAlign = ContentAlignment.MiddleRight,
                LinkBehavior = LinkBehavior.HoverUnderline,
                LinkColor = UIColors.Link,
                ActiveLinkColor = UIColors.LinkHover,
                VisitedLinkColor = UIColors.Link,
                BackColor = Color.Transparent,
                AccessibleName = "查看 CS2 更新提醒"
            };
            _link.LinkClicked += (_, __) => NavigateRequested?.Invoke();
            Controls.AddRange(new Control[] { _time, _source, _title, _link });
            Layout += (_, __) => LayoutChildren();
            Paint += (_, e) => ConsoleUi.DrawBottomDivider(e.Graphics, Width, Height);
        }

        public event Action? NavigateRequested;

        public void Apply(string time, string source, string title, bool visible)
        {
            Visible = visible;
            _time.Text = time;
            _source.Text = source;
            _title.Text = title;
        }

        private void LayoutChildren()
        {
            int pad = UIUtils.S(17);
            int linkWidth = UIUtils.S(42);
            int timeWidth = Math.Min(UIUtils.S(116), Math.Max(UIUtils.S(82), Width / 5));
            int sourceWidth = Math.Min(UIUtils.S(82), Math.Max(UIUtils.S(62), Width / 7));
            _time.SetBounds(pad, 0, timeWidth, Height);
            _source.SetBounds(_time.Right + UIUtils.S(8), 0, sourceWidth, Height);
            _link.SetBounds(Width - pad - linkWidth, 0, linkWidth, Height);
            _title.SetBounds(_source.Right + UIUtils.S(8), 0, Math.Max(1, _link.Left - _source.Right - UIUtils.S(16)), Height);
        }
    }

    internal sealed class ConsoleSegmentedFilter : Panel
    {
        private readonly LiteButton _all;
        private readonly LiteButton _alerts;
        private readonly LiteButton _system;

        public ConsoleSegmentedFilter()
        {
            BackColor = UIColors.ControlBg;
            _all = CreateButton("全部", ConsoleActivityFilter.All);
            _alerts = CreateButton("提醒", ConsoleActivityFilter.Alerts);
            _system = CreateButton("系统", ConsoleActivityFilter.System);
            Controls.AddRange(new Control[] { _all, _alerts, _system });
            Layout += (_, __) => LayoutChildren();
            SelectedFilter = ConsoleActivityFilter.All;
        }

        public event Action<ConsoleActivityFilter>? SelectedFilterChanged;

        public ConsoleActivityFilter SelectedFilter
        {
            get;
            private set;
        }

        public void Select(ConsoleActivityFilter filter)
        {
            SelectedFilter = filter;
            _all.IsActive = filter == ConsoleActivityFilter.All;
            _alerts.IsActive = filter == ConsoleActivityFilter.Alerts;
            _system.IsActive = filter == ConsoleActivityFilter.System;
        }

        private LiteButton CreateButton(string text, ConsoleActivityFilter filter)
        {
            var button = new LiteButton(text, false)
            {
                Height = UIUtils.S(30),
                AccessibleName = text + "动态"
            };
            button.Click += (_, __) =>
            {
                if (SelectedFilter == filter)
                    return;
                Select(filter);
                SelectedFilterChanged?.Invoke(filter);
            };
            return button;
        }

        private void LayoutChildren()
        {
            int width = Math.Max(1, Width / 3);
            _all.SetBounds(0, 0, width, Height);
            _alerts.SetBounds(width, 0, width, Height);
            _system.SetBounds(width * 2, 0, Math.Max(1, Width - width * 2), Height);
        }
    }

    internal sealed class ConsoleStatusDot : Control
    {
        private Color _dotColor = UIColors.TextSub;

        public ConsoleStatusDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public Color DotColor
        {
            get => _dotColor;
            set
            {
                _dotColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_dotColor);
            e.Graphics.FillEllipse(brush, ClientRectangle);
        }
    }

    internal static class ConsoleUi
    {
        public static Label Label(
            string text,
            float size,
            FontStyle style,
            Color color,
            ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                UseMnemonic = false,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color,
                TextAlign = alignment
            };
        }

        public static Label IconLabel(string glyph, float size, Color color, ContentAlignment alignment)
        {
            return new Label
            {
                Text = glyph,
                AutoSize = false,
                AutoEllipsis = false,
                UseMnemonic = false,
                BackColor = Color.Transparent,
                Font = new Font("Segoe MDL2 Assets", size, FontStyle.Regular),
                ForeColor = color,
                TextAlign = alignment
            };
        }

        public static Color ResolveToneColor(ConsoleVisualTone tone)
        {
            return tone switch
            {
                ConsoleVisualTone.Success => UIColors.Positive,
                ConsoleVisualTone.Info => UIColors.Primary,
                ConsoleVisualTone.Warning => UIColors.TextWarn,
                ConsoleVisualTone.Critical => UIColors.TextCrit,
                _ => UIColors.TextSub
            };
        }

        public static string ResolveToneGlyph(ConsoleVisualTone tone)
        {
            return tone switch
            {
                ConsoleVisualTone.Success => ConsoleIconGlyphs.Completed,
                ConsoleVisualTone.Info => ConsoleIconGlyphs.Info,
                ConsoleVisualTone.Warning => ConsoleIconGlyphs.Clock,
                ConsoleVisualTone.Critical => ConsoleIconGlyphs.Error,
                _ => ConsoleIconGlyphs.Info
            };
        }

        public static string FormatReadiness(AlertReadinessState state)
        {
            return state switch
            {
                AlertReadinessState.Disabled => "已关闭",
                AlertReadinessState.Waiting => "等待条件",
                AlertReadinessState.Ready => "已就绪",
                AlertReadinessState.CoolingDown => "冷却中",
                AlertReadinessState.Faulted => "异常",
                _ => "未知"
            };
        }

        public static void DrawBottomDivider(Graphics graphics, int width, int height)
        {
            using var pen = new Pen(UIColors.Border);
            graphics.DrawLine(pen, UIUtils.S(14), height - 1, Math.Max(UIUtils.S(14), width - UIUtils.S(14)), height - 1);
        }
    }
}
