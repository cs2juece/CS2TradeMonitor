using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class SystemSettingsPageHeader : Panel
    {
        private readonly Label _title;
        private readonly Label _subtitle;

        public SystemSettingsPageHeader()
        {
            BackColor = Color.Transparent;
            Height = UIUtils.S(52);
            _title = CreateLabel("系统设置", UIFonts.Bold(19F), UIColors.TextMain);
            _subtitle = CreateLabel("管理软件启动、更新与问题处理。", UIFonts.Regular(9.5F), UIColors.TextSub);
            Controls.Add(_title);
            Controls.Add(_subtitle);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_title == null || _subtitle == null)
                return;
            _title.SetBounds(0, 0, Width, UIUtils.S(30));
            _subtitle.SetBounds(0, UIUtils.S(30), Width, UIUtils.S(20));
        }

        public void RefreshTheme()
        {
            _title.ForeColor = UIColors.TextMain;
            _subtitle.ForeColor = UIColors.TextSub;
            Invalidate(true);
        }

        private static Label CreateLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }
    }

    internal sealed class SystemSettingsSection : Panel
    {
        private readonly Panel _header;
        private readonly Label _title;
        private Control? _headerAction;
        private int _bodyHeight;

        public SystemSettingsSection(string title, int bodyHeight)
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = UIColors.CardBg;
            Margin = Padding.Empty;

            _header = new Panel { BackColor = UIColors.GroupHeader };
            _header.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);
            };
            _title = new Label
            {
                Text = title,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UIFonts.Bold(10F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };
            _header.Controls.Add(_title);

            Body = new Panel { BackColor = UIColors.CardBg };
            Controls.Add(Body);
            Controls.Add(_header);
            SetBodyHeight(bodyHeight);
        }

        public string Title => _title.Text;
        public Panel Body { get; }
        public int BodyHeight => _bodyHeight;

        public void AddHeaderAction(Control action)
        {
            _headerAction = action;
            _header.Controls.Add(action);
            action.BringToFront();
            PerformLayout();
        }

        public void SetBodyHeight(int bodyHeight)
        {
            _bodyHeight = Math.Max(0, bodyHeight);
            Body.Visible = _bodyHeight > 0;
            Height = UIUtils.S(42) + _bodyHeight + UIUtils.S(2);
            PerformLayout();
            Parent?.PerformLayout();
        }

        public void RefreshTheme()
        {
            FrameworkSettingsPageLayoutHelper.RefreshTheme(this);
            BackColor = UIColors.CardBg;
            Body.BackColor = UIColors.CardBg;
            _header.BackColor = UIColors.GroupHeader;
            _title.ForeColor = UIColors.TextMain;
            foreach (Control control in Body.Controls)
            {
                switch (control)
                {
                    case SystemSettingsToggleRow toggleRow:
                        toggleRow.RefreshTheme();
                        break;
                    case SystemSettingsActionRow actionRow:
                        actionRow.RefreshTheme();
                        break;
                    case SystemSettingsUpdateRow updateRow:
                        updateRow.RefreshTheme();
                        break;
                }
            }
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_header == null || _title == null || Body == null)
                return;
            int border = UIUtils.S(1);
            int headerHeight = UIUtils.S(42);
            _header.SetBounds(border, border, Math.Max(1, Width - border * 2), headerHeight);
            _title.SetBounds(UIUtils.S(18), 0, Math.Max(1, _header.Width - UIUtils.S(36)), headerHeight - UIUtils.S(1));
            if (_headerAction != null)
            {
                int top = Math.Max(0, (headerHeight - _headerAction.Height) / 2);
                _headerAction.Location = new Point(Math.Max(UIUtils.S(6), _header.Width - _headerAction.Width - UIUtils.S(14)), top);
                _title.Width = Math.Max(UIUtils.S(80), _headerAction.Left - UIUtils.S(26));
            }

            Body.SetBounds(border, border + headerHeight, Math.Max(1, Width - border * 2), _bodyHeight);
            Height = border + headerHeight + _bodyHeight + border;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }
    }

    internal sealed class SystemSettingsToggleRow : Panel
    {
        private readonly string _title;
        private string _description;
        private readonly string? _tag;
        private readonly Font _titleFont;
        private readonly Font _descriptionFont;
        private readonly Font _stateFont;
        private readonly Font _tagFont;
        private bool _checked;
        private bool _switchHovered;
        private bool _drawRightBorder;
        private Rectangle _switchBounds;

        public SystemSettingsToggleRow(string title, string description, bool value, string? tag = null)
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = UIColors.CardBg;
            Cursor = Cursors.Default;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleName = title;
            Height = UIUtils.S(76);
            _title = title;
            _description = description;
            _tag = string.IsNullOrWhiteSpace(tag) ? null : tag;
            _checked = value;
            _titleFont = UIFonts.Bold(9.5F);
            _descriptionFont = UIFonts.Regular(8.5F);
            _stateFont = UIFonts.Regular(9F);
            _tagFont = UIFonts.Bold(8.5F);
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_description, normalized, StringComparison.Ordinal))
                    return;
                _description = normalized;
                AccessibleDescription = normalized;
                Invalidate();
            }
        }

        public bool DrawRightBorder
        {
            get => _drawRightBorder;
            set
            {
                _drawRightBorder = value;
                Invalidate();
            }
        }

        public event EventHandler? CheckedChanged;

        internal string TitleText => _title;
        internal int SwitchLeft => _switchBounds.Left;
        internal Rectangle SwitchBounds => _switchBounds;

        internal bool TryToggleAt(Point location)
        {
            if (!_switchBounds.Contains(location) || !Enabled)
                return false;

            Checked = !Checked;
            return true;
        }

        public void RefreshTheme()
        {
            BackColor = UIColors.CardBg;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = UIUtils.S(22);
            int right = UIUtils.S(18);
            int controlLeft = Math.Min(
                left + UIUtils.S(196),
                Math.Max(left, Width - right - UIUtils.S(98)));
            _switchBounds = new Rectangle(
                controlLeft,
                UIUtils.S(18),
                UIUtils.S(44),
                UIUtils.S(24));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var background = new SolidBrush(UIColors.CardBg))
                e.Graphics.FillRectangle(background, ClientRectangle);

            int left = UIUtils.S(22);
            int right = UIUtils.S(18);
            int titleWidth = Math.Max(UIUtils.S(72), _switchBounds.Left - left - UIUtils.S(12));
            var titleBounds = new Rectangle(left, UIUtils.S(9), titleWidth, UIUtils.S(26));
            var descriptionBounds = new Rectangle(
                left,
                UIUtils.S(34),
                Math.Max(UIUtils.S(100), Width - left - right),
                UIUtils.S(25));
            TextRenderer.DrawText(
                e.Graphics,
                _title,
                _titleFont,
                titleBounds,
                Enabled ? UIColors.TextMain : UIColors.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(
                e.Graphics,
                _description,
                _descriptionFont,
                descriptionBounds,
                Enabled ? UIColors.TextSub : UIColors.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            DrawSwitch(e.Graphics);
            int stateLeft = _switchBounds.Right + UIUtils.S(9);
            var stateBounds = new Rectangle(stateLeft, UIUtils.S(15), UIUtils.S(48), UIUtils.S(30));
            TextRenderer.DrawText(
                e.Graphics,
                Checked ? "启用" : "关闭",
                _stateFont,
                stateBounds,
                Enabled ? UIColors.TextSub : UIColors.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (_tag != null)
            {
                int tagLeft = stateBounds.Right + UIUtils.S(14);
                var tagBounds = new Rectangle(
                    tagLeft,
                    UIUtils.S(15),
                    Math.Max(UIUtils.S(96), Width - tagLeft - right),
                    UIUtils.S(30));
                TextRenderer.DrawText(
                    e.Graphics,
                    _tag,
                    _tagFont,
                    tagBounds,
                    Enabled ? UIColors.TextWarn : UIColors.TextDisabled,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            if (_drawRightBorder)
                e.Graphics.DrawLine(pen, Width - 1, UIUtils.S(16), Width - 1, Height - UIUtils.S(16));
            if (Focused && ShowFocusCues)
            {
                Rectangle focusBounds = Rectangle.Inflate(_switchBounds, UIUtils.S(3), UIUtils.S(3));
                ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, UIColors.TextMain, UIColors.CardBg);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hovered = _switchBounds.Contains(e.Location);
            if (_switchHovered == hovered)
                return;

            _switchHovered = hovered;
            Cursor = hovered ? Cursors.Hand : Cursors.Default;
            Invalidate(_switchBounds);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _switchHovered = false;
            Cursor = Cursors.Default;
            Invalidate(_switchBounds);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate(Rectangle.Inflate(_switchBounds, UIUtils.S(4), UIUtils.S(4)));
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate(Rectangle.Inflate(_switchBounds, UIUtils.S(4), UIUtils.S(4)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
                TryToggleAt(e.Location);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
            {
                Checked = !Checked;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _descriptionFont.Dispose();
                _stateFont.Dispose();
                _tagFont.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ToggleRowAccessibleObject(this);
        }

        private void DrawSwitch(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color track = !Enabled
                ? UIColors.ControlDisabledBg
                : Checked
                    ? UIColors.Primary
                    : (_switchHovered ? Color.FromArgb(70, 82, 98) : Color.FromArgb(58, 68, 82));
            Rectangle trackRect = new(
                _switchBounds.Left,
                _switchBounds.Top + UIUtils.S(2),
                _switchBounds.Width - 1,
                _switchBounds.Height - UIUtils.S(4));
            using (GraphicsPath path = CreateRoundPath(trackRect, trackRect.Height / 2))
            using (var trackBrush = new SolidBrush(track))
                graphics.FillPath(trackBrush, path);

            int diameter = UIUtils.S(18);
            int thumbX = Checked
                ? _switchBounds.Right - diameter - UIUtils.S(3)
                : _switchBounds.Left + UIUtils.S(3);
            var thumbRect = new Rectangle(
                thumbX,
                _switchBounds.Top + (_switchBounds.Height - diameter) / 2,
                diameter,
                diameter);
            using var thumbBrush = new SolidBrush(Enabled ? Color.FromArgb(230, 236, 244) : UIColors.TextDisabled);
            graphics.FillEllipse(thumbBrush, thumbRect);
        }

        private static GraphicsPath CreateRoundPath(Rectangle rect, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class ToggleRowAccessibleObject : ControlAccessibleObject
        {
            private readonly SystemSettingsToggleRow _owner;

            public ToggleRowAccessibleObject(SystemSettingsToggleRow owner)
                : base(owner)
            {
                _owner = owner;
            }

            public override string? Name
            {
                get => _owner._title;
                set { }
            }

            public override string? Description => string.IsNullOrWhiteSpace(_owner._tag)
                ? _owner._description
                : _owner._description + " " + _owner._tag;

            public override AccessibleRole Role => AccessibleRole.CheckButton;

            public override AccessibleStates State
            {
                get
                {
                    AccessibleStates state = base.State;
                    if (_owner.Checked)
                        state |= AccessibleStates.Checked;
                    return state;
                }
            }

            public override string DefaultAction => "切换";

            public override void DoDefaultAction()
            {
                if (_owner.Enabled)
                    _owner.Checked = !_owner.Checked;
            }
        }
    }

    internal sealed class SystemSettingsActionRow : Panel
    {
        private readonly Label _icon;
        private readonly Label _title;
        private readonly Label _description;
        private readonly Label? _detail;
        private readonly LiteButton _button;
        private readonly LiteButton? _secondaryButton;
        private readonly bool _danger;
        private readonly bool _accentOutline;
        private readonly float? _actionLeftRatio;
        private readonly float? _secondaryLeftRatio;

        public SystemSettingsActionRow(
            string icon,
            string title,
            string description,
            string buttonText,
            Action action,
            bool primary,
            bool danger = false,
            bool accentOutline = false,
            string? secondaryText = null,
            Action? secondaryAction = null,
            string? detail = null,
            float? actionLeftRatio = null,
            float? secondaryLeftRatio = null,
            bool compactSecondary = false)
        {
            _danger = danger;
            _accentOutline = accentOutline;
            _actionLeftRatio = actionLeftRatio;
            _secondaryLeftRatio = secondaryLeftRatio;
            BackColor = UIColors.CardBg;
            Height = UIUtils.S(76);
            _icon = CreateLabel(icon, new Font("Segoe MDL2 Assets", 16F), UIColors.TextMain);
            _title = CreateLabel(title, UIFonts.Bold(9.5F), UIColors.TextMain);
            _description = CreateLabel(description, UIFonts.Regular(8.5F), UIColors.TextSub);
            if (!string.IsNullOrWhiteSpace(detail))
                _detail = CreateLabel(detail, UIFonts.Regular(8.5F), UIColors.TextSub);
            _button = new LiteButton(buttonText, primary)
            {
                Size = new Size(UIUtils.S(Math.Max(116, buttonText.Length * 17 + 28)), UIUtils.S(34))
            };
            ApplyButtonSemantics();
            _button.Click += (_, __) => action();
            if (!string.IsNullOrWhiteSpace(secondaryText) && secondaryAction != null)
            {
                _secondaryButton = new LiteButton(secondaryText, false)
                {
                    Size = compactSecondary
                        ? UIUtils.S(new Size(secondaryText.Length == 1 ? 38 : 56, 34))
                        : new Size(UIUtils.S(Math.Max(86, secondaryText.Length * 17 + 24)), UIUtils.S(34))
                };
                if (compactSecondary && secondaryText.Length == 1)
                    _secondaryButton.Font = new Font("Segoe MDL2 Assets", 10F);
                _secondaryButton.Click += (_, __) => secondaryAction();
            }

            Controls.Add(_icon);
            Controls.Add(_title);
            Controls.Add(_description);
            if (_detail != null)
                Controls.Add(_detail);
            if (_secondaryButton != null)
                Controls.Add(_secondaryButton);
            Controls.Add(_button);
            _secondaryButton?.BringToFront();
            _button.BringToFront();
            Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            };
        }

        public LiteButton ActionButton => _button;
        internal string TitleText => _title.Text;
        internal Rectangle TitleBounds => _title.Bounds;
        internal Rectangle DescriptionBounds => _description.Bounds;
        internal int? MaximumTextWidth { get; set; }

        public void RefreshTheme()
        {
            BackColor = UIColors.CardBg;
            _icon.ForeColor = UIColors.TextMain;
            _title.ForeColor = UIColors.TextMain;
            _description.ForeColor = UIColors.TextSub;
            if (_detail != null)
                _detail.ForeColor = UIColors.TextSub;
            _button.RefreshTheme();
            _secondaryButton?.RefreshTheme();
            ApplyButtonSemantics();
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_icon == null || _title == null || _description == null || _button == null)
                return;
            int left = UIUtils.S(20);
            int iconWidth = UIUtils.S(36);
            int buttonRight = UIUtils.S(18);
            int defaultButtonLeft = Math.Max(left, Width - buttonRight - _button.Width);
            int preferredButtonLeft = _actionLeftRatio.HasValue
                ? (int)Math.Round(Width * _actionLeftRatio.Value)
                : defaultButtonLeft;
            _button.Location = new Point(
                Math.Clamp(preferredButtonLeft, left, defaultButtonLeft),
                Math.Max(0, (Height - _button.Height) / 2));
            int actionsLeft = _button.Left;
            if (_secondaryButton != null)
            {
                int secondaryLeft = _secondaryLeftRatio.HasValue
                    ? (int)Math.Round(Width * _secondaryLeftRatio.Value)
                    : Math.Max(left, _button.Left - UIUtils.S(10) - _secondaryButton.Width);
                _secondaryButton.Location = new Point(secondaryLeft, _button.Top);
                actionsLeft = _secondaryButton.Left;
            }
            int textLeft = left + iconWidth + UIUtils.S(8);
            int textRight = _secondaryLeftRatio.HasValue ? _button.Left : actionsLeft;
            int textWidth = Math.Max(UIUtils.S(120), textRight - textLeft - UIUtils.S(18));
            if (MaximumTextWidth.HasValue)
                textWidth = Math.Min(MaximumTextWidth.Value, textWidth);
            _icon.SetBounds(left, UIUtils.S(18), iconWidth, UIUtils.S(36));
            if (_detail != null)
            {
                _title.SetBounds(textLeft, UIUtils.S(5), textWidth, UIUtils.S(22));
                _description.SetBounds(textLeft, UIUtils.S(27), textWidth, UIUtils.S(22));
                _detail.SetBounds(textLeft, UIUtils.S(49), textWidth, UIUtils.S(22));
            }
            else
            {
                _title.SetBounds(textLeft, UIUtils.S(10), textWidth, UIUtils.S(25));
                _description.SetBounds(textLeft, UIUtils.S(36), textWidth, UIUtils.S(25));
            }
        }

        private static Label CreateLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }

        private void ApplyButtonSemantics()
        {
            if (_danger)
            {
                _button.ForeColor = UIColors.TextCrit;
                _button.BorderColorOverride = UIColors.TextCrit;
            }
            else if (_accentOutline && !_button.IsPrimary)
            {
                _button.BorderColorOverride = UIColors.Primary;
            }
            else
            {
                _button.BorderColorOverride = null;
            }

            _button.Invalidate();
        }
    }

    internal sealed class SystemSettingsUpdateRow : Panel
    {
        private readonly Label _version;
        private readonly Label _status;
        private readonly LiteButton _check;
        private readonly LiteButton _download;

        public SystemSettingsUpdateRow(string version, Action check, Action download)
        {
            BackColor = UIColors.CardBg;
            _version = CreateLabel($"当前版本 v{version}", UIFonts.Regular(9F), UIColors.TextSub);
            _status = CreateLabel("更新状态：尚未检查。", UIFonts.Regular(9F), UIColors.TextSub);
            _check = new LiteButton("检查更新", true) { Size = UIUtils.S(new Size(112, 34)) };
            _download = new LiteButton("打开下载页", false) { Size = UIUtils.S(new Size(116, 34)) };
            _check.Click += (_, __) => check();
            _download.Click += (_, __) => download();
            Controls.Add(_version);
            Controls.Add(_status);
            Controls.Add(_check);
            Controls.Add(_download);
        }

        public Label StatusLabel => _status;
        public LiteButton CheckButton => _check;

        public void RefreshTheme()
        {
            BackColor = UIColors.CardBg;
            _version.ForeColor = UIColors.TextSub;
            _status.ForeColor = UIColors.TextSub;
            _check.RefreshTheme();
            _download.RefreshTheme();
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_version == null || _status == null || _check == null || _download == null)
                return;
            int left = UIUtils.S(22);
            int gap = UIUtils.S(12);
            int right = UIUtils.S(18);
            _download.Location = new Point(Math.Max(left, Width - right - _download.Width), Math.Max(0, (Height - _download.Height) / 2));
            _check.Location = new Point(Math.Max(left, _download.Left - gap - _check.Width), _download.Top);
            int textRight = _check.Left - UIUtils.S(18);
            int versionWidth = UIUtils.S(250);
            _version.SetBounds(left, 0, versionWidth, Height);
            _status.SetBounds(left + versionWidth + UIUtils.S(28), 0, Math.Max(UIUtils.S(120), textRight - left - versionWidth - UIUtils.S(28)), Height);
        }

        private static Label CreateLabel(string text, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent
            };
        }
    }
}
