using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.Market;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class YouPinStopProfitLossHostPage : FrameworkSettingsHostPage<YouPinStopProfitLossPage>
    {
        public YouPinStopProfitLossHostPage()
            : base(new YouPinStopProfitLossPage(YouPinPageRuntimeServices.Resolve()))
        {
        }
    }

    public sealed class YouPinStopProfitLossPage : FrameworkSettingsPageBase
    {
        private Label? _statusLabel;
        private Label? _lastCheckLabel;
        private Label? _alertCountLabel;
        private YouPinStopProfitLossSpecifiedSearchController? _specifiedSearch;
        private System.Windows.Forms.Timer? _specifiedSearchDebounceTimer;
        private readonly ISteamDtItemService _steamDtItemService;
        private readonly IYouPinInventoryService _inventoryService;
        private long _specifiedSearchRequestVersion;
        private bool _updatingSpecifiedInput;
        private bool _busy;
        private bool _disposed;

        public YouPinStopProfitLossPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinStopProfitLossPage(YouPinPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _steamDtItemService = runtimeServices.SteamDtItems;
            _inventoryService = runtimeServices.Inventory;
        }

        protected override void OnStoreAttached()
        {
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            ConfigureInventoryService();
            RefreshStatus();
        }

        public override void Deactivate()
        {
            StopSpecifiedSearchDebounce();
            base.Deactivate();
        }

        public override void Save()
        {
            base.Save();
            ConfigureInventoryService();
            RefreshStatus();
        }

        private void BuildPage()
        {
            StopSpecifiedSearchDebounce();
            ClearPage();
            CreateTestHelpCard();
            CreateRecentAlertCard();
            CreateConfigCard();
            RefreshStatus();
        }

        private void CreateConfigCard()
        {
            var group = new LiteSettingsGroup("单品止盈/损提醒");
            group.AddHeaderInlineAction(CreateHeaderDescription("按单品价格变化提醒：优先对比悠悠有品涨跌数据，包含出租中的饰品。"));
            group.AddFullItem(CreateInlineStatusBlock());

            group.AddFullItem(CreateTwoColumnRow(
                CreateToggleRow(
                "启用提醒",
                () => Get(nameof(Settings.YouPinStopProfitLossEnabled), false),
                value => Set(nameof(Settings.YouPinStopProfitLossEnabled), value)),

                CreateModeRow()));

            group.AddFullItem(CreateTwoColumnRow(
                CreateNumberRow(
                "观察时间",
                "小时",
                () => Math.Max(1, (int)Math.Round(Get(nameof(Settings.YouPinStopProfitLossWindowMinutes), 180) / 60.0)).ToString(),
                value =>
                {
                    if (int.TryParse(value, out int hours))
                        Set(nameof(Settings.YouPinStopProfitLossWindowMinutes), Math.Clamp(hours <= 0 ? 3 : hours, 1, 168) * 60);
                }),

                CreateNumberRow(
                "提醒冷却",
                "分钟",
                () => Get(nameof(Settings.YouPinStopProfitLossCooldownMinutes), 30).ToString(),
                value =>
                {
                    if (int.TryParse(value, out int minutes))
                        Set(nameof(Settings.YouPinStopProfitLossCooldownMinutes), Math.Clamp(minutes <= 0 ? 30 : minutes, 1, 1440));
                })));

            group.AddFullItem(CreateTwoColumnRow(
                CreateNumberRow(
                "单品止盈阈值",
                "%",
                () => Get(nameof(Settings.YouPinStopProfitPercentThreshold), 30.0).ToString("0.##"),
                value =>
                {
                    if (double.TryParse(value, out double threshold))
                        Set(nameof(Settings.YouPinStopProfitPercentThreshold), Math.Clamp(threshold <= 0 ? 30 : threshold, 0.01, 1000));
                }),

                CreateNumberRow(
                "单品止损阈值",
                "%",
                () => Get(nameof(Settings.YouPinStopLossPercentThreshold), 30.0).ToString("0.##"),
                value =>
                {
                    if (double.TryParse(value, out double threshold))
                        Set(nameof(Settings.YouPinStopLossPercentThreshold), Math.Clamp(threshold <= 0 ? 30 : threshold, 0.01, 1000));
                })));

            group.AddFullItem(CreateToggleRow(
                "只看指定单品",
                () => Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false),
                value => Set(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), value)));

            group.AddFullItem(CreateSpecifiedItemSearchBlock());
            AddHint(group, "只读取价格并提醒，不执行交易动作。");
            AddGroupToPage(group);
        }

        private static Label CreateHeaderDescription(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Height = UIUtils.S(24),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Control CreateSectionTitle(string number, string titleText, string descText)
        {
            var panel = new Panel
            {
                Height = UIUtils.S(52),
                BackColor = Color.Transparent
            };

            var badge = new Label
            {
                Text = number,
                AutoSize = false,
                Width = UIUtils.S(34),
                Height = UIUtils.S(20),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
                ForeColor = UIColors.Primary,
                BackColor = UIColors.ControlBg
            };
            badge.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(80, UIColors.Primary));
                e.Graphics.DrawRectangle(pen, 0, 0, badge.Width - 1, badge.Height - 1);
            };

            var title = new Label
            {
                Text = titleText,
                AutoSize = false,
                Height = UIUtils.S(20),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };

            var desc = new Label
            {
                Text = descText,
                AutoSize = false,
                Height = UIUtils.S(18),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };

            panel.Controls.Add(badge);
            panel.Controls.Add(title);
            panel.Controls.Add(desc);
            panel.Layout += (_, __) =>
            {
                int top = UIUtils.S(16);
                badge.SetBounds(0, top, badge.Width, badge.Height);
                int textLeft = badge.Right + UIUtils.S(10);
                title.SetBounds(textLeft, UIUtils.S(12), Math.Max(1, panel.Width - textLeft), UIUtils.S(20));
                desc.SetBounds(textLeft, title.Bottom + UIUtils.S(2), Math.Max(1, panel.Width - textLeft), UIUtils.S(18));
            };

            return panel;
        }

        private Control CreateToggleRow(string title, Func<bool> get, Action<bool> set)
        {
            var check = new LiteCheck(get(), LanguageManager.T("Menu.Enable"));
            check.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                set(check.Checked);
                ConfigureInventoryService();
                RefreshStatus();
            };
            RegisterRefresh(() => check.Checked = get());
            RegisterSave(() => set(check.Checked));
            return CreateSettingRow(title, check, UIUtils.S(120));
        }

        private Control CreateNumberRow(string title, string unit, Func<string> get, Action<string> set)
        {
            var input = new LiteNumberInput(get(), unit, string.Empty, 110);
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                set(input.Inner.Text);
                RefreshStatus();
            };
            RegisterRefresh(() => input.Inner.Text = get());
            RegisterSave(() => set(input.Inner.Text));
            return CreateSettingRow(title, input, UIUtils.S(160));
        }

        private Control CreateInlineStatusBlock()
        {
            var block = YouPinStopProfitLossStatusBlockFactory.Create(button => RunScanAsync(button, useMock: false));
            _statusLabel = block.StatusLabel;
            _lastCheckLabel = block.LastCheckLabel;
            _alertCountLabel = block.AlertCountLabel;
            return block.Panel;
        }

        private Control CreateSpecifiedItemSearchBlock()
        {
            var block = YouPinStopProfitLossSpecifiedSearchBlockFactory.Create(
                onTextChanged: () =>
                {
                    if (_updatingSpecifiedInput || IsUpdatingControls)
                        return;

                    ScheduleSpecifiedCandidateSearch();
                },
                AddSelectedSpecifiedCandidate,
                ClearSpecifiedKeywords,
                UpdateSpecifiedAddButtonState,
                () => ClearSpecifiedCandidateDropdown(clearText: false));

            _specifiedSearch = new YouPinStopProfitLossSpecifiedSearchController(block);

            RegisterRefresh(() =>
            {
                if (_specifiedSearch == null)
                    return;

                _updatingSpecifiedInput = true;
                try
                {
                    _specifiedSearch.SetInputText(string.Empty);
                }
                finally
                {
                    _updatingSpecifiedInput = false;
                }

                ClearSpecifiedCandidateDropdown(clearText: false);
                SetSpecifiedSearchStatus(BuildSpecifiedSearchStatus(), warn: false);
            });
            RegisterSave(() =>
            {
                string normalized = YouPinStopProfitLossPageModel.NormalizeKeywordInput(Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty));
                Set(nameof(Settings.YouPinStopProfitLossSpecifiedItems), normalized);
            });

            SetSpecifiedSearchStatus(BuildSpecifiedSearchStatus(), warn: false);
            return block.Panel;
        }

        private void ScheduleSpecifiedCandidateSearch()
        {
            if (_specifiedSearch == null || _disposed || IsDisposed)
                return;

            string keyword = _specifiedSearch.Keyword;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                Interlocked.Increment(ref _specifiedSearchRequestVersion);
                StopSpecifiedSearchDebounce();
                ClearSpecifiedCandidateDropdown(clearText: false);
                SetSpecifiedSearchStatus(BuildSpecifiedSearchStatus(), warn: false);
                return;
            }

            _specifiedSearch.ShowDropdown();
            SetSpecifiedSearchStatus("正在准备搜索候选...", warn: false);

            _specifiedSearchDebounceTimer ??= new System.Windows.Forms.Timer { Interval = 350 };
            _specifiedSearchDebounceTimer.Tick -= OnSpecifiedSearchDebounceTick;
            _specifiedSearchDebounceTimer.Tick += OnSpecifiedSearchDebounceTick;
            _specifiedSearchDebounceTimer.Stop();
            _specifiedSearchDebounceTimer.Start();
        }

        private async void OnSpecifiedSearchDebounceTick(object? sender, EventArgs e)
        {
            StopSpecifiedSearchDebounce();
            await SearchSpecifiedCandidatesAsync();
        }

        private void StopSpecifiedSearchDebounce()
        {
            _specifiedSearchDebounceTimer?.Stop();
        }

        private async Task SearchSpecifiedCandidatesAsync()
        {
            if (_specifiedSearch == null || _disposed || IsDisposed)
                return;

            CancellationToken cancellationToken = PageToken;
            if (cancellationToken.IsCancellationRequested)
                return;

            string keyword = _specifiedSearch.Keyword;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                Interlocked.Increment(ref _specifiedSearchRequestVersion);
                ClearSpecifiedCandidateDropdown(clearText: false);
                SetSpecifiedSearchStatus(BuildSpecifiedSearchStatus(), warn: false);
                return;
            }

            long requestVersion = Interlocked.Increment(ref _specifiedSearchRequestVersion);
            _specifiedSearch.ShowDropdown();
            SetSpecifiedSearchStatus("正在搜索...", warn: false);
            try
            {
                _steamDtItemService.Configure(GetSteamDtApiKey());
                List<SteamDtSearchCandidate> results = await _steamDtItemService.SearchItemsAsync(keyword);
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;
                if (requestVersion != Interlocked.Read(ref _specifiedSearchRequestVersion)
                    || _specifiedSearch == null
                    || !string.Equals(keyword, _specifiedSearch.Keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                bool hasKey = !string.IsNullOrWhiteSpace(GetSteamDtApiKey());
                _specifiedSearch.RenderCandidates(
                    results,
                    keyword,
                    hasKey,
                    _steamDtItemService.IsLocalItemDatabaseAvailable);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested || _disposed || IsDisposed)
                    return;

                _specifiedSearch?.ClearCandidateItems(keepDropdownVisible: !string.IsNullOrWhiteSpace(_specifiedSearch?.Keyword));
                SetSpecifiedSearchStatus("搜索失败：" + ex.Message, warn: true);
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested && !_disposed && !IsDisposed)
                {
                    UpdateSpecifiedAddButtonState();
                }
            }
        }

        private void ClearSpecifiedCandidateDropdown(bool clearText)
        {
            _specifiedSearch?.ClearDropdown(clearText);
        }

        private void SetSpecifiedSearchStatus(string text, bool warn)
        {
            _specifiedSearch?.SetStatus(text, warn);
        }

        private string BuildSpecifiedSearchStatus()
        {
            return YouPinStopProfitLossStatusPresenter.BuildSpecifiedSearchStatus(
                Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty));
        }

        private void UpdateSpecifiedAddButtonState()
        {
            _specifiedSearch?.UpdateAddButtonState();
        }

        private void AddSelectedSpecifiedCandidate()
        {
            if (_specifiedSearch?.SelectedCandidate is not SpecifiedCandidateListItem selected)
            {
                SetSpecifiedSearchStatus("请先从下拉候选中选择单品。", warn: true);
                UpdateSpecifiedAddButtonState();
                return;
            }

            AddSpecifiedKeyword(YouPinStopProfitLossPageModel.GetCandidateKeyword(selected.Candidate));
        }

        private void ClearSpecifiedKeywords()
        {
            Set(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty);
            ConfigureInventoryService();

            _updatingSpecifiedInput = true;
            try
            {
                _specifiedSearch?.SetInputText(string.Empty);
            }
            finally
            {
                _updatingSpecifiedInput = false;
            }

            ClearSpecifiedCandidateDropdown(clearText: false);
            SetSpecifiedSearchStatus(BuildSpecifiedSearchStatus(), warn: false);
            RefreshStatus();
        }

        private void AddSpecifiedKeyword(string value)
        {
            if (_specifiedSearch == null)
                return;

            value = YouPinStopProfitLossPageModel.CleanCandidateName(value);
            if (string.IsNullOrWhiteSpace(value))
                return;

            var existing = YouPinStopProfitLossPageModel.SplitSpecifiedItems(Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty)).ToList();
            if (!existing.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
                existing.Add(value);

            string normalized = string.Join(", ", existing);
            Set(nameof(Settings.YouPinStopProfitLossSpecifiedItems), normalized);
            ConfigureInventoryService();

            _updatingSpecifiedInput = true;
            try
            {
                _specifiedSearch.SetInputText(string.Empty);
            }
            finally
            {
                _updatingSpecifiedInput = false;
            }

            ClearSpecifiedCandidateDropdown(clearText: false);
            SetSpecifiedSearchStatus(BuildSpecifiedSearchStatus(), warn: false);
            RefreshStatus();
        }

        private string GetSteamDtApiKey()
        {
            return Get(nameof(Settings.SteamDtApiKey), string.Empty);
        }

        private Control CreateModeRow()
        {
            var combo = new LiteComboBox { Width = UIUtils.S(180) };
            combo.AddItem("左下角气泡", ((int)YouPinSaleReminderNotificationMode.Bubble).ToString());
            combo.AddItem("提示音", ((int)YouPinSaleReminderNotificationMode.Sound).ToString());
            combo.AddItem("气泡 + 提示音", ((int)YouPinSaleReminderNotificationMode.BubbleAndSound).ToString());
            combo.AddItem("静默", ((int)YouPinSaleReminderNotificationMode.Silent).ToString());

            string GetSelectedValue()
            {
                var mode = Get(nameof(Settings.YouPinStopProfitLossNotificationMode), YouPinSaleReminderNotificationMode.BubbleAndSound);
                return ((int)mode).ToString();
            }

            void SaveSelectedValue()
            {
                if (int.TryParse(combo.SelectedValue, out int value) && Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), value))
                    Set(nameof(Settings.YouPinStopProfitLossNotificationMode), (YouPinSaleReminderNotificationMode)value);
            }

            combo.SelectValue(GetSelectedValue());
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                SaveSelectedValue();
                ConfigureInventoryService();
            };
            RegisterRefresh(() => combo.SelectValue(GetSelectedValue()));
            RegisterSave(SaveSelectedValue);
            return CreateSettingRow("提醒方式", combo, UIUtils.S(220));
        }

        private static Control CreateTwoColumnRow(Control left, Control right)
        {
            var row = new Panel
            {
                Height = Math.Max(left.Height, right.Height),
                BackColor = Color.Transparent
            };

            row.Controls.Add(left);
            row.Controls.Add(right);
            row.Layout += (_, __) =>
            {
                int gap = UIUtils.S(28);
                bool stacked = row.ClientSize.Width < UIUtils.S(760);
                int desiredHeight = stacked
                    ? left.Height + right.Height + UIUtils.S(8)
                    : Math.Max(left.Height, right.Height);
                if (Math.Abs(row.Height - desiredHeight) > UIUtils.S(1))
                    row.Height = desiredHeight;

                if (stacked)
                {
                    left.SetBounds(0, 0, Math.Max(1, row.ClientSize.Width), left.Height);
                    right.SetBounds(0, left.Bottom + UIUtils.S(8), Math.Max(1, row.ClientSize.Width), right.Height);
                    return;
                }

                int width = Math.Max(1, (row.ClientSize.Width - gap) / 2);
                int leftTop = Math.Max(0, (row.Height - left.Height) / 2);
                int rightTop = Math.Max(0, (row.Height - right.Height) / 2);
                left.SetBounds(0, leftTop, width, left.Height);
                right.SetBounds(width + gap, rightTop, Math.Max(1, row.ClientSize.Width - width - gap), right.Height);
            };

            return row;
        }

        private static Control CreateSettingRow(string title, Control editor, int editorWidth)
        {
            var row = new Panel { Height = UIUtils.S(38), BackColor = Color.Transparent };
            var label = new Label
            {
                Text = title,
                AutoSize = false,
                Width = UIUtils.S(220),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            editor.Width = editorWidth;
            row.Controls.Add(label);
            row.Controls.Add(editor);
            row.Layout += (_, __) =>
            {
                int mid = row.Height / 2;
                int gap = UIUtils.S(12);
                int targetEditorWidth = Math.Min(editorWidth, Math.Max(UIUtils.S(96), row.Width / 2));
                int labelWidth = Math.Min(UIUtils.S(220), Math.Max(UIUtils.S(96), row.Width - targetEditorWidth - gap));
                targetEditorWidth = Math.Min(editorWidth, Math.Max(UIUtils.S(96), row.Width - labelWidth - gap));
                if (editor.Width != targetEditorWidth)
                    editor.Width = targetEditorWidth;

                label.SetBounds(0, 0, labelWidth, row.Height);
                editor.Location = new Point(Math.Max(label.Right + gap, row.Width - editor.Width), mid - editor.Height / 2);
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private Control CreateMockTestRow()
        {
            var actions = new Panel
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(30),
                BackColor = Color.Transparent
            };
            var mockButton = new LiteButton("模拟测试", false) { Width = UIUtils.S(96), Height = UIUtils.S(30) };
            mockButton.Click += async (_, __) => await RunScanAsync(mockButton, true);

            actions.Controls.Add(mockButton);
            actions.Layout += (_, __) =>
            {
                int top = Math.Max(0, (actions.Height - mockButton.Height) / 2);
                mockButton.Location = new Point(0, top);
            };
            return CreateSettingRow("模拟测试", actions, UIUtils.S(120));
        }

        private void CreateTestHelpCard()
        {
            AddGroupToPage(YouPinStopProfitLossActivityCardFactory.CreateTestHelpCard(CreateMockTestRow()));
        }

        private void CreateRecentAlertCard()
        {
            var state = _inventoryService.GetStopProfitLossState();
            AddGroupToPage(YouPinStopProfitLossActivityCardFactory.CreateRecentAlertCard(state.RecentAlerts));
        }

        private async Task RunScanAsync(LiteButton button, bool useMock)
        {
            if (_busy || Config == null)
                return;

            Save();
            _busy = true;
            string oldText = button.Text;
            button.Enabled = false;
            button.Text = "扫描中...";
            try
            {
                _inventoryService.Configure(Config);
                await _inventoryService.FetchNowAsync(useMock);
                RefreshData();
            }
            finally
            {
                if (!button.IsDisposed)
                {
                    button.Text = oldText;
                    button.Enabled = true;
                }
                _busy = false;
            }
        }

        private void RefreshData()
        {
            Container.SuspendLayout();
            try
            {
                BuildPage();
            }
            finally
            {
                Container.ResumeLayout(true);
            }
        }

        private void RefreshStatus()
        {
            var state = _inventoryService.GetStopProfitLossState();
            var trendState = _inventoryService.GetTrendState();
            var viewModel = YouPinStopProfitLossStatusPresenter.Build(
                state,
                trendState,
                Get(nameof(Settings.YouPinStopProfitLossEnabled), false),
                Get(nameof(Settings.YouPinStopProfitLossOnlySpecifiedItems), false),
                Get(nameof(Settings.YouPinStopProfitLossSpecifiedItems), string.Empty));

            ApplyStatusLabel(_statusLabel, viewModel.StatusText, viewModel.StatusTone);
            ApplyStatusLabel(_lastCheckLabel, viewModel.LastCheckText, viewModel.LastCheckTone);
            ApplyStatusLabel(_alertCountLabel, viewModel.AlertCountText, viewModel.AlertCountTone);

            if (_specifiedSearch != null && !_specifiedSearch.SuggestionsVisible)
                SetSpecifiedSearchStatus(viewModel.CandidateStatusText, viewModel.CandidateStatusWarn);
        }

        private static void ApplyStatusLabel(Label? label, string text, YouPinStopProfitLossStatusTone tone)
        {
            if (label == null)
                return;

            label.Text = text;
            label.ForeColor = ResolveStatusColor(tone);
        }

        private static Color ResolveStatusColor(YouPinStopProfitLossStatusTone tone)
        {
            return tone switch
            {
                YouPinStopProfitLossStatusTone.Ok => UIColors.Positive,
                YouPinStopProfitLossStatusTone.Info => UIColors.IsDark ? Color.FromArgb(110, 178, 255) : UIColors.Primary,
                YouPinStopProfitLossStatusTone.Warn => UIColors.IsDark ? Color.FromArgb(255, 205, 92) : Color.FromArgb(176, 102, 0),
                YouPinStopProfitLossStatusTone.Off => UIColors.IsDark ? Color.FromArgb(255, 132, 96) : Color.FromArgb(190, 64, 45),
                YouPinStopProfitLossStatusTone.Critical => UIColors.TextCrit,
                _ => UIColors.TextSub
            };
        }

        private void ConfigureInventoryService()
        {
            if (Config != null)
                _inventoryService.Configure(Config);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                StopSpecifiedSearchDebounce();
                if (_specifiedSearchDebounceTimer != null)
                {
                    _specifiedSearchDebounceTimer.Tick -= OnSpecifiedSearchDebounceTick;
                    _specifiedSearchDebounceTimer.Dispose();
                    _specifiedSearchDebounceTimer = null;
                }
            }

            base.Dispose(disposing);
        }
    }
}
