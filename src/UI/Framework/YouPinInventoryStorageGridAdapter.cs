using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryStorageGridAdapter : IDisposable
    {
        private readonly DataGridView _grid;
        private readonly Font _defaultFont;
        private readonly Font _headerFont;
        private readonly HashSet<string> _selectedAssetIds = new(StringComparer.Ordinal);
        private IReadOnlyList<YouPinInventoryStorageItem> _items = Array.Empty<YouPinInventoryStorageItem>();
        private bool _disposed;

        public YouPinInventoryStorageGridAdapter()
        {
            _defaultFont = new Font("Microsoft YaHei UI", 9F);
            _headerFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _grid = CreateGrid(_defaultFont, _headerFont);
            _grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueNeeded += (_, args) =>
            {
                if (args.RowIndex < 0 || args.RowIndex >= _items.Count)
                    return;

                YouPinInventoryStorageItem item = _items[args.RowIndex];
                args.Value = args.ColumnIndex switch
                {
                    0 => _selectedAssetIds.Contains(item.AssetId),
                    1 => item.Name,
                    2 => BuildDetails(item),
                    3 => item.MarkPrice > 0 ? $"¥{item.MarkPrice:0.00}" : "—",
                    4 => string.IsNullOrWhiteSpace(item.StatusText) ? "可操作" : item.StatusText,
                    _ => null
                };
            };
            _grid.CellValuePushed += (_, args) =>
            {
                if (args.ColumnIndex != 0 || args.RowIndex < 0 || args.RowIndex >= _items.Count)
                    return;

                SetSelected(_items[args.RowIndex].AssetId, args.Value is true);
                SelectionChanged?.Invoke();
            };
            _grid.CellDoubleClick += (_, args) =>
            {
                if (args.ColumnIndex == 0 || args.RowIndex < 0 || args.RowIndex >= _items.Count)
                    return;

                YouPinInventoryStorageItem item = _items[args.RowIndex];
                SetSelected(item.AssetId, !_selectedAssetIds.Contains(item.AssetId));
                _grid.InvalidateCell(0, args.RowIndex);
                SelectionChanged?.Invoke();
            };
        }

        public event Action? SelectionChanged;

        public DataGridView Grid => _grid;

        public int ItemCount => _items.Count;

        public void Bind(IReadOnlyList<YouPinInventoryStorageItem> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            _items = items;
            _selectedAssetIds.IntersectWith(
                items.Select(item => item.AssetId).Where(assetId => !string.IsNullOrWhiteSpace(assetId)));
            _grid.RowCount = _items.Count;
            _grid.Invalidate();
            SelectionChanged?.Invoke();
        }

        public IReadOnlyList<string> GetSelectedAssetIds()
        {
            return _items
                .Select(item => item.AssetId)
                .Where(assetId => _selectedAssetIds.Contains(assetId))
                .ToArray();
        }

        public void SelectAll(bool selected)
        {
            _selectedAssetIds.Clear();
            if (selected)
            {
                foreach (YouPinInventoryStorageItem item in _items)
                    SetSelected(item.AssetId, true);
            }
            _grid.InvalidateColumn(0);
            SelectionChanged?.Invoke();
        }

        public void ClearSelection()
        {
            SelectAll(false);
            _grid.ClearSelection();
        }

        public void ApplyTheme()
        {
            _grid.BackgroundColor = UIColors.CardBg;
            _grid.GridColor = UIColors.Border;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = UIColors.ControlBg;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = UIColors.TextSub;
            _grid.DefaultCellStyle.BackColor = UIColors.CardBg;
            _grid.DefaultCellStyle.ForeColor = UIColors.TextMain;
            _grid.DefaultCellStyle.SelectionBackColor = UIColors.IsDark
                ? Color.FromArgb(38, 58, 80)
                : Color.FromArgb(230, 243, 255);
            _grid.DefaultCellStyle.SelectionForeColor = UIColors.TextMain;
            _grid.AlternatingRowsDefaultCellStyle.BackColor = UIColors.IsDark
                ? Color.FromArgb(29, 35, 43)
                : Color.FromArgb(249, 251, 253);
            _grid.EnableHeadersVisualStyles = false;
            _grid.Invalidate(true);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _defaultFont.Dispose();
            _headerFont.Dispose();
        }

        private static DataGridView CreateGrid(Font defaultFont, Font headerFont)
        {
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                BackgroundColor = UIColors.CardBg,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = UIUtils.S(38),
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                Dock = DockStyle.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false,
                RowHeadersVisible = false,
                RowTemplate = { Height = UIUtils.S(44) },
                ScrollBars = ScrollBars.Vertical,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ShowCellToolTips = true,
                VirtualMode = true
            };
            grid.DefaultCellStyle.Font = defaultFont;
            grid.DefaultCellStyle.Padding = UIUtils.S(new Padding(8, 2, 8, 2));
            grid.ColumnHeadersDefaultCellStyle.Font = headerFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = UIUtils.S(new Padding(8, 2, 8, 2));
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "",
                Width = UIUtils.S(44),
                MinimumWidth = UIUtils.S(40),
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "饰品",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 46,
                MinimumWidth = UIUtils.S(220),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Details",
                HeaderText = "信息",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 24,
                MinimumWidth = UIUtils.S(140),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Price",
                HeaderText = "参考价",
                Width = UIUtils.S(112),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "状态",
                Width = UIUtils.S(132),
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            return grid;
        }

        private void SetSelected(string assetId, bool selected)
        {
            if (string.IsNullOrWhiteSpace(assetId))
                return;
            if (selected)
                _selectedAssetIds.Add(assetId);
            else
                _selectedAssetIds.Remove(assetId);
        }

        private static string BuildDetails(YouPinInventoryStorageItem item)
        {
            return string.Join(
                " · ",
                new[] { item.ExteriorName, item.IsMerged ? "合并库存" : string.Empty }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }
}
