using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsFormThemeTreeStyler
    {
        private const int WM_SETREDRAW = 0x000B;
        private const int RDW_INVALIDATE = 0x0001;
        private const int RDW_ALLCHILDREN = 0x0080;
        private const int RDW_UPDATENOW = 0x0100;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, int flags);

        public static void ApplyNativeThemeToInteractiveTree(Control root)
        {
            var stack = new Stack<Control>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var control = stack.Pop();
                if (control.IsDisposed)
                    continue;

                if (NeedsDeferredNativeTheme(control))
                    UIColors.ApplyNativeTheme(control);

                for (int i = control.Controls.Count - 1; i >= 0; i--)
                    stack.Push(control.Controls[i]);
            }
        }

        public static IDisposable FreezeRedraw(Control control)
        {
            return new RedrawScope(control);
        }

        public static void ApplyToControlTree(Control root, SettingsFormThemePalette? previousPalette = null)
        {
            if (previousPalette.HasValue)
            {
                var palette = previousPalette.Value;
                root.BackColor = palette.TranslateBack(root.BackColor);
                root.ForeColor = palette.TranslateFore(root.ForeColor);
            }

            if (root is SettingsPageBase)
            {
                root.BackColor = UIColors.MainBg;
                root.ForeColor = UIColors.TextMain;
            }

            if (root is LiteComboBox liteCombo)
            {
                liteCombo.RefreshTheme();
                return;
            }

            if (root is LiteUnderlineInput underlineInput)
            {
                underlineInput.RefreshTheme();
                return;
            }

            if (root is LiteNavBtn nav)
            {
                nav.BackColor = UIColors.SidebarBg;
                nav.ForeColor = UIColors.TextMain;
                nav.Invalidate();
            }
            else if (root is LiteButton button)
            {
                button.RefreshTheme();
            }
            else if (root is AntdUI.Button antButton)
            {
                bool primary = antButton.BackColor.HasValue && antButton.BackColor.Value.ToArgb() == UIColors.Primary.ToArgb();
                antButton.BackColor = primary ? UIColors.Primary : UIColors.ControlBg;
                antButton.ForeColor = primary ? Color.White : UIColors.TextMain;
            }
            else if (root is AntdUI.Input antInput)
            {
                antInput.BackColor = UIColors.InputBg;
                antInput.ForeColor = UIColors.TextMain;
            }
            else if (root is AntdUI.Checkbox antCheckbox)
            {
                antCheckbox.BackColor = Color.Transparent;
                antCheckbox.ForeColor = UIColors.TextMain;
            }
            else if (root is LiteSettingsGroup group)
            {
                group.ApplySystemTheme();
            }
            else if (root is LiteLink link)
            {
                link.SetColor(UIColors.Link, UIColors.LinkHover);
            }
            else if (root is TextBox textBox)
            {
                textBox.BackColor = textBox.Enabled ? UIColors.InputBg : UIColors.ControlDisabledBg;
                textBox.ForeColor = UIColors.TextMain;
            }
            else if (root is ComboBox comboBox)
            {
                comboBox.BackColor = comboBox.Enabled ? UIColors.InputBg : UIColors.ControlDisabledBg;
                comboBox.ForeColor = UIColors.TextMain;
            }
            else if (root is DataGridView grid)
            {
                ApplyGridTheme(grid);
            }
            else if (root is Label label && label.GetType() == typeof(Label))
            {
                label.BackColor = Color.Transparent;
            }

            foreach (Control child in root.Controls)
                ApplyToControlTree(child, previousPalette);
        }

        private static bool NeedsDeferredNativeTheme(Control control)
        {
            if (control is TextBoxBase
                || control is ComboBox
                || control is ListBox
                || control is ListView
                || control is TreeView
                || control is DataGridView
                || control is VScrollBar
                || control is HScrollBar
                || control is NumericUpDown
                || control is DateTimePicker
                || control is CheckedListBox
                || control is PropertyGrid)
            {
                return true;
            }

            return control is ScrollableControl scrollable && scrollable.AutoScroll;
        }

        private static void ApplyGridTheme(DataGridView grid)
        {
            Color rowBg = UIColors.CardBg;
            Color altBg = UIColors.IsDark ? Color.FromArgb(27, 33, 40) : Color.FromArgb(248, 249, 250);
            Color selectionBg = UIColors.NavSelected;

            grid.BackgroundColor = rowBg;
            grid.GridColor = UIColors.Border;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;

            grid.DefaultCellStyle.BackColor = rowBg;
            grid.DefaultCellStyle.ForeColor = UIColors.TextMain;
            grid.DefaultCellStyle.SelectionBackColor = selectionBg;
            grid.DefaultCellStyle.SelectionForeColor = UIColors.TextMain;

            grid.RowsDefaultCellStyle.BackColor = rowBg;
            grid.RowsDefaultCellStyle.ForeColor = UIColors.TextMain;
            grid.RowsDefaultCellStyle.SelectionBackColor = selectionBg;
            grid.RowsDefaultCellStyle.SelectionForeColor = UIColors.TextMain;

            grid.AlternatingRowsDefaultCellStyle.BackColor = altBg;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = UIColors.TextMain;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = selectionBg;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = UIColors.TextMain;

            grid.ColumnHeadersDefaultCellStyle.BackColor = UIColors.GroupHeader;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UIColors.TextMain;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UIColors.GroupHeader;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UIColors.TextMain;

            grid.RowHeadersDefaultCellStyle.BackColor = UIColors.GroupHeader;
            grid.RowHeadersDefaultCellStyle.ForeColor = UIColors.TextSub;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = UIColors.GroupHeader;
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = UIColors.TextMain;
        }

        private sealed class RedrawScope : IDisposable
        {
            private readonly Control _control;
            private readonly bool _active;
            private bool _disposed;

            public RedrawScope(Control control)
            {
                _control = control;
                _active = control.IsHandleCreated && !control.IsDisposed;
                if (_active)
                    SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (!_active || _control.IsDisposed || !_control.IsHandleCreated) return;

                SendMessage(_control.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                RedrawWindow(_control.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ALLCHILDREN | RDW_UPDATENOW);
            }
        }
    }

    internal readonly struct SettingsFormThemePalette
    {
        private readonly Color _mainBg;
        private readonly Color _sidebarBg;
        private readonly Color _cardBg;
        private readonly Color _border;
        private readonly Color _groupHeader;
        private readonly Color _controlBg;
        private readonly Color _inputBg;
        private readonly Color _controlHover;
        private readonly Color _controlPressed;
        private readonly Color _controlDisabledBg;
        private readonly Color _navSelected;
        private readonly Color _navHover;
        private readonly Color _textMain;
        private readonly Color _textSub;
        private readonly Color _textDisabled;
        private readonly Color _link;
        private readonly Color _linkHover;
        private readonly Color _positive;
        private readonly Color _negative;
        private readonly Color _textWarn;
        private readonly Color _textCrit;
        private readonly Color _primary;

        private SettingsFormThemePalette(
            Color mainBg,
            Color sidebarBg,
            Color cardBg,
            Color border,
            Color groupHeader,
            Color controlBg,
            Color inputBg,
            Color controlHover,
            Color controlPressed,
            Color controlDisabledBg,
            Color navSelected,
            Color navHover,
            Color textMain,
            Color textSub,
            Color textDisabled,
            Color link,
            Color linkHover,
            Color positive,
            Color negative,
            Color textWarn,
            Color textCrit,
            Color primary)
        {
            _mainBg = mainBg;
            _sidebarBg = sidebarBg;
            _cardBg = cardBg;
            _border = border;
            _groupHeader = groupHeader;
            _controlBg = controlBg;
            _inputBg = inputBg;
            _controlHover = controlHover;
            _controlPressed = controlPressed;
            _controlDisabledBg = controlDisabledBg;
            _navSelected = navSelected;
            _navHover = navHover;
            _textMain = textMain;
            _textSub = textSub;
            _textDisabled = textDisabled;
            _link = link;
            _linkHover = linkHover;
            _positive = positive;
            _negative = negative;
            _textWarn = textWarn;
            _textCrit = textCrit;
            _primary = primary;
        }

        public static SettingsFormThemePalette Capture()
        {
            return new SettingsFormThemePalette(
                UIColors.MainBg,
                UIColors.SidebarBg,
                UIColors.CardBg,
                UIColors.Border,
                UIColors.GroupHeader,
                UIColors.ControlBg,
                UIColors.InputBg,
                UIColors.ControlHover,
                UIColors.ControlPressed,
                UIColors.ControlDisabledBg,
                UIColors.NavSelected,
                UIColors.NavHover,
                UIColors.TextMain,
                UIColors.TextSub,
                UIColors.TextDisabled,
                UIColors.Link,
                UIColors.LinkHover,
                UIColors.Positive,
                UIColors.Negative,
                UIColors.TextWarn,
                UIColors.TextCrit,
                UIColors.Primary);
        }

        public Color TranslateBack(Color color)
        {
            if (Matches(color, _mainBg) || Matches(color, SettingsPageBase.GlobalBackColor)) return UIColors.MainBg;
            if (Matches(color, _sidebarBg)) return UIColors.SidebarBg;
            if (Matches(color, _cardBg)) return UIColors.CardBg;
            if (Matches(color, _border)) return UIColors.Border;
            if (Matches(color, _groupHeader)) return UIColors.GroupHeader;
            if (Matches(color, _controlBg)) return UIColors.ControlBg;
            if (Matches(color, _inputBg)) return UIColors.InputBg;
            if (Matches(color, _controlHover)) return UIColors.ControlHover;
            if (Matches(color, _controlPressed)) return UIColors.ControlPressed;
            if (Matches(color, _controlDisabledBg)) return UIColors.ControlDisabledBg;
            if (Matches(color, _navSelected)) return UIColors.NavSelected;
            if (Matches(color, _navHover)) return UIColors.NavHover;
            return color;
        }

        public Color TranslateFore(Color color)
        {
            if (Matches(color, _textMain)) return UIColors.TextMain;
            if (Matches(color, _textSub)) return UIColors.TextSub;
            if (Matches(color, _textDisabled)) return UIColors.TextDisabled;
            if (Matches(color, _link)) return UIColors.Link;
            if (Matches(color, _linkHover)) return UIColors.LinkHover;
            if (Matches(color, _positive)) return UIColors.Positive;
            if (Matches(color, _negative)) return UIColors.Negative;
            if (Matches(color, _textWarn)) return UIColors.TextWarn;
            if (Matches(color, _textCrit)) return UIColors.TextCrit;
            if (Matches(color, _primary)) return UIColors.Primary;
            return color;
        }

        private static bool Matches(Color left, Color right)
        {
            return left.ToArgb() == right.ToArgb();
        }
    }
}
