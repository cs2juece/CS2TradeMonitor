using System;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryTrendToolbar
    {
        public YouPinInventoryTrendToolbar(
            Panel row,
            LiteUnderlineInput searchInput,
            LiteComboBox filterCombo)
        {
            Row = row ?? throw new ArgumentNullException(nameof(row));
            SearchInput = searchInput ?? throw new ArgumentNullException(nameof(searchInput));
            FilterCombo = filterCombo ?? throw new ArgumentNullException(nameof(filterCombo));
        }

        public Panel Row { get; }

        public LiteUnderlineInput SearchInput { get; }

        public LiteComboBox FilterCombo { get; }
    }

    internal static class YouPinInventoryTrendToolbarFactory
    {
        public static YouPinInventoryTrendToolbar Create(Action scheduleFilterRefresh)
        {
            ArgumentNullException.ThrowIfNull(scheduleFilterRefresh);

            var row = new Panel
            {
                Height = UIUtils.S(58),
                Dock = DockStyle.Top,
                BackColor = UIColors.CardBg
            };

            var searchInput = new LiteUnderlineInput("", "", "", 300, null, HorizontalAlignment.Left)
            {
                Placeholder = "请输入饰品关键词"
            };
            searchInput.Inner.TextChanged += (_, __) => scheduleFilterRefresh();
            searchInput.Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Escape)
                    return;

                searchInput.Inner.Text = string.Empty;
                e.SuppressKeyPress = true;
            };

            var filterCombo = new LiteComboBox { Width = UIUtils.S(150) };
            filterCombo.AddItem("筛选：全部", "all");
            filterCombo.AddItem("筛选：上涨", "up");
            filterCombo.AddItem("筛选：下跌", "down");
            filterCombo.AddItem("筛选：无估值", "missing-price");
            filterCombo.AddItem("筛选：无购入价", "missing-purchase");
            filterCombo.SelectValue("all");
            filterCombo.Inner.SelectedIndexChanged += (_, __) => scheduleFilterRefresh();

            var clearButton = new LiteButton("清空", false) { Width = UIUtils.S(62), Height = UIUtils.S(30) };
            clearButton.Click += (_, __) =>
            {
                searchInput.Inner.Text = string.Empty;
                filterCombo.SelectValue("all");
                scheduleFilterRefresh();
            };

            row.Controls.Add(searchInput);
            row.Controls.Add(filterCombo);
            row.Controls.Add(clearButton);
            row.Layout += (_, __) =>
            {
                var layout = YouPinInventoryTrendToolbarModel.BuildLayout(
                    row.ClientSize,
                    row.Height,
                    searchInput.Height,
                    filterCombo.Width,
                    filterCombo.Height,
                    clearButton.Width,
                    clearButton.Height);
                searchInput.Bounds = layout.SearchBounds;
                filterCombo.Bounds = layout.FilterBounds;
                clearButton.Bounds = layout.ClearBounds;
            };
            row.Paint += YouPinInventoryTrendUiFactory.PaintBorder;

            return new YouPinInventoryTrendToolbar(row, searchInput, filterCombo);
        }
    }
}
