using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core; // 确保引用了 UIUtils
using CS2TradeMonitor.src.UI.SettingsPage;


namespace CS2TradeMonitor.src.UI.Controls
{

    // =======================================================================
    // 2. 交互组件
    // =======================================================================

    // ★★★ [优化版] 下划线输入框：支持前置标签 ★★★
    public class LiteUnderlineInput : Panel
    {
        public TextBox Inner;
        private readonly ImeAwareTextBox _imeAwareInner;
        private Label? _lblUnit;   // 单位 (右侧)
        private Label? _lblLabel;  // 标签 (左侧)
        private readonly Color? _customFontColor;
        private Color? _customBackColor;

        private const int EM_SETCUEBANNER = 0x1501;
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern Int32 SendMessage(IntPtr hWnd, int msg, int wParam, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string lParam);

        public bool IsImeComposing => _imeAwareInner.IsImeComposing;

        public event EventHandler? ImeCompositionStarted;

        public event EventHandler? ImeCompositionEnded;

        public event EventHandler? CommittedTextChanged;

        public string Placeholder
        {
            set
            {
                if (Inner != null && !Inner.IsDisposed)
                {
                    SendMessage(Inner.Handle, EM_SETCUEBANNER, 0, value);
                }
            }
        }

        public LiteUnderlineInput(string text, string unit = "", string labelPrefix = "", int width = 160, Color? fontColor = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            _customFontColor = fontColor;

            // ★★★ 修改：Size/Padding 缩放
            this.Size = new Size(UIUtils.S(width), UIUtils.S(26));
            this.BackColor = UIColors.InputBg;
            this.Padding = UIUtils.S(new Padding(0, 2, 0, 3));
            this.Cursor = Cursors.IBeam;

            // 1. 创建并添加输入框 (垫底)
            _imeAwareInner = new ImeAwareTextBox
            {
                Text = text,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                BackColor = UIColors.InputBg,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                ForeColor = fontColor ?? UIColors.TextMain,
                TextAlign = align
            };
            Inner = _imeAwareInner;
            _imeAwareInner.CompositionStarted += (_, __) => ImeCompositionStarted?.Invoke(this, EventArgs.Empty);
            _imeAwareInner.CompositionEnded += (_, __) =>
            {
                ImeCompositionEnded?.Invoke(this, EventArgs.Empty);
                CommittedTextChanged?.Invoke(this, EventArgs.Empty);
            };
            _imeAwareInner.TextChanged += (_, __) =>
            {
                if (!IsImeComposing)
                    CommittedTextChanged?.Invoke(this, EventArgs.Empty);
            };
            // this.Controls.Add(Inner); // Moved to end to ensure correct Dock order (Inner should be last Docked => Front of Z-Order)

            // 2. 添加单位 (Dock Right, 浮在右边)
            if (!string.IsNullOrEmpty(unit))
            {
                _lblUnit = new Label
                {
                    Text = unit,
                    AutoSize = true,
                    Dock = DockStyle.Right,
                    Font = new Font("Microsoft YaHei UI", 8F),
                    ForeColor = UIColors.TextSub,
                    BackColor = UIColors.InputBg,
                    TextAlign = ContentAlignment.BottomRight,
                    Padding = new Padding(0, 0, 0, 4)
                };
                this.Controls.Add(_lblUnit);
                _lblUnit.Click += (s, e) => Inner.Focus();
            }

            // 3. 添加前置标签 (Dock Left, 浮在左边)
            if (!string.IsNullOrEmpty(labelPrefix))
            {
                _lblLabel = new Label
                {
                    Text = labelPrefix,
                    AutoSize = true,
                    Dock = DockStyle.Left,
                    Font = new Font("Microsoft YaHei UI", 9F),
                    ForeColor = fontColor ?? UIColors.TextSub,
                    BackColor = UIColors.InputBg,
                    TextAlign = ContentAlignment.BottomLeft,
                    Padding = new Padding(0, 0, 4, 3)
                };
                this.Controls.Add(_lblLabel);
                _lblLabel.Click += (s, e) => Inner.Focus();
            }

            // Add Inner last so it is at the Front of Z-Order (Index 0),
            // which means it is docked LAST (filling remaining space).
            this.Controls.Add(Inner);

            // 事件转发
            Inner.Enter += (s, e) => this.Invalidate();
            Inner.Leave += (s, e) => this.Invalidate();
            this.Click += (s, e) => Inner.Focus();

            // ★★★ Fix: Ensure Inner is at the top of Z-Order so it docks LAST (filling remaining space)
            // ensuring it respects the space taken by previously docked controls (Unit/Label)
            Inner.BringToFront();
        }

