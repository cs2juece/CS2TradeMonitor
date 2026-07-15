using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class ItemMonitorSearchPanelController
    {
        internal const int CandidateDisplayLimit = 30;

        private readonly LiteUnderlineInput _searchInput;
        private readonly LiteButton _searchButton;
        private readonly LiteButton _addButton;
        private readonly Label _addHintLabel;
        private readonly ListBox _candidateList;
        private readonly Label _searchStatus;
        private readonly List<CandidateListItem> _candidateItems = new List<CandidateListItem>();

        public ItemMonitorSearchPanelController(ItemMonitorSearchCard searchCard)
        {
            ArgumentNullException.ThrowIfNull(searchCard);

            _searchInput = searchCard.SearchInput;
            _searchButton = searchCard.SearchButton;
            _addButton = searchCard.AddButton;
            _addHintLabel = searchCard.AddHintLabel;
            _candidateList = searchCard.CandidateList;
            _searchStatus = searchCard.SearchStatus;
        }

        public string Keyword => _searchInput.Inner.Text.Trim();

        public CandidateListItem? SelectedCandidate => _candidateList.SelectedItem as CandidateListItem;

        public int CandidateCount => _candidateItems.Count;

        public void ShowCandidateDropdown()
        {
            _candidateList.Visible = true;
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
                .Select(candidate => new CandidateListItem(candidate, ItemMonitorPageModel.GetCandidateDisplay(candidate))));

            _candidateList.BeginUpdate();
            try
            {
                _candidateList.Items.Clear();
                foreach (CandidateListItem item in _candidateItems)
                    _candidateList.Items.Add(item);

                _candidateList.Visible = !string.IsNullOrWhiteSpace(keyword);
                _candidateList.SelectedIndex = -1;
            }
            finally
            {
                _candidateList.EndUpdate();
            }

            ItemMonitorSearchStatusViewModel status = BuildSearchResultStatus(
                _candidateItems.Count,
                allResults.Count,
                hasApiKey,
                localDatabaseAvailable);
            SetStatus(status.Text, status.Warn);
        }

        public void ClearCandidateItems(bool keepDropdownVisible)
        {
            _candidateItems.Clear();
            _candidateList.Items.Clear();
            _candidateList.Visible = keepDropdownVisible;
            UpdateAddButtonState(static _ => false);
        }

        public void ClearDropdown(bool clearText)
        {
            _candidateItems.Clear();
            _candidateList.Items.Clear();
            _candidateList.Visible = false;

            if (clearText)
                _searchInput.Inner.Text = "";

            UpdateAddButtonState(static _ => false);
        }

        public void SetBusy(bool busy)
        {
            _searchButton.Enabled = !busy;
            _addButton.Enabled = !busy && SelectedCandidate is not null;
            if (busy)
                _addButton.Text = "搜索中";
            _addHintLabel.Text = busy ? "正在搜索..." : BuildAddHintText();
        }

        public void UpdateAddButtonState(Func<CandidateListItem, bool> isDuplicate)
        {
            ArgumentNullException.ThrowIfNull(isDuplicate);

            if (SelectedCandidate is CandidateListItem item && isDuplicate(item))
            {
                _addButton.Text = "已添加";
                _addButton.Enabled = false;
                _addHintLabel.Text = "已在监控";
                return;
            }

            bool canAdd = SelectedCandidate is not null;
            _addButton.Text = canAdd ? "添加" : "先选候选";
            _addButton.Enabled = canAdd;
            _addHintLabel.Text = BuildAddHintText();
        }

        public void SetStatus(string text, bool warn)
        {
            _searchStatus.Text = text;
            _searchStatus.ForeColor = ItemMonitorPageControls.SearchStatusColor(warn);
        }

        public static ItemMonitorSearchStatusViewModel BuildSearchResultStatus(
            int candidateCount,
            bool hasApiKey,
            bool localDatabaseAvailable = true)
            => BuildSearchResultStatus(candidateCount, candidateCount, hasApiKey, localDatabaseAvailable);

        public static ItemMonitorSearchStatusViewModel BuildSearchResultStatus(
            int displayedCount,
            int totalCount,
            bool hasApiKey,
            bool localDatabaseAvailable = true)
        {
            displayedCount = Math.Max(0, displayedCount);
            totalCount = Math.Max(0, totalCount);
            if (!localDatabaseAvailable)
            {
                return new ItemMonitorSearchStatusViewModel(
                    totalCount <= 0
                        ? "未找到匹配单品（本地饰品库不可用，已仅使用远程搜索）。"
                        : $"找到 {totalCount} 个候选；本地饰品库不可用，远程结果可能不完整。",
                    Warn: true);
            }

            if (totalCount <= 0)
            {
                return new ItemMonitorSearchStatusViewModel(
                    hasApiKey
                        ? "未找到匹配单品。"
                        : "未找到匹配单品（未配置 API Key 时会使用本地饰品库）。",
                    Warn: true);
            }

            if (totalCount > displayedCount)
            {
                return new ItemMonitorSearchStatusViewModel(
                    $"找到 {totalCount} 个候选，仅显示前 {displayedCount} 个，请缩小关键词。",
                    Warn: true);
            }

            return new ItemMonitorSearchStatusViewModel(
                $"找到 {displayedCount} 个候选，请从下拉列表选择。",
                Warn: false);
        }

        private string BuildAddHintText()
        {
            if (SelectedCandidate is not null)
                return "";

            string keyword = Keyword;
            if (string.IsNullOrWhiteSpace(keyword))
                return "输入后选择候选";

            return _candidateItems.Count > 0 ? "请先选择候选" : "";
        }
    }

    internal sealed record ItemMonitorSearchStatusViewModel(string Text, bool Warn);
}
