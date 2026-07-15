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

    public class LiteActionRow : Panel
    {
        public Label Label { get; private set; }
        public Control RightControl { get; private set; }
        private bool _inLayout;

        public LiteActionRow(string title, Control rightControl)
        {
            this.Height = UIUtils.S(40);
            this.Margin = new Padding(0); // Full width item
            this.Padding = new Padding(0);

            Label = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub, // Slightly gray for descriptions/tips
                TextAlign = ContentAlignment.MiddleLeft
            };

            RightControl = rightControl;

            this.Controls.Add(RightControl);
            this.Controls.Add(Label);

            PositionChildren();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PositionChildren();
        }

        private void PositionChildren()
        {
            if (Label == null || RightControl == null)
                return;

            if (_inLayout) return;
            _inLayout = true;
            try
            {
                int mid = Height / 2;
                LiteLayoutHelpers.SetLocationIfChanged(Label, new Point(UIUtils.S(0), mid - Label.Height / 2)); // Indent slightly

                // Position RightControl on the right.
                if (RightControl.Dock != DockStyle.Fill && RightControl.Dock != DockStyle.Top && RightControl.Dock != DockStyle.Bottom)
                {
                    LiteLayoutHelpers.SetLocationIfChanged(RightControl, new Point(Width - RightControl.Width - UIUtils.S(5), mid - RightControl.Height / 2));
                }
            }
            finally
            {
                _inLayout = false;
            }
        }
    }


    public class LiteHintRow : Panel
    {
        private readonly Label _label;

        public LiteHintRow(string text, int indent = 0)
        {
            this.Margin = new Padding(0);
            this.Padding = UIUtils.S(new Padding(indent, 3, 5, 3));
            this.AutoSize = false;
            this.Height = UIUtils.S(28);

            _label = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            this.Controls.Add(_label);
            Text = text;
        }

        [AllowNull]
        public override string Text
        {
            get => _label?.Text ?? base.Text;
            set
            {
                base.Text = value;
                if (_label != null)
                    _label.Text = value ?? "";
            }
        }

        public void UseMultiline(int height)
        {
            Height = height;
            _label.TextAlign = ContentAlignment.TopLeft;
            _label.AutoEllipsis = false;
        }
    }


    public static class UIFonts
    {
        public static Font Regular(float size) => new Font("Microsoft YaHei UI", size, FontStyle.Regular);
        public static Font Bold(float size) => new Font("Microsoft YaHei UI", size, FontStyle.Bold);
    }


    // =======================================================================
    // 1. 容器组件
    // =======================================================================

    public class LiteSettingsGroup : Panel
    {
        private TableLayoutPanel _layout;
        private Panel _header; // ★★★ 提升为成员变量
        private Label _titleLabel;
        private int _colTracker = 0;
        private int _nextAddRow;
        private int _nextAddColumn;
        private readonly HashSet<Control> _fullWidthItems = new HashSet<Control>();
        private bool _singleColumn;
        private bool _inHeaderInlineLayout;
        private bool _inResponsiveLayout;

        public Panel Header => _header;
        public Label TitleLabel => _titleLabel;

        public new void SuspendLayout()
        {
            base.SuspendLayout();
            _layout?.SuspendLayout();
        }

        public new void ResumeLayout(bool performLayout)
        {
            _layout?.ResumeLayout(performLayout);
            base.ResumeLayout(performLayout);
        }

        public new void ResumeLayout()
        {
            ResumeLayout(true);
        }

        public LiteSettingsGroup(string title)
        {
            this.AutoSize = true;
            this.Dock = DockStyle.Top;
            this.Padding = new Padding(1);
            this.BackColor = UIColors.Border;
            // ★★★ 修改：Margin 缩放
            this.Margin = new Padding(0, 0, 0, UIUtils.S(8));

            var inner = new Panel { Dock = DockStyle.Fill, BackColor = UIColors.CardBg, AutoSize = true };

            // ★★★ 修改：Height 缩放
            _header = new Panel { Dock = DockStyle.Top, Height = UIUtils.S(38), BackColor = UIColors.GroupHeader, Padding = new Padding(0, 0, UIUtils.S(10), 0) }; // 增加右侧Padding
            // ★★★ 修改：Location 缩放
            _titleLabel = new Label
            {
                Text = title,
                Location = new Point(UIUtils.S(15), UIUtils.S(9)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.TextMain
            };
            _header.Controls.Add(_titleLabel);
            // ★★★ 修改：绘图线坐标动态化 (header.Height - 1)
            _header.Paint += (s, e) => { using (var p = new Pen(UIColors.Border)) e.Graphics.DrawLine(p, 0, _header.Height - 1, _header.Width, _header.Height - 1); };

            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                // ★★★ 修改：Padding 缩放
                Padding = UIUtils.S(new Padding(20, 8, 20, 10)),
                BackColor = UIColors.CardBg
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _layout.SizeChanged += (_, __) => ApplyResponsiveColumns();

            inner.Controls.Add(_layout);
            inner.Controls.Add(_header);
            this.Controls.Add(inner);
        }

        public void AddHeaderAction(Control action)
        {
            // 创建一个包装容器来控制垂直位置和边距
            var wrapper = new Panel
            {
                Dock = DockStyle.Right,
                Width = action.Width + UIUtils.S(10), // 额外间距
                Padding = new Padding(0)
            };

            // 手动垂直居中 (Gap on Left)
            action.Location = new Point(UIUtils.S(10), (_header.Height - action.Height) / 2);

            // ★★★ Fix: Draw Bottom Line in Wrapper ★★★
            wrapper.Paint += (s, e) =>
            {
                using (var p = new Pen(UIColors.Border))
                    e.Graphics.DrawLine(p, 0, wrapper.Height - 1, wrapper.Width, wrapper.Height - 1);
            };

            wrapper.Controls.Add(action);
            _header.Controls.Add(wrapper);

            wrapper.BringToFront(); // 确保在最右侧
        }

        public void AddHeaderInlineAction(Control action)
        {
            void LayoutAction()
            {
                if (_inHeaderInlineLayout) return;
                _inHeaderInlineLayout = true;
                try
                {
                    int rowHeight = Math.Max(action.Height, UIUtils.S(24));
                    int top = Math.Max(0, (_header.Height - rowHeight) / 2);
                    int titleWidth = TextRenderer.MeasureText(_titleLabel.Text, _titleLabel.Font).Width + UIUtils.S(4);
                    if (_titleLabel.AutoSize)
                        _titleLabel.AutoSize = false;
                    if (_titleLabel.TextAlign != ContentAlignment.MiddleLeft)
                        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
                    LiteLayoutHelpers.SetBoundsIfChanged(_titleLabel, UIUtils.S(15), top, titleWidth, rowHeight);

                    int left = _titleLabel.Right + UIUtils.S(12);
                    int right = UIUtils.S(10);
                    int width = Math.Max(1, _header.Width - left - right);
                    LiteLayoutHelpers.SetBoundsIfChanged(action, left, top, width, rowHeight);
                }
                finally
                {
                    _inHeaderInlineLayout = false;
                }
            }

            _header.Controls.Add(action);
            _header.SizeChanged += (_, __) => LayoutAction();
            LayoutAction();
            action.BringToFront();
        }

        public void CollapseBody()
        {
            _layout.Visible = false;
            _layout.Height = 0;
            _layout.Padding = Padding.Empty;
        }

        public void AddItem(Control item)
        {
            _layout.Controls.Add(item);
            item.Dock = DockStyle.Fill;
            // ★★★ 修改：Margin 缩放
            if (_colTracker == 0) { item.Margin = UIUtils.S(new Padding(0, 1, 24, 1)); _colTracker = 1; }
            else { item.Margin = UIUtils.S(new Padding(24, 1, 0, 1)); _colTracker = 0; }
            PlaceNewItem(item, fullWidth: false);
        }

        public void AddFullItem(Control item)
        {
            _layout.Controls.Add(item);
            _fullWidthItems.Add(item);
            item.Dock = DockStyle.Fill;
            item.Margin = new Padding(0, 0, 0, 0);
            _colTracker = 0;
            PlaceNewItem(item, fullWidth: true);
        }

        private void PlaceNewItem(Control item, bool fullWidth)
        {
            int columns = Math.Max(1, _singleColumn ? 1 : _layout.ColumnCount);
            bool spansFullRow = fullWidth || _singleColumn;
            if (spansFullRow && _nextAddColumn != 0)
            {
                _nextAddRow++;
                _nextAddColumn = 0;
            }

            EnsureRowCount(_nextAddRow + 1);
            LiteLayoutHelpers.SetCellIfChanged(_layout, item, _nextAddColumn, _nextAddRow);
            LiteLayoutHelpers.SetColumnSpanIfChanged(_layout, item, spansFullRow ? columns : 1);

            if (fullWidth)
            {
                item.Margin = Padding.Empty;
            }
            else if (_singleColumn)
            {
                item.Margin = UIUtils.S(new Padding(0, 1, 0, 1));
            }
            else if (_nextAddColumn == 0)
            {
                item.Margin = UIUtils.S(new Padding(0, 1, 24, 1));
            }
            else
            {
                item.Margin = UIUtils.S(new Padding(24, 1, 0, 1));
            }

            if (spansFullRow)
            {
                _nextAddRow++;
                _nextAddColumn = 0;
            }
            else
            {
                _nextAddColumn++;
                if (_nextAddColumn >= columns)
                {
                    _nextAddRow++;
                    _nextAddColumn = 0;
                }
            }
        }

        private void EnsureRowCount(int rowCount)
        {
            rowCount = Math.Max(1, rowCount);
            while (_layout.RowStyles.Count < rowCount)
                _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (_layout.RowCount < rowCount)
                _layout.RowCount = rowCount;
        }

        private void ApplyResponsiveColumns()
        {
            if (_layout.IsDisposed || _inResponsiveLayout)
                return;

            bool singleColumn = _layout.ClientSize.Width > 0 && _layout.ClientSize.Width < UIUtils.S(760);
            int columns = singleColumn ? 1 : 2;
            if (_layout.ColumnCount == columns && _singleColumn == singleColumn)
                return;

            _inResponsiveLayout = true;
            _layout.SuspendLayout();
            try
            {
                if (_layout.ColumnCount != columns || _singleColumn != singleColumn)
                {
                    _layout.ColumnStyles.Clear();
                    _layout.ColumnCount = columns;
                    for (int i = 0; i < columns; i++)
                        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
                    _singleColumn = singleColumn;
                }

                int row = 0;
                int column = 0;
                foreach (Control control in _layout.Controls)
                {
                    bool fullWidth = _fullWidthItems.Contains(control);
                    bool spansFullRow = fullWidth || singleColumn;

                    if (spansFullRow && column != 0)
                    {
                        row++;
                        column = 0;
                    }

                    LiteLayoutHelpers.SetCellIfChanged(_layout, control, column, row);
                    LiteLayoutHelpers.SetColumnSpanIfChanged(_layout, control, spansFullRow ? columns : 1);

                    if (fullWidth)
                    {
                        LiteLayoutHelpers.SetMarginIfChanged(control, Padding.Empty);
                    }
                    else if (singleColumn)
                    {
                        LiteLayoutHelpers.SetMarginIfChanged(control, UIUtils.S(new Padding(0, 1, 0, 1)));
                    }
                    else if (column == 0)
                    {
                        LiteLayoutHelpers.SetMarginIfChanged(control, UIUtils.S(new Padding(0, 1, 24, 1)));
                    }
                    else
                    {
                        LiteLayoutHelpers.SetMarginIfChanged(control, UIUtils.S(new Padding(24, 1, 0, 1)));
                    }

                    if (spansFullRow)
                    {
                        row++;
                        column = 0;
                    }
                    else
                    {
                        column++;
                        if (column >= columns)
                        {
                            row++;
                            column = 0;
                        }
                    }
                }

                int rowCount = Math.Max(1, row + (column > 0 ? 1 : 0));
                if (_layout.RowCount != rowCount)
                    _layout.RowCount = rowCount;
                while (_layout.RowStyles.Count < rowCount)
                    _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                while (_layout.RowStyles.Count > rowCount)
                    _layout.RowStyles.RemoveAt(_layout.RowStyles.Count - 1);

                _nextAddRow = row + (column > 0 ? 1 : 0);
                _nextAddColumn = 0;
            }
            finally
            {
                _layout.ResumeLayout(false);
                _inResponsiveLayout = false;
            }
        }

        public LiteHintRow AddHint(string text, int indent = 0)
        {
            var row = new LiteHintRow(text, indent);
            AddFullItem(row);
            return row;
        }

        public void ApplySystemTheme()
        {
            BackColor = UIColors.Border;
            _header.BackColor = UIColors.GroupHeader;
            _titleLabel.ForeColor = UIColors.TextMain;
            _layout.BackColor = UIColors.CardBg;
            foreach (Control innerControl in Controls)
                innerControl.BackColor = UIColors.CardBg;
            Invalidate(true);
        }
    }


    public class LiteSettingsItem : Panel
    {
        public Label Label { get; private set; }
        private readonly Control _control;
        private int _preferredControlWidth;
        private bool _inLayout;

        public LiteSettingsItem(string text, Control ctrl)
        {
            _control = ctrl;
            _preferredControlWidth = ctrl.Width;

            // ★★★ 修改：Height/Margin 缩放
            this.Height = UIUtils.S(40);
            this.Margin = UIUtils.S(new Padding(0, 2, 40, 2));
            Label = new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
            // ★★★ 修改：Height 缩放
            if (ctrl is LiteCheck) ctrl.Height = UIUtils.S(22);
            this.Controls.Add(Label);
            this.Controls.Add(ctrl);
            PositionChildren();
            this.Paint += (s, e) =>
            {
                using (var p = new Pen(UIColors.Border))
                    e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
            };
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PositionChildren();
        }

        private void PositionChildren()
        {
            if (Label == null || _control == null)
                return;

            if (_inLayout) return;
            _inLayout = true;
            try
            {
                _preferredControlWidth = Math.Max(_preferredControlWidth, _control.Width);
                int mid = Height / 2;
                int gap = UIUtils.S(12);
                int minLabelWidth = UIUtils.S(72);
                int measuredLabelWidth = TextRenderer.MeasureText(Label.Text, Label.Font).Width + UIUtils.S(8);
                int maxLabelWidth = Math.Min(UIUtils.S(176), Math.Max(minLabelWidth, Width - UIUtils.S(90) - gap));
                int labelWidth = Math.Clamp(measuredLabelWidth, minLabelWidth, Math.Max(minLabelWidth, maxLabelWidth));
                int minControlWidth = _control is LiteCheck ? _preferredControlWidth : UIUtils.S(90);
                int availableForControl = Math.Max(minControlWidth, Width - labelWidth - gap);
                LiteLayoutHelpers.SetWidthIfChanged(_control, Math.Min(_preferredControlWidth, availableForControl));

                LiteLayoutHelpers.SetSizeIfChanged(Label, new Size(labelWidth, Math.Max(UIUtils.S(20), Label.PreferredHeight)));
                LiteLayoutHelpers.SetLocationIfChanged(Label, new Point(0, mid - Label.Height / 2));
                int controlLeft = Math.Max(0, Math.Min(Width - _control.Width, Label.Right + gap));
                LiteLayoutHelpers.SetLocationIfChanged(_control, new Point(controlLeft, mid - _control.Height / 2));
            }
            finally
            {
                _inLayout = false;
            }
        }
    }


    internal static class LiteLayoutHelpers
    {
        public static void SetLocationIfChanged(Control control, Point location)
        {
            if (control.Location != location)
                control.Location = location;
        }

        public static void SetSizeIfChanged(Control control, Size size)
        {
            if (control.Size != size)
                control.Size = size;
        }

        public static void SetWidthIfChanged(Control control, int width)
        {
            if (control.Width != width)
                control.Width = width;
        }

        public static void SetBoundsIfChanged(Control control, int x, int y, int width, int height)
        {
            if (control.Left != x || control.Top != y || control.Width != width || control.Height != height)
                control.SetBounds(x, y, width, height);
        }

        public static void SetMarginIfChanged(Control control, Padding margin)
        {
            if (control.Margin != margin)
                control.Margin = margin;
        }

        public static void SetCellIfChanged(TableLayoutPanel layout, Control control, int column, int row)
        {
            if (layout.GetColumn(control) != column)
                layout.SetColumn(control, column);
            if (layout.GetRow(control) != row)
                layout.SetRow(control, row);
        }

        public static void SetColumnSpanIfChanged(TableLayoutPanel layout, Control control, int span)
        {
            if (layout.GetColumnSpan(control) != span)
                layout.SetColumnSpan(control, span);
        }
    }



    public class LiteCard : Panel
    {
        public LiteCard() { BackColor = UIColors.CardBg; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top; Padding = new Padding(1); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(UIColors.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }

}
