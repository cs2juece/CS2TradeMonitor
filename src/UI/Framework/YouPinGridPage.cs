using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System.Globalization;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class YouPinGridPage : FrameworkSettingsPageBase
    {
        private readonly IYouPinGridTradingService _gridTrading;
        private readonly IYouPinInventoryService _inventory;
        private readonly UiAsyncRefreshController<YouPinGridRuntimeSnapshot> _refreshController;
        private readonly YouPinCcRoundedPanel _mainCard;
        private readonly YouPinCcRoundedPanel _safetyCard;
        private readonly YouPinCcRoundedPanel _summaryCard;
        private readonly Panel _strategyHost;
        private readonly Label _statusLabel;
        private readonly Label _totalValue;
        private readonly Label _enabledValue;
        private readonly Label _triggeredValue;
        private readonly Label _unavailableValue;
        private readonly Label _lastRefreshLabel;
        private readonly AutoQuoteBadgeLabel _safetyBadge;
        private readonly Label _safetyTitle;
        private readonly Label _safetyHint;
        private readonly LiteButton _refreshButton;
        private readonly LiteButton _newButton;
        private readonly Panel _mainWrapper;
        private bool _subscribed;
        private YouPinGridRuntimeSnapshot? _renderedSnapshot;

        public YouPinGridPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinGridPage(YouPinPageRuntimeServices runtimeServices)
            : this(
                runtimeServices?.GridTrading ?? throw new ArgumentNullException(nameof(runtimeServices)),
                runtimeServices.Inventory)
        {
        }

        internal YouPinGridPage(
            IYouPinGridTradingService gridTrading,
            IYouPinInventoryService inventory)
        {
            _gridTrading = gridTrading ?? throw new ArgumentNullException(nameof(gridTrading));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));

            _mainCard = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(500),
                Padding = Padding.Empty
            };
            _safetyCard = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.IsDark
                    ? Color.FromArgb(18, 39, 58)
                    : Color.FromArgb(232, 244, 255),
                DrawBorder = true
            };
            _summaryCard = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.IsDark
                    ? Color.FromArgb(23, 29, 36)
                    : UIColors.ControlBg
            };
            _strategyHost = new Panel { BackColor = Color.Transparent };
            _statusLabel = YouPinCcUi.Label("尚未刷新", 8.8F, FontStyle.Regular, UIColors.TextSub);
            _totalValue = CreateMetricValue();
            _enabledValue = CreateMetricValue();
            _triggeredValue = CreateMetricValue();
            _unavailableValue = CreateMetricValue();
            _lastRefreshLabel = YouPinCcUi.Label("最近刷新：--", 8.5F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleRight);
            _safetyBadge = new AutoQuoteBadgeLabel
            {
                Text = "观察模式",
                Tone = AutoQuoteBadgeTone.Warn,
                Font = UIFonts.Bold(8.5F)
            };
            _safetyTitle = YouPinCcUi.Label("当前没有启用真实自动交易策略", 9F, FontStyle.Bold, UIColors.TextMain);
            _safetyHint = YouPinCcUi.Label(
                "行情读取和规则计算使用真实悠悠数据；只有策略明确开启自动执行后才会提交订单。",
                8.5F,
                FontStyle.Regular,
                UIColors.TextSub);
            _refreshButton = new LiteButton("刷新行情", false)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(34)
            };
            _newButton = new LiteButton("新建策略", true)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(34)
            };
            _refreshController = CreateAsyncRefreshController(
                "YouPinGrid.Refresh",
                async (_, cancellationToken) =>
                {
                    Settings? settings = Config;
                    return settings == null
                        ? _gridTrading.GetSnapshot()
                        : await _gridTrading.RefreshAsync(settings, cancellationToken).ConfigureAwait(false);
                },
                ApplySnapshot,
                new UiRefreshOptions { Name = "YouPinGrid.Refresh", DebounceMs = 0 });
            _refreshController.Faulted += (_, ex) => HandleRefreshFailure(ex);

            BuildPage();
            _mainWrapper = YouPinCcUi.AddTopCard(Container, _mainCard);
            WireEvents();
            ApplySnapshot(_gridTrading.GetSnapshot());
        }

        public override void Activate()
        {
            base.Activate();
            Subscribe();
            ApplySnapshot(_gridTrading.GetSnapshot());
            RequestRefresh("进入交易网格");
        }

        public override void Deactivate()
        {
            Unsubscribe();
            base.Deactivate();
        }

        public override void ApplySystemTheme()
        {
            _renderedSnapshot = null;
            _safetyCard.FillOverride = UIColors.IsDark
                ? Color.FromArgb(18, 39, 58)
                : Color.FromArgb(232, 244, 255);
            _summaryCard.FillOverride = UIColors.IsDark
                ? Color.FromArgb(23, 29, 36)
                : UIColors.ControlBg;
            base.ApplySystemTheme();
            ApplySnapshot(_gridTrading.GetSnapshot());
        }

        private void BuildPage()
        {
            Padding pagePadding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding();
            pagePadding.Top = 0;
            Container.Padding = pagePadding;

            var title = YouPinCcUi.Label("交易网格", 12F, FontStyle.Bold);
            var subtitle = YouPinCcUi.Label(
                "以悠悠同款饰品的最低有效在售价为基准，按网格条件观察或自动执行真实买卖。",
                8.8F,
                FontStyle.Regular,
                UIColors.TextSub);
            var sectionTitle = YouPinCcUi.Label("策略列表", 10F, FontStyle.Bold);
            var sectionHint = YouPinCcUi.Label(
                "同款饰品只保留一条策略，最多 20 条；基准价仅在未来确认悠悠订单成交后更新。",
                8.5F,
                FontStyle.Regular,
                UIColors.TextSub);

            _mainCard.Controls.AddRange(new Control[]
            {
                title,
                subtitle,
                _refreshButton,
                _newButton,
                _safetyCard,
                _summaryCard,
                sectionTitle,
                sectionHint,
                _statusLabel,
                _lastRefreshLabel,
                _strategyHost
            });
            _safetyCard.Controls.AddRange(new Control[] { _safetyBadge, _safetyTitle, _safetyHint });
            AddMetric(_summaryCard, "策略总数", _totalValue);
            AddMetric(_summaryCard, "启用策略", _enabledValue);
            AddMetric(_summaryCard, "条件触发", _triggeredValue);
            AddMetric(_summaryCard, "行情异常", _unavailableValue);

            _safetyCard.Layout += (_, __) =>
            {
                int pad = UIUtils.S(16);
                _safetyBadge.SetBounds(pad, UIUtils.S(13), UIUtils.S(86), UIUtils.S(26));
                _safetyTitle.SetBounds(_safetyBadge.Right + UIUtils.S(14), UIUtils.S(8), Math.Max(1, _safetyCard.Width - _safetyBadge.Right - UIUtils.S(28)), UIUtils.S(24));
                _safetyHint.SetBounds(_safetyTitle.Left, UIUtils.S(30), _safetyTitle.Width, UIUtils.S(22));
            };

            _summaryCard.Layout += (_, __) => LayoutMetrics(_summaryCard);
            _mainCard.Layout += (_, __) =>
            {
                int left = UIUtils.S(26);
                int right = _mainCard.Width - UIUtils.S(26);
                int contentWidth = Math.Max(1, right - left);
                title.SetBounds(left, UIUtils.S(22), UIUtils.S(220), UIUtils.S(30));
                _newButton.SetBounds(right - _newButton.Width, UIUtils.S(20), _newButton.Width, _newButton.Height);
                _refreshButton.SetBounds(_newButton.Left - UIUtils.S(10) - _refreshButton.Width, UIUtils.S(20), _refreshButton.Width, _refreshButton.Height);
                subtitle.SetBounds(left, UIUtils.S(52), Math.Max(1, _refreshButton.Left - left - UIUtils.S(18)), UIUtils.S(24));
                _safetyCard.SetBounds(left, UIUtils.S(82), contentWidth, UIUtils.S(58));
                _summaryCard.SetBounds(left, UIUtils.S(150), contentWidth, UIUtils.S(70));
                sectionTitle.SetBounds(left, UIUtils.S(234), UIUtils.S(120), UIUtils.S(28));
                _lastRefreshLabel.SetBounds(Math.Max(sectionTitle.Right, right - UIUtils.S(210)), UIUtils.S(236), UIUtils.S(210), UIUtils.S(24));
                sectionHint.SetBounds(
                    sectionTitle.Right + UIUtils.S(14),
                    UIUtils.S(236),
                    Math.Max(1, _lastRefreshLabel.Left - sectionTitle.Right - UIUtils.S(28)),
                    UIUtils.S(24));
                _statusLabel.SetBounds(left, UIUtils.S(260), contentWidth, UIUtils.S(22));
                _strategyHost.SetBounds(left, UIUtils.S(290), contentWidth, Math.Max(1, _mainCard.Height - UIUtils.S(312)));
            };
        }

        private void WireEvents()
        {
            _refreshButton.Click += (_, __) => RequestRefresh("手动刷新");
            _newButton.Click += async (_, __) => await EditStrategyAsync(null);
        }

        private void RequestRefresh(string reason)
        {
            if (Config == null || IsDisposed)
                return;

            _refreshButton.Enabled = false;
            _refreshButton.Text = "刷新中…";
            _statusLabel.Text = "正在读取悠悠同款最低有效在售价并回读订单状态…";
            _statusLabel.ForeColor = UIColors.Primary;
            _refreshController.Request(UiRefreshReason.Now(reason, "交易网格"));
        }

        private void ApplySnapshot(YouPinGridRuntimeSnapshot snapshot)
        {
            if (IsDisposed)
                return;
            if (ReferenceEquals(_renderedSnapshot, snapshot))
                return;

            _renderedSnapshot = snapshot;

            _refreshButton.Enabled = true;
            _refreshButton.Text = "刷新行情";
            _totalValue.Text = snapshot.Strategies.Count.ToString(CultureInfo.InvariantCulture);
            _enabledValue.Text = snapshot.EnabledCount.ToString(CultureInfo.InvariantCulture);
            _triggeredValue.Text = snapshot.TriggeredCount.ToString(CultureInfo.InvariantCulture);
            _unavailableValue.Text = snapshot.UnavailableCount.ToString(CultureInfo.InvariantCulture);
            _triggeredValue.ForeColor = snapshot.TriggeredCount > 0 ? UIColors.TextWarn : UIColors.TextMain;
            _unavailableValue.ForeColor = snapshot.UnavailableCount > 0 ? UIColors.TextCrit : UIColors.TextMain;
            _statusLabel.Text = snapshot.Status;
            _statusLabel.ForeColor = snapshot.UnavailableCount > 0 ? UIColors.TextWarn : UIColors.TextSub;
            _lastRefreshLabel.Text = snapshot.LastRefreshAt == DateTime.MinValue
                ? "最近刷新：--"
                : "最近刷新：" + snapshot.LastRefreshAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            ApplyExecutionModeSummary(snapshot);
            RenderStrategies(snapshot.Strategies);
        }

        private void ApplyExecutionModeSummary(YouPinGridRuntimeSnapshot snapshot)
        {
            int automaticCount = snapshot.Strategies.Count(row => row.Strategy.Enabled && !row.Strategy.ObserveOnly);
            if (automaticCount > 0)
            {
                _safetyBadge.Text = "自动执行";
                _safetyBadge.Tone = AutoQuoteBadgeTone.Success;
                _safetyTitle.Text = $"已开启 {automaticCount} 条真实交易策略";
                _safetyHint.Text = "符合条件后会使用悠悠余额买入或将库存上架出售，并以悠悠订单状态回读为准。";
            }
            else
            {
                _safetyBadge.Text = "观察模式";
                _safetyBadge.Tone = AutoQuoteBadgeTone.Warn;
                _safetyTitle.Text = "当前没有启用真实自动交易策略";
                _safetyHint.Text = "行情读取和规则计算使用真实悠悠数据；只有策略明确开启自动执行后才会提交订单。";
            }

            _safetyBadge.Invalidate();
        }

        private void RenderStrategies(IReadOnlyList<YouPinGridStrategySnapshot> rows)
        {
            _strategyHost.SuspendLayout();
            try
            {
                while (_strategyHost.Controls.Count > 0)
                {
                    Control control = _strategyHost.Controls[0];
                    _strategyHost.Controls.RemoveAt(0);
                    control.Dispose();
                }

                int contentHeight;
                if (rows.Count == 0)
                {
                    Control empty = CreateEmptyState();
                    empty.SetBounds(0, 0, _strategyHost.Width, UIUtils.S(150));
                    empty.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    _strategyHost.Controls.Add(empty);
                    contentHeight = UIUtils.S(150);
                }
                else
                {
                    int rowHeight = UIUtils.S(170);
                    int gap = UIUtils.S(12);
                    int y = 0;
                    foreach (YouPinGridStrategySnapshot row in rows)
                    {
                        Control card = CreateStrategyCard(row);
                        card.SetBounds(0, y, _strategyHost.Width, rowHeight);
                        card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                        _strategyHost.Controls.Add(card);
                        y += rowHeight + gap;
                    }
                    contentHeight = y - gap;
                }

                _strategyHost.Height = contentHeight;
                int cardHeight = UIUtils.S(312) + contentHeight;
                _mainCard.Height = cardHeight;
                _mainWrapper.Height = cardHeight + UIUtils.S(14);
                _mainCard.PerformLayout();
                Container.PerformLayout();
            }
            finally
            {
                _strategyHost.ResumeLayout(true);
            }
        }

        private Control CreateEmptyState()
        {
            var card = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.IsDark ? Color.FromArgb(22, 27, 34) : UIColors.ControlBg
            };
            var title = YouPinCcUi.Label("还没有交易网格策略", 10.5F, FontStyle.Bold, UIColors.TextMain, ContentAlignment.MiddleCenter);
            var hint = YouPinCcUi.Label(
                "从当前悠悠库存带入同款饰品，设置基准价和网格比例，再选择观察或自动执行。",
                8.8F,
                FontStyle.Regular,
                UIColors.TextSub,
                ContentAlignment.MiddleCenter);
            var button = new LiteButton("创建第一条策略", true)
            {
                Width = UIUtils.S(138),
                Height = UIUtils.S(34)
            };
            button.Click += async (_, __) => await EditStrategyAsync(null);
            card.Controls.AddRange(new Control[] { title, hint, button });
            card.Layout += (_, __) =>
            {
                title.SetBounds(UIUtils.S(20), UIUtils.S(24), Math.Max(1, card.Width - UIUtils.S(40)), UIUtils.S(28));
                hint.SetBounds(UIUtils.S(20), UIUtils.S(54), Math.Max(1, card.Width - UIUtils.S(40)), UIUtils.S(24));
                button.SetBounds(Math.Max(0, (card.Width - button.Width) / 2), UIUtils.S(92), button.Width, button.Height);
            };
            return card;
        }

        private Control CreateStrategyCard(YouPinGridStrategySnapshot row)
        {
            var card = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg
            };
            var accent = new Panel
            {
                BackColor = ResolveAccent(row),
                Width = UIUtils.S(3)
            };
            var name = YouPinCcUi.Label(row.Strategy.ItemName, 10F, FontStyle.Bold);
            var identity = YouPinCcUi.Label(
                "悠悠模板 ID：" + row.Strategy.TemplateId,
                8.2F,
                FontStyle.Regular,
                UIColors.TextSub);
            var enabledBadge = new AutoQuoteBadgeLabel
            {
                Text = row.Strategy.Enabled ? "已启用" : "未启用",
                Tone = row.Strategy.Enabled ? AutoQuoteBadgeTone.Success : AutoQuoteBadgeTone.Subtle,
                Font = UIFonts.Bold(8F)
            };
            var modeBadge = new AutoQuoteBadgeLabel
            {
                Text = row.Strategy.ObserveOnly ? "观察预演" : "自动执行",
                Tone = row.Strategy.ObserveOnly ? AutoQuoteBadgeTone.Warn : AutoQuoteBadgeTone.Success,
                Font = UIFonts.Bold(8F)
            };
            var toggle = new LiteButton(row.Strategy.Enabled ? "停用" : "启用", false)
            {
                Width = UIUtils.S(70),
                Height = UIUtils.S(30)
            };
            var edit = new LiteButton("编辑", false)
            {
                Width = UIUtils.S(70),
                Height = UIUtils.S(30),
                Enabled = !IsExecutionBlocking(row.Execution.Stage)
            };
            var delete = new LiteButton("删除", false)
            {
                Width = UIUtils.S(70),
                Height = UIUtils.S(30),
                ForeColor = UIColors.TextCrit,
                Enabled = !IsExecutionBlocking(row.Execution.Stage)
            };
            toggle.Click += async (_, __) => await ToggleStrategyAsync(row.Strategy);
            edit.Click += async (_, __) => await EditStrategyAsync(row.Strategy);
            delete.Click += async (_, __) => await DeleteStrategyAsync(row);

            var metrics = new[]
            {
                CreateCardMetric("悠悠观察价", FormatMoney(row.MarketQuote.LowestPrice), row.MarketQuote.Available ? UIColors.TextMain : UIColors.TextDisabled),
                CreateCardMetric("当前基准价", FormatMoney(row.Strategy.BasePrice), UIColors.TextMain),
                CreateCardMetric("下一档买入", FormatMoney(row.Plan.NextBuyPrice), UIColors.Positive),
                CreateCardMetric("下一档卖出", FormatMoney(row.Plan.NextSellPrice), UIColors.Negative),
                CreateCardMetric("当前持有", row.Holdings + " 件", UIColors.TextMain)
            };
            var status = YouPinCcUi.Label(row.Status, 8.5F, FontStyle.Regular, ResolveStatusColor(row));
            var actionBadge = new AutoQuoteBadgeLabel
            {
                Text = ResolveActionText(row),
                Tone = ResolveActionTone(row),
                Font = UIFonts.Bold(8F)
            };

            card.Controls.AddRange(new Control[]
            {
                accent, name, identity, enabledBadge, modeBadge,
                toggle, edit, delete, status, actionBadge
            });
            foreach (Control metric in metrics)
                card.Controls.Add(metric);

            card.Layout += (_, __) =>
            {
                int left = UIUtils.S(20);
                int right = card.Width - UIUtils.S(18);
                accent.SetBounds(0, 0, UIUtils.S(3), card.Height);
                int actionWidth = UIUtils.S(230);
                name.SetBounds(left, UIUtils.S(14), Math.Max(1, right - left - actionWidth - UIUtils.S(170)), UIUtils.S(27));
                enabledBadge.SetBounds(Math.Min(right - actionWidth - UIUtils.S(154), name.Right + UIUtils.S(10)), UIUtils.S(16), UIUtils.S(64), UIUtils.S(24));
                modeBadge.SetBounds(enabledBadge.Right + UIUtils.S(8), UIUtils.S(16), UIUtils.S(76), UIUtils.S(24));
                identity.SetBounds(left, UIUtils.S(40), Math.Max(1, right - left - actionWidth), UIUtils.S(22));
                delete.SetBounds(right - delete.Width, UIUtils.S(15), delete.Width, delete.Height);
                edit.SetBounds(delete.Left - UIUtils.S(8) - edit.Width, UIUtils.S(15), edit.Width, edit.Height);
                toggle.SetBounds(edit.Left - UIUtils.S(8) - toggle.Width, UIUtils.S(15), toggle.Width, toggle.Height);

                int metricsTop = UIUtils.S(70);
                int gap = UIUtils.S(10);
                int metricWidth = Math.Max(UIUtils.S(100), (right - left - gap * (metrics.Length - 1)) / metrics.Length);
                for (int index = 0; index < metrics.Length; index++)
                {
                    metrics[index].SetBounds(left + index * (metricWidth + gap), metricsTop, metricWidth, UIUtils.S(50));
                }

                actionBadge.SetBounds(left, UIUtils.S(132), UIUtils.S(86), UIUtils.S(24));
                status.SetBounds(actionBadge.Right + UIUtils.S(12), UIUtils.S(132), Math.Max(1, right - actionBadge.Right - UIUtils.S(12)), UIUtils.S(24));
            };
            return card;
        }

        private static Control CreateCardMetric(string caption, string value, Color valueColor)
        {
            var panel = new Panel { BackColor = Color.Transparent };
            var captionLabel = YouPinCcUi.Label(caption, 8.2F, FontStyle.Regular, UIColors.TextSub);
            var valueLabel = YouPinCcUi.Label(value, 10.5F, FontStyle.Bold, valueColor);
            panel.Controls.AddRange(new Control[] { captionLabel, valueLabel });
            panel.Layout += (_, __) =>
            {
                captionLabel.SetBounds(0, 0, panel.Width, UIUtils.S(20));
                valueLabel.SetBounds(0, UIUtils.S(20), panel.Width, UIUtils.S(28));
            };
            return panel;
        }

        private async Task EditStrategyAsync(YouPinGridStrategy? strategy)
        {
            IReadOnlyList<YouPinInventoryItem> inventoryItems = _inventory.GetState().Items;
            if (!YouPinGridStrategyDialog.TryShow(FindForm(), strategy, inventoryItems, out YouPinGridStrategy result))
                return;

            YouPinGridMutationResult mutation = await _gridTrading.UpsertStrategyAsync(result, PageToken);
            if (!mutation.Succeeded)
            {
                ShowMessage(mutation.Message, MessageBoxIcon.Warning);
                return;
            }

            RequestRefresh(strategy == null ? "新建策略" : "编辑策略");
        }

        private async Task ToggleStrategyAsync(YouPinGridStrategy strategy)
        {
            YouPinGridStrategy copy = CloneStrategy(strategy);
            copy.Enabled = !copy.Enabled;
            YouPinGridMutationResult result = await _gridTrading.UpsertStrategyAsync(copy, PageToken);
            if (!result.Succeeded)
            {
                ShowMessage(result.Message, MessageBoxIcon.Warning);
                return;
            }

            RequestRefresh(copy.Enabled ? "启用策略" : "停用策略");
        }

        private async Task DeleteStrategyAsync(YouPinGridStrategySnapshot row)
        {
            YouPinGridStrategy strategy = row.Strategy;
            DialogResult confirm = GlobalPromptService.Show(
                FindForm(),
                $"确定删除“{strategy.ItemName}”的交易网格策略吗？\n此操作不会影响悠悠库存或已有订单。",
                "删除交易网格策略",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            YouPinGridMutationResult result = await _gridTrading.DeleteStrategyAsync(strategy.Id, PageToken);
            if (!result.Succeeded)
            {
                ShowMessage(result.Message, MessageBoxIcon.Warning);
                return;
            }

            ApplySnapshot(_gridTrading.GetSnapshot());
        }

        private static bool IsExecutionBlocking(YouPinGridExecutionStage stage)
        {
            return stage is YouPinGridExecutionStage.Prepared
                or YouPinGridExecutionStage.AwaitingSettlement
                or YouPinGridExecutionStage.RequiresManualReview;
        }

        private void ShowMessage(string message, MessageBoxIcon icon)
        {
            GlobalPromptService.Show(
                FindForm(),
                string.IsNullOrWhiteSpace(message) ? "操作失败，请查看日志。" : message,
                "交易网格",
                MessageBoxButtons.OK,
                icon);
        }

        private void HandleRefreshFailure(Exception _)
        {
            if (IsDisposed)
                return;

            void ApplyFailure()
            {
                if (IsDisposed)
                    return;
                _refreshButton.Enabled = true;
                _refreshButton.Text = "刷新行情";
                _statusLabel.Text = "刷新失败，请检查悠悠登录状态或网络。";
                _statusLabel.ForeColor = UIColors.TextWarn;
            }

            try
            {
                if (InvokeRequired)
                    BeginInvoke((Action)ApplyFailure);
                else
                    ApplyFailure();
            }
            catch (ObjectDisposedException)
            {
                // Page closed while the queued UI update was being scheduled.
            }
            catch (InvalidOperationException)
            {
                // The WinForms handle can disappear during a fast tab switch.
            }
        }

        private void OnDataUpdated()
        {
            if (IsDisposed || !_subscribed)
                return;

            try
            {
                if (IsHandleCreated)
                    BeginInvoke((Action)(() => ApplySnapshot(_gridTrading.GetSnapshot())));
            }
            catch (ObjectDisposedException)
            {
                // Page closed while the service event was being marshalled.
            }
            catch (InvalidOperationException)
            {
                // The next activation renders the latest service snapshot.
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;
            _gridTrading.DataUpdated += OnDataUpdated;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;
            _gridTrading.DataUpdated -= OnDataUpdated;
            _subscribed = false;
        }

        private static void AddMetric(Control host, string caption, Label value)
        {
            host.Controls.Add(YouPinCcUi.Label(caption, 8.5F, FontStyle.Regular, UIColors.TextSub));
            host.Controls.Add(value);
        }

        private static void LayoutMetrics(Control host)
        {
            int count = 4;
            int pad = UIUtils.S(18);
            int contentWidth = Math.Max(1, host.Width - pad * 2);
            int columnWidth = Math.Max(UIUtils.S(120), contentWidth / count);
            for (int index = 0; index < count; index++)
            {
                Control caption = host.Controls[index * 2];
                Control value = host.Controls[index * 2 + 1];
                int left = pad + index * columnWidth;
                caption.SetBounds(left, UIUtils.S(11), columnWidth - UIUtils.S(10), UIUtils.S(20));
                value.SetBounds(left, UIUtils.S(30), columnWidth - UIUtils.S(10), UIUtils.S(28));
            }
        }

        private static Label CreateMetricValue()
        {
            return YouPinCcUi.Label("0", 13F, FontStyle.Bold, UIColors.TextMain);
        }

        private static string FormatMoney(decimal value)
        {
            return value > 0m
                ? "¥ " + value.ToString("0.00", CultureInfo.InvariantCulture)
                : "--";
        }

        private static Color ResolveAccent(YouPinGridStrategySnapshot row)
        {
            if (row.Execution.Stage == YouPinGridExecutionStage.RequiresManualReview
                || row.Execution.Stage == YouPinGridExecutionStage.Failed)
            {
                return UIColors.TextCrit;
            }
            if (row.Execution.Stage == YouPinGridExecutionStage.Prepared
                || row.Execution.Stage == YouPinGridExecutionStage.AwaitingSettlement)
            {
                return UIColors.TextWarn;
            }
            if (row.Execution.Stage == YouPinGridExecutionStage.Completed)
                return UIColors.Positive;
            if (!row.MarketQuote.Available && row.MarketQuote.CapturedAt != DateTime.MinValue)
                return UIColors.TextCrit;
            return row.Plan.Action == YouPinGridAction.None ? UIColors.Border : UIColors.TextWarn;
        }

        private static Color ResolveStatusColor(YouPinGridStrategySnapshot row)
        {
            if (row.Execution.Stage == YouPinGridExecutionStage.RequiresManualReview
                || row.Execution.Stage == YouPinGridExecutionStage.Failed)
            {
                return UIColors.TextCrit;
            }
            if (row.Execution.Stage == YouPinGridExecutionStage.Completed)
                return UIColors.Positive;
            if (row.Execution.Stage == YouPinGridExecutionStage.Prepared
                || row.Execution.Stage == YouPinGridExecutionStage.AwaitingSettlement)
            {
                return UIColors.TextWarn;
            }
            if (!row.MarketQuote.Available && row.MarketQuote.CapturedAt != DateTime.MinValue)
                return UIColors.TextWarn;
            return row.Plan.Action == YouPinGridAction.None ? UIColors.TextSub : UIColors.TextWarn;
        }

        private static string ResolveActionText(YouPinGridStrategySnapshot row)
        {
            switch (row.Execution.Stage)
            {
                case YouPinGridExecutionStage.Prepared:
                case YouPinGridExecutionStage.AwaitingSettlement:
                    return "等待回读";
                case YouPinGridExecutionStage.Completed:
                    return "已完成";
                case YouPinGridExecutionStage.Failed:
                    return "执行失败";
                case YouPinGridExecutionStage.RequiresManualReview:
                    return "需人工核对";
            }

            return row.Plan.Action switch
            {
                YouPinGridAction.Buy => $"买入 ×{row.Plan.Quantity}",
                YouPinGridAction.Sell => $"卖出 ×{row.Plan.Quantity}",
                _ when !row.MarketQuote.Available && row.MarketQuote.CapturedAt != DateTime.MinValue => "行情不可用",
                _ => "等待触发"
            };
        }

        private static AutoQuoteBadgeTone ResolveActionTone(YouPinGridStrategySnapshot row)
        {
            switch (row.Execution.Stage)
            {
                case YouPinGridExecutionStage.Prepared:
                case YouPinGridExecutionStage.AwaitingSettlement:
                    return AutoQuoteBadgeTone.Warn;
                case YouPinGridExecutionStage.Completed:
                    return AutoQuoteBadgeTone.Success;
                case YouPinGridExecutionStage.Failed:
                case YouPinGridExecutionStage.RequiresManualReview:
                    return AutoQuoteBadgeTone.Critical;
            }

            if (!row.MarketQuote.Available && row.MarketQuote.CapturedAt != DateTime.MinValue)
                return AutoQuoteBadgeTone.Critical;
            return row.Plan.Action == YouPinGridAction.None
                ? AutoQuoteBadgeTone.Subtle
                : AutoQuoteBadgeTone.Warn;
        }

        private static YouPinGridStrategy CloneStrategy(YouPinGridStrategy strategy)
        {
            return new YouPinGridStrategy
            {
                Id = strategy.Id,
                ItemName = strategy.ItemName,
                TemplateId = strategy.TemplateId,
                Enabled = strategy.Enabled,
                ObserveOnly = strategy.ObserveOnly,
                BasePrice = strategy.BasePrice,
                GridPercent = strategy.GridPercent,
                QuantityPerGrid = strategy.QuantityPerGrid,
                MinimumPrice = strategy.MinimumPrice,
                MaximumPrice = strategy.MaximumPrice,
                MinimumHoldings = strategy.MinimumHoldings,
                MaxHoldings = strategy.MaxHoldings,
                MaxCapital = strategy.MaxCapital,
                CrossGridMultiplierEnabled = strategy.CrossGridMultiplierEnabled,
                MaxBatchQuantity = strategy.MaxBatchQuantity
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Unsubscribe();
            base.Dispose(disposing);
        }
    }
}
