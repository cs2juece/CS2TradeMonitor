using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryStoragePage : FrameworkSettingsPageBase
    {
        private const string QueryPendingRetryKey = "youpin-inventory-storage-query";
        private const int QueryPendingRetryMaxAttempts = 3;
        private static readonly Color AccessibleActionFill = Color.FromArgb(0, 96, 184);

        private readonly IYouPinInventoryStorageService _storageService;
        private readonly YouPinInventoryStorageGridAdapter _gridAdapter = new();
        private readonly UiAsyncRefreshController<StorageRefreshSnapshot> _refreshController;
        private readonly UiDeferredActionScheduler _queryPendingRetry;
        private readonly ToolTip _statusToolTip = new();
        private readonly object _refreshRequestGate = new();
        private YouPinInventoryStorageDirection _direction = YouPinInventoryStorageDirection.Store;
        private StorageRefreshRequest? _latestRefreshRequest;
        private YouPinInventoryStorageViewState? _state;
        private IReadOnlyList<YouPinInventoryStorageItem> _sourceItems = Array.Empty<YouPinInventoryStorageItem>();
        private bool _loading;
        private bool _writePending;
        private bool _updatingUnitSelection;
        private bool _queryPending;
        private bool _hasRefreshError;
        private int _queryPendingRetryAttempt;

        private YouPinCcRoundedPanel _summaryCard = null!;
        private YouPinCcRoundedPanel _inventoryCard = null!;
        private Panel _summaryWrapper = null!;
        private Panel _inventoryWrapper = null!;
        private Label _statusLabel = null!;
        private Label _storableLabel = null!;
        private Label _storedLabel = null!;
        private Label _takeOutLabel = null!;
        private Label _emptyLabel = null!;
        private Label _actionHint = null!;
        private LiteButton _refreshButton = null!;
        private LiteButton _storeTab = null!;
        private LiteButton _takeOutTab = null!;
        private LiteButton _actionButton = null!;
        private LiteUnderlineInput _searchInput = null!;
        private LiteComboBox _storageUnitCombo = null!;
        private LiteCheck _selectAll = null!;

        public YouPinInventoryStoragePage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinInventoryStoragePage(YouPinPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);
            _storageService = runtimeServices.InventoryStorage;
            _queryPendingRetry = new UiDeferredActionScheduler(
                () => !IsDisposed && !Disposing && Config != null);
            _refreshController = CreateAsyncRefreshController(
                "YouPinInventoryStorage.Refresh",
                BuildRefreshSnapshotAsync,
                ApplyRefreshSnapshot,
                new UiRefreshOptions
                {
                    Name = "YouPinInventoryStorage.Refresh",
                    DebounceMs = 0
                });
            BuildPage();
            Container.SizeChanged += (_, _) => ApplyResponsiveLayout();
        }

        public override void Activate()
        {
            base.Activate();
            RequestRefresh("进入库存存取", reloadUnits: true);
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            _gridAdapter.ApplyTheme();
            _storeTab.RefreshTheme();
            _takeOutTab.RefreshTheme();
            _refreshButton.RefreshTheme();
            _actionButton.RefreshTheme();
            _storageUnitCombo.RefreshTheme();
            UpdateSummary();
            ApplyResponsiveLayout();
        }

        public override void Deactivate()
        {
            _queryPendingRetry.Cancel(QueryPendingRetryKey);
            base.Deactivate();
        }

        private void BuildPage()
        {
            Container.SuspendLayout();
            try
            {
                ClearPage();
                Container.Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding(18);
                CreateInventoryCard();
                CreateSummaryCard();
            }
            finally
            {
                Container.ResumeLayout(false);
            }
            _gridAdapter.ApplyTheme();
            UpdateSummary();
            UpdateActionState();
            ApplyResponsiveLayout();
        }

        private void CreateSummaryCard()
        {
            _summaryCard = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(150),
                DrawBorder = false,
                FillOverride = UIColors.CardBg
            };
            YouPinCcRoundedPanel card = _summaryCard;
            var title = YouPinCcUi.Label("库存存取", 15F, FontStyle.Bold);
            var description = YouPinCcUi.Label(
                "管理悠悠有品已有存储单元中的饰品。每次操作都会重新核对资格并等待平台回读。",
                9F,
                FontStyle.Regular,
                UIColors.TextSub);
            _statusLabel = YouPinCcUi.Label("等待读取悠悠库存", 9.2F, FontStyle.Regular, UIColors.TextSub);
            _statusLabel.AccessibleName = "库存读取状态";
            _refreshButton = new LiteButton("刷新库存");
            _refreshButton.Name = "YouPinInventoryStorage.Refresh";
            _refreshButton.AccessibleName = "刷新悠悠库存";
            _refreshButton.ShowKeyboardFocusCue = true;
            _refreshButton.Click += (_, _) => RequestRefresh("手动刷新", reloadUnits: true);

            Panel storable = CreateSummaryPill("可存入", out _storableLabel);
            Panel stored = CreateSummaryPill("已存入", out _storedLabel);
            Panel takeOut = CreateSummaryPill("可取出", out _takeOutLabel);

            card.Controls.Add(title);
            card.Controls.Add(description);
            card.Controls.Add(_statusLabel);
            card.Controls.Add(_refreshButton);
            card.Controls.Add(storable);
            card.Controls.Add(stored);
            card.Controls.Add(takeOut);
            card.Layout += (_, _) =>
            {
                int pad = UIUtils.S(22);
                _refreshButton.SetBounds(card.Width - pad - UIUtils.S(108), UIUtils.S(18), UIUtils.S(108), UIUtils.S(34));
                title.SetBounds(pad, UIUtils.S(14), Math.Max(1, _refreshButton.Left - pad - UIUtils.S(12)), UIUtils.S(30));
                description.SetBounds(pad, UIUtils.S(44), Math.Max(1, card.Width - pad * 2), UIUtils.S(20));
                _statusLabel.SetBounds(pad, UIUtils.S(64), Math.Max(1, card.Width - pad * 2), UIUtils.S(20));

                int top = UIUtils.S(84);
                int gap = UIUtils.S(12);
                int width = Math.Max(1, (card.Width - pad * 2 - gap * 2) / 3);
                storable.SetBounds(pad, top, width, UIUtils.S(48));
                stored.SetBounds(storable.Right + gap, top, width, UIUtils.S(48));
                takeOut.SetBounds(stored.Right + gap, top, Math.Max(1, card.Width - pad - stored.Right - gap), UIUtils.S(48));
            };

            _summaryWrapper = YouPinCcUi.AddTopCard(Container, card, 14);
        }

        private void CreateInventoryCard()
        {
            _inventoryCard = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(500),
                FillOverride = UIColors.CardBg
            };
            YouPinCcRoundedPanel card = _inventoryCard;
            _storeTab = new LiteButton("可存入");
            _takeOutTab = new LiteButton("已存入");
            _storeTab.Name = "YouPinInventoryStorage.StoreTab";
            _takeOutTab.Name = "YouPinInventoryStorage.TakeOutTab";
            _storeTab.AccessibleName = "可存入选项卡";
            _takeOutTab.AccessibleName = "已存入选项卡";
            _storeTab.FillColorOverride = AccessibleActionFill;
            _takeOutTab.FillColorOverride = AccessibleActionFill;
            _storeTab.ShowKeyboardFocusCue = true;
            _takeOutTab.ShowKeyboardFocusCue = true;
            _storeTab.TabIndex = 0;
            _takeOutTab.TabIndex = 1;
            _storeTab.Click += (_, _) => SwitchDirection(YouPinInventoryStorageDirection.Store);
            _takeOutTab.Click += (_, _) => SwitchDirection(YouPinInventoryStorageDirection.TakeOut);

            _searchInput = new LiteUnderlineInput("", "", "", 240, null, HorizontalAlignment.Left)
            {
                Placeholder = "搜索饰品名称"
            };
            _searchInput.Inner.ImeMode = ImeMode.On;
            _searchInput.Inner.AccessibleName = "搜索饰品名称";
            _searchInput.Inner.TabIndex = 2;
            _searchInput.CommittedTextChanged += (_, _) => ApplyFilter();

            _storageUnitCombo = new LiteComboBox();
            _storageUnitCombo.Inner.AccessibleName = "悠悠库存存储单元";
            _storageUnitCombo.Inner.TabIndex = 4;
            _storageUnitCombo.Inner.SelectedIndexChanged += (_, _) => HandleStorageUnitChanged();
            _selectAll = new LiteCheck(false, "全选当前结果");
            _selectAll.AccessibleName = "全选当前搜索结果";
            _selectAll.TabIndex = 3;
            _selectAll.CheckedChanged += (_, _) =>
            {
                if (!_loading)
                    _gridAdapter.SelectAll(_selectAll.Checked);
            };

            var toolbar = new Panel { BackColor = Color.Transparent };
            toolbar.Controls.Add(_storeTab);
            toolbar.Controls.Add(_takeOutTab);
            toolbar.Controls.Add(_searchInput);
            toolbar.Controls.Add(_storageUnitCombo);
            toolbar.Controls.Add(_selectAll);
            toolbar.Layout += (_, _) => LayoutToolbar(toolbar);

            var gridHost = new Panel
            {
                BackColor = UIColors.CardBg
            };
            _emptyLabel = YouPinCcUi.Label(
                "暂无可显示的饰品",
                10F,
                FontStyle.Regular,
                UIColors.TextSub,
                ContentAlignment.MiddleCenter);
            gridHost.Controls.Add(_gridAdapter.Grid);
            gridHost.Controls.Add(_emptyLabel);
            _gridAdapter.Grid.AccessibleName = "悠悠库存饰品列表";
            _gridAdapter.Grid.TabIndex = 5;
            _emptyLabel.BringToFront();
            gridHost.Layout += (_, _) =>
            {
                _gridAdapter.Grid.Bounds = gridHost.ClientRectangle;
                _emptyLabel.Bounds = gridHost.ClientRectangle;
            };
            _gridAdapter.SelectionChanged += () =>
            {
                if (_selectAll.Checked && _gridAdapter.GetSelectedAssetIds().Count != _gridAdapter.ItemCount)
                    _selectAll.Checked = false;
                UpdateActionState();
            };

            var actionBar = new Panel
            {
                BackColor = Color.Transparent
            };
            _actionHint = YouPinCcUi.Label("勾选饰品后即可执行。", 9F, FontStyle.Regular, UIColors.TextSub);
            _actionButton = new LiteButton("存入所选", true);
            _actionButton.Name = "YouPinInventoryStorage.Execute";
            _actionButton.AccessibleName = "执行悠悠库存存取";
            _actionButton.FillColorOverride = AccessibleActionFill;
            _actionButton.ShowKeyboardFocusCue = true;
            _actionButton.TabIndex = 6;
            _actionButton.Click += async (_, _) => await ExecuteAsync();
            actionBar.Controls.Add(_actionHint);
            actionBar.Controls.Add(_actionButton);
            actionBar.Layout += (_, _) =>
            {
                int buttonWidth = UIUtils.S(132);
                _actionButton.SetBounds(actionBar.Width - buttonWidth, UIUtils.S(10), buttonWidth, UIUtils.S(38));
                _actionHint.SetBounds(0, UIUtils.S(8), Math.Max(1, _actionButton.Left - UIUtils.S(14)), UIUtils.S(42));
            };

            card.Controls.Add(toolbar);
            card.Controls.Add(gridHost);
            card.Controls.Add(actionBar);
            card.Layout += (_, _) =>
            {
                int pad = UIUtils.S(20);
                int toolbarWidth = Math.Max(1, card.Width - pad * 2);
                int toolbarHeight = YouPinInventoryStoragePageModel
                    .BuildToolbarLayout(toolbarWidth, UIUtils.ScaleFactor)
                    .Height;
                toolbar.SetBounds(pad, UIUtils.S(14), toolbarWidth, toolbarHeight);
                actionBar.SetBounds(pad, card.Height - UIUtils.S(68), Math.Max(1, card.Width - pad * 2), UIUtils.S(54));
                gridHost.SetBounds(pad, toolbar.Bottom + UIUtils.S(4), Math.Max(1, card.Width - pad * 2), Math.Max(1, actionBar.Top - toolbar.Bottom - UIUtils.S(12)));
            };

            _inventoryWrapper = YouPinCcUi.AddTopCard(Container, card, 14);
        }

        private static Panel CreateSummaryPill(string title, out Label valueLabel)
        {
            var panel = new YouPinCcRoundedPanel
            {
                DrawBorder = false,
                FillOverride = UIColors.ControlBg,
                Radius = UIUtils.S(5)
            };
            var titleLabel = YouPinCcUi.Label(title, 8.5F, FontStyle.Regular, UIColors.TextSub);
            var value = YouPinCcUi.Label("—", 11F, FontStyle.Bold);
            valueLabel = value;
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(value);
            panel.Layout += (_, _) =>
            {
                int pad = UIUtils.S(14);
                titleLabel.SetBounds(pad, UIUtils.S(4), Math.Max(1, panel.Width - pad * 2), UIUtils.S(18));
                value.SetBounds(pad, UIUtils.S(20), Math.Max(1, panel.Width - pad * 2), UIUtils.S(24));
            };
            return panel;
        }

        private void LayoutToolbar(Panel toolbar)
        {
            YouPinInventoryStorageToolbarLayout layout = YouPinInventoryStoragePageModel
                .BuildToolbarLayout(toolbar.Width, UIUtils.ScaleFactor);
            _storeTab.Bounds = layout.StoreTab;
            _takeOutTab.Bounds = layout.TakeOutTab;
            _searchInput.Bounds = layout.SearchInput;
            _selectAll.Bounds = layout.SelectAll;
            _storageUnitCombo.Bounds = layout.StorageUnit;
        }

        private void SwitchDirection(YouPinInventoryStorageDirection direction)
        {
            if (_direction == direction)
                return;
            _queryPendingRetry.Cancel(QueryPendingRetryKey);
            _queryPendingRetryAttempt = 0;
            _queryPending = false;
            _hasRefreshError = false;
            _direction = direction;
            _state = null;
            _sourceItems = Array.Empty<YouPinInventoryStorageItem>();
            _selectAll.Checked = false;
            _gridAdapter.ClearSelection();
            UpdateModeAppearance();
            ApplyFilter();
            UpdateSummary();
            RequestRefresh("切换存取模式", reloadUnits: true);
        }

        private void RequestRefresh(
            string reason,
            bool reloadUnits,
            bool resetQueryPendingRetry = true)
        {
            if (Config == null || IsDisposed || Disposing)
                return;

            if (resetQueryPendingRetry)
            {
                _queryPendingRetry.Cancel(QueryPendingRetryKey);
                _queryPendingRetryAttempt = 0;
            }
            _queryPending = false;
            _hasRefreshError = false;

            var request = new StorageRefreshRequest(
                Config,
                _direction,
                _storageUnitCombo.SelectedValue,
                reloadUnits);
            lock (_refreshRequestGate)
                _latestRefreshRequest = request;

            _loading = true;
            ApplyFilter();
            UpdateActionState();
            _refreshButton.Enabled = false;
            SetStatus("正在读取悠悠库存…", UIColors.TextSub);
            _refreshController.Request(UiRefreshReason.Now(reason, "库存存取"));
        }

        private async Task<StorageRefreshSnapshot> BuildRefreshSnapshotAsync(
            UiRefreshReason _,
            CancellationToken cancellationToken)
        {
            StorageRefreshRequest request;
            YouPinInventoryStorageViewState? partialUnitsState = null;
            lock (_refreshRequestGate)
            {
                request = _latestRefreshRequest
                    ?? throw new InvalidOperationException("缺少库存刷新请求上下文。");
            }

            try
            {
                if (request.Direction == YouPinInventoryStorageDirection.Store)
                {
                    YouPinInventoryStorageViewState items = await _storageService.LoadAsync(
                        request.Settings,
                        new YouPinInventoryStorageQuery(YouPinInventoryStorageView.Storable),
                        cancellationToken).ConfigureAwait(false);
                    return new StorageRefreshSnapshot(request, null, items, null);
                }

                if (!request.ReloadUnits)
                {
                    if (string.IsNullOrWhiteSpace(request.PreferredStorageAssetId))
                        return new StorageRefreshSnapshot(request, null, null, null);

                    YouPinInventoryStorageViewState items = await _storageService.LoadAsync(
                        request.Settings,
                        new YouPinInventoryStorageQuery(
                            YouPinInventoryStorageView.StoredItems,
                            request.PreferredStorageAssetId),
                        cancellationToken).ConfigureAwait(false);
                    return new StorageRefreshSnapshot(request, null, items, null);
                }

                YouPinInventoryStorageViewState units = await _storageService.LoadAsync(
                    request.Settings,
                    new YouPinInventoryStorageQuery(YouPinInventoryStorageView.StoredUnits),
                    cancellationToken).ConfigureAwait(false);
                partialUnitsState = units;
                string selectedUnit = ResolveStorageUnit(units.Units, request.PreferredStorageAssetId);
                if (string.IsNullOrWhiteSpace(selectedUnit))
                    return new StorageRefreshSnapshot(request, units, null, null);

                request = request with { PreferredStorageAssetId = selectedUnit };

                YouPinInventoryStorageViewState storedItems = await _storageService.LoadAsync(
                    request.Settings,
                    new YouPinInventoryStorageQuery(YouPinInventoryStorageView.StoredItems, selectedUnit),
                    cancellationToken).ConfigureAwait(false);
                return new StorageRefreshSnapshot(
                    request,
                    units,
                    storedItems,
                    null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new StorageRefreshSnapshot(request, partialUnitsState, null, ex);
            }
        }

        private void ApplyRefreshSnapshot(StorageRefreshSnapshot snapshot)
        {
            if (snapshot.Request.Direction != _direction)
                return;

            _loading = false;
            bool refreshSucceeded = snapshot.Error == null;
            if (snapshot.Error != null)
            {
                _queryPending = snapshot.Error is YouPinInventoryStorageQueryPendingException;
                _hasRefreshError = !_queryPending;
                YouPinInventoryStorageViewState? previousState = YouPinInventoryStoragePageModel
                    .CanPreserveStateForRequest(
                        _state,
                        snapshot.Request.Direction,
                        snapshot.Request.PreferredStorageAssetId)
                    ? _state
                    : null;
                YouPinInventoryStorageViewState? preserved = YouPinInventoryStoragePageModel
                    .ResolveStateAfterRefreshFailure(previousState, snapshot.UnitsState);
                if (preserved != null)
                    ApplyState(preserved, snapshot.Request.PreferredStorageAssetId);
                else
                {
                    ApplyFilter();
                    UpdateSummary();
                }

                if (snapshot.Error is YouPinInventoryStorageQueryPendingException pending)
                {
                    int nextAttempt = _queryPendingRetryAttempt + 1;
                    YouPinInventoryStorageRetryModel retry = YouPinInventoryStoragePageModel
                        .BuildQueryPendingRetry(
                            pending,
                            nextAttempt,
                            QueryPendingRetryMaxAttempts);
                    SetStatus(retry.StatusText, UIColors.TextWarn);
                    if (retry.ShouldRetry)
                    {
                        _queryPendingRetryAttempt = nextAttempt;
                        bool reloadUnits = snapshot.UnitsState == null;
                        _queryPendingRetry.Schedule(
                            QueryPendingRetryKey,
                            retry.DelayMs,
                            () => RequestRefresh(
                                "悠悠库存等待重试",
                                reloadUnits,
                                resetQueryPendingRetry: false));
                    }
                }
                else
                {
                    _queryPendingRetry.Cancel(QueryPendingRetryKey);
                    SetStatus(BuildFriendlyError(snapshot.Error), UIColors.TextCrit);
                }
            }
            else if (snapshot.Request.Direction == YouPinInventoryStorageDirection.Store)
            {
                ResetRefreshFailureState();
                if (snapshot.ItemsState != null)
                    ApplyState(snapshot.ItemsState, snapshot.Request.PreferredStorageAssetId);
            }
            else
            {
                ResetRefreshFailureState();
                if (snapshot.Request.ReloadUnits && snapshot.UnitsState != null)
                    ApplyState(snapshot.UnitsState, snapshot.Request.PreferredStorageAssetId);
                if (snapshot.ItemsState != null)
                {
                    ApplyState(
                        snapshot.ItemsState,
                        snapshot.Request.PreferredStorageAssetId,
                        preserveUnits: true);
                }
            }

            _writePending = YouPinInventoryStoragePageModel.ShouldKeepWritePending(
                _writePending,
                refreshSucceeded,
                _state);
            _refreshButton.Enabled = true;
            UpdateActionState();
        }

        private void HandleStorageUnitChanged()
        {
            if (_updatingUnitSelection || _loading)
                return;
            _gridAdapter.ClearSelection();
            if (_direction == YouPinInventoryStorageDirection.TakeOut
                && !string.IsNullOrWhiteSpace(_storageUnitCombo.SelectedValue))
            {
                RequestRefresh("切换存储单元", reloadUnits: false);
            }
            else
            {
                UpdateActionState();
            }
        }

        private static string ResolveStorageUnit(
            IReadOnlyList<YouPinInventoryStorageUnit> units,
            string preferredStorageAssetId)
        {
            YouPinInventoryStorageUnit? preferred = units.FirstOrDefault(unit => string.Equals(
                unit.StorageAssetId,
                preferredStorageAssetId,
                StringComparison.Ordinal));
            return preferred?.StorageAssetId ?? units.FirstOrDefault()?.StorageAssetId ?? string.Empty;
        }

        private void ApplyState(
            YouPinInventoryStorageViewState state,
            string preferredUnit,
            bool preserveUnits = false)
        {
            _state = state;
            _sourceItems = state.Items;
            if (!preserveUnits)
                PopulateUnits(state.Units, preferredUnit);
            ApplyFilter();
            UpdateSummary();
            SetStatus(
                state.Message,
                state.Access.IsBusy ? UIColors.TextWarn : UIColors.TextSub);
        }

        private void PopulateUnits(IReadOnlyList<YouPinInventoryStorageUnit> units, string preferredUnit)
        {
            _updatingUnitSelection = true;
            try
            {
                _storageUnitCombo.Items.Clear();
                foreach (YouPinInventoryStorageUnit unit in units)
                {
                    _storageUnitCombo.AddItem(
                        $"{unit.Name} · {unit.CountText}",
                        unit.StorageAssetId);
                }
                if (!_storageUnitCombo.SelectValue(preferredUnit) && _storageUnitCombo.Items.Count > 0)
                    _storageUnitCombo.SelectedIndex = 0;
            }
            finally
            {
                _updatingUnitSelection = false;
            }
        }

        private void ApplyFilter()
        {
            IReadOnlyList<YouPinInventoryStorageItem> filtered = YouPinInventoryStoragePageModel.FilterItems(
                _sourceItems,
                _searchInput.Inner.Text);
            _gridAdapter.Bind(filtered);
            _emptyLabel.Text = YouPinInventoryStoragePageModel.BuildEmptyText(
                _direction,
                _loading,
                _state != null,
                _sourceItems.Count,
                filtered.Count,
                _searchInput.Inner.Text,
                !string.IsNullOrWhiteSpace(_storageUnitCombo.SelectedValue),
                _queryPending,
                _hasRefreshError);
            _emptyLabel.Visible = filtered.Count == 0;
            _gridAdapter.Grid.Visible = filtered.Count > 0;
            UpdateActionState();
        }

        private async Task ExecuteAsync()
        {
            if (_loading || _writePending || Config == null || _state == null)
                return;
            IReadOnlyList<string> assetIds = _gridAdapter.GetSelectedAssetIds();
            string storageAssetId = _storageUnitCombo.SelectedValue;
            string storageUnitName = (_storageUnitCombo.SelectedItem as LiteComboItem)?.Text ?? string.Empty;
            YouPinInventoryStorageActionModel action = YouPinInventoryStoragePageModel.BuildAction(
                _direction,
                _state.Access,
                assetIds.Count,
                storageUnitName,
                false);
            if (!action.Enabled)
                return;

            string title = _direction == YouPinInventoryStorageDirection.Store ? "确认存入" : "确认取出";
            string confirmText = _direction == YouPinInventoryStorageDirection.Store ? "确认存入" : "确认取出";
            string confirmation = YouPinInventoryStoragePageModel.BuildConfirmation(
                _direction,
                assetIds.Count,
                storageUnitName);
            IWin32Window owner = FindForm() is Form form ? form : this;
            if (!YouPinInventoryStorageConfirmDialog.Confirm(owner, title, confirmation, confirmText))
                return;

            _loading = true;
            _actionButton.Enabled = false;
            _refreshButton.Enabled = false;
            SetStatus("正在重新核对资产并提交…", UIColors.TextSub);
            CancellationToken pageToken = PageToken;
            bool shouldRefresh = false;
            try
            {
                YouPinInventoryStorageTransferResult result = await _storageService.ExecuteAsync(
                    Config,
                    new YouPinInventoryStorageTransferCommand(_direction, storageAssetId, assetIds),
                    pageToken);
                _writePending = result.Status == YouPinInventoryStorageTransferStatus.AcceptedPending;
                shouldRefresh = result.Status == YouPinInventoryStorageTransferStatus.Confirmed;
                if (!CanApplyWriteResult(pageToken))
                    return;

                SetStatus(
                    result.Message,
                    result.Status switch
                    {
                        YouPinInventoryStorageTransferStatus.Confirmed => UIColors.Positive,
                        YouPinInventoryStorageTransferStatus.AcceptedPending => UIColors.TextWarn,
                        _ => UIColors.TextCrit
                    });
                _gridAdapter.ClearSelection();
            }
            catch (OperationCanceledException)
            {
                // Page shutdown may cancel confirmation polling after a user action.
            }
            catch (Exception ex)
            {
                if (CanApplyWriteResult(pageToken))
                    SetStatus(BuildFriendlyError(ex), UIColors.TextCrit);
            }
            finally
            {
                _loading = false;
                if (CanApplyWriteResult(pageToken))
                {
                    _refreshButton.Enabled = true;
                    UpdateActionState();
                }
            }

            if (shouldRefresh && CanApplyWriteResult(pageToken))
                RequestRefresh("操作后确认", reloadUnits: true);
        }

        private bool CanApplyWriteResult(CancellationToken pageToken)
        {
            return !pageToken.IsCancellationRequested && !IsDisposed && !Disposing;
        }

        private void UpdateSummary()
        {
            YouPinInventoryStorageSummaryModel model = YouPinInventoryStoragePageModel.BuildSummary(_state);
            _storableLabel.Text = model.StorableText;
            _storedLabel.Text = model.StoredText;
            _takeOutLabel.Text = model.TakeOutText;
            if (!_loading && string.IsNullOrWhiteSpace(_statusLabel.Text))
                _statusLabel.Text = model.StatusText;
            UpdateModeAppearance();
        }

        private void UpdateModeAppearance()
        {
            _storeTab.IsActive = _direction == YouPinInventoryStorageDirection.Store;
            _takeOutTab.IsActive = _direction == YouPinInventoryStorageDirection.TakeOut;
            _storeTab.AccessibleDescription = _storeTab.IsActive ? "当前选项卡" : "切换到可存入饰品";
            _takeOutTab.AccessibleDescription = _takeOutTab.IsActive ? "当前选项卡" : "切换到已存入饰品";
        }

        private void UpdateActionState()
        {
            YouPinInventoryStorageAccess access = _state?.Access ?? YouPinInventoryStorageAccess.Empty;
            int count = _gridAdapter.GetSelectedAssetIds().Count;
            string unitName = (_storageUnitCombo.SelectedItem as LiteComboItem)?.Text ?? string.Empty;
            YouPinInventoryStorageActionModel model = YouPinInventoryStoragePageModel.BuildAction(
                _direction,
                access,
                count,
                unitName,
                _loading || _writePending || _queryPending || _hasRefreshError);
            _actionButton.Text = model.ButtonText;
            _actionButton.Enabled = model.Enabled;
            _actionHint.Text = _writePending
                ? "悠悠已接受操作，请刷新确认同步结果后再继续。"
                : model.HintText;
            bool canChooseUnit = !_loading
                && !_writePending
                && !_queryPending
                && !_hasRefreshError
                && _state != null
                && _storageUnitCombo.Items.Count > 0;
            _storageUnitCombo.Enabled = canChooseUnit;
            _storageUnitCombo.Inner.TabStop = canChooseUnit;
            _selectAll.Enabled = !_loading
                && !_writePending
                && !_queryPending
                && !_hasRefreshError
                && _gridAdapter.ItemCount > 0;
        }

        private void SetStatus(string text, Color color)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(text) ? "暂无状态" : text;
            _statusLabel.ForeColor = color;
            _statusLabel.AccessibleDescription = _statusLabel.Text;
            _statusToolTip.SetToolTip(_statusLabel, _statusLabel.Text);
        }

        private void ResetRefreshFailureState()
        {
            _queryPendingRetry.Cancel(QueryPendingRetryKey);
            _queryPendingRetryAttempt = 0;
            _queryPending = false;
            _hasRefreshError = false;
        }

        private void ApplyResponsiveLayout()
        {
            if (_summaryCard == null
                || _inventoryCard == null
                || _summaryWrapper == null
                || _inventoryWrapper == null)
            {
                return;
            }

            YouPinInventoryStoragePageLayout layout = YouPinInventoryStoragePageModel
                .BuildPageLayout(Container.ClientSize.Height, UIUtils.ScaleFactor);
            Padding pagePadding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding(18);
            if (Container.Padding != pagePadding)
                Container.Padding = pagePadding;

            int gap = UIUtils.S(14);
            int summaryHeight = UIUtils.S(150);
            _summaryCard.Height = summaryHeight;
            _summaryWrapper.Padding = new Padding(0, 0, 0, gap);
            _summaryWrapper.Height = summaryHeight + gap;
            _inventoryCard.Height = layout.InventoryCardHeight;
            _inventoryWrapper.Padding = new Padding(0, 0, 0, gap);
            _inventoryWrapper.Height = layout.InventoryCardHeight + gap;
            Container.AutoScrollMinSize = layout.RequiresScroll
                ? new Size(0, layout.TotalContentHeight)
                : Size.Empty;
            Container.PerformLayout();
        }

        private static string BuildFriendlyError(Exception ex)
        {
            string message = YouPinMobileApiClient.Sanitize(ex.Message);
            if (YouPinMobileApiClient.LooksLikeSignatureFailure(message))
                return "悠悠库存存取接口要求官方签名，当前请求无法通过验证。";
            return string.IsNullOrWhiteSpace(message) ? "读取悠悠库存失败。" : message;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _queryPendingRetry.Dispose();
                _statusToolTip.Dispose();
                _gridAdapter.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed record StorageRefreshRequest(
            Settings Settings,
            YouPinInventoryStorageDirection Direction,
            string PreferredStorageAssetId,
            bool ReloadUnits);

        private sealed record StorageRefreshSnapshot(
            StorageRefreshRequest Request,
            YouPinInventoryStorageViewState? UnitsState,
            YouPinInventoryStorageViewState? ItemsState,
            Exception? Error);
    }
}