        private sealed class ImeAwareTextBox : TextBox
        {
            private const int WmImeStartComposition = 0x010D;
            private const int WmImeEndComposition = 0x010E;

            public bool IsImeComposing { get; private set; }

            public event EventHandler? CompositionStarted;

            public event EventHandler? CompositionEnded;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmImeStartComposition)
                {
                    IsImeComposing = true;
                    CompositionStarted?.Invoke(this, EventArgs.Empty);
                }

                base.WndProc(ref m);

                if (m.Msg == WmImeEndComposition)
                {
                    IsImeComposing = false;
                    CompositionEnded?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void SetTextColor(Color c) => Inner.ForeColor = c;
        public void SetBg(Color c)
        {
            _customBackColor = c;
            BackColor = c;
            Inner.BackColor = c;
            if (_lblUnit != null) _lblUnit.BackColor = c;
            if (_lblLabel != null) _lblLabel.BackColor = c;
        }

        public void RefreshTheme()
        {
            var bg = _customBackColor ?? UIColors.InputBg;
            BackColor = bg;
            Inner.BackColor = bg;
            if (!_customFontColor.HasValue)
            {
                Inner.ForeColor = Inner.ReadOnly ? UIColors.TextSub : UIColors.TextMain;
            }

            if (_lblUnit != null)
            {
                _lblUnit.ForeColor = UIColors.TextSub;
                _lblUnit.BackColor = bg;
            }

            if (_lblLabel != null)
            {
                if (!_customFontColor.HasValue)
                {
                    _lblLabel.ForeColor = UIColors.TextSub;
                }
                _lblLabel.BackColor = bg;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RefreshTheme();
            using (var bg = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(bg, ClientRectangle);

            var c = Inner.Focused ? UIColors.Primary : UIColors.Border;
            int h = Inner.Focused ? 2 : 1;

            // 画线逻辑：如果有左侧标签，线条从标签右侧开始画
            int startX = 0;
            if (_lblLabel != null) startX = _lblLabel.Width;
            int drawWidth = this.Width - startX;

            // 线条画在底部 (Height - h)
            using (var b = new SolidBrush(c))
                e.Graphics.FillRectangle(b, startX, Height - h, drawWidth, h);
        }
    }


    // =======================================================================
    // 新增：专门的数字输入框 (继承自下划线输入框)
    // =======================================================================
    public class LiteNumberInput : LiteUnderlineInput
    {
        private readonly bool _allowNegative;
        private bool _restoringText;
        private string _lastAcceptedText;

        public LiteNumberInput(
            string text,
            string unit = "",
            string label = "",
            int width = 160,
            Color? color = null,
            int maxLength = 10,
            bool allowNegative = true)
            : base(text, unit, label, width, color, HorizontalAlignment.Center)
        {
            _allowNegative = allowNegative;
            _lastAcceptedText = IsValidEditText(text, allowNegative) ? text : string.Empty;
            this.Inner.MaxLength = maxLength;

            this.Inner.KeyPress += (s, e) =>
            {
                if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar)) return;
                if (e.KeyChar == '.' && !this.Inner.Text.Contains(".")) return;
                if (e.KeyChar == '-' && _allowNegative && this.Inner.SelectionStart == 0 && !this.Inner.Text.Contains("-")) return;
                e.Handled = true;
            };

            this.Inner.TextChanged += (s, e) =>
            {
                if (_allowNegative || _restoringText)
                    return;

                if (IsValidEditText(this.Inner.Text, allowNegative: false))
                {
                    _lastAcceptedText = this.Inner.Text;
                    return;
                }

                int selectionStart = Math.Min(this.Inner.SelectionStart, _lastAcceptedText.Length);
                _restoringText = true;
                this.Inner.Text = _lastAcceptedText;
                this.Inner.SelectionStart = selectionStart;
                _restoringText = false;
            };

            this.Inner.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(this.Inner.Text) || this.Inner.Text == "." || this.Inner.Text == "-")
                {
                    this.Inner.Text = "0";
                }
            };
        }

