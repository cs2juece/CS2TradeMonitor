using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    public sealed class SteamOfferRedesignHostPage : FrameworkSettingsHostPage<SteamOfferRedesignPage>
    {
        public SteamOfferRedesignHostPage()
            : base(new SteamOfferRedesignPage(SteamOfferPageRuntimeServices.Resolve()))
        {
        }
    }

    public sealed class SteamOfferRedesignPage : FrameworkSettingsPageBase, ISettingsSubRouteHost
    {
        private const string QuoteTab = "Quote";
        private const string AutoTab = "Auto";
        private const string SettingsTab = "Settings";

        private readonly SteamOfferPageRuntimeServices _runtimeServices;
        private readonly SteamOfferPagePresenter _presenter;
        private readonly SteamOfferCredentialCoordinator _credentialCoordinator;
        private readonly System.Windows.Forms.Timer _autoRefreshTimer;
        private readonly List<SteamOfferRuleCard> _ruleCards = new();
        private readonly List<ToggleSwitch> _autoRefreshSwitches = new();
        private readonly List<LiteNumberInput> _refreshMinutesInputs = new();
        private readonly List<Label> _tabs = new();
        private readonly Dictionary<string, Control> _contentPanels = new(StringComparer.OrdinalIgnoreCase);

        private TableLayoutPanel? _root;
        private Label? _titleStatusLabel;
        private Label? _tokenStatusLabel;
        private Label? _loginStatusLabel;
        private Label? _encryptionStatusLabel;
        private Label? _autoRefreshStatusLabel;
        private Label? _networkStatusLabel;
        private Label? _operationStatusLabel;
        private Label? _offerStatusLabel;
        private Label? _singleConfirmStatusLabel;
        private Label? _batchConfirmStatusLabel;
        private Label? _confirmReminderHelpLabel;
        private Label? _autoTradeStatusLabel;
        private Label? _autoTradeLastProcessLabel;
        private Label? _autoTradeNextCheckLabel;
        private Label? _autoTradeTodaySuccessLabel;
        private Label? _autoTradeTodayFailureLabel;
        private LiteCheck? _acceptPureIncomingCheck;
        private LiteCheck? _acceptYouPinPurchaseCheck;
        private LiteCheck? _sendYouPinSaleCheck;
        private LiteCheck? _sendYouPinRentalCheck;
        private Panel? _autoTradeRecordTable;
        private LiteButton? _acceptAllButton;
        private SteamOfferListSurface? _offerListSurface;
        private Control? _quoteTabPanel;
        private string _activeTab = QuoteTab;
        private string _expandedOfferId = "";
        private bool _busy;
        private bool _updating;
        private bool _headerRefreshInProgress;
        private bool _operationStatusTracksOfferState;
        private bool _widthSyncQueued;
        private bool _disposed;

        private Rectangle ContentBounds
        {
            get
            {
                int viewportWidth = FrameworkSettingsPageLayoutHelper.CalculateVisibleWidthWithinForm(Container);
                return FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(
                    viewportWidth,
                    Container.Padding,
                    FrameworkSettingsPageLayoutHelper.StandardContentMinimumWidth);
            }
        }
        private int ContentWidth => ContentBounds.Width;

        internal SteamOfferRedesignPage(SteamOfferPageRuntimeServices runtimeServices)
        {
            _runtimeServices = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            _presenter = new SteamOfferPagePresenter(runtimeServices);
            _credentialCoordinator = new SteamOfferCredentialCoordinator(
                runtimeServices,
                FindForm,
                () => { },
                RefreshHeader,
                (_, __) => RenderOffers(),
                prompt => ShowCredentialConfirmation(prompt),
                SetOperationStatus,
                _presenter.ClearTokenSecrets,
                _presenter.ClearLoginState,
                (sessionId, steamLoginSecure, steamLogin, accessToken, refreshToken, steamId) =>
                    _presenter.UpdateSession(sessionId, steamLoginSecure, steamLogin, accessToken: accessToken, refreshToken: refreshToken, steamId: steamId),
                _presenter);
            _autoRefreshTimer = new System.Windows.Forms.Timer();
            _autoRefreshTimer.Tick += async (_, __) => await RefreshOffersAsync(false);
            _presenter.OfferDataUpdated += OnOfferDataUpdated;
            _presenter.ConnectionStatusChanged += OnConnectionStatusChanged;
            Container.SizeChanged += (_, __) => QueueDeferredContentWidthSync();
        }

        protected override void OnStoreAttached()
        {
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            SyncContentWidth();
            RefreshHeader();
            RenderOffers();
            SyncContentWidth();
            QueueDeferredContentWidthSync();
            ApplyAutoRefreshTimer();
            _presenter.ApplyAutoTradeSettings(BuildAutoTradeSettingsFromStore());
        }

        public override void Deactivate()
        {
            _autoRefreshTimer.Stop();
            base.Deactivate();
        }

        public override void Save()
        {
            base.Save();
            SaveAutoRefreshSettings();
            Set(nameof(Settings.SteamOfferRedesignRule), SteamOfferRedesignModel.ToSettingValue(CurrentRule));
            SaveAutoTradeSettings();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            SyncContentWidth();
        }

        protected override int GetTopLevelContentWidth()
        {
            return ContentWidth;
        }

        public bool SwitchSubRoute(string subRoute)
        {
            if (!TryResolveTab(subRoute, out string tab))
                return false;

            _activeTab = tab;
            UpdateTabSelection();
            SyncContentWidth();
            QueueDeferredContentWidthSync();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _autoRefreshTimer.Dispose();
                _presenter.OfferDataUpdated -= OnOfferDataUpdated;
                _presenter.ConnectionStatusChanged -= OnConnectionStatusChanged;
            }

            base.Dispose(disposing);
        }

        private SteamOfferRedesignRule CurrentRule
            => SteamOfferRedesignModel.ParseRule(Get(nameof(Settings.SteamOfferRedesignRule), SteamOfferRedesignModel.DefaultRuleKey));

        private void BuildPage()
        {
            ClearPage();
            _ruleCards.Clear();
            _autoRefreshSwitches.Clear();
            _refreshMinutesInputs.Clear();
            _tabs.Clear();
            _contentPanels.Clear();
            Rectangle bounds = ContentBounds;

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                AutoSize = false,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Container.Controls.Add(_root);

            AddRootRow(CreateHeaderPanel());
            AddRootRow(CreateStatusPanel());
            AddRootRow(CreateTabHeader());
            AddRootRow(CreateContentHost());
            RefreshFromStore();
            RefreshHeader();
            RenderOffers();
            QueueDeferredContentWidthSync();
        }

        private Control CreateHeaderPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(58),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(4))
            };
            var avatar = new SteamOfferAvatar();
            var title = CreateTextLabel("Steam 报价", 16F, FontStyle.Bold, UIColors.TextMain);
            _titleStatusLabel = CreateBadgeLabel("已保存", UIColors.Positive);
            var account = CreateTextLabel("", 9F, FontStyle.Regular, UIColors.TextSub);
            account.Name = "AccountLabel";

            var btnCheck = new LiteButton("刷新/校验", false) { Width = UIUtils.S(104), Height = UIUtils.S(36) };
            var btnManage = new LiteButton("令牌管理", true) { Width = UIUtils.S(112), Height = UIUtils.S(36) };
            btnCheck.Click += async (_, __) => await RefreshOffersAsync(false);
            btnManage.Click += (_, __) => _credentialCoordinator.ShowAuthDialog();

            panel.Controls.AddRange(new Control[] { avatar, title, _titleStatusLabel, account, btnCheck, btnManage });
            panel.Layout += (_, __) =>
            {
                int visibleWidth = GetVisibleLayoutWidth(panel);
                int y = UIUtils.S(8);
                avatar.SetBounds(0, y, UIUtils.S(42), UIUtils.S(42));
                title.SetBounds(avatar.Right + UIUtils.S(14), y + UIUtils.S(1), UIUtils.S(138), UIUtils.S(34));
                _titleStatusLabel.SetBounds(title.Right + UIUtils.S(8), y + UIUtils.S(8), UIUtils.S(64), UIUtils.S(24));
                btnManage.SetBounds(visibleWidth - btnManage.Width, y + UIUtils.S(2), btnManage.Width, btnManage.Height);
                btnCheck.SetBounds(btnManage.Left - UIUtils.S(12) - btnCheck.Width, btnManage.Top, btnCheck.Width, btnCheck.Height);
                account.SetBounds(_titleStatusLabel.Right + UIUtils.S(20), y + UIUtils.S(8), Math.Max(1, btnCheck.Left - _titleStatusLabel.Right - UIUtils.S(32)), UIUtils.S(24));
            };
            return panel;
        }

        private Control CreateStatusPanel()
        {
            var card = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(82),
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(8))
            };

            _tokenStatusLabel = CreateStatusValueLabel();
            _loginStatusLabel = CreateStatusValueLabel();
            _encryptionStatusLabel = CreateStatusValueLabel();
            _autoRefreshStatusLabel = CreateStatusValueLabel();
            _networkStatusLabel = CreateStatusValueLabel();
            var tokenBlock = CreateMetricBlock("令牌", _tokenStatusLabel);
            var loginBlock = CreateMetricBlock("登录状态", _loginStatusLabel);
            var encryptionBlock = CreateMetricBlock("加密", _encryptionStatusLabel);
            var autoBlock = CreateMetricBlock("自动刷新", _autoRefreshStatusLabel);
            var networkBlock = CreateMetricBlock("Steam 网络", _networkStatusLabel);

            card.Controls.AddRange(new Control[]
            {
                tokenBlock, loginBlock, encryptionBlock, autoBlock, networkBlock
            });
            card.Layout += (_, __) =>
            {
                int visibleWidth = GetVisibleLayoutWidth(card);
                int pad = UIUtils.S(18);
                int gap = UIUtils.S(10);
                int blockW = Math.Max(UIUtils.S(120), (visibleWidth - pad * 2 - gap * 4) / 5);
                int top = UIUtils.S(20);
                Control[] blocks = { tokenBlock, loginBlock, encryptionBlock, autoBlock, networkBlock };
                int x = pad;
                foreach (Control block in blocks)
                {
                    block.SetBounds(x, top, blockW, UIUtils.S(42));
                    x += blockW + gap;
                }
            };
            return card;
        }

        private Control CreateTabHeader()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(48),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(10))
            };
            AddTab(panel, QuoteTab, "报价列表", 0, UIUtils.S(96));
            AddTab(panel, AutoTab, "自动报价", UIUtils.S(118), UIUtils.S(96));
            AddTab(panel, SettingsTab, "设置", UIUtils.S(236), UIUtils.S(74));
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
            };
            return panel;
        }

        private Control CreateContentHost()
        {
            var host = new Panel
            {
                Height = UIUtils.S(640),
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty
            };
            var quote = CreateQuoteTabContent();
            var auto = CreateAutoTabContent();
            var settings = CreateSettingsTabContent();
            _contentPanels[QuoteTab] = quote;
            _contentPanels[AutoTab] = auto;
            _contentPanels[SettingsTab] = settings;
            host.Controls.Add(settings);
            host.Controls.Add(auto);
            host.Controls.Add(quote);
            host.Layout += (_, __) =>
            {
                foreach (Control panel in host.Controls)
                    panel.SetBounds(0, 0, host.Width, host.Height);
            };
            UpdateTabSelection();
            return host;
        }

        private Control CreateQuoteTabContent()
        {
            var panel = new Panel { BackColor = UIColors.MainBg };
            _quoteTabPanel = panel;
            var list = CreateOfferListCard();
            panel.Controls.Add(list);
            panel.Layout += (_, __) =>
            {
                int listHeight = CalculateOfferListCardHeight(panel.Height);
                list.SetBounds(0, 0, panel.Width, listHeight);
            };
            return panel;
        }

        private int CalculateOfferListCardHeight(int contentHostHeight)
        {
            int surfaceHeight = _offerListSurface?.GetDesiredSurfaceHeight() ?? SteamOfferListSurface.EmptySurfaceHeight;
            int minimumHeight = UIUtils.S(102) + surfaceHeight + UIUtils.S(20);
            return SteamOfferRedesignModel.CalculateExpandedOfferListCardHeight(
                contentHostHeight,
                minimumHeight,
                UIUtils.S(24));
        }

        private Control CreateAutoTabContent()
        {
            var panel = new Panel { BackColor = UIColors.MainBg };
            var rules = CreateAutoRulesCard(compact: false);
            panel.Controls.Add(rules);
            panel.Layout += (_, __) => rules.SetBounds(0, 0, panel.Width, UIUtils.S(482));
            return panel;
        }

        private Control CreateSettingsTabContent()
        {
            var card = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                BackColor = Color.Transparent
            };
            var title = CreateTextLabel("设置", 12F, FontStyle.Bold, UIColors.TextMain);
            var help = CreateTextLabel("Steam 报价重构页只保存显示规则、刷新间隔和二次确认提醒偏好；令牌和登录状态仍沿用原 Steam 模块。", 9F, FontStyle.Regular, UIColors.TextSub);
            var statusTitle = CreateTextLabel("二次确认提醒", 9.5F, FontStyle.Bold, UIColors.TextMain);
            _singleConfirmStatusLabel = CreateTextLabel("", 8.8F, FontStyle.Regular, UIColors.TextSub);
            _batchConfirmStatusLabel = CreateTextLabel("", 8.8F, FontStyle.Regular, UIColors.TextSub);
            _confirmReminderHelpLabel = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextWarn);
            var resetSingle = new LiteButton("恢复单条确认提醒", false) { Width = UIUtils.S(152), Height = UIUtils.S(34) };
            var resetBatch = new LiteButton("恢复一键确认提醒", false) { Width = UIUtils.S(152), Height = UIUtils.S(34) };
            resetSingle.Click += (_, __) =>
            {
                Set(nameof(Settings.SteamOfferRedesignSkipSingleConfirm), false);
                RefreshConfirmReminderStatus();
                SetOperationStatus("已恢复单条同意报价二次确认。", SteamOfferOperationStatusTone.Success);
            };
            resetBatch.Click += (_, __) =>
            {
                Set(nameof(Settings.SteamOfferRedesignSkipBatchConfirm), false);
                RefreshConfirmReminderStatus();
                SetOperationStatus("已恢复一键同意报价二次确认。", SteamOfferOperationStatusTone.Success);
            };
            card.Controls.AddRange(new Control[] { title, help, statusTitle, _singleConfirmStatusLabel, _batchConfirmStatusLabel, _confirmReminderHelpLabel, resetSingle, resetBatch });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(20);
                title.SetBounds(pad, UIUtils.S(18), UIUtils.S(260), UIUtils.S(28));
                help.SetBounds(pad, title.Bottom + UIUtils.S(8), Math.Max(1, card.Width - pad * 2), UIUtils.S(32));
                statusTitle.SetBounds(pad, help.Bottom + UIUtils.S(18), UIUtils.S(150), UIUtils.S(24));
                _singleConfirmStatusLabel.SetBounds(pad, statusTitle.Bottom + UIUtils.S(4), Math.Max(1, card.Width - pad * 2), UIUtils.S(22));
                _batchConfirmStatusLabel.SetBounds(pad, _singleConfirmStatusLabel.Bottom + UIUtils.S(2), Math.Max(1, card.Width - pad * 2), UIUtils.S(22));
                _confirmReminderHelpLabel.SetBounds(pad, _batchConfirmStatusLabel.Bottom + UIUtils.S(8), Math.Max(1, card.Width - pad * 2), UIUtils.S(24));
                resetSingle.SetBounds(pad, _confirmReminderHelpLabel.Bottom + UIUtils.S(22), resetSingle.Width, resetSingle.Height);
                resetBatch.SetBounds(resetSingle.Right + UIUtils.S(12), resetSingle.Top, resetBatch.Width, resetBatch.Height);
            };
            RegisterRefresh(RefreshConfirmReminderStatus);
            RefreshConfirmReminderStatus();
            return card;
        }

        private Control CreateOfferListCard()
        {
            var card = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                BackColor = Color.Transparent
            };
            var title = CreateTextLabel("报价列表", 12F, FontStyle.Bold, UIColors.TextMain);
            _offerStatusLabel = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub);
            _operationStatusLabel = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var hint = CreateTextLabel("默认只显示收到或失去的物品，单击报价查看详情。", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var refresh = new LiteButton("刷新报价", true) { Width = UIUtils.S(112), Height = UIUtils.S(36) };
            var acceptAll = new LiteButton("一键同意所有报价", false) { Width = UIUtils.S(162), Height = UIUtils.S(36), ForeColor = UIColors.TextWarn };
            _acceptAllButton = acceptAll;
            var open = new LiteButton("打开 Steam", false) { Width = UIUtils.S(116), Height = UIUtils.S(36) };
            _offerListSurface = new SteamOfferListSurface(ToggleOfferDetails, AcceptSingleOfferAsync);
            refresh.Click += async (_, __) => await RefreshOffersAsync(false);
            acceptAll.Click += async (_, __) => await AcceptBatchAsync();
            open.Click += (_, __) => OpenSteamOffersPage();

            card.Controls.AddRange(new Control[] { title, _offerStatusLabel, _operationStatusLabel, hint, refresh, acceptAll, open, _offerListSurface });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int top = UIUtils.S(14);
                open.SetBounds(card.Width - pad - open.Width, UIUtils.S(12), open.Width, open.Height);
                acceptAll.SetBounds(open.Left - UIUtils.S(12) - acceptAll.Width, open.Top, acceptAll.Width, acceptAll.Height);
                refresh.SetBounds(acceptAll.Left - UIUtils.S(12) - refresh.Width, open.Top, refresh.Width, refresh.Height);
                title.SetBounds(pad, top, UIUtils.S(104), UIUtils.S(28));
                int statusTop = Math.Max(title.Bottom, open.Bottom) + UIUtils.S(6);
                _offerStatusLabel.SetBounds(pad, statusTop, Math.Max(1, card.Width - pad * 2), UIUtils.S(24));
                hint.SetBounds(pad, _offerStatusLabel.Bottom + UIUtils.S(2), Math.Max(1, card.Width - pad * 2), UIUtils.S(22));
                _operationStatusLabel.SetBounds(pad, hint.Bottom + UIUtils.S(2), Math.Max(1, card.Width - pad * 2), UIUtils.S(20));
                _offerListSurface.SetBounds(pad, _operationStatusLabel.Bottom + UIUtils.S(8), Math.Max(1, card.Width - pad * 2), Math.Max(1, card.Height - _operationStatusLabel.Bottom - UIUtils.S(22)));
            };
            return card;
        }

        private Control CreateAutoRulesCard(bool compact)
        {
            var card = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                BackColor = Color.Transparent
            };
            var title = CreateTextLabel("自动处理交易", 12F, FontStyle.Bold, UIColors.TextMain);
            var statusTitle = CreateTextLabel("状态", 9F, FontStyle.Regular, UIColors.TextSub);
            _autoTradeStatusLabel = CreateBadgeLabel("已关闭", UIColors.TextSub);
            var lastTitle = CreateTextLabel("上次处理", 9F, FontStyle.Regular, UIColors.TextSub);
            _autoTradeLastProcessLabel = CreateTextLabel("暂无", 9F, FontStyle.Regular, UIColors.TextSub);
            var nextTitle = CreateTextLabel("下次检查", 9F, FontStyle.Regular, UIColors.TextSub);
            _autoTradeNextCheckLabel = CreateTextLabel("暂无", 9F, FontStyle.Regular, UIColors.TextSub);
            var successTitle = CreateTextLabel("今日成功", 9F, FontStyle.Regular, UIColors.TextSub);
            _autoTradeTodaySuccessLabel = CreateTextLabel("0", 9F, FontStyle.Regular, UIColors.TextMain);
            var failureTitle = CreateTextLabel("今日失败", 9F, FontStyle.Regular, UIColors.TextSub);
            _autoTradeTodayFailureLabel = CreateTextLabel("0", 9F, FontStyle.Regular, UIColors.TextCrit);
            var openLog = new LiteButton("打开日志", false) { Width = UIUtils.S(122), Height = UIUtils.S(34) };
            openLog.Click += (_, __) => OpenSteamOfferLogFile();

            var receiveGroup = CreateAutoTradeRuleGroup(
                "自动接收报价",
                out _acceptPureIncomingCheck,
                "自动接收纯收货报价",
                "我方只收到饰品、未付出饰品，且未匹配悠悠订单。",
                out _acceptYouPinPurchaseCheck,
                "自动接收悠悠购买报价",
                "匹配悠悠购买订单后自动接收收货报价。");
            var sendGroup = CreateAutoTradeRuleGroup(
                "自动发送报价",
                out _sendYouPinSaleCheck,
                "自动发送悠悠出售报价",
                "匹配悠悠出售订单后自动发送报价。",
                out _sendYouPinRentalCheck,
                "自动发送悠悠出租报价",
                "匹配悠悠出租订单后自动发送租赁报价。");
            var recordTitle = CreateTextLabel("自动处理记录", 11F, FontStyle.Bold, UIColors.TextMain);
            var recordHint = CreateTextLabel("默认显示最新 5 条，完整记录请打开日志查看。", 8.5F, FontStyle.Regular, UIColors.TextSub);
            _autoTradeRecordTable = new Panel { BackColor = UIColors.ControlBg };
            _autoTradeRecordTable.Paint += (_, e) => PaintAutoTradeRecordTable(e.Graphics, _autoTradeRecordTable.ClientRectangle);
            _autoTradeRecordTable.Layout += LayoutAutoTradeRecordTable;

            void bind(LiteCheck check)
            {
                check.CheckedChanged += (_, __) =>
                {
                    if (_updating) return;
                    SteamAutoTradeSettings settings = SaveAndPersistAutoTradeSettings();
                    _presenter.ApplyAutoTradeSettings(settings);
                    RefreshAutoTradePanel();
                };
            }
            bind(_acceptPureIncomingCheck);
            bind(_acceptYouPinPurchaseCheck);
            bind(_sendYouPinSaleCheck);
            bind(_sendYouPinRentalCheck);

            card.Controls.AddRange(new Control[]
            {
                title, statusTitle, _autoTradeStatusLabel, lastTitle, _autoTradeLastProcessLabel,
                nextTitle, _autoTradeNextCheckLabel, successTitle, _autoTradeTodaySuccessLabel,
                failureTitle, _autoTradeTodayFailureLabel, openLog, receiveGroup, sendGroup,
                recordTitle, recordHint, _autoTradeRecordTable
            });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int top = UIUtils.S(12);
                openLog.SetBounds(card.Width - pad - openLog.Width, UIUtils.S(8), openLog.Width, openLog.Height);
                title.SetBounds(pad, top, UIUtils.S(158), UIUtils.S(30));
                statusTitle.SetBounds(title.Right + UIUtils.S(12), top + UIUtils.S(3), UIUtils.S(40), UIUtils.S(24));
                _autoTradeStatusLabel.SetBounds(statusTitle.Right + UIUtils.S(6), top + UIUtils.S(1), UIUtils.S(72), UIUtils.S(28));
                lastTitle.SetBounds(_autoTradeStatusLabel.Right + UIUtils.S(12), top + UIUtils.S(3), UIUtils.S(66), UIUtils.S(24));
                _autoTradeLastProcessLabel.SetBounds(lastTitle.Right + UIUtils.S(6), top + UIUtils.S(3), UIUtils.S(78), UIUtils.S(24));
                nextTitle.SetBounds(_autoTradeLastProcessLabel.Right + UIUtils.S(18), top + UIUtils.S(3), UIUtils.S(70), UIUtils.S(24));
                _autoTradeNextCheckLabel.SetBounds(nextTitle.Right + UIUtils.S(6), top + UIUtils.S(3), UIUtils.S(96), UIUtils.S(24));
                int statsTop = top + UIUtils.S(34);
                successTitle.SetBounds(pad, statsTop, UIUtils.S(70), UIUtils.S(24));
                _autoTradeTodaySuccessLabel.SetBounds(successTitle.Right + UIUtils.S(6), statsTop, UIUtils.S(54), UIUtils.S(24));
                failureTitle.SetBounds(_autoTradeTodaySuccessLabel.Right + UIUtils.S(18), statsTop, UIUtils.S(70), UIUtils.S(24));
                _autoTradeTodayFailureLabel.SetBounds(failureTitle.Right + UIUtils.S(6), statsTop, UIUtils.S(54), UIUtils.S(24));

                int groupTop = UIUtils.S(78);
                int gap = UIUtils.S(12);
                int groupWidth = Math.Max(UIUtils.S(300), (card.Width - pad * 2 - gap) / 2);
                receiveGroup.SetBounds(pad, groupTop, groupWidth, UIUtils.S(166));
                sendGroup.SetBounds(receiveGroup.Right + gap, groupTop, Math.Max(UIUtils.S(300), card.Width - pad - receiveGroup.Right - gap), UIUtils.S(166));
                recordTitle.SetBounds(pad, receiveGroup.Bottom + UIUtils.S(12), UIUtils.S(136), UIUtils.S(28));
                recordHint.SetBounds(recordTitle.Right + UIUtils.S(8), recordTitle.Top + UIUtils.S(3), UIUtils.S(360), UIUtils.S(22));
                _autoTradeRecordTable.SetBounds(pad, recordTitle.Bottom + UIUtils.S(6), Math.Max(1, card.Width - pad * 2), UIUtils.S(184));
            };
            RegisterRefresh(() =>
            {
                _updating = true;
                try
                {
                    RefreshAutoTradeChecks();
                    RefreshAutoTradePanel();
                }
                finally
                {
                    _updating = false;
                }
            });
            RegisterSave(SaveAutoTradeSettings);
            RefreshAutoTradeChecks();
            RefreshAutoTradePanel();
            return card;
        }

        private Control CreateAutoTradeRuleGroup(
            string titleText,
            out LiteCheck firstCheck,
            string firstTitle,
            string firstDescription,
            out LiteCheck secondCheck,
            string secondTitle,
            string secondDescription)
        {
            var group = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(4),
                FillOverride = UIColors.ControlBg,
                BackColor = Color.Transparent
            };
            var title = CreateTextLabel(titleText, 10F, FontStyle.Bold, UIColors.TextMain);
            var first = new LiteCheck(false, firstTitle) { Width = UIUtils.S(260), Height = UIUtils.S(24) };
            var firstHint = CreateTextLabel(firstDescription, 8.5F, FontStyle.Regular, UIColors.TextSub);
            var second = new LiteCheck(false, secondTitle) { Width = UIUtils.S(260), Height = UIUtils.S(24) };
            var secondHint = CreateTextLabel(secondDescription, 8.5F, FontStyle.Regular, UIColors.TextSub);
            firstCheck = first;
            secondCheck = second;
            group.Controls.AddRange(new Control[] { title, first, firstHint, second, secondHint });
            group.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                title.SetBounds(pad, UIUtils.S(12), Math.Max(1, group.Width - pad * 2), UIUtils.S(28));
                int firstTop = UIUtils.S(54);
                first.SetBounds(pad, firstTop, Math.Min(UIUtils.S(280), group.Width - pad * 2), UIUtils.S(24));
                firstHint.SetBounds(first.Left + UIUtils.S(26), first.Bottom + UIUtils.S(2), Math.Max(1, group.Width - first.Left - pad), UIUtils.S(22));
                int secondTop = UIUtils.S(100);
                second.SetBounds(pad, secondTop, Math.Min(UIUtils.S(280), group.Width - pad * 2), UIUtils.S(24));
                secondHint.SetBounds(second.Left + UIUtils.S(26), second.Bottom + UIUtils.S(2), Math.Max(1, group.Width - second.Left - pad), UIUtils.S(22));
            };
            return group;
        }

        private SteamAutoTradeSettings BuildAutoTradeSettingsFromStore()
        {
            SteamAutoTradeSettings settings = ReadPersistedAutoTradeSettings();
            ApplyAutoTradeSettingsToStore(settings);
            return settings;
        }

        private SteamAutoTradeSettings SaveAndPersistAutoTradeSettings()
        {
            SteamAutoTradeSettings settings = CaptureAutoTradeSettingsFromControls();
            ApplyAutoTradeSettingsToStore(settings);
            PersistAutoTradeSettings(settings);
            return settings;
        }

        private void SaveAutoTradeSettings()
        {
            ApplyAutoTradeSettingsToStore(CaptureAutoTradeSettingsFromControls());
        }

        private SteamAutoTradeSettings CaptureAutoTradeSettingsFromControls()
        {
            return SteamAutoTradeProjection.BuildSteamSettings(
                _acceptPureIncomingCheck?.Checked == true,
                _acceptYouPinPurchaseCheck?.Checked == true,
                _sendYouPinSaleCheck?.Checked == true,
                _sendYouPinRentalCheck?.Checked == true,
                Get(
                    nameof(Settings.SteamAutoTradeIntervalSeconds),
                    Get(nameof(Settings.SteamOfferRedesignRefreshMinutes), 5) * 60));
        }

        private void ApplyAutoTradeSettingsToStore(SteamAutoTradeSettings settings)
        {
            SteamAutoTradeSettings normalized = SteamAutoTradeSettingsPersistence.Normalize(settings);
            Set(nameof(Settings.SteamAutoTradeAcceptPureIncomingEnabled), normalized.AcceptPureIncomingEnabled);
            Set(nameof(Settings.SteamAutoTradeAcceptYouPinPurchaseEnabled), normalized.AcceptYouPinPurchaseEnabled);
            Set(nameof(Settings.SteamAutoTradeSendYouPinSaleEnabled), normalized.SendYouPinSaleEnabled);
            Set(nameof(Settings.SteamAutoTradeSendYouPinRentalEnabled), normalized.SendYouPinRentalEnabled);
            Set(nameof(Settings.SteamAutoTradeEnabled), normalized.Enabled);
            Set(nameof(Settings.SteamAutoTradeIntervalSeconds), normalized.IntervalSeconds);
        }

        private void PersistAutoTradeSettings(SteamAutoTradeSettings settings)
        {
            SteamAutoTradeSettings normalized = SteamAutoTradeSettingsPersistence.Normalize(settings);
            SteamAutoTradeSettingsPersistence.ApplyTo(Settings.Load(), normalized);
            SaveSettingsStoreToDisk();
        }

        private void RefreshAutoTradeChecks()
        {
            SteamAutoTradeSettings settings = ReadPersistedAutoTradeSettings();
            ApplyAutoTradeSettingsToStore(settings);

            bool previousUpdating = _updating;
            _updating = true;
            try
            {
                if (_acceptPureIncomingCheck != null)
                    _acceptPureIncomingCheck.Checked = settings.AcceptPureIncomingEnabled;
                if (_acceptYouPinPurchaseCheck != null)
                    _acceptYouPinPurchaseCheck.Checked = settings.AcceptYouPinPurchaseEnabled;
                if (_sendYouPinSaleCheck != null)
                    _sendYouPinSaleCheck.Checked = settings.SendYouPinSaleEnabled;
                if (_sendYouPinRentalCheck != null)
                    _sendYouPinRentalCheck.Checked = settings.SendYouPinRentalEnabled;
            }
            finally
            {
                _updating = previousUpdating;
            }
        }

        private SteamAutoTradeSettings ReadPersistedAutoTradeSettings()
        {
            try
            {
                return SteamAutoTradeSettingsPersistence.ReadFrom(SettingsHelper.Load(forceReload: true));
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamOffer", "Read persisted auto trade settings failed: " + ex.Message);
            }

            if (Config != null)
                return SteamAutoTradeSettingsPersistence.ReadFrom(Config);

            return SteamAutoTradeSettingsPersistence.ReadFrom(Settings.Load());
        }

        private void RefreshAutoTradePanel()
        {
            SteamAutoTradeState state = _presenter.GetOfferState().AutoTrade;
            if (_autoTradeStatusLabel != null)
            {
                _autoTradeStatusLabel.Text = state.StatusText;
                _autoTradeStatusLabel.ForeColor = state.StatusText switch
                {
                    "运行中" => UIColors.Positive,
                    "需要登录" or "最近失败" => UIColors.TextWarn,
                    _ => UIColors.TextSub
                };
            }
            if (_autoTradeLastProcessLabel != null)
                _autoTradeLastProcessLabel.Text = state.LastProcessTime == default ? "暂无" : state.LastProcessTime.ToString("HH:mm:ss");
            if (_autoTradeNextCheckLabel != null)
            {
                if (state.NextCheckTime == default)
                {
                    _autoTradeNextCheckLabel.Text = "暂无";
                }
                else
                {
                    TimeSpan remain = state.NextCheckTime - DateTime.Now;
                    if (remain < TimeSpan.Zero)
                        remain = TimeSpan.Zero;
                    _autoTradeNextCheckLabel.Text = remain.TotalMinutes >= 1
                        ? $"{Math.Max(0, (int)remain.TotalMinutes)} 分 {remain.Seconds:D2} 秒"
                        : $"{remain.Seconds:D2} 秒";
                }
            }
            if (_autoTradeTodaySuccessLabel != null)
                _autoTradeTodaySuccessLabel.Text = state.TodaySuccess.ToString();
            if (_autoTradeTodayFailureLabel != null)
                _autoTradeTodayFailureLabel.Text = state.TodayFailure.ToString();

            RenderAutoTradeRecords(SteamAutoTradeProjection.BuildRecordRows(
                state.RecentRecords,
                SteamAutoTradeProjectionView.SteamOffers));
        }

        private void RenderAutoTradeRecords(IReadOnlyList<SteamAutoTradeRecordRow> records)
        {
            if (_autoTradeRecordTable == null)
                return;

            foreach (Control old in _autoTradeRecordTable.Controls.Cast<Control>().ToList())
                old.Dispose();
            _autoTradeRecordTable.Controls.Clear();

            string[] headers = { "时间", "类型", "方向", "饰品", "来源", "结果" };
            for (int i = 0; i < headers.Length; i++)
            {
                Label label = CreateTextLabel(headers[i], 9F, FontStyle.Bold, UIColors.TextSub);
                label.Tag = "header:" + i.ToString();
                _autoTradeRecordTable.Controls.Add(label);
            }

            for (int row = 0; row < records.Count; row++)
            {
                SteamAutoTradeRecordRow record = records[row];
                string[] values =
                {
                    record.TimeText,
                    record.TypeText,
                    record.DirectionText,
                    record.ItemsText,
                    record.SourceText,
                    record.ResultText
                };
                for (int col = 0; col < values.Length; col++)
                {
                    Label label = CreateTextLabel(values[col], 9F, FontStyle.Regular, GetAutoTradeRecordColor(record, col));
                    label.Tag = $"cell:{row}:{col}";
                    _autoTradeRecordTable.Controls.Add(label);
                }
            }

            LayoutAutoTradeRecordTable(_autoTradeRecordTable, EventArgs.Empty);
            _autoTradeRecordTable.Invalidate();
        }

        private void LayoutAutoTradeRecordTable(object? sender, EventArgs e)
        {
            if (_autoTradeRecordTable == null)
                return;

            int[] widths =
            {
                UIUtils.S(128),
                UIUtils.S(138),
                UIUtils.S(78),
                Math.Max(UIUtils.S(180), _autoTradeRecordTable.Width - UIUtils.S(128 + 138 + 78 + 166 + 236)),
                UIUtils.S(166),
                UIUtils.S(236)
            };
            int rowHeight = UIUtils.S(30);
            foreach (Control control in _autoTradeRecordTable.Controls)
            {
                string tag = control.Tag as string ?? "";
                string[] parts = tag.Split(':');
                if (parts.Length < 2)
                    continue;

                int x = 0;
                if (parts[0] == "header" && int.TryParse(parts[1], out int headerCol))
                {
                    for (int i = 0; i < headerCol; i++)
                        x += widths[i];
                    control.SetBounds(x + UIUtils.S(14), 0, Math.Max(1, widths[headerCol] - UIUtils.S(18)), rowHeight);
                }
                else if (parts[0] == "cell"
                    && parts.Length == 3
                    && int.TryParse(parts[1], out int row)
                    && int.TryParse(parts[2], out int col))
                {
                    for (int i = 0; i < col; i++)
                        x += widths[i];
                    control.SetBounds(x + UIUtils.S(14), rowHeight * (row + 1), Math.Max(1, widths[col] - UIUtils.S(18)), rowHeight);
                }
            }
        }

        private static Color GetAutoTradeRecordColor(SteamAutoTradeRecordRow record, int column)
        {
            if (column == 2)
            {
                return record.Direction == SteamAutoTradeDirection.Incoming
                    ? UIColors.Positive
                    : record.Direction == SteamAutoTradeDirection.Outgoing ? UIColors.TextCrit : UIColors.TextSub;
            }
            if (column == 1 && record.Type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure)
                return UIColors.TextCrit;
            if (column == 5)
            {
                return record.ResultTone switch
                {
                    SteamAutoTradeResultTone.Failure => UIColors.TextCrit,
                    SteamAutoTradeResultTone.Success => UIColors.Positive,
                    SteamAutoTradeResultTone.Warning => UIColors.TextWarn,
                    _ => UIColors.TextMain
                };
            }
            return UIColors.TextMain;
        }

        private static void PaintAutoTradeRecordTable(Graphics graphics, Rectangle bounds)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(UIColors.ControlBg);
            using var border = new Pen(UIColors.Border);
            using var path = UIUtils.RoundRect(new Rectangle(0, 0, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1)), UIUtils.S(4));
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);

            int rowHeight = UIUtils.S(30);
            for (int y = rowHeight; y < bounds.Height; y += rowHeight)
                graphics.DrawLine(border, 0, y, bounds.Width, y);

            int[] widths =
            {
                UIUtils.S(128),
                UIUtils.S(138),
                UIUtils.S(78),
                Math.Max(UIUtils.S(180), bounds.Width - UIUtils.S(128 + 138 + 78 + 166 + 236)),
                UIUtils.S(166),
                UIUtils.S(236)
            };
            int x = 0;
            for (int i = 0; i < widths.Length - 1; i++)
            {
                x += widths[i];
                graphics.DrawLine(border, x, 0, x, bounds.Height);
            }
        }

        private void AddTab(Control parent, string key, string text, int left, int width)
        {
            var label = CreateTextLabel(text, 9.5F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleCenter);
            label.Cursor = Cursors.Hand;
            label.Tag = key;
            label.AccessibleName = "Steam报价-" + text;
            label.AccessibleDescription = "切换到" + text + "标签";
            label.AccessibleRole = AccessibleRole.PushButton;
            label.SetBounds(left, UIUtils.S(6), width, UIUtils.S(38));
            label.Click += (_, __) =>
            {
                SwitchSubRoute(key);
            };
            label.Paint += (_, e) =>
            {
                if (!string.Equals(_activeTab, key, StringComparison.OrdinalIgnoreCase))
                    return;
                using var pen = new Pen(UIColors.Primary, UIUtils.S(3));
                e.Graphics.DrawLine(pen, 0, label.Height - 1, label.Width, label.Height - 1);
            };
            parent.Controls.Add(label);
            _tabs.Add(label);
        }

        private void UpdateTabSelection()
        {
            foreach (Label tab in _tabs)
            {
                string key = tab.Tag as string ?? "";
                bool active = string.Equals(key, _activeTab, StringComparison.OrdinalIgnoreCase);
                tab.Font = new Font("Microsoft YaHei UI", 9.5F, active ? FontStyle.Bold : FontStyle.Regular);
                tab.ForeColor = active ? UIColors.Primary : UIColors.TextSub;
                tab.Invalidate();
            }

            foreach (var pair in _contentPanels)
                pair.Value.Visible = string.Equals(pair.Key, _activeTab, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveTab(string subRoute, out string tab)
        {
            tab = QuoteTab;
            string key = (subRoute ?? string.Empty).Trim();
            if (key.Length == 0)
                return true;

            if (key.Equals("Quote", StringComparison.OrdinalIgnoreCase)
                || key.Equals("List", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Offers", StringComparison.OrdinalIgnoreCase))
            {
                tab = QuoteTab;
                return true;
            }

            if (key.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Automation", StringComparison.OrdinalIgnoreCase))
            {
                tab = AutoTab;
                return true;
            }

            if (key.Equals("Settings", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Setting", StringComparison.OrdinalIgnoreCase))
            {
                tab = SettingsTab;
                return true;
            }

            return false;
        }

        private async Task RefreshOffersAsync(bool useMock)
        {
            if (_busy || _disposed)
                return;

            _busy = true;
            UpdateOfferActionButtons();
            SetOperationStatus("正在刷新 Steam 报价...", SteamOfferOperationStatusTone.Muted);
            try
            {
                SteamOfferActionResult result = await _presenter.LoadOffersAsync(useMock);
                SetOfferStateOperationStatus(result.Message, result.Ok ? SteamOfferOperationStatusTone.Success : SteamOfferOperationStatusTone.Warning);
                RefreshHeader();
                RenderOffers();
            }
            finally
            {
                _busy = false;
                UpdateOfferActionButtons();
            }
        }

        private async Task AcceptSingleOfferAsync(SteamOfferItem offer)
        {
            if (_busy || _disposed)
                return;

            if (!Get(nameof(Settings.SteamOfferRedesignSkipSingleConfirm), false))
            {
                SteamOfferConfirmResult confirm = SteamOfferRedesignConfirmDialog.ShowSingle(FindForm(), offer);
                if (!confirm.Confirmed)
                    return;
                if (confirm.SkipNextTime)
                    Set(nameof(Settings.SteamOfferRedesignSkipSingleConfirm), true);
            }

            _busy = true;
            UpdateOfferActionButtons();
            SetOperationStatus("正在同意报价...", SteamOfferOperationStatusTone.Muted);
            try
            {
                SteamOfferActionResult result = await _presenter.AcceptOfferAsync(offer.TradeOfferId, requireSafe: false);
                SetOperationStatus(result.Message, result.Ok ? SteamOfferOperationStatusTone.Success : SteamOfferOperationStatusTone.Warning);
                RenderOffers();
            }
            finally
            {
                _busy = false;
                UpdateOfferActionButtons();
            }
        }

        private async Task AcceptBatchAsync()
        {
            if (_busy || _disposed)
                return;

            BatchAcceptSummary summary = SteamOfferRedesignModel.BuildBatchSummary(_presenter.GetOfferState().Offers, SteamOfferRedesignRule.Any);
            if (summary.EligibleCount <= 0)
            {
                SetOperationStatus("当前没有可同意的报价。", SteamOfferOperationStatusTone.Warning);
                return;
            }

            if (!Get(nameof(Settings.SteamOfferRedesignSkipBatchConfirm), false))
            {
                SteamOfferConfirmResult confirm = SteamOfferRedesignConfirmDialog.ShowBatch(FindForm(), summary);
                if (!confirm.Confirmed)
                    return;
                if (confirm.SkipNextTime)
                    Set(nameof(Settings.SteamOfferRedesignSkipBatchConfirm), true);
            }

            _busy = true;
            UpdateOfferActionButtons();
            SetOperationStatus($"正在同意全部 {summary.EligibleCount} 条报价...", SteamOfferOperationStatusTone.Muted);
            try
            {
                int ok = 0;
                int failed = 0;
                foreach (SteamOfferItem offer in summary.EligibleOffers)
                {
                    SteamOfferActionResult result = await _presenter.AcceptOfferAsync(offer.TradeOfferId, requireSafe: false);
                    if (result.Ok) ok++;
                    else failed++;
                }

                SetOperationStatus(
                    failed == 0 ? $"已同意 {ok} 条报价。" : $"已同意 {ok} 条，失败 {failed} 条，请刷新后核对。",
                    failed == 0 ? SteamOfferOperationStatusTone.Success : SteamOfferOperationStatusTone.Warning);
                RenderOffers();
            }
            finally
            {
                _busy = false;
                UpdateOfferActionButtons();
            }
        }

        private void RenderOffers()
        {
            SteamOfferState state = _presenter.GetOfferState();
            var pending = state.Offers.Where(offer => offer.Status == SteamOfferStatus.Pending).OrderByDescending(offer => offer.CreatedAt).ToList();
            _offerListSurface?.SetOffers(pending, _expandedOfferId);
            _quoteTabPanel?.PerformLayout();
            if (_offerStatusLabel != null)
            {
                string last = state.LastRefresh == default ? "暂无" : state.LastRefresh.ToString("HH:mm");
                _offerStatusLabel.Text = $"待处理 {pending.Count} 条   上次刷新 {last}   自动刷新 {Get(nameof(Settings.SteamOfferRedesignRefreshMinutes), 5)} 分钟";
            }
            UpdateOfferActionButtons();
        }

        private void UpdateOfferActionButtons()
        {
            if (_acceptAllButton == null || _acceptAllButton.IsDisposed)
                return;

            var summary = SteamOfferRedesignModel.BuildBatchSummary(
                _presenter.GetOfferState().Offers,
                SteamOfferRedesignRule.Any);
            _acceptAllButton.Enabled = !_busy && summary.EligibleCount > 0;
        }

        private void ToggleOfferDetails(SteamOfferItem offer)
        {
            _expandedOfferId = string.Equals(_expandedOfferId, offer.TradeOfferId, StringComparison.OrdinalIgnoreCase)
                ? ""
                : offer.TradeOfferId;
            RenderOffers();
        }

        private void SelectRule(SteamOfferRedesignRule rule)
        {
            Set(nameof(Settings.SteamOfferRedesignRule), SteamOfferRedesignModel.ToSettingValue(rule));
            UpdateRuleCards();
            SetOperationStatus($"已切换规则：{SteamOfferRedesignModel.BuildRuleTitle(rule)}。", SteamOfferOperationStatusTone.Success);
        }

        private void UpdateRuleCards()
        {
            SteamOfferRedesignRule current = CurrentRule;
            foreach (SteamOfferRuleCard card in _ruleCards)
                card.Selected = card.Option.Rule == current;
        }

        private void RefreshHeader()
        {
            if (_headerRefreshInProgress)
                return;

            _headerRefreshInProgress = true;
            try
            {
                SteamOfferState state = _presenter.GetOfferState();
                SteamConnectionStatusViewModel connection = _presenter.GetConnectionStatus();
                SteamOfferStatusHeaderViewModel status = SteamOfferStatusHeaderModel.Build(state.AuthStatus, state.AutoConfirm);
                SetStatusLabel(_tokenStatusLabel, status.Token.Text, status.Token.Tone);
                SetStatusLabel(_loginStatusLabel, status.Session.Text, status.Session.Tone);
                SetStatusLabel(_encryptionStatusLabel, status.Encryption.Text, status.Encryption.Tone);
                SetStatusLabel(_autoRefreshStatusLabel, Get(nameof(Settings.SteamOfferRedesignAutoRefresh), true) ? $"{Get(nameof(Settings.SteamOfferRedesignRefreshMinutes), 5)}分钟" : "已停止", Get(nameof(Settings.SteamOfferRedesignAutoRefresh), true) ? SteamOfferStatusTone.Success : SteamOfferStatusTone.Muted);
                if (_networkStatusLabel != null)
                {
                    _networkStatusLabel.Text = connection.RouteText;
                    _networkStatusLabel.ForeColor = connection.Tone == SteamConnectionStatusTone.Success ? UIColors.Positive : connection.Tone == SteamConnectionStatusTone.Warning ? UIColors.TextWarn : UIColors.TextSub;
                    _networkStatusLabel.AccessibleDescription = string.IsNullOrWhiteSpace(connection.TooltipText) ? connection.DetailText : connection.TooltipText;
                }

                if (_root != null)
                {
                    Label? account = FindLabelByName(_root, "AccountLabel");
                    string accountLine = BuildAccountLine();
                    if (account != null && !string.Equals(account.Text, accountLine, StringComparison.Ordinal))
                        account.Text = accountLine;
                }
            }
            finally
            {
                _headerRefreshInProgress = false;
            }
        }

        private string BuildAccountLine()
        {
            SteamTokenBarSnapshot snapshot = _presenter.GetTokenBarSnapshot();
            SteamTokenEntry? token = snapshot.VisibleTokens.FirstOrDefault();
            if (token == null)
                return "Steam · 未绑定令牌 · DPAPI 加密";

            string name = SteamOfferPagePresenter.BuildTokenComboText(token).Replace("Steam  ", "Steam · ");
            return name + " · DPAPI 加密";
        }

        private void ApplyAutoRefreshTimer()
        {
            _autoRefreshTimer.Stop();
            if (!Get(nameof(Settings.SteamOfferRedesignAutoRefresh), true))
                return;

            int minutes = Math.Clamp(Get(nameof(Settings.SteamOfferRedesignRefreshMinutes), 5), 1, 60);
            _autoRefreshTimer.Interval = minutes * 60 * 1000;
            _autoRefreshTimer.Start();
        }

        private void SaveAutoRefreshSettings()
        {
            ToggleSwitch? autoRefreshSwitch = _autoRefreshSwitches.FirstOrDefault();
            LiteNumberInput? refreshMinutesInput = _refreshMinutesInputs.FirstOrDefault();
            SaveAutoRefreshSettings(autoRefreshSwitch, refreshMinutesInput);
        }

        private void SaveAutoRefreshSettings(ToggleSwitch? autoRefreshSwitch, LiteNumberInput? refreshMinutesInput)
        {
            bool enabled = autoRefreshSwitch?.Checked ?? Get(nameof(Settings.SteamOfferRedesignAutoRefresh), true);
            int minutes = Math.Clamp(refreshMinutesInput?.ValueInt ?? Get(nameof(Settings.SteamOfferRedesignRefreshMinutes), 5), 1, 60);
            Set(nameof(Settings.SteamOfferRedesignAutoRefresh), enabled);
            Set(nameof(Settings.SteamOfferRedesignRefreshMinutes), minutes);
            if (refreshMinutesInput != null && refreshMinutesInput.Inner.Text != minutes.ToString())
                refreshMinutesInput.Inner.Text = minutes.ToString();
        }

        private void SyncAutoRefreshControls(ToggleSwitch sourceSwitch, LiteNumberInput sourceInput)
        {
            _updating = true;
            try
            {
                foreach (ToggleSwitch target in _autoRefreshSwitches)
                {
                    if (!ReferenceEquals(target, sourceSwitch))
                        target.Checked = sourceSwitch.Checked;
                }

                foreach (LiteNumberInput target in _refreshMinutesInputs)
                {
                    if (!ReferenceEquals(target, sourceInput))
                        target.Inner.Text = sourceInput.Inner.Text;
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private void OnOfferDataUpdated()
        {
            if (_disposed || IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_disposed || IsDisposed)
                        return;
                    RefreshHeader();
                    RefreshAutoTradePanel();
                    RenderOffers();
                    if (_operationStatusTracksOfferState && !_busy)
                        ApplyOfferStateOperationStatus();
                }));
            }
            catch
            {
                // 页面关闭期间报价列表刷新投递可能失败，忽略本次刷新。
            }
        }

        private void OnConnectionStatusChanged()
        {
            if (_disposed || IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(RefreshHeader));
            }
            catch
            {
                // 页面关闭期间头部刷新投递可能失败，忽略本次刷新。
            }
        }

        private void RefreshConfirmReminderStatus()
        {
            bool skipSingle = Get(nameof(Settings.SteamOfferRedesignSkipSingleConfirm), false);
            bool skipBatch = Get(nameof(Settings.SteamOfferRedesignSkipBatchConfirm), false);

            if (_singleConfirmStatusLabel != null)
            {
                _singleConfirmStatusLabel.Text = skipSingle
                    ? "单条同意：已选择跳过确认，点击下方按钮可恢复。"
                    : "单条同意：每次点击同意报价前会弹确认。";
                _singleConfirmStatusLabel.ForeColor = skipSingle ? UIColors.TextWarn : UIColors.Positive;
            }

            if (_batchConfirmStatusLabel != null)
            {
                _batchConfirmStatusLabel.Text = skipBatch
                    ? "一键同意：已选择跳过确认，点击下方按钮可恢复。"
                    : "一键同意：批量同意前会弹确认。";
                _batchConfirmStatusLabel.ForeColor = skipBatch ? UIColors.TextWarn : UIColors.Positive;
            }

            if (_confirmReminderHelpLabel != null)
                _confirmReminderHelpLabel.Text = SteamOfferRedesignModel.BuildConfirmReminderHelp(skipSingle, skipBatch);
        }

        private bool ShowCredentialConfirmation(SteamOfferCredentialPrompt prompt)
        {
            return GlobalPromptService.Show(FindForm(), prompt.Message, prompt.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }

        private void SetOperationStatus(string text, SteamOfferOperationStatusTone tone)
        {
            SetOperationStatus(text, tone, tracksOfferState: false);
        }

        private void SetOfferStateOperationStatus(string text, SteamOfferOperationStatusTone tone)
        {
            SetOperationStatus(text, tone, tracksOfferState: true);
        }

        private void ApplyOfferStateOperationStatus()
        {
            SteamOfferState state = _presenter.GetOfferState();
            if (string.IsNullOrWhiteSpace(state.LastStatus))
                return;

            SetOfferStateOperationStatus(
                state.LastStatus,
                string.IsNullOrWhiteSpace(state.LastError)
                    ? SteamOfferOperationStatusTone.Success
                    : SteamOfferOperationStatusTone.Warning);
        }

        private void SetOperationStatus(string text, SteamOfferOperationStatusTone tone, bool tracksOfferState)
        {
            if (_operationStatusLabel == null)
                return;

            _operationStatusTracksOfferState = tracksOfferState;
            _operationStatusLabel.Text = text;
            _operationStatusLabel.ForeColor = tone switch
            {
                SteamOfferOperationStatusTone.Success => UIColors.Positive,
                SteamOfferOperationStatusTone.Warning => UIColors.TextWarn,
                _ => UIColors.TextSub
            };
        }

        private void SyncContentWidth()
        {
            if (_root == null || _root.IsDisposed)
                return;

            Rectangle bounds = ContentBounds;
            int width = bounds.Width;
            bool changed = false;
            if (_root.Left != bounds.Left || _root.Top != bounds.Top || _root.Width != width)
            {
                _root.SetBounds(bounds.Left, bounds.Top, width, _root.Height);
                changed = true;
            }

            foreach (Control child in _root.Controls)
            {
                if (child.Width == width)
                    continue;

                child.Width = width;
                changed = true;
            }

            int height = Math.Max(UIUtils.S(1), _root.GetPreferredSize(new Size(width, 0)).Height);
            if (_root.Height != height)
            {
                _root.Height = height;
                changed = true;
            }

            if (changed)
                _root.PerformLayout();
            HideHorizontalScroll(Container);
        }

        private void QueueDeferredContentWidthSync()
        {
            if (_widthSyncQueued || IsDisposed)
                return;

            _widthSyncQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _widthSyncQueued = false;
                    SyncContentWidth();
                }));
            }
            catch
            {
                _widthSyncQueued = false;
                SyncContentWidth();
            }
        }

        private void AddRootRow(Control control)
        {
            if (_root == null)
                return;

            int row = _root.RowCount;
            _root.RowCount = row + 1;
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            control.Width = ContentWidth;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _root.Controls.Add(control, 0, row);
            SyncContentWidth();
        }

        private void OpenSteamOfferLogFile()
        {
            try
            {
                string path = SteamOfferAuditLog.EnsureLogFile();
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                SetOperationStatus("报价动作日志已打开。", SteamOfferOperationStatusTone.Success);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("SteamOfferLog", "Opening Steam offer log failed.", ex);
                SetOperationStatus("打开报价动作日志失败：" + SteamOfferAuditLog.RedactSecrets(ex.Message), SteamOfferOperationStatusTone.Warning);
            }
        }

        private static void OpenSteamOffersPage()
        {
            Process.Start(new ProcessStartInfo { FileName = SteamOfferPageModel.SteamOffersPageUrl, UseShellExecute = true });
        }

        private static Label CreateTextLabel(string text, float size, FontStyle style, Color color, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color,
                TextAlign = align
            };
        }

        private static int GetVisibleLayoutWidth(Control control)
        {
            return FrameworkSettingsPageLayoutHelper.CalculateVisibleWidthWithinForm(control);
        }

        private static Label CreateBadgeLabel(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static Label CreateStatusValueLabel()
        {
            return CreateTextLabel("", 9F, FontStyle.Bold, UIColors.Positive);
        }

        private static Label CreateCodeLabel()
        {
            return new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = UIColors.InputBg
            };
        }

        private static Control CreateMetricBlock(string title, Label value)
        {
            var panel = new Panel { BackColor = Color.Transparent };
            var label = CreateTextLabel(title, 8.5F, FontStyle.Regular, UIColors.TextSub);
            panel.Controls.Add(label);
            panel.Controls.Add(value);
            panel.Layout += (_, __) =>
            {
                label.SetBounds(0, 0, panel.Width, UIUtils.S(20));
                value.SetBounds(0, UIUtils.S(20), panel.Width, UIUtils.S(24));
            };
            panel.Paint += (_, e) =>
            {
                if (panel.Right <= panel.Parent?.ClientSize.Width - UIUtils.S(40))
                {
                    using var pen = new Pen(UIColors.Border);
                    e.Graphics.DrawLine(pen, panel.Width - 1, UIUtils.S(4), panel.Width - 1, panel.Height - UIUtils.S(4));
                }
            };
            return panel;
        }

        private static void SetStatusLabel(Label? label, string text, SteamOfferStatusTone tone)
        {
            if (label == null)
                return;

            label.Text = text;
            label.ForeColor = tone switch
            {
                SteamOfferStatusTone.Success => UIColors.Positive,
                SteamOfferStatusTone.Warning => UIColors.TextWarn,
                _ => UIColors.TextSub
            };
        }

        private static Label? FindLabelByName(Control root, string name)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label && string.Equals(label.Name, name, StringComparison.Ordinal))
                    return label;
                Label? nested = FindLabelByName(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static void CreateInlineTitle(Control parent, string text, int x, int y, int width)
        {
            const string name = "InlineTokenTitle";
            Control? old = parent.Controls.Cast<Control>().FirstOrDefault(control => string.Equals(control.Name, name, StringComparison.Ordinal));
            if (old == null)
            {
                old = CreateTextLabel(text, 9F, FontStyle.Regular, UIColors.TextSub);
                old.Name = name;
                parent.Controls.Add(old);
            }
            old.SetBounds(x, y, width, UIUtils.S(32));
        }
    }

    internal sealed class SteamOfferAvatar : Control
    {
        public SteamOfferAvatar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(UIColors.Primary);
            e.Graphics.FillEllipse(brush, ClientRectangle);
            TextRenderer.DrawText(e.Graphics, "ST", new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

}
