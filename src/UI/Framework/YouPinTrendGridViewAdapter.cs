using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinTrendGridViewAdapter : IDisposable
    {
        private readonly Func<float> _getFontSize;
        private readonly Func<Color> _getTextColor;
        private readonly Func<Color> _getSubTextColor;
        private readonly Func<double, bool, Color> _getTrendColor;
        private Font? _defaultFont;
        private Font? _headerFont;
        private Font? _nameFont;
        private Font? _valueFont;
        private float _fontCacheSize = -1f;

        public YouPinTrendGridViewAdapter(
            Func<float> getFontSize,
            Func<Color> getTextColor,
            Func<Color> getSubTextColor,
            Func<double, bool, Color> getTrendColor)
        {
            _getFontSize = getFontSize ?? throw new ArgumentNullException(nameof(getFontSize));
            _getTextColor = getTextColor ?? throw new ArgumentNullException(nameof(getTextColor));
            _getSubTextColor = getSubTextColor ?? throw new ArgumentNullException(nameof(getSubTextColor));
            _getTrendColor = getTrendColor ?? throw new ArgumentNullException(nameof(getTrendColor));
        }

        public DataGridView CreateGrid(
            Func<IReadOnlyList<TrendGridRow>> getRows,
            DataGridViewCellMouseEventHandler columnHeaderMouseClick,
            MouseEventHandler mouseWheel,
            Action updateScrollBar)
        {
            ArgumentNullException.ThrowIfNull(getRows);
            ArgumentNullException.ThrowIfNull(columnHeaderMouseClick);
            ArgumentNullException.ThrowIfNull(mouseWheel);
            ArgumentNullException.ThrowIfNull(updateScrollBar);

            var grid = new DataGridView
            {
                Height = UIUtils.S(430),
                Dock = DockStyle.Top,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeight = UIUtils.S(38),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EnableHeadersVisualStyles = false,
                MultiSelect = false,
                ScrollBars = ScrollBars.None,
                VirtualMode = true,
                RowHeadersVisible = false,
                RowTemplate = { Height = UIUtils.S(42) },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            grid.Columns.Add(CreateTextColumn("Name", "饰品", 34, DataGridViewContentAlignment.MiddleLeft));
            grid.Columns.Add(CreateTextColumn("Meta", "数量 / 购入价", 24, DataGridViewContentAlignment.MiddleLeft));
            grid.Columns.Add(CreateTextColumn("Price", "市场价", 14, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(CreateTextColumn("Percent", "涨跌幅", 14, DataGridViewContentAlignment.MiddleRight));
            grid.Columns.Add(CreateTextColumn("Delta", "涨跌", 14, DataGridViewContentAlignment.MiddleRight));
            grid.CellValueNeeded += (_, e) => ApplyCellValue(grid, getRows(), e);
            grid.CellFormatting += (_, e) => ApplyCellFormatting(grid, getRows(), e);
            grid.ColumnHeaderMouseClick += columnHeaderMouseClick;
            grid.MouseWheel += mouseWheel;
            grid.Scroll += (_, args) =>
            {
                using (UiJankProfiler.Measure("YouPinInventoryTrend.GridScrollEvent", $"Type={args.ScrollOrientation}; Rows={grid.RowCount}", thresholdMs: 1))
                {
                    updateScrollBar();
                }
            };
            grid.Resize += (_, __) => updateScrollBar();
            return grid;
        }

        public void ApplyTheme(Panel? gridHost, ThemedVerticalScrollBar? gridScrollBar, DataGridView? grid)
        {
            if (gridHost != null)
                gridHost.BackColor = UIColors.CardBg;
            gridScrollBar?.Invalidate();
            if (grid == null)
                return;

            EnsureFonts();
            Color textColor = _getTextColor();
            Color subTextColor = _getSubTextColor();
            grid.BackgroundColor = UIColors.CardBg;
            grid.GridColor = UIColors.Border;
            grid.RowsDefaultCellStyle.BackColor = UIColors.CardBg;
            grid.RowsDefaultCellStyle.ForeColor = textColor;
            grid.RowsDefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
            grid.RowsDefaultCellStyle.SelectionForeColor = textColor;
            grid.AlternatingRowsDefaultCellStyle.BackColor = UIColors.IsDark
                ? Color.FromArgb(30, 35, 43)
                : Color.FromArgb(250, 250, 250);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = textColor;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = textColor;
            grid.DefaultCellStyle.BackColor = UIColors.CardBg;
            grid.DefaultCellStyle.ForeColor = textColor;
            grid.DefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
            grid.DefaultCellStyle.SelectionForeColor = textColor;
            grid.DefaultCellStyle.Font = _defaultFont;
            grid.ColumnHeadersDefaultCellStyle.BackColor = UIColors.GroupHeader;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = subTextColor;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UIColors.GroupHeader;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = subTextColor;
            grid.ColumnHeadersDefaultCellStyle.Font = _headerFont;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.DefaultCellStyle.BackColor = UIColors.CardBg;
                column.DefaultCellStyle.ForeColor = textColor;
                column.DefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
                column.DefaultCellStyle.SelectionForeColor = textColor;
            }

            if (grid.VirtualMode)
                return;

            foreach (DataGridViewRow row in grid.Rows)
            {
                row.DefaultCellStyle.BackColor = row.Index % 2 == 0
                    ? UIColors.CardBg
                    : grid.AlternatingRowsDefaultCellStyle.BackColor;
                row.DefaultCellStyle.ForeColor = textColor;
                row.DefaultCellStyle.SelectionBackColor = UIColors.NavSelected;
                row.DefaultCellStyle.SelectionForeColor = textColor;
            }
        }

        public void UpdateSortGlyphs(DataGridView? grid, string sortColumn, bool sortDescending)
        {
            if (grid == null)
                return;

            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.HeaderText = GetColumnBaseHeader(column.Name);
                bool isCurrent = string.Equals(column.Name, sortColumn, StringComparison.OrdinalIgnoreCase);
                column.HeaderCell.SortGlyphDirection =
                    isCurrent
                        ? (sortDescending ? SortOrder.Descending : SortOrder.Ascending)
                        : SortOrder.None;
            }
        }

        internal static object GetCellValue(TrendGridRow item, string columnName)
        {
            return columnName switch
            {
                "Name" => item.Name,
                "Meta" => item.MetaText,
                "Price" => item.PriceText,
                "Percent" => item.PercentText,
                "Delta" => item.DeltaText,
                _ => string.Empty
            };
        }

        internal static string GetColumnBaseHeader(string columnName)
        {
            return columnName switch
            {
                "Meta" => "数量 / 购入价",
                "Price" => "市场价",
                "Percent" => "涨跌幅",
                "Delta" => "涨跌",
                _ => "饰品"
            };
        }

        internal static bool IsSortableColumn(string columnName)
        {
            return columnName is "Meta" or "Price" or "Percent" or "Delta";
        }

        internal static int GetColumnMinimumLogicalWidth(string name)
        {
            return name switch
            {
                "Name" => 220,
                "Meta" => 150,
                "Delta" => 92,
                _ => 88
            };
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, float fillWeight, DataGridViewContentAlignment alignment)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = fillWeight,
                SortMode = IsSortableColumn(name)
                    ? DataGridViewColumnSortMode.Programmatic
                    : DataGridViewColumnSortMode.NotSortable
            };
            column.DefaultCellStyle.Alignment = alignment;
            column.HeaderCell.Style.Alignment = alignment;
            column.MinimumWidth = UIUtils.S(GetColumnMinimumLogicalWidth(name));
            if (alignment == DataGridViewContentAlignment.MiddleRight)
            {
                column.DefaultCellStyle.Padding = new Padding(0, 0, UIUtils.S(name == "Delta" ? 18 : 10), 0);
                column.HeaderCell.Style.Padding = new Padding(0, 0, UIUtils.S(name == "Delta" ? 18 : 10), 0);
            }
            return column;
        }

        private void ApplyCellValue(DataGridView grid, IReadOnlyList<TrendGridRow> rows, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex < 0)
                return;

            e.Value = GetCellValue(rows[e.RowIndex], grid.Columns[e.ColumnIndex].Name);
        }

        private void ApplyCellFormatting(DataGridView grid, IReadOnlyList<TrendGridRow> rows, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= rows.Count || e.ColumnIndex < 0 || e.CellStyle is null)
                return;

            EnsureFonts();
            var item = rows[e.RowIndex];
            Color textColor = _getTextColor();
            e.CellStyle.BackColor = e.RowIndex % 2 == 0
                ? UIColors.CardBg
                : grid.AlternatingRowsDefaultCellStyle.BackColor;
            e.CellStyle.ForeColor = textColor;
            e.CellStyle.SelectionBackColor = UIColors.NavSelected;
            e.CellStyle.SelectionForeColor = textColor;

            switch (grid.Columns[e.ColumnIndex].Name)
            {
                case "Name":
                    e.CellStyle.Font = _nameFont;
                    break;
                case "Meta":
                    e.CellStyle.ForeColor = _getSubTextColor();
                    break;
                case "Price":
                    e.CellStyle.Font = _valueFont;
                    break;
                case "Percent":
                case "Delta":
                    e.CellStyle.ForeColor = _getTrendColor(item.Delta, item.HasComparison);
                    e.CellStyle.Font = _valueFont;
                    break;
            }
        }

        private void EnsureFonts()
        {
            float size = _getFontSize();
            if (_defaultFont != null && Math.Abs(_fontCacheSize - size) < 0.01f)
                return;

            DisposeFonts();
            _fontCacheSize = size;
            _defaultFont = new Font("Microsoft YaHei UI", size, FontStyle.Regular);
            _headerFont = new Font("Microsoft YaHei UI", size, FontStyle.Bold);
            _nameFont = new Font("Microsoft YaHei UI", size, FontStyle.Bold);
            _valueFont = new Font("Microsoft YaHei UI", Math.Min(18f, size + 0.5f), FontStyle.Bold);
        }

        private void DisposeFonts()
        {
            _defaultFont?.Dispose();
            _headerFont?.Dispose();
            _nameFont?.Dispose();
            _valueFont?.Dispose();
            _defaultFont = null;
            _headerFont = null;
            _nameFont = null;
            _valueFont = null;
            _fontCacheSize = -1f;
        }

        public void Dispose()
        {
            DisposeFonts();
        }
    }
}
