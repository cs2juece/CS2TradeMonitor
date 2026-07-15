using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinStopProfitLossSpecifiedSearchController
    {
        internal const int CandidateDisplayLimit = 30;

        private readonly Panel _panel;
        private readonly LiteUnderlineInput _input;
        private readonly LiteButton _addButton;
        private readonly ListBox _suggestionList;
        private readonly Label _statusLabel;
        private readonly List<SpecifiedCandidateListItem> _candidateItems = new();

        public YouPinStopProfitLossSpecifiedSearchController(YouPinStopProfitLossSpecifiedSearchBlock block)
        {
            ArgumentNullException.ThrowIfNull(block);

            _panel = block.Panel;
            _input = block.Input;
            _addButton = block.AddButton;
            _suggestionList = block.SuggestionList;
            _statusLabel = block.StatusLabel;
        }

        public string Keyword => _input.Inner.Text.Trim();

        public bool SuggestionsVisible => _suggestionList.Visible;

        public SpecifiedCandidateListItem? SelectedCandidate => _suggestionList.SelectedItem as SpecifiedCandidateListItem;

        public int CandidateCount => _candidateItems.Count;

        public void SetInputText(string text)
        {
            _input.Inner.Text = text ?? string.Empty;
        }

        public void ShowDropdown()
        {
            _suggestionList.Visible = true;
            UpdatePanelHeight(hasCandidates: true);
        }

        public void RenderCandidates(
            IEnumerable<SteamDtSearchCandidate> results,
            string keyword,
            bool hasApiKey,
            bool localDatabaseAvailable = true)
        {
            ArgumentNullException.ThrowIfNull(results);

            List<SteamDtSearchCandidate> allResults = results.ToList();
            _candidateItems.Clear();
            _candidateItems.AddRange(allResults
                .Take(CandidateDisplayLimit)
                .Select(candidate => new SpecifiedCandidateListItem(
                    candidate,
                    YouPinStopProfitLossPageModel.GetCandidateDisplay(candidate))));

            _suggestionList.BeginUpdate();
            try
            {
                _suggestionList.Items.Clear();
                foreach (SpecifiedCandidateListItem item in _candidateItems)
                    _suggestionList.Items.Add(item);

                _suggestionList.Visible = !string.IsNullOrWhiteSpace(keyword);
                _suggestionList.SelectedIndex = -1;
            }
            finally
            {
                _suggestionList.EndUpdate();
            }

            YouPinStopProfitLossSpecifiedSearchStatusViewModel status = BuildSearchResultStatus(
                _candidateItems.Count,
                allResults.Count,
                hasApiKey,
                localDatabaseAvailable);
            SetStatus(status.Text, status.Warn);
            UpdatePanelHeight(hasCandidates: _suggestionList.Visible);
        }

        public void ClearCandidateItems(bool keepDropdownVisible)
        {
            _candidateItems.Clear();
            _suggestionList.Items.Clear();
            _suggestionList.Visible = keepDropdownVisible;
            UpdateAddButtonState();
            UpdatePanelHeight(hasCandidates: keepDropdownVisible);
        }

        public void ClearDropdown(bool clearText)
        {
            _candidateItems.Clear();
            _suggestionList.Items.Clear();
            _suggestionList.Visible = false;

            if (clearText)
                SetInputText(string.Empty);

            UpdateAddButtonState();
            UpdatePanelHeight(hasCandidates: false);
        }

        public void UpdateAddButtonState()
        {
            bool canAdd = _suggestionList.Visible && SelectedCandidate is not null;
            _addButton.Enabled = canAdd;
            _addButton.Text = canAdd ? "添加" : "先选候选";
        }

        public void SetStatus(string text, bool warn)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = warn ? UIColors.TextWarn : UIColors.TextSub;
        }

        public static YouPinStopProfitLossSpecifiedSearchStatusViewModel BuildSearchResultStatus(
            int candidateCount,
            bool hasApiKey,
            bool localDatabaseAvailable = true)
            => BuildSearchResultStatus(candidateCount, candidateCount, hasApiKey, localDatabaseAvailable);

        public static YouPinStopProfitLossSpecifiedSearchStatusViewModel BuildSearchResultStatus(
            int displayedCount,
            int totalCount,
            bool hasApiKey,
            bool localDatabaseAvailable = true)
        {
            displayedCount = Math.Max(0, displayedCount);
            totalCount = Math.Max(0, totalCount);
            if (!localDatabaseAvailable)
            {
                return new YouPinStopProfitLossSpecifiedSearchStatusViewModel(
                    totalCount <= 0
                        ? "未找到匹配单品（本地饰品库不可用，已仅使用远程搜索）。"
                        : $"找到 {totalCount} 个候选；本地饰品库不可用，远程结果可能不完整。",
                    Warn: true);
            }

            if (totalCount <= 0)
            {
                return new YouPinStopProfitLossSpecifiedSearchStatusViewModel(
                    hasApiKey
                        ? "未找到匹配单品。"
                        : "未找到匹配单品（未配置 API Key 时会使用本地饰品库）。",
                    Warn: true);
            }

            if (totalCount > displayedCount)
            {
                return new YouPinStopProfitLossSpecifiedSearchStatusViewModel(
                    $"找到 {totalCount} 个候选，仅显示前 {displayedCount} 个，请缩小关键词。",
                    Warn: true);
            }

            return new YouPinStopProfitLossSpecifiedSearchStatusViewModel(
                $"找到 {displayedCount} 个候选，请从下拉列表选择。",
                Warn: false);
        }

        private void UpdatePanelHeight(bool hasCandidates)
        {
            int targetHeight = YouPinStopProfitLossSpecifiedSearchBlockModel.BuildPanelHeight(hasCandidates);
            if (_panel.Height == targetHeight)
                return;

            _panel.Height = targetHeight;
            _panel.Parent?.PerformLayout();
        }
    }

    internal sealed record YouPinStopProfitLossSpecifiedSearchStatusViewModel(string Text, bool Warn);
}