        internal static bool IsValidEditText(string? text, bool allowNegative)
        {
            string value = text ?? string.Empty;
            bool decimalPointSeen = false;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsDigit(current))
                    continue;

                if (current == '.' && !decimalPointSeen)
                {
                    decimalPointSeen = true;
                    continue;
                }

                if (current == '-' && allowNegative && index == 0)
                    continue;

                return false;
            }

            return true;
        }

        public int ValueInt => int.TryParse(Inner.Text, out int v) ? v : 0;
        public double ValueDouble => double.TryParse(Inner.Text, out double v) ? v : 0.0;
    }


    public class LiteColorInput : Panel
    {
        public LiteUnderlineInput Input;
        public LiteColorPicker Picker;
        public string HexValue { get => Input.Inner.Text; set { Input.Inner.Text = value; Picker.SetHex(value); } }

        public LiteColorInput(string initialHex)
        {
            // ★★★ 修改：Size/Location 缩放
            this.Size = new Size(UIUtils.S(95), UIUtils.S(26));
            Picker = new LiteColorPicker(initialHex) { Size = new Size(UIUtils.S(26), UIUtils.S(22)), Location = new Point(this.Width - UIUtils.S(26), UIUtils.S(3)) };

            // 适配新构造函数
            Input = new LiteUnderlineInput(initialHex, "", "", 60) { Location = new Point(0, 0) };

            Picker.ColorChanged += (s, e) => Input.Inner.Text = $"#{Picker.Value.R:X2}{Picker.Value.G:X2}{Picker.Value.B:X2}";
            Input.Inner.TextChanged += (s, e) => Picker.SetHex(Input.Inner.Text);
            this.Controls.Add(Input);
            this.Controls.Add(Picker);

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem itemCopy = new ToolStripMenuItem(LanguageManager.T("Menu.CopyHex"));
            ToolStripMenuItem itemPaste = new ToolStripMenuItem(LanguageManager.T("Menu.PasteHex"));

            itemCopy.Click += (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(HexValue))
                    {
                        Clipboard.SetText(HexValue);
                    }
                }
                catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
            };

            itemPaste.Click += (s, e) =>
            {
                try
                {
                    string text = Clipboard.GetText()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(text))
                    {
                        System.Media.SystemSounds.Beep.Play();
                        return;
                    }
                    if (text.StartsWith("#"))
                    {
                        text = text.Substring(1);
                    }
                    if (text.Length == 6 && System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9a-fA-F]{6}$"))
                    {
                        HexValue = "#" + text.ToUpper();
                    }
                    else
                    {
                        System.Media.SystemSounds.Beep.Play();
                        ToolTip tt = new ToolTip();
                        tt.Show("格式无效，只接受 #RRGGBB 或 RRGGBB", Input.Inner, 0, -30, 2000);
                    }
                }
                catch
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            };

            menu.Items.Add(itemCopy);
            menu.Items.Add(itemPaste);

            Input.Inner.ContextMenuStrip = menu;
            Picker.ContextMenuStrip = menu;
            this.ContextMenuStrip = menu;
        }
    }


    public class LiteColorPicker : Control
    {
        private Color _color;
        public event EventHandler? ColorChanged;
        public Color Value { get => _color; set { _color = value; Invalidate(); } }
        // ★★★ 修改：Size 缩放
        public LiteColorPicker(string initialHex) { SetHex(initialHex); this.Size = new Size(UIUtils.S(24), UIUtils.S(24)); this.Cursor = Cursors.Hand; this.DoubleBuffered = true; this.Click += (s, e) => PickColor(); }
        public void SetHex(string hex) { try { _color = ColorTranslator.FromHtml(hex); Invalidate(); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); } }
        private void PickColor() { using (var cd = new ColorDialog()) { cd.Color = _color; cd.FullOpen = true; if (cd.ShowDialog() == DialogResult.OK) { _color = cd.Color; ColorChanged?.Invoke(this, EventArgs.Empty); Invalidate(); } } }
        protected override void OnPaint(PaintEventArgs e) { using (var b = new SolidBrush(_color)) e.Graphics.FillRectangle(b, 0, 0, Width - 1, Height - 1); using (var p = new Pen(UIColors.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }


    // 其他原有组件
    public class LiteNote : Panel { public LiteNote(string text, int indent = 0) { this.Dock = DockStyle.Top; this.Height = UIUtils.S(32); this.Margin = new Padding(0); var lbl = new Label { Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", 8F), ForeColor = UIColors.TextSub, Location = new Point(UIUtils.S(indent), UIUtils.S(10)) }; this.Controls.Add(lbl); } }
    public class LiteComboItem
    {
        public string Text { get; set; } = "";
        public string Value { get; set; } = "";
        public override string ToString() => Text;
    }


    public class NoScrollComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            // WM_MOUSEWHEEL = 0x020A
            if (m.Msg == 0x020A && !this.DroppedDown) return;
            base.WndProc(ref m);
        }
    }


    public class LiteComboBox : Panel
    {
        public ComboBox Inner;
        private readonly Panel _arrowOverlay;
        private bool _isHovered = false;
        private bool _isFocused = false;

        public LiteComboBox()
        {
            this.Size = new Size(UIUtils.S(110), UIUtils.S(28));
            this.BackColor = UIColors.InputBg;
            this.Padding = new Padding(1);
            this.DoubleBuffered = true;

            Inner = new NoScrollComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed,
                FlatStyle = FlatStyle.Flat,
                ItemHeight = UIUtils.S(22),
                ForeColor = UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", 9F),
                Dock = DockStyle.None,
                BackColor = UIColors.InputBg,
                Margin = new Padding(0)
            };
            Inner.DrawItem += DrawComboItem;
            this.Controls.Add(Inner);

            _arrowOverlay = new Panel
            {
                Width = UIUtils.S(24),
                BackColor = UIColors.InputBg,
                Cursor = Cursors.Hand
            };
            _arrowOverlay.Paint += PaintArrowOverlay;
            _arrowOverlay.MouseDown += (_, __) => OpenDropDown();
            _arrowOverlay.Click += (_, __) => OpenDropDown();
            this.Controls.Add(_arrowOverlay);
            _arrowOverlay.BringToFront();

            // Hover events
            this.MouseEnter += (s, e) => { _isHovered = true; this.Invalidate(); };
            this.MouseLeave += (s, e) => { if (!this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition))) { _isHovered = false; this.Invalidate(); } };

            Inner.MouseEnter += (s, e) => { _isHovered = true; this.Invalidate(); };
            Inner.MouseLeave += (s, e) => { if (!this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition))) { _isHovered = false; this.Invalidate(); } };

            _arrowOverlay.MouseEnter += (s, e) => { _isHovered = true; this.Invalidate(); };
            _arrowOverlay.MouseLeave += (s, e) => { if (!this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition))) { _isHovered = false; this.Invalidate(); } };

            // Focus events
            Inner.GotFocus += (s, e) => { _isFocused = true; this.Invalidate(); };
            Inner.LostFocus += (s, e) => { _isFocused = false; this.Invalidate(); };

            // Layout
            this.Layout += (s, e) =>
            {
                SyncThemeColors();

                // Dynamically adjust ItemHeight to match Panel's height
                int desiredItemHeight = this.Height;
                if (Inner.ItemHeight != desiredItemHeight)
                {
                    Inner.ItemHeight = desiredItemHeight;
                }

                // Center vertically and clip borders
                int innerTop = (this.Height - Inner.Height) / 2;
                Inner.SetBounds(-2, innerTop, this.Width + UIUtils.S(24), Inner.Height);

                int arrowWidth = UIUtils.S(24);
                _arrowOverlay.SetBounds(Math.Max(1, this.Width - arrowWidth - 1), 1, arrowWidth, Math.Max(1, this.Height - 2));
                _arrowOverlay.BringToFront();
            };

            this.Paint += (s, e) =>
            {
                SyncThemeColors();
                using (var b = new SolidBrush(BackColor))
                    e.Graphics.FillRectangle(b, ClientRectangle);

                Color borderColor = UIColors.Border;
                if (!Enabled)
                {
                    borderColor = UIColors.Border;
                }
                else if (_isFocused)
                {
                    borderColor = UIColors.Primary;
                }
                else if (_isHovered)
                {
                    borderColor = UIColors.Primary;
                }

                using (var p = new Pen(borderColor))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Inner.Enabled = Enabled;
            SyncThemeColors();
            this.Invalidate();
        }

        private void SyncThemeColors()
        {
            Color targetBg = Enabled ? UIColors.InputBg : UIColors.ControlDisabledBg;
            Color targetFg = Enabled ? UIColors.TextMain : UIColors.TextDisabled;

            if (BackColor != targetBg) BackColor = targetBg;
            if (Inner.BackColor != targetBg) Inner.BackColor = targetBg;
            if (Inner.ForeColor != targetFg) Inner.ForeColor = targetFg;
            if (_arrowOverlay.BackColor != targetBg) _arrowOverlay.BackColor = targetBg;
        }

        public void RefreshTheme()
        {
            SyncThemeColors();
            Inner.Invalidate();
            _arrowOverlay.Invalidate();
            Invalidate();
        }

        private void OpenDropDown()
        {
            if (!Enabled || !Inner.Enabled) return;
            Inner.Focus();
            try { Inner.DroppedDown = true; } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
        }

        private void PaintArrowOverlay(object? sender, PaintEventArgs e)
        {
            SyncThemeColors();
            using (var b = new SolidBrush(_arrowOverlay.BackColor))
                e.Graphics.FillRectangle(b, _arrowOverlay.ClientRectangle);

            Color arrowColor = Enabled ? UIColors.TextSub : UIColors.TextDisabled;
            if (Enabled && _isHovered)
            {
                arrowColor = UIColors.TextMain;
            }

            TextRenderer.DrawText(
                e.Graphics,
                "▼",
                new Font("Microsoft YaHei UI", 7F, FontStyle.Bold),
                _arrowOverlay.ClientRectangle,
                arrowColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawComboItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool isEdit = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;

            Color bg;
            if (!Enabled)
            {
                bg = UIColors.ControlDisabledBg;
            }
            else if (selected)
            {
                bg = UIColors.IsDark ? Color.FromArgb(40, 49, 61) : Color.FromArgb(232, 244, 255);
            }
            else
            {
                bg = UIColors.InputBg;
            }

            Color fg = Enabled ? UIColors.TextMain : UIColors.TextDisabled;

            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);

            string text = Inner.GetItemText(Inner.Items[e.Index]) ?? "";

            int leftPadding = UIUtils.S(8);
            int textWidth;

            if (isEdit)
            {
                leftPadding = UIUtils.S(10); // Offset to align nicely when Inner is shifted Left = -2
                int arrowWidth = UIUtils.S(24);
                textWidth = this.Width - arrowWidth - leftPadding - UIUtils.S(2);
                if (textWidth < 1) textWidth = 1;
            }
            else
            {
                textWidth = Math.Max(1, e.Bounds.Width - UIUtils.S(12));
            }

            var textRect = new Rectangle(e.Bounds.Left + leftPadding, e.Bounds.Top, textWidth, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, e.Font ?? Inner.Font, textRect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        public object? SelectedItem { get => Inner.SelectedItem; set => Inner.SelectedItem = value; }
        public int SelectedIndex { get => Inner.SelectedIndex; set => Inner.SelectedIndex = value; }
        public ComboBox.ObjectCollection Items => Inner.Items;
        [AllowNull]
        public override string Text { get => Inner.Text; set => Inner.Text = value ?? ""; }

        // Helper methods for Key-Value pairs
        public void AddItem(string text, string value)
        {
            if (Inner.Items.Count == 0)
            {
                Inner.DisplayMember = "Text";
                Inner.ValueMember = "Value";
            }
            Inner.Items.Add(new LiteComboItem { Text = text, Value = value });
        }

        public bool SelectValue(string value)
        {
            for (int i = 0; i < Inner.Items.Count; i++)
            {
                if (Inner.Items[i] is LiteComboItem item && item.Value == value)
                {
                    Inner.SelectedIndex = i;
                    return true;
                }
            }
            for (int i = 0; i < Inner.Items.Count; i++)
            {
                if (Inner.Items[i] is LiteComboItem item &&
                    string.Equals(item.Text, value, StringComparison.OrdinalIgnoreCase))
                {
                    Inner.SelectedIndex = i;
                    return true;
                }
            }
            if (Inner.Items.Count > 0) Inner.SelectedIndex = 0;
            return false;
        }

        public string SelectedValue
        {
            get => (Inner.SelectedItem as LiteComboItem)?.Value ?? "";
        }
    }

}
