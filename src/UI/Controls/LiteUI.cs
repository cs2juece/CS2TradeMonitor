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
    // Central theme tokens and native dark-mode hooks. Concrete Lite controls
    // are split into layout/input/button files to keep open-source review smaller.
    public static class UIColors
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int WM_THEMECHANGED = 0x031A;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        private static extern int SetPreferredAppMode(PreferredAppMode appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            ForceLight = 3
        }

        public static bool IsDark { get; private set; } = true;
        public static Color MainBg = Color.FromArgb(27, 30, 36);
        public static Color SidebarBg = Color.FromArgb(24, 27, 32);
        public static Color CardBg = Color.FromArgb(34, 38, 45);
        public static Color Border = Color.FromArgb(58, 64, 74);
        public static Color Primary = Color.FromArgb(0, 120, 215);
        public static Color TextMain = Color.FromArgb(238, 241, 245);
        public static Color TextSub = Color.FromArgb(174, 184, 196);
        public static Color GroupHeader = Color.FromArgb(31, 35, 42);
        public static Color ControlBg = Color.FromArgb(25, 30, 37);
        public static Color InputBg = Color.FromArgb(34, 38, 45);
        public static Color ControlHover = Color.FromArgb(37, 44, 54);
        public static Color ControlPressed = Color.FromArgb(21, 26, 32);
        public static Color ControlDisabledBg = Color.FromArgb(26, 31, 38);
        public static Color TextDisabled = Color.FromArgb(104, 115, 131);
        public static Color Link = Color.FromArgb(88, 166, 255);
        public static Color LinkHover = Color.FromArgb(122, 184, 255);
        public static Color Positive = Color.FromArgb(47, 191, 143);
        public static Color Negative = Color.FromArgb(255, 107, 122);

        public static Color NavSelected = Color.FromArgb(42, 47, 56);
        public static Color NavHover = Color.FromArgb(36, 40, 48);

        public static Color TextWarn = Color.FromArgb(230, 165, 48);
        public static Color TextCrit = Color.FromArgb(255, 82, 82);

        public static void ApplySettingsTheme(bool dark)
        {
            IsDark = dark;
            ApplyNativeAppMode();
            if (dark)
            {
                MainBg = Color.FromArgb(14, 17, 21);
                SidebarBg = Color.FromArgb(17, 21, 26);
                CardBg = Color.FromArgb(28, 34, 42);
                Border = Color.FromArgb(48, 57, 69);
                Primary = Color.FromArgb(10, 132, 255);
                TextMain = Color.FromArgb(242, 244, 247);
                TextSub = Color.FromArgb(184, 192, 204);
                GroupHeader = Color.FromArgb(29, 35, 43);
                ControlBg = Color.FromArgb(21, 27, 34);
                InputBg = Color.FromArgb(15, 20, 27);
                ControlHover = Color.FromArgb(36, 44, 54);
                ControlPressed = Color.FromArgb(18, 23, 30);
                ControlDisabledBg = Color.FromArgb(22, 27, 34);
                TextDisabled = Color.FromArgb(104, 115, 131);
                Link = Color.FromArgb(88, 166, 255);
                LinkHover = Color.FromArgb(122, 184, 255);
                Positive = Color.FromArgb(47, 191, 143);
                Negative = Color.FromArgb(255, 107, 122);
                NavSelected = Color.FromArgb(38, 45, 55);
                NavHover = Color.FromArgb(32, 38, 46);
                TextWarn = Color.FromArgb(214, 158, 46);
                TextCrit = Color.FromArgb(245, 101, 101);
                AntdThemeBridge.Apply(dark);
                return;
            }

            MainBg = Color.FromArgb(244, 247, 251);
            SidebarBg = Color.FromArgb(238, 242, 247);
            CardBg = Color.White;
            Border = Color.FromArgb(210, 218, 229);
            Primary = Color.FromArgb(0, 120, 215);
            TextMain = Color.FromArgb(28, 37, 50);
            TextSub = Color.FromArgb(82, 96, 112);
            GroupHeader = Color.FromArgb(237, 243, 250);
            ControlBg = Color.FromArgb(248, 250, 253);
            InputBg = Color.FromArgb(243, 246, 250);
            ControlHover = Color.FromArgb(235, 242, 250);
            ControlPressed = Color.FromArgb(221, 234, 248);
            ControlDisabledBg = Color.FromArgb(236, 240, 245);
            TextDisabled = Color.FromArgb(132, 145, 160);
            Link = Color.FromArgb(0, 120, 215);
            LinkHover = Color.FromArgb(0, 90, 180);
            Positive = Color.FromArgb(0, 170, 75);
            Negative = Color.FromArgb(220, 50, 50);
            NavSelected = Color.FromArgb(227, 239, 253);
            NavHover = Color.FromArgb(234, 242, 251);
            TextWarn = Color.FromArgb(215, 145, 0);
            TextCrit = Color.FromArgb(220, 50, 50);
            AntdThemeBridge.Apply(dark);
        }

        public static void ApplyNativeThemeRecursively(Control? root)
        {
            if (root == null || root.IsDisposed) return;

            ApplyManagedTheme(root);
            RegisterNativeTheme(root);
            foreach (Control child in root.Controls)
            {
                ApplyNativeThemeRecursively(child);
            }
        }

        public static void ApplyNativeTheme(Control? control)
        {
            if (control == null || control.IsDisposed) return;
            ApplyManagedTheme(control);
            RegisterNativeTheme(control);
        }

        private static void ApplyManagedTheme(Control control)
        {
            switch (control)
            {
                case LiteSettingsGroup group:
                    group.ApplySystemTheme();
                    break;
                case LiteButton liteButton:
                    liteButton.RefreshTheme();
                    break;
                case LiteComboBox liteComboBox:
                    liteComboBox.RefreshTheme();
                    break;
                case LiteUnderlineInput underlineInput:
                    underlineInput.RefreshTheme();
                    break;
                case DataGridView grid:
                    ApplyDataGridTheme(grid);
                    break;
                case NumericUpDown numeric:
                    ApplyNumericUpDownTheme(numeric);
                    break;
                case DateTimePicker dateTimePicker:
                    ApplyDateTimePickerTheme(dateTimePicker);
                    break;
                case CheckedListBox checkedListBox:
                    ApplyCheckedListBoxTheme(checkedListBox);
                    break;
                case PropertyGrid propertyGrid:
                    ApplyPropertyGridTheme(propertyGrid);
                    break;
                case TextBoxBase textBox:
                    ApplyTextBoxTheme(textBox);
                    break;
                case ComboBox comboBox:
                    ApplyComboBoxTheme(comboBox);
                    break;
                case ListBox listBox:
                    ApplyListBoxTheme(listBox);
                    break;
                case ListView listView:
                    ApplyListViewTheme(listView);
                    break;
                case TreeView treeView:
                    ApplyTreeViewTheme(treeView);
                    break;
                case Button button when ShouldThemeNativeButton(button):
                    ApplyButtonTheme(button);
                    break;
                case GroupBox groupBox:
                    ApplyGroupBoxTheme(groupBox);
                    break;
                case TabControl tabControl:
                    ApplyTabControlTheme(tabControl);
                    break;
                case TabPage tabPage:
                    ApplyTabPageTheme(tabPage);
                    break;
                case SplitContainer splitContainer:
                    ApplySplitContainerTheme(splitContainer);
                    break;
                case TableLayoutPanel tableLayoutPanel:
                    ApplyContainerTheme(tableLayoutPanel);
                    break;
                case FlowLayoutPanel flowLayoutPanel:
                    ApplyContainerTheme(flowLayoutPanel);
                    break;
                case Panel panel:
                    ApplyContainerTheme(panel);
                    break;
                case UserControl userControl:
                    ApplyContainerTheme(userControl);
                    break;
                case Form form:
                    ApplyContainerTheme(form);
                    break;
                case Label label:
                    ApplyLabelTheme(label);
                    break;
            }
        }

        private static void ApplyTextBoxTheme(TextBoxBase textBox)
        {
            textBox.BackColor = textBox.Enabled ? InputBg : ControlDisabledBg;
            textBox.ForeColor = textBox.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplyComboBoxTheme(ComboBox comboBox)
        {
            comboBox.BackColor = comboBox.Enabled ? InputBg : ControlDisabledBg;
            comboBox.ForeColor = comboBox.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplyListBoxTheme(ListBox listBox)
        {
            listBox.BackColor = CardBg;
            listBox.ForeColor = listBox.Enabled ? TextMain : TextDisabled;
            listBox.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void ConfigureThemedListBox(ListBox listBox)
        {
            if (listBox == null || listBox.IsDisposed)
                return;

            ApplyListBoxTheme(listBox);
            RegisterNativeTheme(listBox);
            listBox.DrawMode = DrawMode.OwnerDrawFixed;
            listBox.ItemHeight = Math.Max(listBox.ItemHeight, UIUtils.S(22));
            listBox.DrawItem -= ThemedListBox_DrawItem;
            listBox.DrawItem += ThemedListBox_DrawItem;
            listBox.HandleCreated -= NativeThemeControl_HandleCreated;
            listBox.HandleCreated += NativeThemeControl_HandleCreated;
        }

        private static void ThemedListBox_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not ListBox listBox || e.Index < 0 || e.Index >= listBox.Items.Count)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = selected
                ? (IsDark ? Color.FromArgb(38, 60, 82) : Color.FromArgb(229, 241, 255))
                : CardBg;
            Color fg = listBox.Enabled ? TextMain : TextDisabled;
            using (var brush = new SolidBrush(bg))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                listBox.GetItemText(listBox.Items[e.Index]),
                e.Font ?? listBox.Font,
                new Rectangle(e.Bounds.Left + UIUtils.S(4), e.Bounds.Top, Math.Max(1, e.Bounds.Width - UIUtils.S(8)), e.Bounds.Height),
                fg,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private static void ApplyListViewTheme(ListView listView)
        {
            listView.BackColor = CardBg;
            listView.ForeColor = listView.Enabled ? TextMain : TextDisabled;
            listView.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void ApplyTreeViewTheme(TreeView treeView)
        {
            treeView.BackColor = CardBg;
            treeView.ForeColor = treeView.Enabled ? TextMain : TextDisabled;
            treeView.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void ApplyDataGridTheme(DataGridView grid)
        {
            grid.BackgroundColor = CardBg;
            grid.GridColor = Border;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;

            grid.DefaultCellStyle.BackColor = CardBg;
            grid.DefaultCellStyle.ForeColor = TextMain;
            grid.DefaultCellStyle.SelectionBackColor = IsDark ? Color.FromArgb(38, 60, 82) : Color.FromArgb(229, 241, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextMain;

            grid.AlternatingRowsDefaultCellStyle.BackColor = IsDark ? Color.FromArgb(27, 33, 40) : Color.FromArgb(248, 249, 250);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = TextMain;

            grid.ColumnHeadersDefaultCellStyle.BackColor = GroupHeader;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMain;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = GroupHeader;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextMain;

            grid.RowHeadersDefaultCellStyle.BackColor = GroupHeader;
            grid.RowHeadersDefaultCellStyle.ForeColor = TextSub;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = GroupHeader;
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = TextMain;
        }

        private static void ApplyNumericUpDownTheme(NumericUpDown numeric)
        {
            numeric.BackColor = numeric.Enabled ? InputBg : ControlDisabledBg;
            numeric.ForeColor = numeric.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplyDateTimePickerTheme(DateTimePicker dateTimePicker)
        {
            dateTimePicker.CalendarForeColor = TextMain;
            dateTimePicker.CalendarMonthBackground = CardBg;
            dateTimePicker.CalendarTitleBackColor = GroupHeader;
            dateTimePicker.CalendarTitleForeColor = TextMain;
            dateTimePicker.CalendarTrailingForeColor = TextDisabled;
            dateTimePicker.BackColor = dateTimePicker.Enabled ? InputBg : ControlDisabledBg;
            dateTimePicker.ForeColor = dateTimePicker.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplyCheckedListBoxTheme(CheckedListBox checkedListBox)
        {
            checkedListBox.BackColor = CardBg;
            checkedListBox.ForeColor = checkedListBox.Enabled ? TextMain : TextDisabled;
            checkedListBox.BorderStyle = BorderStyle.FixedSingle;
        }

        private static void ApplyPropertyGridTheme(PropertyGrid propertyGrid)
        {
            propertyGrid.BackColor = CardBg;
            propertyGrid.ViewBackColor = CardBg;
            propertyGrid.ViewForeColor = TextMain;
            propertyGrid.CategoryForeColor = TextMain;
            propertyGrid.CategorySplitterColor = Border;
            propertyGrid.HelpBackColor = GroupHeader;
            propertyGrid.HelpForeColor = TextSub;
            propertyGrid.LineColor = Border;
        }

        private static bool ShouldThemeNativeButton(Button button)
        {
            if (button is LiteButton) return false;

            return button.UseVisualStyleBackColor
                || button.BackColor.IsEmpty
                || button.BackColor.ToArgb() == SystemColors.Control.ToArgb()
                || button.BackColor.ToArgb() == Color.White.ToArgb();
        }

        private static void ApplyButtonTheme(Button button)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = ControlBg;
            button.ForeColor = button.Enabled ? TextMain : TextDisabled;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = ControlHover;
            button.FlatAppearance.MouseDownBackColor = ControlPressed;
        }

        private static void ApplyGroupBoxTheme(GroupBox groupBox)
        {
            ApplyContainerTheme(groupBox);
            groupBox.ForeColor = groupBox.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplyTabControlTheme(TabControl tabControl)
        {
            tabControl.BackColor = MainBg;
            tabControl.ForeColor = tabControl.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplyTabPageTheme(TabPage tabPage)
        {
            tabPage.BackColor = CardBg;
            tabPage.ForeColor = tabPage.Enabled ? TextMain : TextDisabled;
        }

        private static void ApplySplitContainerTheme(SplitContainer splitContainer)
        {
            splitContainer.BackColor = Border;
            ApplyContainerTheme(splitContainer.Panel1);
            ApplyContainerTheme(splitContainer.Panel2);
        }

        private static void ApplyContainerTheme(Control control)
        {
            if (IsExplicitMainBackground(control.BackColor))
            {
                control.BackColor = MainBg;
            }
            else if (IsExplicitSidebarBackground(control.BackColor))
            {
                control.BackColor = SidebarBg;
            }
            else if (ShouldUpdateContainerBackColor(control.BackColor))
            {
                control.BackColor = control is Form ? MainBg : CardBg;
            }

            if (IsNeutralLabelColor(control.ForeColor))
            {
                control.ForeColor = control.Enabled ? TextMain : TextDisabled;
            }
        }

        private static bool IsExplicitMainBackground(Color color)
        {
            if (color.IsEmpty || color == Color.Transparent) return false;

            int argb = color.ToArgb();
            return argb == MainBg.ToArgb()
                || argb == SettingsPageBase.GlobalBackColor.ToArgb();
        }

        private static bool IsExplicitSidebarBackground(Color color)
        {
            return !color.IsEmpty
                && color != Color.Transparent
                && color.ToArgb() == SidebarBg.ToArgb();
        }

        private static bool ShouldUpdateContainerBackColor(Color color)
        {
            if (color.IsEmpty) return true;
            if (color == Color.Transparent) return false;

            int argb = color.ToArgb();
            return argb == SystemColors.Control.ToArgb()
                || argb == SystemColors.Window.ToArgb()
                || argb == Color.White.ToArgb()
                || argb == Color.Black.ToArgb()
                || argb == MainBg.ToArgb()
                || argb == SidebarBg.ToArgb()
                || argb == CardBg.ToArgb()
                || argb == GroupHeader.ToArgb()
                || argb == ControlBg.ToArgb()
                || argb == ControlHover.ToArgb()
                || argb == ControlDisabledBg.ToArgb()
                || argb == InputBg.ToArgb()
                || argb == Color.FromArgb(243, 243, 243).ToArgb()
                || argb == Color.FromArgb(240, 240, 240).ToArgb()
                || argb == Color.FromArgb(248, 250, 252).ToArgb()
                || argb == Color.FromArgb(14, 17, 21).ToArgb()
                || argb == Color.FromArgb(17, 21, 26).ToArgb()
                || argb == Color.FromArgb(30, 35, 42).ToArgb()
                || argb == Color.FromArgb(30, 36, 44).ToArgb();
        }

        private static void ApplyLabelTheme(Label label)
        {
            if (ShouldUpdateLabelBackColor(label.BackColor))
                label.BackColor = Color.Transparent;

            if (ShouldKeepSemanticLabelColor(label.ForeColor))
                return;

            if (IsNeutralLabelColor(label.ForeColor))
                label.ForeColor = label.Enabled ? TextMain : TextDisabled;
        }

        private static bool ShouldUpdateLabelBackColor(Color color)
        {
            if (color.IsEmpty || color == Color.Transparent) return false;

            int argb = color.ToArgb();
            return argb == SystemColors.Control.ToArgb()
                || argb == SystemColors.Window.ToArgb()
                || argb == SystemColors.ControlLight.ToArgb()
                || argb == SystemColors.ControlDark.ToArgb()
                || argb == Color.White.ToArgb()
                || argb == Color.Black.ToArgb()
                || argb == MainBg.ToArgb()
                || argb == SidebarBg.ToArgb()
                || argb == CardBg.ToArgb()
                || argb == GroupHeader.ToArgb()
                || argb == ControlBg.ToArgb()
                || argb == ControlHover.ToArgb()
                || argb == ControlDisabledBg.ToArgb()
                || argb == InputBg.ToArgb()
                || argb == Color.FromArgb(243, 243, 243).ToArgb()
                || argb == Color.FromArgb(240, 240, 240).ToArgb()
                || argb == Color.FromArgb(248, 250, 252).ToArgb()
                || argb == Color.FromArgb(14, 17, 21).ToArgb()
                || argb == Color.FromArgb(17, 21, 26).ToArgb()
                || argb == Color.FromArgb(30, 35, 42).ToArgb()
                || argb == Color.FromArgb(30, 36, 44).ToArgb();
        }

        private static bool ShouldKeepSemanticLabelColor(Color color)
        {
            if (color.IsEmpty) return false;

            int argb = color.ToArgb();
            return argb == Primary.ToArgb()
                || argb == Link.ToArgb()
                || argb == LinkHover.ToArgb()
                || argb == Positive.ToArgb()
                || argb == Negative.ToArgb()
                || argb == TextWarn.ToArgb()
                || argb == TextCrit.ToArgb()
                || argb == Color.Red.ToArgb()
                || argb == Color.Green.ToArgb()
                || argb == Color.Lime.ToArgb()
                || argb == Color.Orange.ToArgb()
                || argb == Color.Yellow.ToArgb();
        }

        private static bool IsNeutralLabelColor(Color color)
        {
            if (color.IsEmpty) return true;

            int argb = color.ToArgb();
            return argb == SystemColors.ControlText.ToArgb()
                || argb == SystemColors.ControlDarkDark.ToArgb()
                || argb == Color.Black.ToArgb()
                || argb == Color.White.ToArgb()
                || argb == TextMain.ToArgb()
                || argb == TextSub.ToArgb()
                || argb == TextDisabled.ToArgb()
                || argb == Color.FromArgb(32, 32, 32).ToArgb()
                || argb == Color.FromArgb(90, 90, 90).ToArgb()
                || argb == Color.FromArgb(242, 244, 247).ToArgb()
                || argb == Color.FromArgb(184, 192, 204).ToArgb();
        }

        private static void RegisterNativeTheme(Control control)
        {
            if (!NeedsNativeTheme(control)) return;

            control.HandleCreated -= NativeThemeControl_HandleCreated;
            control.HandleCreated += NativeThemeControl_HandleCreated;

            if (control.IsHandleCreated)
            {
                ApplyNativeThemeToHandle(control);
            }
        }

        private static bool NeedsNativeTheme(Control control)
        {
            return control is Form
                || control is Panel
                || control is ScrollableControl
                || control is ListBox
                || control is ListView
                || control is TreeView
                || control is DataGridView
                || control is VScrollBar
                || control is HScrollBar
                || control is NumericUpDown
                || control is DateTimePicker
                || control is CheckedListBox
                || control is PropertyGrid
                || control is TextBox
                || control is RichTextBox
                || control is ComboBox;
        }

        private static void NativeThemeControl_HandleCreated(object? sender, EventArgs e)
        {
            if (sender is Control control)
            {
                ApplyNativeThemeToHandle(control);
            }
        }

        private static void ApplyNativeThemeToHandle(Control control)
        {
            ApplyNativeAppMode();

            try
            {
                AllowDarkModeForWindow(control.Handle, IsDark);
            }
            catch
            {
                // Optional Win10 dark-mode helper.
            }

            try
            {
                if (control is Form form)
                {
                    int dark = IsDark ? 1 : 0;
                    if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                    {
                        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref dark, sizeof(int));
                    }

                    int captionColor = ColorTranslator.ToWin32(IsDark ? MainBg : SidebarBg);
                    int captionTextColor = ColorTranslator.ToWin32(IsDark ? TextMain : Color.Black);
                    int borderColor = ColorTranslator.ToWin32(IsDark ? Border : Color.FromArgb(190, 190, 190));
                    DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
                    DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref captionTextColor, sizeof(int));
                    DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
                }
            }
            catch
            {
                // Window frame dark mode is optional.
            }

            try
            {
                SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
                SendMessage(control.Handle, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
                control.Invalidate();
            }
            catch
            {
                // Native dark scrollbars are a visual enhancement. Keep UI usable if the OS rejects it.
            }
        }

        private static void ApplyNativeAppMode()
        {
            try
            {
                SetPreferredAppMode(IsDark ? PreferredAppMode.AllowDark : PreferredAppMode.ForceLight);
                FlushMenuThemes();
            }
            catch
            {
                // These uxtheme ordinal APIs are unavailable on older Windows builds.
            }
        }
    }
}
