using System;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinStopProfitLossSpecifiedSearchBlock
    {
        public YouPinStopProfitLossSpecifiedSearchBlock(
            Panel panel,
            LiteUnderlineInput input,
            LiteButton addButton,
            LiteButton clearButton,
            ListBox suggestionList,
            Label statusLabel)
        {
            Panel = panel ?? throw new ArgumentNullException(nameof(panel));
            Input = input ?? throw new ArgumentNullException(nameof(input));
            AddButton = addButton ?? throw new ArgumentNullException(nameof(addButton));
            ClearButton = clearButton ?? throw new ArgumentNullException(nameof(clearButton));
            SuggestionList = suggestionList ?? throw new ArgumentNullException(nameof(suggestionList));
            StatusLabel = statusLabel ?? throw new ArgumentNullException(nameof(statusLabel));
        }

        public Panel Panel { get; }
        public LiteUnderlineInput Input { get; }
        public LiteButton AddButton { get; }
        public LiteButton ClearButton { get; }
        public ListBox SuggestionList { get; }
        public Label StatusLabel { get; }
    }

    internal static class YouPinStopProfitLossSpecifiedSearchBlockFactory
    {
        public static YouPinStopProfitLossSpecifiedSearchBlock Create(
            Action onTextChanged,
            Action addSelectedCandidate,
            Action clearKeywords,
            Action updateAddButtonState,
            Action clearDropdownKeepText)
        {
            ArgumentNullException.ThrowIfNull(onTextChanged);
            ArgumentNullException.ThrowIfNull(addSelectedCandidate);
            ArgumentNullException.ThrowIfNull(clearKeywords);
            ArgumentNullException.ThrowIfNull(updateAddButtonState);
            ArgumentNullException.ThrowIfNull(clearDropdownKeepText);

            var panel = new Panel
            {
                Height = YouPinStopProfitLossSpecifiedSearchBlockModel.BuildPanelHeight(hasCandidates: false),
                BackColor = UIColors.ControlBg,
                Padding = UIUtils.S(new Padding(12, 6, 12, 6))
            };
            var label = new Label
            {
                Text = "搜索指定单品",
                AutoSize = false,
                Width = UIUtils.S(140),
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var input = new LiteUnderlineInput(
                string.Empty,
                string.Empty,
                string.Empty,
                520,
                null,
                HorizontalAlignment.Left);
            input.Placeholder = "输入饰品名";
            input.Inner.TextChanged += (_, __) => onTextChanged();

            var addButton = new LiteButton("添加", true)
            {
                Width = UIUtils.S(72),
                Height = UIUtils.S(30),
                Enabled = false
            };
            addButton.Click += (_, __) => addSelectedCandidate();

            var clearButton = new LiteButton("清空", false)
            {
                Width = UIUtils.S(72),
                Height = UIUtils.S(30)
            };
            clearButton.Click += (_, __) => clearKeywords();

            var suggestionList = new ListBox
            {
                Visible = false,
                IntegralHeight = false,
                Height = UIUtils.S(104),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.CardBg,
                ForeColor = UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            UIColors.ConfigureThemedListBox(suggestionList);
            suggestionList.Click += (_, __) => updateAddButtonState();
            suggestionList.DoubleClick += (_, __) => addSelectedCandidate();
            suggestionList.SelectedIndexChanged += (_, __) => updateAddButtonState();

            input.Inner.KeyDown += (_, e) =>
            {
                CandidateListKeyboardHelper.HandleKeyDown(
                    suggestionList,
                    e,
                    addSelectedCandidate,
                    clearDropdownKeepText);
            };

            var statusLabel = new Label
            {
                AutoSize = false,
                Height = UIUtils.S(20),
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            panel.Controls.Add(label);
            panel.Controls.Add(input);
            panel.Controls.Add(addButton);
            panel.Controls.Add(clearButton);
            panel.Controls.Add(suggestionList);
            panel.Controls.Add(statusLabel);

            panel.Layout += (_, __) =>
            {
                var layout = YouPinStopProfitLossSpecifiedSearchBlockModel.BuildLayout(
                    panel.Width,
                    panel.Padding,
                    input.Height,
                    addButton.Height,
                    clearButton.Height,
                    suggestionList.Visible,
                    UIUtils.S(104));
                label.Bounds = layout.LabelBounds;
                input.Bounds = layout.InputBounds;
                addButton.Bounds = layout.AddButtonBounds;
                clearButton.Bounds = layout.ClearButtonBounds;
                suggestionList.Bounds = layout.SuggestionBounds;
                statusLabel.Bounds = layout.StatusBounds;
            };
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            };

            return new YouPinStopProfitLossSpecifiedSearchBlock(
                panel,
                input,
                addButton,
                clearButton,
                suggestionList,
                statusLabel);
        }
    }
}
