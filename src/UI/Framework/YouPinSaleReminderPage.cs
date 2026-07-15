using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using static CS2TradeMonitor.src.UI.Framework.YouPinSaleReminderPageControls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum YouPinSaleReminderPageLayoutMode
    {
        QuoteOnly,
        SettingsOnly,
        // 与 QuoteOnly 行为一致，仅供「悠悠有品」新主入口使用：渲染后把状态 chip 文案/颜色改成
        // 「自动发货/报价未启用」(琥珀)。不影响线上 QuoteOnly。
        QuoteOnlyCc
    }

    internal readonly record struct YouPinCcQuoteLayoutPlan(
        int Gap,
        int SummaryHeight,
        int ActionHeight,
        int PassiveHeight,
        bool PassiveVisible);

    internal static class YouPinCcQuoteLayoutModel
    {
        public static YouPinCcQuoteLayoutPlan Calculate(int available, bool constrained, int actionableCount, int passiveCount)
        {
            int normalizedAvailable = Math.Max(UIUtils.S(360), available);
            int gap = constrained ? UIUtils.S(8) : UIUtils.S(12);
            int summaryHeight = constrained ? UIUtils.S(96) : UIUtils.S(124);
            bool passiveVisible = passiveCount > 0;
            int actionDesired = CalculateActionDesiredHeight(normalizedAvailable, actionableCount);
            if (!passiveVisible)
            {
                int actionOnlyHeight = Math.Max(
                    actionDesired,
                    normalizedAvailable - summaryHeight - gap);
                return new YouPinCcQuoteLayoutPlan(gap, summaryHeight, Math.Max(UIUtils.S(112), actionOnlyHeight), 0, false);
            }

            int remaining = Math.Max(UIUtils.S(240), normalizedAvailable - summaryHeight - gap * 2);
            int passiveMinimum = CalculatePassiveMinimumHeight(normalizedAvailable);
            int passiveFloor = UIUtils.S(96);
            if (actionableCount <= 0)
            {
                int emptyMaximum = constrained ? UIUtils.S(96) : UIUtils.S(128);
                int emptyHeight = Math.Min(emptyMaximum, Math.Max(UIUtils.S(88), remaining - passiveFloor));
                int passiveOnlyHeight = Math.Max(passiveFloor, remaining - emptyHeight);
                return new YouPinCcQuoteLayoutPlan(gap, summaryHeight, emptyHeight, passiveOnlyHeight, true);
            }

            int actionMinimum = Math.Max(UIUtils.S(120), actionDesired);
            int actionHeight = Math.Max(actionMinimum, remaining - passiveMinimum);
            actionHeight = Math.Min(actionHeight, Math.Max(actionMinimum, remaining - passiveFloor));
            int passiveHeight = remaining - actionHeight;
            if (passiveHeight < passiveFloor)
            {
                passiveHeight = passiveFloor;
                actionHeight = Math.Max(UIUtils.S(120), remaining - passiveHeight);
            }

            return new YouPinCcQuoteLayoutPlan(gap, summaryHeight, actionHeight, passiveHeight, true);
        }

        public static int CalculateActionDesiredHeight(int available, int actionableCount)
        {
            if (actionableCount <= 0)
            {
                int emptyMinimum = available < UIUtils.S(500) ? UIUtils.S(112) : UIUtils.S(150);
                int emptyMaximum = available < UIUtils.S(500) ? UIUtils.S(128) : UIUtils.S(220);
                return Math.Min(emptyMaximum, Math.Max(emptyMinimum, UIUtils.S(112)));
            }

            int visibleRows = Math.Min(3, Math.Max(1, actionableCount));
            return UIUtils.S(20) + visibleRows * UIUtils.S(78);
        }

        private static int CalculatePassiveMinimumHeight(int available)
        {
            if (available < UIUtils.S(620))
                return UIUtils.S(120);
            return UIUtils.S(180);
        }
    }

    public sealed class YouPinSaleReminderPage : FrameworkSettingsPageBase
    {
        private readonly Action _dataUpdatedHandler;
        private readonly List<LiteButton> _actionButtons = new();
        private readonly YouPinSaleReminderPagePresenter _presenter;
        private readonly ISteamOfferService _steamOffers;
        private readonly YouPinSaleReminderPageActionRunner _actionRunner;
        private readonly YouPinSaleReminderTabLayoutController _tabLayoutController;
        private readonly YouPinSaleReminderPageRenderer _renderer;
        private readonly YouPinSaleReminderPageLayoutMode _layoutMode;
        private readonly ToolTip _statusToolTip = new() { AutomaticDelay = 250, ReshowDelay = 100, ShowAlways = true };

        private Panel? _todoSettingsWrapper;
        private Panel? _waitDeliverWrapper;
        private Panel? _msgSettingsWrapper;
        private Panel? _msgWrapper;
        private Panel? _autoDeliveryWrapper;
        private Panel? _accountToolsWrapper;

        private Label? _todoActionStatusLabel;
        private Label? _waitDeliverStatusLabel;
        private Label? _waitDeliverSummaryLabel;
        private Label? _waitDeliverActionStatusLabel;
        private YouPinCcQuoteSummaryPanel? _quoteSummaryPanel;
        private Panel? _quoteActionSection;
        private YouPinCcQuoteEmptyPanel? _quoteActionEmptyPanel;
        private YouPinSaleReminderOrderListPanel? _quoteActionList;
        private Panel? _quotePassiveSection;
        private Label? _quotePassiveTitleLabel;
        private Label? _quotePassiveHintLabel;
        private YouPinCcQuoteTableHeaderPanel? _quotePassiveHeader;
        private Label? _msgStatusLabel;
        private Label? _msgSummaryLabel;
        private Label? _msgActionStatusLabel;
        private Label? _autoDeliveryStatusLabel;
        private Label? _autoDeliveryTimeLabel;
        private Label? _autoDeliveryErrorLabel;
        private LiteButton? _sendAllOffersButton;

        private YouPinSaleReminderOrderListPanel? _waitDeliverList;
        private YouPinSaleReminderOrderListPanel? _msgList;

        private bool _disposed;
        private bool _refreshQueued;
        private int _quoteActionableCount;
        private int _quotePassiveCount;
        private System.Windows.Forms.Timer? _deferredRefreshTimer;

        internal YouPinSaleReminderPage(
            YouPinPageRuntimeServices runtimeServices,
            YouPinSaleReminderPageLayoutMode layoutMode)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _layoutMode = layoutMode;
            _presenter = new YouPinSaleReminderPagePresenter(runtimeServices);
            _steamOffers = runtimeServices.SteamOffers;
            _actionRunner = new YouPinSaleReminderPageActionRunner(
                _actionButtons,
                EnsureSettings,
                ConfigureService,
                RefreshState,
                SetStatus);
            _renderer = new YouPinSaleReminderPageRenderer(_actionButtons);
            _tabLayoutController = new YouPinSaleReminderTabLayoutController(
                Container,
                () => _disposed || IsDisposed,
                () => IsHandleCreated,
                action => BeginInvoke(action),
                BuildTabWrappers);
            _dataUpdatedHandler = () => QueueRefreshStateFromAnyThread();

            _presenter.DataUpdated += _dataUpdatedHandler;
            if (_layoutMode == YouPinSaleReminderPageLayoutMode.QuoteOnlyCc)
                Container.Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding();
            Container.SizeChanged += (_, __) => HandleContainerSizeChanged();

            using (UiJankProfiler.Measure("YouPinSaleReminder.BuildPage", thresholdMs: 1))
            {
                Container.SuspendLayout();
                try
                {
                    EnsureLayoutContentCreated();
                    UpdateVisibility();
                    _tabLayoutController.ReorderWrappers();
                }
                finally
                {
                    Container.ResumeLayout(true);
                }
            }

            _tabLayoutController.Stabilize(deferIfNotReady: true);
        }

        protected override void OnStoreAttached()
        {
            EnsureSettings();
        }

        public override void Activate()
        {
            EnsureSettings();
            ConfigureService();
            EnsureLayoutContentCreated();
            base.Activate();
            _tabLayoutController.QueueRetry();
            QueueRefreshState();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _tabLayoutController.Stabilize(deferIfNotReady: true);
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            QueueRefreshState(delayMs: 1);
            _tabLayoutController.QueueRetry();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (_deferredRefreshTimer != null)
                {
                    _deferredRefreshTimer.Stop();
                    _deferredRefreshTimer.Dispose();
                    _deferredRefreshTimer = null;
                }
                _statusToolTip.Dispose();
                _presenter.DataUpdated -= _dataUpdatedHandler;
            }

            base.Dispose(disposing);
        }

        private void EnsureSettings()
        {
            YouPinSaleReminderSettingsDefaults defaults = YouPinSaleReminderPageModel.BuildSettingsDefaults(
                Get(nameof(Settings.YouPinInventoryToken), string.Empty),
                Get(nameof(Settings.YouPinInventoryDeviceToken), string.Empty),
                Get(nameof(Settings.YouPinSaleReminderRefreshSec), 180),
                Get(nameof(Settings.YouPinQuoteAutoRefreshSec), 180),
                Get(nameof(Settings.YouPinMsgCenterRefreshSec), 60),
                Get(nameof(Settings.YouPinSaleReminderNotificationMode), YouPinSaleReminderNotificationMode.Bubble),
                Get(nameof(Settings.YouPinMsgCenterNotificationMode), YouPinSaleReminderNotificationMode.Bubble));

            Set(nameof(Settings.YouPinInventoryToken), defaults.InventoryToken);
            Set(nameof(Settings.YouPinInventoryDeviceToken), defaults.InventoryDeviceToken);
            Set(nameof(Settings.YouPinSaleReminderRefreshSec), defaults.TodoRefreshSec);
            Set(nameof(Settings.YouPinQuoteAutoRefreshEnabled), Get(nameof(Settings.YouPinQuoteAutoRefreshEnabled), false));
            Set(nameof(Settings.YouPinQuoteAutoRefreshSec), defaults.QuoteAutoRefreshSec);
            Set(nameof(Settings.YouPinMsgCenterRefreshSec), defaults.MsgCenterRefreshSec);
            Set(nameof(Settings.YouPinSaleReminderNotificationMode), defaults.TodoNotificationMode);
            Set(nameof(Settings.YouPinMsgCenterNotificationMode), defaults.MsgCenterNotificationMode);
        }

        private void ConfigureService()
        {
            if (Config is null)
                return;

            _presenter.Configure(Config);
        }

        private bool EnsureLayoutContentCreated()
        {
            return _layoutMode switch
            {
                YouPinSaleReminderPageLayoutMode.QuoteOnly => EnsureQuoteContentCreated(),
                YouPinSaleReminderPageLayoutMode.QuoteOnlyCc => EnsureQuoteContentCreated(),
                YouPinSaleReminderPageLayoutMode.SettingsOnly => EnsureSettingsContentCreated(),
                _ => false
            };
        }

        private bool EnsureQuoteContentCreated()
        {
            if (_waitDeliverWrapper != null)
                return false;

            _waitDeliverWrapper = _layoutMode == YouPinSaleReminderPageLayoutMode.QuoteOnlyCc
                ? CreateCcWaitDeliverCard()
                : CreateWaitDeliverCard();
            _tabLayoutController.ReorderWrappers();
            return true;
        }

        private bool EnsureSettingsContentCreated()
        {
            bool created = false;
            if (_todoSettingsWrapper == null)
            {
                _todoSettingsWrapper = CreateTodoSettingsCard();
                created = true;
            }
            if (_msgSettingsWrapper == null)
            {
                _msgSettingsWrapper = CreateMsgCenterSettingsCard();
                created = true;
            }
            if (_msgWrapper == null)
            {
                _msgWrapper = CreateMsgCenterCard();
                created = true;
            }
            if (_autoDeliveryWrapper == null)
            {
                _autoDeliveryWrapper = CreateAutoDeliveryCard();
                created = true;
            }
            if (_accountToolsWrapper == null)
            {
                _accountToolsWrapper = CreateAccountToolsCard();
                created = true;
            }

            if (created)
                _tabLayoutController.ReorderWrappers();
            return created;
        }

        private Panel CreateTodoSettingsCard()
        {
            var group = new LiteSettingsGroup("悠悠有品待办与报价刷新设置");
            AddHint(group, "监控悠悠有品待办消息，默认只展示需要处理的事项。登录凭据与库存读取功能共用。");
            AddToggle(group, "启用待办消息提醒", nameof(Settings.YouPinSaleReminderEnabled), false, _ =>
            {
                ConfigureService();
                RefreshState();
            });
            AddInt(group, "检查间隔", nameof(Settings.YouPinSaleReminderRefreshSec), 180, "秒", 90,
                value => Math.Max(30, value), _ => ConfigureService());
            AddNotificationMode(group, "提醒方式", nameof(Settings.YouPinSaleReminderNotificationMode));
            AddOnlyWaitDeliverToggle(group);
            group.AddFullItem(CreateCheckActionsRow(
                "立即检查待办",
                "测试待办提醒",
                label => _todoActionStatusLabel = label,
                async source => await RunTodoCheckAsync(useMock: false, source),
                async source => await RunTodoCheckAsync(useMock: true, source),
                button => _actionButtons.Add(button)));
            AddHint(group, "报价自动刷新只同步待发货/报价处理列表，不会自动发送或确认报价。");
            AddToggle(group, "启用报价自动刷新", nameof(Settings.YouPinQuoteAutoRefreshEnabled), false, _ =>
            {
                ConfigureService();
                RefreshState();
            });
            AddInt(group, "报价刷新间隔", nameof(Settings.YouPinQuoteAutoRefreshSec), 180, "秒", 90,
                value => YouPinSaleReminderPageModel.NormalizeRefreshSeconds(value, 180), _ => ConfigureService());
            return AddGroupToPage(group);
        }

        private Panel CreateMsgCenterSettingsCard()
        {
            var group = new LiteSettingsGroup("悠悠有品提醒设置");
            AddHint(group, "用于展示和测试交易、系统、活动、租赁等普通通知；真实消息中心接口暂未开放时可用测试提醒验证通知通道。");
            AddToggle(group, "启用消息中心提醒", nameof(Settings.YouPinMsgCenterEnabled), false, _ =>
            {
                ConfigureService();
                RefreshState();
            });
            AddInt(group, "检查间隔", nameof(Settings.YouPinMsgCenterRefreshSec), 60, "秒", 90,
                value => Math.Max(30, value), _ => ConfigureService());
            AddNotificationMode(group, "提醒方式", nameof(Settings.YouPinMsgCenterNotificationMode));
            group.AddFullItem(CreateCheckActionsRow(
                "立即检查消息",
                "测试消息提醒",
                label => _msgActionStatusLabel = label,
                async source => await RunMsgCenterCheckAsync(useMock: false, source),
                async source => await RunMsgCenterCheckAsync(useMock: true, source),
                button => _actionButtons.Add(button)));
            return AddGroupToPage(group);
        }

        private void AddNotificationMode(LiteSettingsGroup group, string title, string settingKey)
        {
            var combo = new LiteComboBox { Width = UIUtils.S(180) };
            foreach (var option in YouPinSaleReminderPageModel.NotificationModeOptions)
                combo.AddItem(option.Text, ((int)option.Mode).ToString());
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (YouPinSaleReminderPageModel.TryParseNotificationMode(combo.SelectedValue, out var mode))
                {
                    Set(settingKey, mode);
                    ConfigureService();
                }
            };
            RegisterRefresh(() =>
            {
                var mode = YouPinSaleReminderPageModel.NormalizeNotificationMode(
                    Get(settingKey, YouPinSaleReminderNotificationMode.Bubble));
                combo.SelectValue(((int)mode).ToString());
            });
            RegisterSave(() =>
            {
                if (YouPinSaleReminderPageModel.TryParseNotificationMode(combo.SelectedValue, out var mode))
                    Set(settingKey, mode);
            });
            group.AddItem(new LiteSettingsItem(title, combo));
        }

        private void AddOnlyWaitDeliverToggle(LiteSettingsGroup group)
        {
            var check = new LiteCheck(!Get(nameof(Settings.YouPinSaleReminderIncludeAllTodos), false), LanguageManager.T("Menu.Enable"));
            check.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(nameof(Settings.YouPinSaleReminderIncludeAllTodos), !check.Checked);
                ConfigureService();
                RefreshState();
            };
            RegisterRefresh(() => check.Checked = !Get(nameof(Settings.YouPinSaleReminderIncludeAllTodos), false));
            RegisterSave(() => Set(nameof(Settings.YouPinSaleReminderIncludeAllTodos), !check.Checked));
            group.AddItem(new LiteSettingsItem("只提醒需发报价的订单", check));
        }

        private Panel CreateWaitDeliverCard()
        {
            var group = new LiteSettingsGroup("待发货 / 发送报价");
            _waitDeliverStatusLabel = CreateMutedLabel("运行：未检查");
            _waitDeliverSummaryLabel = CreateMutedLabel("暂无待发货或报价处理数据。");
            _waitDeliverActionStatusLabel = CreateMutedLabel(string.Empty);
            group.AddFullItem(CreateStatusBlock(
                _waitDeliverStatusLabel,
                _waitDeliverSummaryLabel,
                _waitDeliverActionStatusLabel,
                refreshAction: async source => await RunTodoInlineRefreshAsync(source),
                trackActionButton: button => _actionButtons.Add(button)));
            group.AddFullItem(CreateQuoteLogRow());
            _waitDeliverList = CreateOrderList(waitDeliverActions: true, height: 320);
            group.AddFullItem(_waitDeliverList);
            AddHint(group, "需要发送 Steam 报价的订单优先显示在这里。发送报价、查询状态会调用悠悠有品接口，请确认订单后再操作。");
            return AddGroupToPage(group);
        }

        private Panel CreateCcWaitDeliverCard()
        {
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                Height = CalculateCcQuoteWrapperHeight(),
                BackColor = Color.Transparent,
                Padding = Padding.Empty
            };

            _waitDeliverSummaryLabel = CreateMutedLabel("上次刷新 暂无");
            _waitDeliverSummaryLabel.Font = new Font("Microsoft YaHei UI", 9.2F);
            _waitDeliverStatusLabel = new YouPinCcPillLabel("自动发货/报价未启用")
            {
                ForeColor = Color.FromArgb(224, 146, 47)
            };
            _waitDeliverActionStatusLabel = CreateMutedLabel(string.Empty);

            var refreshButton = new LiteButton("立即刷新", true)
            {
                Width = UIUtils.S(82),
                Height = UIUtils.S(38)
            };
            _actionButtons.Add(refreshButton);
            refreshButton.Click += async (_, __) => await RunTodoInlineRefreshAsync(refreshButton);

            var logButton = new LiteButton("报价日志", false)
            {
                Width = UIUtils.S(82),
                Height = UIUtils.S(38)
            };
            logButton.Click += (_, __) => OpenQuoteLogFile();

            var sendAllButton = new LiteButton("一键处理全部报价", false)
            {
                Width = UIUtils.S(148),
                Height = UIUtils.S(38),
                Enabled = false
            };
            _sendAllOffersButton = sendAllButton;
            _actionButtons.Add(sendAllButton);
            sendAllButton.Click += async (_, __) => await RunSendAllOffersAsync(sendAllButton);

            var statusRow = new Panel
            {
                Height = UIUtils.S(54),
                BackColor = Color.Transparent
            };
            statusRow.Controls.Add(_waitDeliverSummaryLabel);
            statusRow.Controls.Add(_waitDeliverActionStatusLabel);
            statusRow.Controls.Add(_waitDeliverStatusLabel);
            statusRow.Controls.Add(sendAllButton);
            statusRow.Controls.Add(logButton);
            statusRow.Controls.Add(refreshButton);
            statusRow.Layout += (_, __) =>
            {
                int mid = statusRow.Height / 2;
                bool compact = statusRow.Width < UIUtils.S(860);
                int gap = compact ? UIUtils.S(8) : UIUtils.S(12);
                SetButtonText(sendAllButton, compact ? "处理全部报价" : "一键处理全部报价");
                sendAllButton.Width = compact ? UIUtils.S(126) : UIUtils.S(148);
                logButton.Width = compact ? UIUtils.S(76) : UIUtils.S(82);
                refreshButton.Width = compact ? UIUtils.S(78) : UIUtils.S(82);
                _waitDeliverStatusLabel.Visible = statusRow.Width >= UIUtils.S(700);

                refreshButton.SetBounds(Math.Max(0, statusRow.Width - refreshButton.Width), mid - refreshButton.Height / 2, refreshButton.Width, refreshButton.Height);
                logButton.SetBounds(Math.Max(0, refreshButton.Left - gap - logButton.Width), mid - logButton.Height / 2, logButton.Width, logButton.Height);
                sendAllButton.SetBounds(Math.Max(0, logButton.Left - gap - sendAllButton.Width), mid - sendAllButton.Height / 2, sendAllButton.Width, sendAllButton.Height);
                _waitDeliverStatusLabel.SetBounds(Math.Max(0, sendAllButton.Left - gap - UIUtils.S(160)), mid - UIUtils.S(17), UIUtils.S(160), UIUtils.S(34));
                int rightLimit = _waitDeliverStatusLabel.Visible ? _waitDeliverStatusLabel.Left : sendAllButton.Left;
                int textWidth = Math.Max(1, rightLimit - UIUtils.S(14));
                _waitDeliverSummaryLabel.SetBounds(0, 0, textWidth, UIUtils.S(27));
                _waitDeliverActionStatusLabel.SetBounds(0, UIUtils.S(27), textWidth, UIUtils.S(24));
            };

            _quoteSummaryPanel = new YouPinCcQuoteSummaryPanel
            {
                BackColor = Color.Transparent
            };

            _quoteActionSection = new YouPinCcQuoteCardPanel
            {
                BackColor = Color.Transparent
            };
            _quoteActionEmptyPanel = new YouPinCcQuoteEmptyPanel
            {
                BackColor = Color.Transparent
            };
            _quoteActionList = CreateOrderList(
                waitDeliverActions: true,
                height: 180,
                compactQuoteStyle: true,
                groupCompactQuotes: false);
            _quoteActionList.Dock = DockStyle.None;
            _quoteActionList.Visible = false;
            _quoteActionSection.Controls.Add(_quoteActionEmptyPanel);
            _quoteActionSection.Controls.Add(_quoteActionList);
            _quoteActionSection.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                var bounds = new Rectangle(pad, UIUtils.S(10), Math.Max(1, _quoteActionSection.Width - pad * 2), Math.Max(1, _quoteActionSection.Height - UIUtils.S(20)));
                _quoteActionEmptyPanel.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                _quoteActionList.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            };

            _quotePassiveSection = new YouPinCcQuoteCardPanel
            {
                BackColor = Color.Transparent
            };
            _quotePassiveTitleLabel = CreateSectionTitleLabel("无需处理");
            _quotePassiveHintLabel = CreateMutedLabel("已发出或等待对方/平台同步");
            _quotePassiveHeader = new YouPinCcQuoteTableHeaderPanel
            {
                BackColor = Color.Transparent
            };
            _waitDeliverList = CreateOrderList(
                waitDeliverActions: true,
                height: 270,
                compactQuoteStyle: true,
                groupCompactQuotes: false);
            _waitDeliverList.Dock = DockStyle.None;
            _quotePassiveSection.Controls.Add(_quotePassiveTitleLabel);
            _quotePassiveSection.Controls.Add(_quotePassiveHintLabel);
            _quotePassiveSection.Controls.Add(_quotePassiveHeader);
            _quotePassiveSection.Controls.Add(_waitDeliverList);
            _quotePassiveSection.Layout += (_, __) =>
            {
                bool compact = _quotePassiveSection.Width < UIUtils.S(760) || _quotePassiveSection.Height < UIUtils.S(220);
                bool hideHint = _quotePassiveSection.Height < UIUtils.S(210);
                int pad = compact ? UIUtils.S(18) : UIUtils.S(28);
                int top = compact ? UIUtils.S(12) : UIUtils.S(16);
                int width = Math.Max(1, _quotePassiveSection.Width - pad * 2);
                _quotePassiveHintLabel.Visible = !hideHint;
                _quotePassiveTitleLabel.SetBounds(pad, top, width, UIUtils.S(28));
                int headerTop;
                if (hideHint)
                {
                    _quotePassiveHintLabel.SetBounds(pad, _quotePassiveTitleLabel.Bottom, width, 0);
                    headerTop = _quotePassiveTitleLabel.Bottom + UIUtils.S(8);
                }
                else
                {
                    _quotePassiveHintLabel.SetBounds(pad, _quotePassiveTitleLabel.Bottom + UIUtils.S(2), width, UIUtils.S(22));
                    headerTop = _quotePassiveHintLabel.Bottom + (compact ? UIUtils.S(8) : UIUtils.S(14));
                }
                _quotePassiveHeader.SetBounds(pad, headerTop, width, UIUtils.S(32));
                _waitDeliverList.SetBounds(
                    pad,
                    _quotePassiveHeader.Bottom + UIUtils.S(4),
                    width,
                    Math.Max(1, _quotePassiveSection.Height - _quotePassiveHeader.Bottom - UIUtils.S(16)));
            };

            wrapper.Controls.Add(statusRow);
            wrapper.Controls.Add(_quoteSummaryPanel);
            wrapper.Controls.Add(_quoteActionSection);
            wrapper.Controls.Add(_quotePassiveSection);
            wrapper.Layout += (_, __) =>
            {
                UpdateCcQuoteWrapperHeight(wrapper);
                statusRow.SetBounds(0, 0, wrapper.Width, UIUtils.S(54));
                LayoutCcQuoteDashboard(wrapper, statusRow);
            };

            Container.SuspendLayout();
            Container.Controls.Add(wrapper);
            Container.ResumeLayout(false);
            _tabLayoutController.Stabilize(deferIfNotReady: true);
            return wrapper;
        }

        private void HandleContainerSizeChanged()
        {
            if (_layoutMode == YouPinSaleReminderPageLayoutMode.QuoteOnlyCc)
                UpdateCcQuoteWrapperHeight(_waitDeliverWrapper as Panel);
            _tabLayoutController.Stabilize(deferIfNotReady: true);
        }

        private void LayoutCcQuoteDashboard(Panel wrapper, Control statusRow)
        {
            if (_quoteSummaryPanel == null || _quoteActionSection == null || _quotePassiveSection == null)
                return;

            using var _ = UiJankProfiler.Measure(
                "YouPinSaleReminder.LayoutCcQuoteDashboard",
                $"Action={_quoteActionableCount}; Passive={_quotePassiveCount}; Size={wrapper.Width}x{wrapper.Height}",
                thresholdMs: 1);

            bool constrained = wrapper.Height < UIUtils.S(560);
            int gap = constrained ? UIUtils.S(8) : UIUtils.S(12);
            int top = statusRow.Bottom + UIUtils.S(10);
            int width = Math.Max(1, wrapper.Width);
            int available = Math.Max(UIUtils.S(360), wrapper.Height - top);
            var plan = YouPinCcQuoteLayoutModel.Calculate(available, constrained, _quoteActionableCount, _quotePassiveCount);

            _quoteSummaryPanel.SetBounds(0, top, width, plan.SummaryHeight);
            _quoteActionSection.SetBounds(0, _quoteSummaryPanel.Bottom + plan.Gap, width, plan.ActionHeight);
            _quotePassiveSection.Visible = plan.PassiveVisible;
            _quotePassiveSection.SetBounds(
                0,
                _quoteActionSection.Bottom + (plan.PassiveVisible ? plan.Gap : 0),
                width,
                Math.Max(0, plan.PassiveHeight));
        }

        private int CalculateCcQuoteWrapperHeight()
        {
            int available = Container.ClientSize.Height - Container.Padding.Vertical;
            if (available <= 0)
                available = UIUtils.S(420);
            return Math.Max(UIUtils.S(420), available);
        }

        private bool UpdateCcQuoteWrapperHeight(Panel? wrapper)
        {
            if (wrapper == null || wrapper.IsDisposed)
                return false;

            int desired = CalculateCcQuoteWrapperHeight();
            if (Math.Abs(wrapper.Height - desired) <= 1)
                return false;

            wrapper.Height = desired;
            _tabLayoutController.QueueRetry();
            return true;
        }

        private static Label CreateSectionTitleLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void SetButtonText(Button button, string text)
        {
            if (!string.Equals(button.Text, text, StringComparison.Ordinal))
                button.Text = text;
        }

        private Panel CreateMsgCenterCard()
        {
            var group = new LiteSettingsGroup("消息中心最近提醒");
            _msgStatusLabel = CreateMutedLabel("运行：未检查");
            _msgSummaryLabel = CreateMutedLabel("暂无消息中心提醒。");
            group.AddFullItem(CreateStatusBlock(_msgStatusLabel, _msgSummaryLabel));
            _msgList = CreateOrderList(waitDeliverActions: false);
            group.AddFullItem(_msgList);
            return AddGroupToPage(group);
        }

        private Panel CreateAutoDeliveryCard()
        {
            var group = new LiteSettingsGroup("自动发货诊断");
            AddHint(group, "本页只读取待发货/报价处理状态，不修改手机端自动发货配置。");
            _autoDeliveryStatusLabel = CreateMutedLabel("状态：未检查");
            _autoDeliveryTimeLabel = CreateMutedLabel("上次诊断：暂无");
            _autoDeliveryErrorLabel = CreateMutedLabel("错误：无");
            group.AddFullItem(CreateInfoLine("诊断状态", _autoDeliveryStatusLabel));
            group.AddFullItem(CreateInfoLine("诊断时间", _autoDeliveryTimeLabel));
            group.AddFullItem(CreateInfoLine("最近错误", _autoDeliveryErrorLabel));
            return AddGroupToPage(group);
        }

        private Control CreateQuoteLogRow()
        {
            var row = new Panel
            {
                Height = UIUtils.S(42),
                BackColor = Color.Transparent
            };
            var button = new LiteButton("报价日志", false)
            {
                Width = UIUtils.S(96),
                Height = UIUtils.S(30)
            };
            var hint = CreateMutedLabel("打开本机 txt 日志，查看自动刷新、发送报价、确认报价和查询状态记录。");
            button.Click += (_, __) => OpenQuoteLogFile();
            row.Controls.Add(button);
            row.Controls.Add(hint);
            row.Layout += (_, __) =>
            {
                int mid = row.Height / 2;
                button.SetBounds(0, mid - button.Height / 2, button.Width, button.Height);
                hint.SetBounds(button.Right + UIUtils.S(12), 0, Math.Max(1, row.Width - button.Right - UIUtils.S(12)), row.Height);
            };
            return row;
        }

        private Panel CreateAccountToolsCard()
        {
            var group = new LiteSettingsGroup("账号与其它");
            AddHint(group, "纯收货/安全报价自动处理沿用当前 Steam 与悠悠登录状态；涉及转出库存的报价不会自动处理。");
            var row = new Panel
            {
                Height = UIUtils.S(44),
                BackColor = Color.Transparent
            };
            var button = new LiteButton("安全报价自动处理", false)
            {
                Width = UIUtils.S(150),
                Height = UIUtils.S(30)
            };
            button.Click += (_, __) =>
            {
                YouPinAuthStatusCard.OpenAutomationDialog(
                    FindForm(),
                    () => Config,
                    () =>
                    {
                        EnsureSettings();
                        ConfigureService();
                        RefreshState();
                    },
                    YouPinAuthRuntimeServices.Resolve());
                RefreshState();
            };
            row.Controls.Add(button);
            row.Layout += (_, __) => button.SetBounds(0, (row.Height - button.Height) / 2, button.Width, button.Height);
            group.AddFullItem(row);
            return AddGroupToPage(group);
        }

        private YouPinSaleReminderOrderListPanel CreateOrderList(
            bool waitDeliverActions,
            int height = 340,
            bool compactQuoteStyle = false,
            bool groupCompactQuotes = true)
        {
            var actions = new YouPinSaleReminderOrderListActions(
                ShowOrderDetail,
                RunResolvedOrderActionAsync,
                order => RunOrderActionAsync(order, "查询报价状态", source => _presenter.QueryOfferStatusAsync(source.OrderNo)),
                button => _actionButtons.Add(button));
            return CreateOrderListPanel(actions, waitDeliverActions, height, compactQuoteStyle, groupCompactQuotes);
        }

        private void RefreshState()
        {
            if (IsDisposed)
                return;

            var view = _presenter.BuildPageState(_presenter.GetState());
            YouPinSaleReminderTabRefreshPlan refreshPlan = BuildRefreshPlan();
            _renderer.ApplyPageState(view, refreshPlan, BuildRenderTargets());

            if (_layoutMode == YouPinSaleReminderPageLayoutMode.QuoteOnlyCc)
                ApplyCcQuotePresentation(view.WaitDeliver);
        }

        // 仅「悠悠有品」新主入口：渲染后把状态 chip 的「后台未启用」改成「自动发货/报价未启用」并染琥珀色。
        // 不动 presenter/renderer，因此线上 QuoteOnly 不受影响。
        private void ApplyCcQuotePresentation(YouPinSaleReminderOrderSectionViewModel section)
        {
            using var _ = UiJankProfiler.Measure(
                "YouPinSaleReminder.ApplyCcQuotePresentation",
                $"Count={section.Orders.Count}",
                thresholdMs: 1);

            if (_waitDeliverStatusLabel == null)
                return;

            string text = _waitDeliverStatusLabel.Text ?? string.Empty;
            const string legacy = "后台未启用";
            if (text.StartsWith(legacy, StringComparison.Ordinal))
            {
                _waitDeliverStatusLabel.Text = "自动发货/报价未启用";
                _waitDeliverStatusLabel.ForeColor = Color.FromArgb(224, 146, 47);
            }

            if (_waitDeliverSummaryLabel != null)
            {
                string lastRefresh = ExtractLastRefresh(section.Status.SummaryText);
                string error = ExtractError(section.Status.SummaryText);
                _waitDeliverSummaryLabel.Text = $"上次刷新 {lastRefresh}" +
                    (string.IsNullOrWhiteSpace(error) ? string.Empty : " · 错误 " + error);
            }

            var actionableOrders = section.Orders
                .Where(YouPinSaleReminderOrderDisplay.IsActionableQuoteOrder)
                .ToList();
            var passiveOrders = section.Orders
                .Where(order => !YouPinSaleReminderOrderDisplay.IsActionableQuoteOrder(order))
                .ToList();
            _quoteActionableCount = actionableOrders.Count;
            _quotePassiveCount = passiveOrders.Count;
            string summaryRefresh = ExtractLastRefresh(section.Status.SummaryText);
            _quoteSummaryPanel?.SetSnapshot(actionableOrders.Count, passiveOrders.Count, summaryRefresh);

            if (_quoteActionEmptyPanel != null)
                _quoteActionEmptyPanel.Visible = actionableOrders.Count == 0;
            if (_quoteActionList != null)
            {
                _quoteActionList.Visible = actionableOrders.Count > 0;
                if (actionableOrders.Count > 0)
                    _quoteActionList.SetOrders(actionableOrders, "当前暂无需要处理的报价");
            }
            if (_quotePassiveTitleLabel != null)
                _quotePassiveTitleLabel.Text = "无需处理 " + passiveOrders.Count;
            if (_waitDeliverList != null)
                _waitDeliverList.SetOrders(passiveOrders, "暂无无需处理的报价。");

            RefreshSendAllOffersButton(section.Orders);
            _waitDeliverWrapper?.PerformLayout();
        }

        private void RefreshSendAllOffersButton(IReadOnlyList<YouPinSaleOrder> orders)
        {
            if (_sendAllOffersButton == null || _sendAllOffersButton.IsDisposed)
                return;

            bool hasActionable = orders.Any(IsSendOrConfirmAction);
            _sendAllOffersButton.Enabled = hasActionable && !_actionRunner.IsBusy;
            _statusToolTip.SetToolTip(
                _sendAllOffersButton,
                hasActionable ? "处理当前需要操作的报价" : "当前暂无需要处理的报价");
        }

        private static string ExtractLastRefresh(string summary)
        {
            const string marker = "上次刷新：";
            int start = summary.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return "暂无";
            start += marker.Length;
            int end = summary.IndexOf("错误：", start, StringComparison.Ordinal);
            string value = (end >= 0 ? summary[start..end] : summary[start..]).Trim();
            return string.IsNullOrWhiteSpace(value) ? "暂无" : value;
        }

        private static string ExtractError(string summary)
        {
            const string marker = "错误：";
            int start = summary.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            start += marker.Length;
            return summary[start..].Trim();
        }

        private YouPinSaleReminderTabRefreshPlan BuildRefreshPlan()
        {
            return _layoutMode switch
            {
                YouPinSaleReminderPageLayoutMode.QuoteOnly => new YouPinSaleReminderTabRefreshPlan(true, false, false),
                YouPinSaleReminderPageLayoutMode.QuoteOnlyCc => new YouPinSaleReminderTabRefreshPlan(false, false, false),
                YouPinSaleReminderPageLayoutMode.SettingsOnly => new YouPinSaleReminderTabRefreshPlan(false, true, true),
                _ => new YouPinSaleReminderTabRefreshPlan(false, false, false)
            };
        }

        private void QueueRefreshState(int delayMs = 90)
        {
            if (_refreshQueued || IsDisposed || !IsHandleCreated || !Visible)
                return;

            _refreshQueued = true;
            _deferredRefreshTimer ??= CreateDeferredRefreshTimer();
            _deferredRefreshTimer.Stop();
            _deferredRefreshTimer.Interval = Math.Max(1, delayMs);
            _deferredRefreshTimer.Start();
        }

        private void QueueRefreshStateFromAnyThread(int delayMs = 90)
        {
            if (_disposed || IsDisposed || !IsHandleCreated || !Visible)
                return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => QueueRefreshState(delayMs)));
                }
                catch (InvalidOperationException)
                {
                    // 页面正在销毁、已销毁或句柄失效，后台刷新事件可以安全丢弃。
                }
                return;
            }

            QueueRefreshState(delayMs);
        }

        private System.Windows.Forms.Timer CreateDeferredRefreshTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 90 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                _refreshQueued = false;
                if (!IsDisposed && !_disposed && Visible)
                    RefreshState();
            };
            return timer;
        }

        private async Task RunTodoCheckAsync(bool useMock, Control sourceButton)
        {
            await RunCheckAsync(
                sourceButton,
                _todoActionStatusLabel,
                useMock ? "正在发送测试待办提醒..." : "正在检查悠悠有品待办...",
                async () => await _presenter.CheckTodoNowAsync(useMock: useMock, notify: true));
        }

        private async Task RunTodoInlineRefreshAsync(Control sourceButton)
        {
            await RunCheckAsync(
                sourceButton,
                null,
                "运行：正在刷新报价",
                async () => await _presenter.CheckQuoteNowAsync());
        }

        private void OpenQuoteLogFile()
        {
            try
            {
                string path = _presenter.EnsureQuoteLogFile();
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                SetStatus(_waitDeliverActionStatusLabel, "报价日志已打开。", ok: true);
            }
            catch (Exception ex)
            {
                SetStatus(_waitDeliverActionStatusLabel, "打开报价日志失败：" + YouPinMobileApiClient.Sanitize(ex.Message), ok: false);
            }
        }

        private async Task RunMsgCenterCheckAsync(bool useMock, Control sourceButton)
        {
            await RunCheckAsync(
                sourceButton,
                _msgActionStatusLabel,
                useMock ? "正在发送测试消息提醒..." : "正在检查消息中心...",
                async () => await _presenter.CheckMsgCenterNowAsync(useMock: useMock, notify: true));
        }

        private async Task RunCheckAsync(
            Control sourceButton,
            Label? statusLabel,
            string busyText,
            Func<Task<YouPinSaleReminderCheckResult>> action)
        {
            await _actionRunner.RunCheckAsync(sourceButton, statusLabel, busyText, action);
        }

        private async Task RunOrderActionAsync(
            YouPinSaleOrder order,
            string actionName,
            Func<YouPinSaleOrder, Task<YouPinSaleActionResult>> action)
        {
            await _actionRunner.RunOrderActionAsync(order, _waitDeliverActionStatusLabel, actionName, action);
        }

        private async Task RunResolvedOrderActionAsync(YouPinSaleOrder order)
        {
            var resolved = YouPinSaleOrderActionResolver.Resolve(order);
            if (!resolved.CanRun)
            {
                SetStatus(_waitDeliverActionStatusLabel, resolved.StatusReason, ok: false);
                return;
            }

            await RunOrderActionAsync(
                order,
                resolved.ActionName,
                source => RunResolvedPresenterActionAsync(source, resolved.Kind));
        }

        private async Task<YouPinSaleActionResult> RunResolvedPresenterActionAsync(YouPinSaleOrder order, YouPinSaleOrderActionKind kind)
        {
            var result = kind switch
            {
                YouPinSaleOrderActionKind.SendOffer => await _presenter.SendOfferAsync(order.OrderNo),
                YouPinSaleOrderActionKind.ConfirmOffer => await _presenter.ConfirmOfferAsync(order.OrderNo, order.TradeOfferId),
                YouPinSaleOrderActionKind.QueryStatus => await _presenter.QueryOfferStatusAsync(order.OrderNo),
                _ => YouPinSaleActionResult.Skip(YouPinSaleOrderActionResolver.Resolve(order).StatusReason)
            };

            if (kind == YouPinSaleOrderActionKind.ConfirmOffer && result.Ok)
                SchedulePostConfirmOfferRefreshes();

            return result;
        }

        private void SchedulePostConfirmOfferRefreshes()
        {
            _ = RunDelayedQuoteRefreshAsync(500);
            _ = RunDelayedSteamOfferRefreshAsync(1500);
        }

        private async Task RunDelayedQuoteRefreshAsync(int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                if (_disposed || IsDisposed)
                    return;

                var result = await _presenter.CheckQuoteNowAsync("确认报价后刷新").ConfigureAwait(false);
                DiagnosticsLogger.Info("YouPinQuote", $"Post-confirm quote refresh completed. Ok={result.Ok} Skipped={result.Skipped}");
                QueueRefreshStateFromAnyThread(delayMs: 1);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("YouPinQuote", "Post-confirm quote refresh failed.", ex);
            }
        }

        private async Task RunDelayedSteamOfferRefreshAsync(int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                if (_disposed || IsDisposed)
                    return;

                var result = await _steamOffers.LoadOffersAsync(useMock: false).ConfigureAwait(false);
                DiagnosticsLogger.Info("SteamOffer", $"Post-confirm Steam offer refresh completed. Ok={result.Ok}");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("SteamOffer", "Post-confirm Steam offer refresh failed.", ex);
            }
        }

        private async Task RunSendAllOffersAsync(Control sourceButton)
        {
            var orders = _presenter.GetState().RecentWaitDeliverOrders
                .Where(order => !string.IsNullOrWhiteSpace(order.OrderNo))
                .Where(IsSendOrConfirmAction)
                .ToList();
            if (orders.Count == 0)
            {
                SetStatus(_waitDeliverActionStatusLabel, "当前没有可发送或确认的报价。", ok: false);
                return;
            }

            await _actionRunner.RunBatchOrderActionAsync(
                orders,
                _waitDeliverActionStatusLabel,
                "一键处理全部报价",
                async (order, _, __) =>
                {
                    var resolved = YouPinSaleOrderActionResolver.Resolve(order);
                    return resolved.CanRun
                        ? await RunResolvedPresenterActionAsync(order, resolved.Kind)
                        : YouPinSaleActionResult.Skip(resolved.StatusReason);
                },
                interItemDelayMs: 1000);
        }

        private static bool IsSendOrConfirmAction(YouPinSaleOrder order)
        {
            return !YouPinSaleOrderActionResolver.IsPendingBuyQuote(order)
                && YouPinSaleReminderOrderDisplay.IsActionableQuoteOrder(order);
        }

        private void UpdateVisibility()
        {
            if (_layoutMode == YouPinSaleReminderPageLayoutMode.QuoteOnly
                || _layoutMode == YouPinSaleReminderPageLayoutMode.QuoteOnlyCc)
            {
                SetVisible(_todoSettingsWrapper, false);
                SetVisible(_msgSettingsWrapper, false);
                SetVisible(_msgWrapper, false);
                SetVisible(_waitDeliverWrapper, true);
                SetVisible(_autoDeliveryWrapper, false);
                SetVisible(_accountToolsWrapper, false);
                return;
            }

            if (_layoutMode == YouPinSaleReminderPageLayoutMode.SettingsOnly)
            {
                SetVisible(_todoSettingsWrapper, true);
                SetVisible(_msgSettingsWrapper, true);
                SetVisible(_msgWrapper, true);
                SetVisible(_waitDeliverWrapper, false);
                SetVisible(_autoDeliveryWrapper, true);
                SetVisible(_accountToolsWrapper, true);
                return;
            }
        }

        private static void SetVisible(Control? control, bool visible)
        {
            if (control != null && control.Visible != visible)
                control.Visible = visible;
        }

        private YouPinSaleReminderTabWrapperSet BuildTabWrappers()
        {
            return new YouPinSaleReminderTabWrapperSet(
                _waitDeliverWrapper,
                _autoDeliveryWrapper,
                _todoSettingsWrapper,
                _msgSettingsWrapper,
                _msgWrapper,
                _accountToolsWrapper);
        }

        private YouPinSaleReminderRenderTargets BuildRenderTargets()
        {
            return new YouPinSaleReminderRenderTargets(
                _waitDeliverStatusLabel,
                _waitDeliverSummaryLabel,
                _msgStatusLabel,
                _msgSummaryLabel,
                _autoDeliveryStatusLabel,
                _autoDeliveryTimeLabel,
                _autoDeliveryErrorLabel,
                _waitDeliverList,
                _msgList);
        }

        private async void ShowOrderDetail(YouPinSaleOrder order)
        {
            var detailOrder = order;
            if (ShouldFetchSteamCounterparty(order))
            {
                try
                {
                    SetStatus(_waitDeliverActionStatusLabel, "正在读取 Steam 对方信息...", ok: true);
                    detailOrder = await _presenter.EnrichOrderDetailAsync(order).ConfigureAwait(true);
                    bool hasCounterparty = string.Equals(detailOrder.SteamCounterpartyStatus, "已获取", StringComparison.Ordinal)
                        || !string.IsNullOrWhiteSpace(detailOrder.SteamPersonaName);
                    SetStatus(
                        _waitDeliverActionStatusLabel,
                        hasCounterparty ? "Steam 对方信息已读取。" : "Steam 对方信息暂未获取。",
                        ok: hasCounterparty);
                }
                catch
                {
                    order.SteamCounterpartyStatus = "未获取";
                    detailOrder = order;
                    SetStatus(_waitDeliverActionStatusLabel, "Steam 对方信息暂未获取。", ok: false);
                }
            }

            var detail = YouPinSaleReminderPageModel.BuildOrderDetail(detailOrder);
            var rows = detail.Rows
                .Select(row => new LiteDetailDialog.DetailRow(row.Title, row.Value))
                .ToList();

            LiteDetailDialog.Show(
                FindForm(),
                detail.Title,
                rows,
                detail.Body);
        }

        private static bool ShouldFetchSteamCounterparty(YouPinSaleOrder order)
        {
            if (order == null)
                return false;
            if (!string.IsNullOrWhiteSpace(order.SteamPersonaName)
                || order.SteamCounterpartyStatus.StartsWith("未获取", StringComparison.Ordinal)
                || string.Equals(order.SteamCounterpartyStatus, "已获取", StringComparison.Ordinal))
            {
                return false;
            }

            return YouPinSaleReminderOrderDisplay.IsWaitingForSteamToken(order)
                || order.OrderType == 2
                || !string.IsNullOrWhiteSpace(order.OfferId);
        }

        private void SetStatus(Label? label, string text, bool ok)
        {
            if (label == null)
                return;
            label.Text = text;
            label.ForeColor = ok ? UIColors.Positive : UIColors.TextWarn;
            label.AccessibleDescription = text;
            _statusToolTip.SetToolTip(label, text);
        }

    }

    internal sealed class YouPinCcPillLabel : Label
    {
        public YouPinCcPillLabel(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Text = text;
            AutoSize = false;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            ForeColor = Color.FromArgb(224, 146, 47);
            BackColor = Color.Transparent;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(UIColors.IsDark ? Color.FromArgb(66, 48, 20) : Color.FromArgb(255, 247, 230));
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Math.Max(1, Height / 2));
            e.Graphics.FillPath(bg, path);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class YouPinCcQuoteCardPanel : Panel
    {
        public YouPinCcQuoteCardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(rect, UIUtils.S(6));
            using var fill = new SolidBrush(UIColors.CardBg);
            using var border = new Pen(UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class YouPinCcQuoteSummaryPanel : Control
    {
        private readonly Font _labelFont = new("Microsoft YaHei UI", 9F);
        private readonly Font _valueFont = new("Microsoft YaHei UI", 16F, FontStyle.Bold);
        private readonly Font _hintFont = new("Microsoft YaHei UI", 8.5F);
        private int _actionableCount;
        private int _passiveCount;
        private string _lastRefresh = "暂无";

        public YouPinCcQuoteSummaryPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public void SetSnapshot(int actionableCount, int passiveCount, string lastRefresh)
        {
            actionableCount = Math.Max(0, actionableCount);
            passiveCount = Math.Max(0, passiveCount);
            lastRefresh = string.IsNullOrWhiteSpace(lastRefresh) ? "暂无" : lastRefresh.Trim();
            if (_actionableCount == actionableCount
                && _passiveCount == passiveCount
                && string.Equals(_lastRefresh, lastRefresh, StringComparison.Ordinal))
                return;

            _actionableCount = actionableCount;
            _passiveCount = passiveCount;
            _lastRefresh = lastRefresh;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = RoundedRect(rect, UIUtils.S(6)))
            using (var fill = new SolidBrush(UIColors.CardBg))
            using (var border = new Pen(UIColors.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            bool compact = Width < UIUtils.S(760);
            int pad = compact ? UIUtils.S(16) : UIUtils.S(26);
            int cellWidth = Math.Max(1, (Width - pad * 2) / 3);
            DrawCell(e.Graphics, new Rectangle(pad, 0, cellWidth, Height), SummaryKind.Actionable);
            DrawDivider(e.Graphics, pad + cellWidth);
            DrawCell(e.Graphics, new Rectangle(pad + cellWidth, 0, cellWidth, Height), SummaryKind.Passive);
            DrawDivider(e.Graphics, pad + cellWidth * 2);
            DrawCell(e.Graphics, new Rectangle(pad + cellWidth * 2, 0, cellWidth, Height), SummaryKind.Refresh);
        }

        private void DrawCell(Graphics graphics, Rectangle rect, SummaryKind kind)
        {
            bool compact = Width < UIUtils.S(760);
            bool shortHeight = Height < UIUtils.S(110);
            int iconSize = compact ? UIUtils.S(28) : UIUtils.S(34);
            int iconX = rect.Left + (compact ? UIUtils.S(8) : UIUtils.S(16));
            int iconY = rect.Top + (rect.Height - iconSize) / 2;
            Rectangle iconRect = new(iconX, iconY, iconSize, iconSize);
            DrawIcon(graphics, iconRect, kind);

            int textLeft = iconRect.Right + (compact ? UIUtils.S(10) : UIUtils.S(18));
            int textWidth = Math.Max(1, rect.Right - textLeft - (compact ? UIUtils.S(6) : UIUtils.S(12)));
            string label;
            string value;
            string hint;
            Color valueColor = UIColors.TextMain;
            switch (kind)
            {
                case SummaryKind.Actionable:
                    label = "需要处理";
                    value = _actionableCount.ToString();
                    hint = _actionableCount == 0 ? "当前暂无需要处理的报价" : "需要确认或发送报价";
                    valueColor = _actionableCount == 0 ? UIColors.Positive : Color.FromArgb(115, 186, 255);
                    break;
                case SummaryKind.Passive:
                    label = "无需处理";
                    value = _passiveCount.ToString();
                    hint = "已发出或等待对方/平台同步";
                    valueColor = Color.FromArgb(115, 186, 255);
                    break;
                default:
                    label = "最近刷新";
                    value = _lastRefresh;
                    hint = "系统自动刷新";
                    break;
            }

            int labelY = rect.Top + (shortHeight ? UIUtils.S(12) : UIUtils.S(24));
            int valueY = rect.Top + (shortHeight ? UIUtils.S(32) : UIUtils.S(46));
            int hintY = rect.Top + (shortHeight ? UIUtils.S(64) : UIUtils.S(78));
            TextRenderer.DrawText(graphics, label, _labelFont, new Rectangle(textLeft, labelY, textWidth, UIUtils.S(22)), UIColors.TextSub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics, value, _valueFont, new Rectangle(textLeft, valueY, textWidth, UIUtils.S(32)), valueColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(graphics, hint, _hintFont, new Rectangle(textLeft, hintY, textWidth, UIUtils.S(24)), UIColors.TextSub, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static void DrawDivider(Graphics graphics, int x)
        {
            using var pen = new Pen(UIColors.Border);
            graphics.DrawLine(pen, x, UIUtils.S(22), x, Math.Max(UIUtils.S(22), graphics.VisibleClipBounds.Height - UIUtils.S(22)));
        }

        private static void DrawIcon(Graphics graphics, Rectangle rect, SummaryKind kind)
        {
            Color color = kind == SummaryKind.Actionable
                ? UIColors.Positive
                : kind == SummaryKind.Passive
                    ? UIColors.TextSub
                    : Color.FromArgb(150, 165, 182);
            using var pen = new Pen(color, 2F);
            if (kind == SummaryKind.Refresh)
            {
                graphics.DrawEllipse(pen, rect);
                int cx = rect.Left + rect.Width / 2;
                int cy = rect.Top + rect.Height / 2;
                graphics.DrawLine(pen, cx, cy, cx, rect.Top + UIUtils.S(8));
                graphics.DrawLine(pen, cx, cy, rect.Right - UIUtils.S(8), cy);
                return;
            }

            if (kind == SummaryKind.Passive)
            {
                Rectangle box = new(rect.Left + UIUtils.S(3), rect.Top + UIUtils.S(9), rect.Width - UIUtils.S(6), rect.Height - UIUtils.S(12));
                using var path = RoundedRect(box, UIUtils.S(4));
                graphics.DrawPath(pen, path);
                graphics.DrawLine(pen, box.Left + UIUtils.S(5), box.Top, box.Left + UIUtils.S(10), box.Top - UIUtils.S(7));
                graphics.DrawLine(pen, box.Right - UIUtils.S(5), box.Top, box.Right - UIUtils.S(10), box.Top - UIUtils.S(7));
                graphics.DrawLine(pen, box.Left + UIUtils.S(10), box.Top - UIUtils.S(7), box.Right - UIUtils.S(10), box.Top - UIUtils.S(7));
                return;
            }

            graphics.DrawEllipse(pen, rect);
            graphics.DrawLine(pen, rect.Left + UIUtils.S(9), rect.Top + UIUtils.S(18), rect.Left + UIUtils.S(15), rect.Top + UIUtils.S(24));
            graphics.DrawLine(pen, rect.Left + UIUtils.S(15), rect.Top + UIUtils.S(24), rect.Right - UIUtils.S(8), rect.Top + UIUtils.S(10));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _labelFont.Dispose();
                _valueFont.Dispose();
                _hintFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private enum SummaryKind
        {
            Actionable,
            Passive,
            Refresh
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class YouPinCcQuoteEmptyPanel : Control
    {
        private readonly Font _titleFont = new("Microsoft YaHei UI", 15F, FontStyle.Bold);
        private readonly Font _hintFont = new("Microsoft YaHei UI", 10F);

        public YouPinCcQuoteEmptyPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool compact = Height < UIUtils.S(170);
            bool veryCompact = Height < UIUtils.S(130);
            int iconSize = veryCompact ? UIUtils.S(38) : compact ? UIUtils.S(44) : UIUtils.S(54);
            int iconX = (Width - iconSize) / 2;
            int iconY = Math.Max(UIUtils.S(6), Height / 2 - (veryCompact ? UIUtils.S(48) : compact ? UIUtils.S(50) : UIUtils.S(58)));
            DrawInboxCheck(e.Graphics, new Rectangle(iconX, iconY, iconSize, iconSize));
            int titleGap = veryCompact ? UIUtils.S(6) : compact ? UIUtils.S(10) : UIUtils.S(16);
            int hintGap = veryCompact ? UIUtils.S(32) : compact ? UIUtils.S(40) : UIUtils.S(50);

            TextRenderer.DrawText(
                e.Graphics,
                "当前暂无需要处理的报价",
                _titleFont,
                new Rectangle(UIUtils.S(20), iconY + iconSize + titleGap, Math.Max(1, Width - UIUtils.S(40)), UIUtils.S(30)),
                UIColors.TextMain,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(
                e.Graphics,
                "系统会自动刷新；无需手动查询状态",
                _hintFont,
                new Rectangle(UIUtils.S(20), iconY + iconSize + hintGap, Math.Max(1, Width - UIUtils.S(40)), UIUtils.S(24)),
                UIColors.TextSub,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static void DrawInboxCheck(Graphics graphics, Rectangle rect)
        {
            using var pen = new Pen(Color.FromArgb(150, 165, 182), 2.4F);
            Rectangle box = new(rect.Left + UIUtils.S(7), rect.Top + UIUtils.S(14), rect.Width - UIUtils.S(14), rect.Height - UIUtils.S(16));
            using (var path = RoundedRect(box, UIUtils.S(6)))
                graphics.DrawPath(pen, path);
            graphics.DrawLine(pen, box.Left + UIUtils.S(8), box.Top, box.Left + UIUtils.S(15), box.Top - UIUtils.S(10));
            graphics.DrawLine(pen, box.Right - UIUtils.S(8), box.Top, box.Right - UIUtils.S(15), box.Top - UIUtils.S(10));
            graphics.DrawLine(pen, box.Left + UIUtils.S(15), box.Top - UIUtils.S(10), box.Right - UIUtils.S(15), box.Top - UIUtils.S(10));

            int badge = UIUtils.S(26);
            Rectangle badgeRect = new(rect.Right - badge - UIUtils.S(3), rect.Bottom - badge - UIUtils.S(2), badge, badge);
            using var badgeBrush = new SolidBrush(UIColors.Positive);
            graphics.FillEllipse(badgeBrush, badgeRect);
            using var checkPen = new Pen(Color.White, 2.4F);
            graphics.DrawLine(checkPen, badgeRect.Left + UIUtils.S(7), badgeRect.Top + UIUtils.S(14), badgeRect.Left + UIUtils.S(11), badgeRect.Top + UIUtils.S(18));
            graphics.DrawLine(checkPen, badgeRect.Left + UIUtils.S(11), badgeRect.Top + UIUtils.S(18), badgeRect.Right - UIUtils.S(6), badgeRect.Top + UIUtils.S(8));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _hintFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class YouPinCcQuoteTableHeaderPanel : Control
    {
        private readonly Font _font = new("Microsoft YaHei UI", 9F, FontStyle.Bold);

        public YouPinCcQuoteTableHeaderPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            bool compact = Width < UIUtils.S(720);
            int pad = compact ? UIUtils.S(12) : UIUtils.S(18);
            int right = Math.Max(pad, Width - pad);
            int actionWidth = compact ? UIUtils.S(62) : UIUtils.S(72);
            int priceWidth = compact ? UIUtils.S(86) : UIUtils.S(112);
            int statusWidth = compact ? UIUtils.S(94) : UIUtils.S(112);
            Rectangle action = new(Math.Max(pad, right - actionWidth), 0, actionWidth, Height - 1);
            right = action.Left - UIUtils.S(14);
            Rectangle price = new(Math.Max(pad, right - priceWidth), 0, priceWidth, Height - 1);
            right = price.Left - UIUtils.S(14);
            Rectangle status = new(Math.Max(pad, right - statusWidth), 0, statusWidth, Height - 1);
            Rectangle item = new(pad, 0, Math.Max(1, status.Left - pad - UIUtils.S(22)), Height - 1);

            DrawHeader(e.Graphics, "物品信息", item, ContentAlignment.MiddleLeft);
            DrawHeader(e.Graphics, "状态", status, ContentAlignment.MiddleCenter);
            DrawHeader(e.Graphics, "价格", price, ContentAlignment.MiddleRight);
            DrawHeader(e.Graphics, "操作", action, ContentAlignment.MiddleCenter);
        }

        private void DrawHeader(Graphics graphics, string text, Rectangle rect, ContentAlignment align)
        {
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            flags |= align == ContentAlignment.MiddleRight
                ? TextFormatFlags.Right
                : align == ContentAlignment.MiddleCenter ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
            TextRenderer.DrawText(graphics, text, _font, rect, UIColors.TextSub, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _font.Dispose();
            base.Dispose(disposing);
        }
    }
}
