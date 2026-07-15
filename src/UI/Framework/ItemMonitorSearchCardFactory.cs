using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;
using static CS2TradeMonitor.src.UI.Framework.ItemMonitorPageControls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct ItemMonitorSearchRowLayout(
        Rectangle LabelBounds,
        Rectangle InputBounds,
        Rectangle AddButtonBounds,
        Rectangle AddHintBounds,
        Rectangle ClearButtonBounds,
        Rectangle MoreButtonBounds);

    internal static class ItemMonitorSearchCardModel
    {
        public static ItemMonitorSearchRowLayout BuildInputRowLayout(
            int rowWidth,
            int rowHeight,
            int inputHeight,
            int addButtonWidth,
            int addButtonHeight,
            int clearButtonWidth,
            int clearButtonHeight,
            int moreButtonWidth,
            int moreButtonHeight)
        {
            int gap = UIUtils.S(10);
            int labelWidth = UIUtils.S(70);
            int mid = rowHeight / 2;
            int hintWidth = UIUtils.S(116);
            int rightWidth = addButtonWidth + hintWidth + clearButtonWidth + moreButtonWidth + gap * 4;
            int inputWidth = Math.Max(UIUtils.S(180), rowWidth - labelWidth - rightWidth - gap * 2);

            var labelBounds = new Rectangle(0, mid - UIUtils.S(11), labelWidth, UIUtils.S(22));
            var inputBounds = new Rectangle(labelWidth, mid - inputHeight / 2, inputWidth, inputHeight);
            var addButtonBounds = new Rectangle(inputBounds.Right + gap, mid - addButtonHeight / 2, addButtonWidth, addButtonHeight);
            var addHintBounds = new Rectangle(addButtonBounds.Right + gap, mid - UIUtils.S(11), hintWidth, UIUtils.S(22));
            var clearButtonBounds = new Rectangle(addHintBounds.Right + gap, mid - clearButtonHeight / 2, clearButtonWidth, clearButtonHeight);
            var moreButtonBounds = new Rectangle(clearButtonBounds.Right + gap, mid - moreButtonHeight / 2, moreButtonWidth, moreButtonHeight);

            return new ItemMonitorSearchRowLayout(
                labelBounds,
                inputBounds,
                addButtonBounds,
                addHintBounds,
                clearButtonBounds,
                moreButtonBounds);
        }
    }

    internal sealed class ItemMonitorSearchCard
    {
        public ItemMonitorSearchCard(
            LiteSettingsGroup group,
            LiteUnderlineInput searchInput,
            LiteButton searchButton,
            LiteButton addButton,
            Label addHintLabel,
            ListBox candidateList,
            Label searchStatus)
        {
            Group = group;
            SearchInput = searchInput;
            SearchButton = searchButton;
            AddButton = addButton;
            AddHintLabel = addHintLabel;
            CandidateList = candidateList;
            SearchStatus = searchStatus;
        }

        public LiteSettingsGroup Group { get; }

        public LiteUnderlineInput SearchInput { get; }

        public LiteButton SearchButton { get; }

        public LiteButton AddButton { get; }

        public Label AddHintLabel { get; }

        public ListBox CandidateList { get; }

        public Label SearchStatus { get; }
    }

    internal static class ItemMonitorSearchCardFactory
    {
        public static ItemMonitorSearchCard Create(
            Action scheduleCandidateSearch,
            Func<Task> addSelectedCandidateAsync,
            Action<Control> showSearchMoreMenu,
            Action clearSearch,
            Action clearCandidateDropdown,
            Action updateAddButtonState)
        {
            ArgumentNullException.ThrowIfNull(scheduleCandidateSearch);
            ArgumentNullException.ThrowIfNull(addSelectedCandidateAsync);
            ArgumentNullException.ThrowIfNull(showSearchMoreMenu);
            ArgumentNullException.ThrowIfNull(clearSearch);
            ArgumentNullException.ThrowIfNull(clearCandidateDropdown);
            ArgumentNullException.ThrowIfNull(updateAddButtonState);

            var group = new LiteSettingsGroup("搜索添加单品");
            group.AddFullItem(new LiteHintRow("输入饰品名会自动显示候选；候选只显示中文名、价格和来源，选择候选后再添加到监控。"));

            var row = new Panel { Height = UIUtils.S(48), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var label = CreateRowLabel("关键字");
            var searchInput = new LiteUnderlineInput("", "", "", 260)
            {
                Placeholder = "输入饰品名"
            };
            var addButton = new LiteButton("添加", true) { Width = UIUtils.S(72), Height = UIUtils.S(30), Enabled = false };
            var addHintLabel = CreateTinyLabel("请先选择候选");
            addHintLabel.ForeColor = UIColors.TextSub;
            var moreButton = new LiteButton("更多", false) { Width = UIUtils.S(62), Height = UIUtils.S(30) };
            var clearButton = new LiteButton("清空", false) { Width = UIUtils.S(62), Height = UIUtils.S(30) };
            var candidateList = CreateCandidateList();
            var searchStatus = CreateStatusLabel("");

            searchInput.Inner.TextChanged += (_, __) => scheduleCandidateSearch();
            searchInput.Inner.KeyDown += (_, e) =>
            {
                CandidateListKeyboardHelper.HandleKeyDown(
                    candidateList,
                    e,
                    () => _ = addSelectedCandidateAsync(),
                    clearCandidateDropdown);
            };
            addButton.Click += async (_, __) => await addSelectedCandidateAsync();
            moreButton.Click += (_, __) => showSearchMoreMenu(moreButton);
            clearButton.Click += (_, __) => clearSearch();
            candidateList.SelectedIndexChanged += (_, __) => updateAddButtonState();
            candidateList.DoubleClick += async (_, __) => await addSelectedCandidateAsync();

            row.Controls.Add(label);
            row.Controls.Add(searchInput);
            row.Controls.Add(addButton);
            row.Controls.Add(addHintLabel);
            row.Controls.Add(clearButton);
            row.Controls.Add(moreButton);
            row.Layout += (_, __) =>
            {
                ItemMonitorSearchRowLayout layout = ItemMonitorSearchCardModel.BuildInputRowLayout(
                    row.Width,
                    row.Height,
                    searchInput.Height,
                    addButton.Width,
                    addButton.Height,
                    clearButton.Width,
                    clearButton.Height,
                    moreButton.Width,
                    moreButton.Height);

                label.Bounds = layout.LabelBounds;
                searchInput.Bounds = layout.InputBounds;
                addButton.Bounds = layout.AddButtonBounds;
                addHintLabel.Bounds = layout.AddHintBounds;
                clearButton.Bounds = layout.ClearButtonBounds;
                moreButton.Bounds = layout.MoreButtonBounds;
            };
            row.Paint += PaintBottomLine;

            group.AddFullItem(row);
            group.AddFullItem(candidateList);
            group.AddFullItem(searchStatus);

            return new ItemMonitorSearchCard(
                group,
                searchInput,
                moreButton,
                addButton,
                addHintLabel,
                candidateList,
                searchStatus);
        }

        private static ListBox CreateCandidateList()
        {
            var candidateList = new ListBox
            {
                Height = UIUtils.S(104),
                Dock = DockStyle.Top,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F),
                BackColor = UIColors.CardBg,
                ForeColor = UIColors.TextMain,
                Visible = false
            };
            UIColors.ConfigureThemedListBox(candidateList);
            return candidateList;
        }
    }
}
