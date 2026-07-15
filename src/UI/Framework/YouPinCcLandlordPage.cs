using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System.Drawing;
using System.Globalization;
using AntdSwitch = AntdUI.Switch;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class YouPinCcLandlordPage : FrameworkSettingsPageBase
    {
        private readonly YouPinLandlordPagePresenter _presenter;
        private readonly LiteButton _repriceTab;
        private readonly LiteButton _inventoryTab;
        private readonly Panel _contextBar;
        private Panel? _contextWrapper;
        private readonly LiteButton _unifiedSettingsButton;
        private readonly LiteButton _separateSettingsButton;
        private readonly Label _configurationCaption;
        private readonly Label _scopeLabel;
        private readonly Label _currentTypeCaption;
        private readonly LiteButton _helpButton;
        private readonly YouPinCcRoundedPanel _settingsCard;
        private readonly YouPinCcRoundedPanel _shelfCard;
        private readonly Panel _inventoryPage;
        private readonly LiteButton _inventoryWhitelistButton;
        private readonly LiteButton _inventoryBlacklistButton;
        private readonly LiteButton _inventoryPerAssetButton;
        private readonly LiteButton _inventorySameNameButton;
        private readonly LandlordNumberInput _inventoryIntervalInput;
        private readonly LandlordNumberInput _inventoryExecutionIntervalInput;
        private readonly Label _inventoryWeeklyFreeStatus;
        private readonly LiteButton _inventoryWeeklyFreeButton;
        private readonly AntdSwitch _inventoryCooldownSwitch;
        private readonly LandlordTimeInput _inventoryCooldownStartInput;
        private readonly LandlordTimeInput _inventoryCooldownEndInput;
        private readonly AntdSwitch _inventoryEnabledSwitch;
        private readonly Label _inventoryStatusLabel;
        private readonly Label _inventoryLastCheckedLabel;
        private readonly Label _inventoryLastExecutedLabel;
        private readonly Label _inventoryListTitle;
        private readonly Label _inventoryListHint;
        private readonly YouPinLandlordInventoryHeader _inventoryHeader;
        private readonly TextBox _inventorySearchInput;
        private readonly YouPinLandlordInventoryListPanel _inventoryList;
        private readonly Label _inventoryEmptyLabel;
        private readonly LiteButton _inventoryScanButton;
        private readonly LiteButton _inventoryExecuteButton;
        private readonly LiteButton _inventoryHelpButton;
        private readonly Panel _repriceMasterBar;
        private readonly Panel _inventoryMasterBar;
        private readonly LiteButton _zeroCdButton;
        private readonly LiteButton _inventoryRentalButton;
        private readonly LandlordNumberInput _targetRankInput;
        private readonly LandlordNumberInput _intervalInput;
        private readonly LandlordNumberInput _executionIntervalInput;
        private readonly LiteButton _repricePerAssetButton;
        private readonly LiteButton _repriceSameNameButton;
        private readonly Label _weeklyFreeStatus;
        private readonly LiteButton _weeklyFreeButton;
        private readonly AntdSwitch _cooldownSwitch;
        private readonly LandlordTimeInput _cooldownStartInput;
        private readonly LandlordTimeInput _cooldownEndInput;
        private readonly AntdSwitch _enabledSwitch;
        private readonly Label _statusLabel;
        private readonly Label _lastCheckedLabel;
        private readonly Label _lastExecutedLabel;
        private readonly Label _preferenceLabel;
        private readonly Label _shelfTitle;
        private readonly Label _shelfHint;
        private readonly Label _emptyShelfLabel;
        private readonly YouPinLandlordShelfListPanel _shelfList;
        private readonly LiteButton _scanButton;
        private readonly LiteButton _executeButton;
        private readonly UiAsyncRefreshController<YouPinLandlordPageStateViewModel> _refreshController;
        private readonly UiAsyncRefreshController<YouPinLandlordInventoryPageStateViewModel> _inventoryRefreshController;

        private YouPinRentalShelfType _selectedRentalType = YouPinRentalShelfType.ZeroCd;
        private bool _updatingPolicyControls;
        private bool _snapshotSubscribed;
        private bool _showingReprice = true;
        private IReadOnlyList<YouPinLandlordInventoryRowViewModel> _inventoryRows
            = Array.Empty<YouPinLandlordInventoryRowViewModel>();
        private IReadOnlyList<YouPinLandlordShelfRowViewModel> _shelfRows
            = Array.Empty<YouPinLandlordShelfRowViewModel>();

        public YouPinCcLandlordPage()
            : this(new YouPinLandlordPagePresenter())
        {
        }

        internal YouPinCcLandlordPage(YouPinLandlordPagePresenter presenter)
        {
            _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

            (_repriceTab, _inventoryTab) = CreatePageTabs();
            (_settingsCard,
                _zeroCdButton,
                _inventoryRentalButton,
                _targetRankInput,
                _intervalInput,
                _executionIntervalInput,
                _repricePerAssetButton,
                _repriceSameNameButton,
                _weeklyFreeStatus,
                _weeklyFreeButton,
                _cooldownSwitch,
                _cooldownStartInput,
                _cooldownEndInput,
                _enabledSwitch,
                _statusLabel,
                _lastCheckedLabel,
                _lastExecutedLabel,
                _preferenceLabel,
                _scanButton,
                _executeButton,
                _helpButton) = CreateSettingsCard();
            _unifiedSettingsButton = new LiteButton("统一设置", false);
            _separateSettingsButton = new LiteButton("分别设置", false);
            _configurationCaption = CreateLabel("配置", 8.8F, FontStyle.Regular, UIColors.TextSub);
            _scopeLabel = CreateLabel("作用于 0CD + 库存出租", 8.8F, FontStyle.Bold, UIColors.TextMain);
            _currentTypeCaption = CreateLabel("当前类型", 8.8F, FontStyle.Regular, UIColors.TextSub);
            (_shelfCard, _shelfTitle, _shelfHint, _emptyShelfLabel, _shelfList) = CreateShelfCard();
            (_inventoryPage,
                _inventoryWhitelistButton,
                _inventoryBlacklistButton,
                _inventoryPerAssetButton,
                _inventorySameNameButton,
                _inventoryIntervalInput,
                _inventoryExecutionIntervalInput,
                _inventoryWeeklyFreeStatus,
                _inventoryWeeklyFreeButton,
                _inventoryCooldownSwitch,
                _inventoryCooldownStartInput,
                _inventoryCooldownEndInput,
                _inventoryEnabledSwitch,
                _inventoryStatusLabel,
                _inventoryLastCheckedLabel,
                _inventoryLastExecutedLabel,
                _inventoryListTitle,
                _inventoryListHint,
                _inventoryHeader,
                _inventorySearchInput,
                _inventoryList,
                _inventoryEmptyLabel,
                _inventoryScanButton,
                _inventoryExecuteButton,
                _inventoryHelpButton) = CreateInventoryPage();
            _repriceMasterBar = CreateRepriceMasterBar();
            _inventoryMasterBar = CreateInventoryMasterBar();
            _contextBar = CreateContextBar();

            _refreshController = CreateAsyncRefreshController(
                "YouPinLandlord.Refresh",
                async (reason, cancellationToken) =>
                {
                    if (!string.Equals(reason.Key, "运行快照变化", StringComparison.Ordinal))
                    {
                        ParseRepriceRunSource(
                            reason.Source,
                            out YouPinLandlordRunMode mode,
                            out YouPinRentalScanScope scope);
                        YouPinLandlordRunResult result = mode == YouPinLandlordRunMode.ScanOnly
                            ? await _presenter.ScanRentalNowAsync(
                                scope,
                                reason.Key,
                                cancellationToken).ConfigureAwait(false)
                            : await _presenter.ExecuteRentalNowAsync(
                                scope,
                                reason.Key,
                                cancellationToken).ConfigureAwait(false);
                        YouPinLandlordPageStateViewModel state = _presenter.GetPageState(GetCurrentScope());
                        return result.Skipped ? state with { StatusText = result.Message } : state;
                    }

                    return _presenter.GetPageState(GetCurrentScope());
                },
                RenderPageState,
                new UiRefreshOptions { Name = "YouPinLandlord.Refresh", DebounceMs = 0 });
            _inventoryRefreshController = CreateAsyncRefreshController(
                "YouPinLandlord.InventoryRefresh",
                async (reason, cancellationToken) =>
                {
                    if (!string.Equals(reason.Key, "运行快照变化", StringComparison.Ordinal))
                    {
                        YouPinLandlordRunMode mode = Enum.TryParse(
                            reason.Source,
                            ignoreCase: false,
                            out YouPinLandlordRunMode parsed)
                                ? parsed
                                : YouPinLandlordRunMode.ScanOnly;
                        YouPinLandlordRunResult result = mode == YouPinLandlordRunMode.ScanOnly
                            ? await _presenter.ScanInventoryNowAsync(
                                reason.Key,
                                cancellationToken).ConfigureAwait(false)
                            : await _presenter.ExecuteInventoryNowAsync(
                                reason.Key,
                                cancellationToken).ConfigureAwait(false);
                        YouPinLandlordInventoryPageStateViewModel state = _presenter.GetInventoryPageState();
                        return result.Skipped ? state with { StatusText = result.Message } : state;
                    }
                    return _presenter.GetInventoryPageState();
                },
                RenderInventoryPageState,
                new UiRefreshOptions { Name = "YouPinLandlord.InventoryRefresh", DebounceMs = 0 });

            BuildPage();
            WireEvents();
            RenderPolicy();
            RenderPageState(_presenter.GetPageState(GetCurrentScope()));
            RenderInventoryPolicy();
            RenderInventoryPageState(_presenter.GetInventoryPageState());
            ShowPage(reprice: true);
        }

        public override void SetSettingsContext(Settings config, MainForm mainForm, UIController ui)
        {
            base.SetSettingsContext(config, mainForm, ui);
            _presenter.Configure(config);
            RenderPolicy();
            RenderPageState(_presenter.GetPageState(GetCurrentScope()));
            RenderInventoryPolicy();
            RenderInventoryPageState(_presenter.GetInventoryPageState());
        }

        public override void Activate()
        {
            base.Activate();
            SubscribeSnapshot();

            YouPinLandlordPageStateViewModel state = _presenter.GetPageState(GetCurrentScope());
            RenderPolicy();
            RenderPageState(state);
            RenderInventoryPolicy();
            RenderInventoryPageState(_presenter.GetInventoryPageState());
        }

        public override void Deactivate()
        {
            UnsubscribeSnapshot();
            base.Deactivate();
        }

        private void BuildPage()
        {
            ClearPage();
            Container.Padding = UIUtils.S(new Padding(
                FrameworkSettingsPageLayoutHelper.DefaultPageHorizontalPadding,
                YouPinCcLandlordLayoutModel.CompactPageTopPadding,
                FrameworkSettingsPageLayoutHelper.DefaultPageHorizontalPadding,
                FrameworkSettingsPageLayoutHelper.DefaultPageBottomPadding));

            YouPinCcUi.AddTopCard(Container, _inventoryPage);
            YouPinCcUi.AddTopCard(Container, _shelfCard);
            YouPinCcUi.AddTopCard(Container, _settingsCard);
            _contextWrapper = YouPinCcUi.AddTopCard(
                Container,
                _contextBar,
                bottomGap: YouPinCcLandlordLayoutModel.ContextBarBottomGap);
        }

        private Panel CreateContextBar()
        {
            var bar = new Panel
            {
                Height = UIUtils.S(YouPinCcLandlordLayoutModel.GetContextBarLogicalHeight(reprice: true)),
                BackColor = Color.Transparent
            };
            bar.Controls.AddRange(new Control[]
            {
                _repriceTab, _inventoryTab, _configurationCaption,
                _unifiedSettingsButton, _separateSettingsButton,
                _scopeLabel, _currentTypeCaption, _zeroCdButton, _inventoryRentalButton,
                _repriceMasterBar, _inventoryMasterBar
            });
            bar.Layout += (_, __) =>
            {
                int buttonHeight = UIUtils.S(36);
                _repriceTab.SetBounds(0, UIUtils.S(4), UIUtils.S(136), buttonHeight);
                _inventoryTab.SetBounds(_repriceTab.Right + UIUtils.S(8), UIUtils.S(4), UIUtils.S(136), buttonHeight);
                int masterLeft = _inventoryTab.Right + UIUtils.S(16);
                _repriceMasterBar.SetBounds(
                    masterLeft,
                    0,
                    Math.Max(1, bar.Width - masterLeft),
                    UIUtils.S(44));
                _inventoryMasterBar.SetBounds(
                    masterLeft,
                    0,
                    Math.Max(1, bar.Width - masterLeft),
                    UIUtils.S(44));

                int y = UIUtils.S(48);
                _configurationCaption.SetBounds(0, y, UIUtils.S(48), UIUtils.S(30));
                _unifiedSettingsButton.SetBounds(_configurationCaption.Right, y - UIUtils.S(2), UIUtils.S(92), UIUtils.S(34));
                _separateSettingsButton.SetBounds(_unifiedSettingsButton.Right + UIUtils.S(6), y - UIUtils.S(2), UIUtils.S(92), UIUtils.S(34));
                _scopeLabel.SetBounds(_separateSettingsButton.Right + UIUtils.S(16), y, UIUtils.S(190), UIUtils.S(30));
                _currentTypeCaption.SetBounds(_separateSettingsButton.Right + UIUtils.S(16), y, UIUtils.S(76), UIUtils.S(30));
                _zeroCdButton.SetBounds(_currentTypeCaption.Right, y - UIUtils.S(2), UIUtils.S(102), UIUtils.S(34));
                _inventoryRentalButton.SetBounds(_zeroCdButton.Right + UIUtils.S(6), y - UIUtils.S(2), UIUtils.S(102), UIUtils.S(34));
            };
            return bar;
        }

        private Panel CreateRepriceMasterBar()
        {
            return CreateMasterBar(_statusLabel, _enabledSwitch, _helpButton, "自动改价总开关");
        }

        private Panel CreateInventoryMasterBar()
        {
            return CreateMasterBar(
                _inventoryStatusLabel,
                _inventoryEnabledSwitch,
                _inventoryHelpButton,
                "自动出租总开关");
        }

        private Panel CreateMasterBar(
            Label status,
            AntdSwitch enabled,
            LiteButton help,
            string captionText)
        {
            var bar = new Panel
            {
                Height = UIUtils.S(44),
                BackColor = Color.Transparent
            };
            var masterCaption = CreateLabel(
                captionText,
                9.2F,
                FontStyle.Bold,
                UIColors.Primary,
                ContentAlignment.MiddleRight);
            status.AutoEllipsis = false;
            status.TextAlign = ContentAlignment.MiddleRight;
            bar.Controls.AddRange(new Control[]
            {
                status,
                masterCaption,
                enabled,
                help
            });
            bar.Layout += (_, __) =>
            {
                int pad = UIUtils.S(10);
                int right = bar.Width - pad;
                help.SetBounds(
                    right - help.Width,
                    UIUtils.S(5),
                    help.Width,
                    UIUtils.S(34));
                enabled.SetBounds(
                    help.Left - UIUtils.S(58),
                    UIUtils.S(9),
                    UIUtils.S(48),
                    UIUtils.S(26));
                masterCaption.SetBounds(
                    enabled.Left - UIUtils.S(124),
                    UIUtils.S(7),
                    UIUtils.S(116),
                    UIUtils.S(30));
                status.SetBounds(
                    pad,
                    UIUtils.S(3),
                    Math.Max(1, masterCaption.Left - pad - UIUtils.S(12)),
                    UIUtils.S(38));
            };
            return bar;
        }

        private static (LiteButton Reprice, LiteButton Inventory) CreatePageTabs()
        {
            return (
                new LiteButton("租赁自动改价", false),
                new LiteButton("库存自动出租", false));
        }

        private static (
            YouPinCcRoundedPanel Card,
            LiteButton ZeroCd,
            LiteButton InventoryRental,
            LandlordNumberInput TargetRank,
            LandlordNumberInput Interval,
            LandlordNumberInput ExecutionInterval,
            LiteButton PerAsset,
            LiteButton SameName,
            Label WeeklyFreeStatus,
            LiteButton WeeklyFreeSettings,
            AntdSwitch Cooldown,
            LandlordTimeInput CooldownStart,
            LandlordTimeInput CooldownEnd,
            AntdSwitch Enabled,
            Label Status,
            Label LastChecked,
            Label LastExecuted,
            Label Preference,
            LiteButton Scan,
            LiteButton Execute,
            LiteButton Help) CreateSettingsCard()
        {
            var card = new YouPinCcRoundedPanel { Height = UIUtils.S(384) };
            var title = CreateLabel("租赁自动改价", 14F, FontStyle.Bold, UIColors.TextMain);
            var subtitle = CreateLabel(
                "扫描只生成计划；执行按独立间隔自动运行，也可立即执行一次。",
                8.8F,
                FontStyle.Regular,
                UIColors.TextSub);
            var basicSection = CreateLabel("基础设置", 8.8F, FontStyle.Bold, UIColors.TextMain);
            var limitSection = CreateLabel("执行限制", 8.8F, FontStyle.Bold, UIColors.TextMain);
            var bodyDivider = new Panel { BackColor = UIColors.Border };
            var footerDivider = new Panel { BackColor = UIColors.Border };
            var help = new LiteButton("页面说明", false) { Width = UIUtils.S(96) };
            var zeroCd = new LiteButton("0CD出租", false) { Width = UIUtils.S(110) };
            var inventoryRental = new LiteButton("库存出租", false) { Width = UIUtils.S(110) };
            var targetCaption = CreateLabel("目标出租位", 9F, FontStyle.Regular, UIColors.TextSub);
            var target = CreateNumberInput(1, 20, 3);
            var targetUnit = CreateLabel("位以内", 9F, FontStyle.Regular, UIColors.TextSub);
            var antiPressureCaption = CreateLabel("防自压保护", 9F, FontStyle.Regular, UIColors.TextSub);
            var antiPressure = CreateLabel("● 始终开启", 9F, FontStyle.Bold, UIColors.Positive);
            var selectionScopeCaption = CreateLabel("名单方式", 9F, FontStyle.Regular, UIColors.TextSub);
            var perAsset = new LiteButton("逐件名单", false) { Width = UIUtils.S(110) };
            var sameName = new LiteButton("同款名单", false) { Width = UIUtils.S(110) };
            var weeklyCaption = CreateLabel("周周免租", 9F, FontStyle.Regular, UIColors.TextSub);
            var weeklyStatus = CreateLabel("已关闭", 9F, FontStyle.Bold, UIColors.TextSub);
            var weeklySettings = new LiteButton("设置", false) { Width = UIUtils.S(86) };
            var cooldownCaption = CreateLabel("冷却时段", 9F, FontStyle.Regular, UIColors.TextSub);
            var cooldown = new AntdSwitch { Checked = false, Size = UIUtils.S(new Size(42, 22)) };
            var cooldownStart = CreateTimeInput(new TimeSpan(0, 0, 0));
            var cooldownSeparator = CreateLabel("至", 8.8F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleCenter);
            var cooldownEnd = CreateTimeInput(new TimeSpan(8, 0, 0));
            var intervalCaption = CreateLabel("扫描间隔", 9F, FontStyle.Regular, UIColors.TextSub);
            var interval = CreateIntervalInput(execution: false);
            var intervalUnit = CreateLabel("分钟（最低 20 分钟）", 9F, FontStyle.Regular, UIColors.TextSub);
            var executionIntervalCaption = CreateLabel("执行间隔", 9F, FontStyle.Regular, UIColors.TextSub);
            var executionInterval = CreateIntervalInput(execution: true);
            var executionIntervalUnit = CreateLabel(
                "分钟（最低 20；3 分钟批次冷却）",
                8.6F,
                FontStyle.Regular,
                UIColors.TextSub);
            var enabled = new AntdSwitch { Checked = false, Size = UIUtils.S(new Size(48, 26)) };
            var status = CreateLabel("未检查", 9F, FontStyle.Bold, UIColors.TextMain);
            status.AutoEllipsis = false;
            status.TextAlign = ContentAlignment.MiddleRight;
            var lastChecked = CreateLabel("上次检查：暂无", 8.6F, FontStyle.Regular, UIColors.TextSub);
            var lastExecuted = CreateLabel("上次执行：暂无", 8.6F, FontStyle.Regular, UIColors.TextSub);
            var preference = CreateLabel("尚未读取悠悠云端偏好", 8.6F, FontStyle.Regular, UIColors.TextSub);
            var preferenceButton = new LiteButton("一键定价偏好", false) { Width = UIUtils.S(142) };
            var queueButton = new LiteButton("执行队列", false) { Width = UIUtils.S(104) };
            var logButton = new LiteButton("运行日志", false) { Width = UIUtils.S(104) };
            var scan = new LiteButton("扫描货架", false) { Width = UIUtils.S(104) };
            var execute = new LiteButton("立即执行一次", true) { Width = UIUtils.S(126) };

            card.Controls.AddRange(new Control[]
            {
                title, subtitle, basicSection, limitSection, bodyDivider, footerDivider,
                zeroCd, inventoryRental,
                targetCaption, target, targetUnit,
                selectionScopeCaption, perAsset, sameName,
                antiPressureCaption, antiPressure,
                weeklyCaption, weeklyStatus, weeklySettings,
                cooldownCaption, cooldown, cooldownStart, cooldownSeparator, cooldownEnd,
                intervalCaption, interval, intervalUnit,
                executionIntervalCaption, executionInterval, executionIntervalUnit,
                lastChecked, lastExecuted, preference,
                preferenceButton, queueButton, logButton, scan, execute
            });

            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(24);
                int right = card.Width - pad;
                int columnGap = UIUtils.S(34);
                int middle = card.Width / 2;
                int rightColumn = middle + columnGap;
                title.SetBounds(pad, UIUtils.S(18), Math.Max(1, right - pad), UIUtils.S(30));
                subtitle.SetBounds(pad, UIUtils.S(47), Math.Max(1, right - pad), UIUtils.S(24));
                basicSection.SetBounds(pad, UIUtils.S(78), UIUtils.S(120), UIUtils.S(24));
                limitSection.SetBounds(rightColumn, UIUtils.S(78), UIUtils.S(120), UIUtils.S(24));
                bodyDivider.SetBounds(middle, UIUtils.S(76), 1, UIUtils.S(218));

                targetCaption.SetBounds(pad, UIUtils.S(108), UIUtils.S(88), UIUtils.S(30));
                target.SetBounds(pad + UIUtils.S(92), UIUtils.S(106), UIUtils.S(84), UIUtils.S(32));
                targetUnit.SetBounds(target.Right + UIUtils.S(8), UIUtils.S(108), UIUtils.S(80), UIUtils.S(30));
                intervalCaption.SetBounds(pad, UIUtils.S(148), UIUtils.S(88), UIUtils.S(30));
                interval.SetBounds(pad + UIUtils.S(92), UIUtils.S(146), UIUtils.S(84), UIUtils.S(32));
                intervalUnit.SetBounds(interval.Right + UIUtils.S(8), UIUtils.S(148), UIUtils.S(170), UIUtils.S(30));
                executionIntervalCaption.SetBounds(pad, UIUtils.S(188), UIUtils.S(88), UIUtils.S(30));
                executionInterval.SetBounds(pad + UIUtils.S(92), UIUtils.S(186), UIUtils.S(84), UIUtils.S(32));
                executionIntervalUnit.SetBounds(
                    executionInterval.Right + UIUtils.S(8),
                    UIUtils.S(188),
                    Math.Max(1, middle - executionInterval.Right - UIUtils.S(18)),
                    UIUtils.S(30));

                selectionScopeCaption.SetBounds(pad, UIUtils.S(230), UIUtils.S(88), UIUtils.S(30));
                perAsset.SetBounds(pad + UIUtils.S(92), UIUtils.S(228), perAsset.Width, UIUtils.S(34));
                sameName.SetBounds(perAsset.Right + UIUtils.S(6), UIUtils.S(228), sameName.Width, UIUtils.S(34));
                antiPressureCaption.SetBounds(pad, UIUtils.S(270), UIUtils.S(88), UIUtils.S(28));
                antiPressure.SetBounds(pad + UIUtils.S(92), UIUtils.S(270), UIUtils.S(120), UIUtils.S(28));

                weeklyCaption.SetBounds(rightColumn, UIUtils.S(108), UIUtils.S(88), UIUtils.S(30));
                weeklyStatus.SetBounds(rightColumn + UIUtils.S(92), UIUtils.S(108), UIUtils.S(74), UIUtils.S(30));
                weeklySettings.SetBounds(weeklyStatus.Right + UIUtils.S(8), UIUtils.S(106), weeklySettings.Width, UIUtils.S(34));

                cooldownCaption.SetBounds(rightColumn, UIUtils.S(154), UIUtils.S(88), UIUtils.S(30));
                cooldown.SetBounds(rightColumn + UIUtils.S(92), UIUtils.S(158), UIUtils.S(42), UIUtils.S(22));
                cooldownStart.SetBounds(cooldown.Right + UIUtils.S(12), UIUtils.S(152), UIUtils.S(76), UIUtils.S(32));
                cooldownSeparator.SetBounds(cooldownStart.Right + UIUtils.S(5), UIUtils.S(154), UIUtils.S(24), UIUtils.S(30));
                cooldownEnd.SetBounds(cooldownSeparator.Right + UIUtils.S(5), UIUtils.S(152), UIUtils.S(76), UIUtils.S(32));
                lastChecked.SetBounds(rightColumn, UIUtils.S(202), Math.Max(1, right - rightColumn), UIUtils.S(28));
                lastExecuted.SetBounds(rightColumn, UIUtils.S(232), Math.Max(1, right - rightColumn), UIUtils.S(28));
                preference.SetBounds(rightColumn, UIUtils.S(262), Math.Max(1, right - rightColumn), UIUtils.S(28));

                footerDivider.SetBounds(0, UIUtils.S(304), card.Width, 1);
                execute.SetBounds(right - execute.Width, UIUtils.S(322), execute.Width, UIUtils.S(36));
                scan.SetBounds(execute.Left - UIUtils.S(8) - scan.Width, UIUtils.S(322), scan.Width, UIUtils.S(36));
                logButton.SetBounds(scan.Left - UIUtils.S(8) - logButton.Width, UIUtils.S(322), logButton.Width, UIUtils.S(36));
                queueButton.SetBounds(logButton.Left - UIUtils.S(8) - queueButton.Width, UIUtils.S(322), queueButton.Width, UIUtils.S(36));
                preferenceButton.SetBounds(queueButton.Left - UIUtils.S(8) - preferenceButton.Width, UIUtils.S(322), preferenceButton.Width, UIUtils.S(36));
            };

            preferenceButton.Tag = "preference";
            queueButton.Tag = "queue";
            logButton.Tag = "log";
            help.Tag = "help";
            return (card, zeroCd, inventoryRental, target, interval, executionInterval,
                perAsset, sameName, weeklyStatus, weeklySettings,
                cooldown, cooldownStart, cooldownEnd,
                enabled, status, lastChecked, lastExecuted, preference, scan, execute, help);
        }

        private static (YouPinCcRoundedPanel Card, Label Title, Label Hint, Label Empty, YouPinLandlordShelfListPanel List)
            CreateShelfCard()
        {
            var card = new YouPinCcRoundedPanel { Height = UIUtils.S(414) };
            var title = CreateLabel("当前货架 0", 11F, FontStyle.Bold, UIColors.TextMain);
            var hint = CreateLabel(
                "仅显示饰品名称与判断结果，不加载饰品图片。",
                8.6F,
                FontStyle.Regular,
                UIColors.TextSub);
            var header = new YouPinLandlordShelfHeader();
            var list = new YouPinLandlordShelfListPanel();
            var empty = CreateLabel(
                "暂无货架数据\r\n点击“扫描货架”读取 0CD 或普通出租货架。",
                9F,
                FontStyle.Regular,
                UIColors.TextSub,
                ContentAlignment.MiddleCenter);
            card.Controls.AddRange(new Control[] { title, hint, header, list, empty });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                title.SetBounds(pad, UIUtils.S(12), UIUtils.S(300), UIUtils.S(28));
                hint.SetBounds(title.Right + UIUtils.S(8), UIUtils.S(13), Math.Max(1, card.Width - title.Right - pad - UIUtils.S(8)), UIUtils.S(26));
                header.SetBounds(pad, UIUtils.S(48), Math.Max(1, card.Width - pad * 2), UIUtils.S(36));
                list.SetBounds(pad, UIUtils.S(84), Math.Max(1, card.Width - pad * 2), Math.Max(1, card.Height - UIUtils.S(100)));
                empty.SetBounds(pad, UIUtils.S(84), Math.Max(1, card.Width - pad * 2), Math.Max(1, card.Height - UIUtils.S(100)));
            };
            return (card, title, hint, empty, list);
        }

        private static (
            Panel Page,
            LiteButton Whitelist,
            LiteButton Blacklist,
            LiteButton PerAsset,
            LiteButton SameName,
            LandlordNumberInput Interval,
            LandlordNumberInput ExecutionInterval,
            Label WeeklyFreeStatus,
            LiteButton WeeklyFreeSettings,
            AntdSwitch Cooldown,
            LandlordTimeInput CooldownStart,
            LandlordTimeInput CooldownEnd,
            AntdSwitch Enabled,
            Label Status,
            Label LastChecked,
            Label LastExecuted,
            Label ListTitle,
            Label ListHint,
            YouPinLandlordInventoryHeader Header,
            TextBox Search,
            YouPinLandlordInventoryListPanel List,
            Label Empty,
            LiteButton Scan,
            LiteButton Execute,
            LiteButton Help) CreateInventoryPage()
        {
            var page = new Panel { Height = UIUtils.S(886), BackColor = Color.Transparent };
            var card = new YouPinCcRoundedPanel { Height = UIUtils.S(382) };
            var title = CreateLabel("库存自动出租", 14F, FontStyle.Bold, UIColors.TextMain);
            var subtitle = CreateLabel(
                "只处理库存出租；名单和出租资格独立管理，不包含 0CD。",
                8.8F, FontStyle.Regular, UIColors.TextSub);
            var basicSection = CreateLabel("基础设置", 8.8F, FontStyle.Bold, UIColors.TextMain);
            var limitSection = CreateLabel("执行限制", 8.8F, FontStyle.Bold, UIColors.TextMain);
            var bodyDivider = new Panel { BackColor = UIColors.Border };
            var footerDivider = new Panel { BackColor = UIColors.Border };
            var help = new LiteButton("页面说明", false) { Width = UIUtils.S(96) };
            var enabled = new AntdSwitch { Checked = false, Size = UIUtils.S(new Size(48, 26)) };
            var status = CreateLabel("未扫描", 8.8F, FontStyle.Bold, UIColors.TextSub);
            var listModeCaption = CreateLabel("名单模式", 9F, FontStyle.Regular, UIColors.TextSub);
            var whitelist = new LiteButton("白名单", false) { Width = UIUtils.S(110) };
            var blacklist = new LiteButton("黑名单", false) { Width = UIUtils.S(110) };
            var selectionScopeCaption = CreateLabel("选择方式", 9F, FontStyle.Regular, UIColors.TextSub);
            var perAsset = new LiteButton("逐件选择", false) { Width = UIUtils.S(110) };
            var sameName = new LiteButton("按同款选择", false) { Width = UIUtils.S(110) };
            var intervalCaption = CreateLabel("扫描间隔", 9F, FontStyle.Regular, UIColors.TextSub);
            var interval = CreateIntervalInput(execution: false);
            var intervalUnit = CreateLabel("分钟（最低 20 分钟）", 9F, FontStyle.Regular, UIColors.TextSub);
            var executionIntervalCaption = CreateLabel("执行间隔", 9F, FontStyle.Regular, UIColors.TextSub);
            var executionInterval = CreateIntervalInput(execution: true);
            var executionIntervalUnit = CreateLabel(
                "分钟（最低 20 分钟）",
                9F,
                FontStyle.Regular,
                UIColors.TextSub);
            var antiPressureCaption = CreateLabel("防自压保护", 9F, FontStyle.Regular, UIColors.TextSub);
            var antiPressure = CreateLabel("● 始终开启", 9F, FontStyle.Bold, UIColors.Positive);
            var weeklyCaption = CreateLabel("周周免租", 9F, FontStyle.Regular, UIColors.TextSub);
            var weeklyStatus = CreateLabel("已关闭", 9F, FontStyle.Bold, UIColors.TextSub);
            var weeklySettings = new LiteButton("设置", false) { Width = UIUtils.S(86) };
            var cooldownCaption = CreateLabel("冷却时段", 9F, FontStyle.Regular, UIColors.TextSub);
            var cooldown = new AntdSwitch { Checked = false, Size = UIUtils.S(new Size(42, 22)) };
            var cooldownStart = CreateTimeInput(TimeSpan.Zero);
            var cooldownSeparator = CreateLabel("至", 8.8F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleCenter);
            var cooldownEnd = CreateTimeInput(TimeSpan.FromHours(8));
            var lastChecked = CreateLabel("上次扫描：暂无", 8.6F, FontStyle.Regular, UIColors.TextSub);
            var lastExecuted = CreateLabel("上次执行：暂无", 8.6F, FontStyle.Regular, UIColors.TextSub);
            var repeatProtection = CreateLabel(
                "重复执行保护：3 分钟（仅限制批次，不限制批内单件）",
                8.6F,
                FontStyle.Regular,
                UIColors.TextSub);
            var preferenceButton = new LiteButton("一键定价偏好", false) { Width = UIUtils.S(142) };
            var queueButton = new LiteButton("上架队列", false) { Width = UIUtils.S(104) };
            var logButton = new LiteButton("运行日志", false) { Width = UIUtils.S(104) };
            var scan = new LiteButton("扫描库存", false) { Width = UIUtils.S(104) };
            var execute = new LiteButton("立即执行一次", true) { Width = UIUtils.S(126) };
            preferenceButton.Tag = "inventory-preference";
            queueButton.Tag = "inventory-queue";
            logButton.Tag = "inventory-log";
            card.Controls.AddRange(new Control[]
            {
                title, subtitle,
                basicSection, limitSection, bodyDivider, footerDivider,
                listModeCaption, whitelist, blacklist, intervalCaption, interval, intervalUnit,
                executionIntervalCaption, executionInterval, executionIntervalUnit,
                selectionScopeCaption, perAsset, sameName,
                antiPressureCaption, antiPressure,
                weeklyCaption, weeklyStatus, weeklySettings,
                cooldownCaption, cooldown, cooldownStart, cooldownSeparator, cooldownEnd,
                lastChecked, lastExecuted, repeatProtection,
                preferenceButton, queueButton, logButton, scan, execute
            });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(24);
                int right = card.Width - pad;
                int columnGap = UIUtils.S(34);
                int middle = card.Width / 2;
                int rightColumn = middle + columnGap;
                title.SetBounds(pad, UIUtils.S(18), Math.Max(1, right - pad), UIUtils.S(30));
                subtitle.SetBounds(pad, UIUtils.S(47), Math.Max(1, right - pad), UIUtils.S(24));

                basicSection.SetBounds(pad, UIUtils.S(78), UIUtils.S(120), UIUtils.S(24));
                limitSection.SetBounds(rightColumn, UIUtils.S(78), UIUtils.S(120), UIUtils.S(24));
                bodyDivider.SetBounds(middle, UIUtils.S(76), 1, UIUtils.S(224));

                listModeCaption.SetBounds(pad, UIUtils.S(108), UIUtils.S(88), UIUtils.S(30));
                whitelist.SetBounds(pad + UIUtils.S(92), UIUtils.S(106), whitelist.Width, UIUtils.S(34));
                blacklist.SetBounds(whitelist.Right + UIUtils.S(6), UIUtils.S(106), blacklist.Width, UIUtils.S(34));
                selectionScopeCaption.SetBounds(pad, UIUtils.S(150), UIUtils.S(88), UIUtils.S(30));
                perAsset.SetBounds(pad + UIUtils.S(92), UIUtils.S(148), perAsset.Width, UIUtils.S(34));
                sameName.SetBounds(perAsset.Right + UIUtils.S(6), UIUtils.S(148), sameName.Width, UIUtils.S(34));
                intervalCaption.SetBounds(pad, UIUtils.S(192), UIUtils.S(84), UIUtils.S(30));
                interval.SetBounds(pad + UIUtils.S(92), UIUtils.S(190), UIUtils.S(84), UIUtils.S(32));
                intervalUnit.SetBounds(interval.Right + UIUtils.S(8), UIUtils.S(192), UIUtils.S(180), UIUtils.S(30));
                executionIntervalCaption.SetBounds(pad, UIUtils.S(232), UIUtils.S(84), UIUtils.S(30));
                executionInterval.SetBounds(pad + UIUtils.S(92), UIUtils.S(230), UIUtils.S(84), UIUtils.S(32));
                executionIntervalUnit.SetBounds(executionInterval.Right + UIUtils.S(8), UIUtils.S(232), UIUtils.S(180), UIUtils.S(30));
                antiPressureCaption.SetBounds(pad, UIUtils.S(272), UIUtils.S(88), UIUtils.S(28));
                antiPressure.SetBounds(pad + UIUtils.S(92), UIUtils.S(272), UIUtils.S(92), UIUtils.S(28));

                weeklyCaption.SetBounds(rightColumn, UIUtils.S(108), UIUtils.S(88), UIUtils.S(30));
                weeklyStatus.SetBounds(rightColumn + UIUtils.S(92), UIUtils.S(108), UIUtils.S(74), UIUtils.S(30));
                weeklySettings.SetBounds(weeklyStatus.Right + UIUtils.S(8), UIUtils.S(106), weeklySettings.Width, UIUtils.S(34));

                cooldownCaption.SetBounds(rightColumn, UIUtils.S(196), UIUtils.S(88), UIUtils.S(30));
                cooldown.SetBounds(rightColumn + UIUtils.S(92), UIUtils.S(200), UIUtils.S(42), UIUtils.S(22));
                cooldownStart.SetBounds(cooldown.Right + UIUtils.S(12), UIUtils.S(194), UIUtils.S(76), UIUtils.S(32));
                cooldownSeparator.SetBounds(cooldownStart.Right + UIUtils.S(5), UIUtils.S(196), UIUtils.S(24), UIUtils.S(30));
                cooldownEnd.SetBounds(cooldownSeparator.Right + UIUtils.S(5), UIUtils.S(194), UIUtils.S(76), UIUtils.S(32));
                lastChecked.SetBounds(rightColumn, UIUtils.S(232), Math.Max(1, right - rightColumn), UIUtils.S(28));
                lastExecuted.SetBounds(rightColumn, UIUtils.S(260), Math.Max(1, right - rightColumn), UIUtils.S(28));
                repeatProtection.SetBounds(rightColumn, UIUtils.S(286), Math.Max(1, right - rightColumn), UIUtils.S(28));

                footerDivider.SetBounds(0, UIUtils.S(312), card.Width, 1);
                execute.SetBounds(right - execute.Width, UIUtils.S(328), execute.Width, UIUtils.S(36));
                scan.SetBounds(execute.Left - UIUtils.S(8) - scan.Width, UIUtils.S(328), scan.Width, UIUtils.S(36));
                logButton.SetBounds(scan.Left - UIUtils.S(8) - logButton.Width, UIUtils.S(328), logButton.Width, UIUtils.S(36));
                queueButton.SetBounds(logButton.Left - UIUtils.S(8) - queueButton.Width, UIUtils.S(328), queueButton.Width, UIUtils.S(36));
                preferenceButton.SetBounds(queueButton.Left - UIUtils.S(8) - preferenceButton.Width, UIUtils.S(328), preferenceButton.Width, UIUtils.S(36));
            };

            var listCard = new YouPinCcRoundedPanel { Height = UIUtils.S(458) };
            var listTitle = CreateLabel("库存名单 0", 11F, FontStyle.Bold, UIColors.TextMain);
            var listHint = CreateLabel(
                "白名单：勾选代表允许自动上架，未勾选会跳过；暂不可出租也可提前勾选。",
                8.6F, FontStyle.Regular, UIColors.TextSub);
            listTitle.AutoEllipsis = false;
            listHint.AutoEllipsis = false;
            var search = new TextBox
            {
                PlaceholderText = "按饰品名称筛选",
                BackColor = UIColors.InputBg,
                ForeColor = UIColors.TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            var searchCaption = CreateLabel(
                "筛选",
                8.6F,
                FontStyle.Regular,
                UIColors.TextSub,
                ContentAlignment.MiddleRight);
            var header = new YouPinLandlordInventoryHeader();
            var list = new YouPinLandlordInventoryListPanel();
            var empty = CreateLabel(
                "暂无库存资格数据\r\n点击“扫描库存”读取库存。",
                9F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleCenter);
            listCard.Controls.AddRange(new Control[]
            {
                listTitle, listHint, searchCaption, search, header, list, empty
            });
            listCard.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                search.SetBounds(listCard.Width - pad - UIUtils.S(220), UIUtils.S(12), UIUtils.S(220), UIUtils.S(30));
                searchCaption.SetBounds(
                    search.Left - UIUtils.S(56),
                    UIUtils.S(12),
                    UIUtils.S(48),
                    UIUtils.S(30));
                listTitle.SetBounds(
                    pad,
                    UIUtils.S(12),
                    Math.Max(1, searchCaption.Left - pad - UIUtils.S(12)),
                    UIUtils.S(28));
                listHint.SetBounds(pad, UIUtils.S(42), Math.Max(1, listCard.Width - pad * 2), UIUtils.S(26));
                header.SetBounds(pad, UIUtils.S(72), Math.Max(1, listCard.Width - pad * 2), UIUtils.S(36));
                list.SetBounds(pad, UIUtils.S(108), Math.Max(1, listCard.Width - pad * 2), Math.Max(1, listCard.Height - UIUtils.S(124)));
                empty.SetBounds(pad, UIUtils.S(108), Math.Max(1, listCard.Width - pad * 2), Math.Max(1, listCard.Height - UIUtils.S(124)));
            };
            page.Controls.AddRange(new Control[] { card, listCard });
            page.Layout += (_, __) =>
            {
                card.SetBounds(0, 0, page.Width, UIUtils.S(382));
                listCard.SetBounds(0, card.Bottom + UIUtils.S(14), page.Width, UIUtils.S(458));
            };
            return (page, whitelist, blacklist, perAsset, sameName, interval, executionInterval,
                weeklyStatus, weeklySettings,
                cooldown, cooldownStart, cooldownEnd, enabled, status, lastChecked, lastExecuted, listTitle,
                listHint, header, search, list, empty, scan, execute, help);
        }

        private void WireEvents()
        {
            _repriceTab.Click += (_, __) => ShowPage(reprice: true);
            _inventoryTab.Click += (_, __) => ShowPage(reprice: false);
            _unifiedSettingsButton.Click += (_, __) => SelectConfigurationMode(
                YouPinLandlordRepriceConfigurationMode.Unified);
            _separateSettingsButton.Click += (_, __) => SelectConfigurationMode(
                YouPinLandlordRepriceConfigurationMode.Separate);
            _zeroCdButton.Click += (_, __) => SelectRentalType(YouPinRentalShelfType.ZeroCd);
            _inventoryRentalButton.Click += (_, __) => SelectRentalType(YouPinRentalShelfType.InventoryRental);
            _helpButton.Click += (_, __) =>
            {
                if (_showingReprice)
                    ShowRepriceHelp(FindForm());
                else
                    ShowInventoryHelp(FindForm());
            };
            WireInputValidation();
            _targetRankInput.ValueChanged += (_, __) => ApplyPolicyFromControls();
            _intervalInput.ValueChanged += (_, __) => ApplyPolicyFromControls();
            _executionIntervalInput.ValueChanged += (_, __) => ApplyPolicyFromControls();
            _repricePerAssetButton.Click += (_, __) => SelectRepriceSelectionScope(
                YouPinLandlordSelectionScope.PerAsset);
            _repriceSameNameButton.Click += (_, __) => SelectRepriceSelectionScope(
                YouPinLandlordSelectionScope.SameItemName);
            _weeklyFreeButton.Click += (_, __) => OpenRepriceWeeklyFreeSettings();
            _cooldownSwitch.CheckedChanged += (_, __) => ApplyPolicyFromControls();
            _cooldownStartInput.ValueChanged += (_, __) => ApplyPolicyFromControls();
            _cooldownEndInput.ValueChanged += (_, __) => ApplyPolicyFromControls();
            _enabledSwitch.CheckedChanged += (_, __) =>
            {
                if (_updatingPolicyControls)
                    return;
                ApplyPolicyFromControls();
                if (_enabledSwitch.Checked)
                    RequestRepriceRun(YouPinLandlordRunMode.ScanOnly, "启用后立即扫描货架");
            };
            _scanButton.Click += (_, __) => RequestRepriceRun(
                YouPinLandlordRunMode.ScanOnly,
                "用户立即扫描货架");
            _executeButton.Click += (_, __) => RequestRepriceRun(
                YouPinLandlordRunMode.Execute,
                "用户立即执行一次改价");
            _inventoryWhitelistButton.Click += (_, __) => SelectInventoryListMode(
                YouPinLandlordInventoryListMode.Whitelist);
            _inventoryBlacklistButton.Click += (_, __) => SelectInventoryListMode(
                YouPinLandlordInventoryListMode.Blacklist);
            _inventoryPerAssetButton.Click += (_, __) => SelectInventorySelectionScope(
                YouPinLandlordSelectionScope.PerAsset);
            _inventorySameNameButton.Click += (_, __) => SelectInventorySelectionScope(
                YouPinLandlordSelectionScope.SameItemName);
            _inventoryIntervalInput.ValueChanged += (_, __) => ApplyInventoryPolicyFromControls();
            _inventoryExecutionIntervalInput.ValueChanged += (_, __) => ApplyInventoryPolicyFromControls();
            _inventoryWeeklyFreeButton.Click += (_, __) => OpenInventoryWeeklyFreeSettings();
            _inventoryCooldownSwitch.CheckedChanged += (_, __) => ApplyInventoryPolicyFromControls();
            _inventoryCooldownStartInput.ValueChanged += (_, __) => ApplyInventoryPolicyFromControls();
            _inventoryCooldownEndInput.ValueChanged += (_, __) => ApplyInventoryPolicyFromControls();
            _inventoryEnabledSwitch.CheckedChanged += (_, __) =>
            {
                if (_updatingPolicyControls)
                    return;
                ApplyInventoryPolicyFromControls();
                if (_inventoryEnabledSwitch.Checked)
                    RequestInventoryRun(YouPinLandlordRunMode.ScanOnly, "启用后立即扫描库存");
            };
            _inventoryScanButton.Click += (_, __) => RequestInventoryRun(
                YouPinLandlordRunMode.ScanOnly,
                "用户立即扫描库存");
            _inventoryExecuteButton.Click += (_, __) => RequestInventoryRun(
                YouPinLandlordRunMode.Execute,
                "用户立即执行一次库存自动出租");
            _inventoryHelpButton.Click += (_, __) => ShowInventoryHelp(FindForm());
            _inventorySearchInput.TextChanged += (_, __) => RenderInventoryRows();
            _inventoryList.SelectionChanged += HandleInventorySelectionChanged;
            _shelfList.SelectionChanged += HandleShelfSelectionChanged;

            foreach (Control control in _inventoryPage.Controls.Cast<Control>()
                .SelectMany(parent => parent.Controls.Cast<Control>()))
            {
                if (control is not LiteButton button)
                    continue;
                if (Equals(button.Tag, "inventory-preference"))
                {
                    button.Click += (_, __) => ShowPricingPreference();
                }
                else if (Equals(button.Tag, "inventory-log"))
                {
                    button.Click += (_, __) => YouPinLandlordHistoryDialog.Show(
                        FindForm(),
                        _presenter,
                        YouPinLandlordWorkflow.InventoryAutoRent);
                }
                else if (Equals(button.Tag, "inventory-queue"))
                {
                    button.Click += (_, __) => ShowDetail(
                        FindForm(),
                        "库存自动出租队列",
                        _presenter.BuildInventoryExecutionQueueText());
                }
            }

            foreach (Control control in _settingsCard.Controls)
            {
                if (control is not LiteButton button)
                    continue;
                if (Equals(button.Tag, "preference"))
                {
                    button.Click += (_, __) => ShowPricingPreference();
                }
                else if (Equals(button.Tag, "log"))
                {
                    button.Click += (_, __) => YouPinLandlordHistoryDialog.Show(
                        FindForm(),
                        _presenter,
                        YouPinLandlordWorkflow.RentalReprice);
                }
                else if (Equals(button.Tag, "queue"))
                {
                    button.Click += (_, __) => ShowDetail(
                        FindForm(),
                        "包租公执行队列",
                        _presenter.BuildExecutionQueueText(GetCurrentScope()));
                }
            }
        }

        private void ShowPage(bool reprice)
        {
            _showingReprice = reprice;
            _repriceTab.IsActive = reprice;
            _inventoryTab.IsActive = !reprice;
            _settingsCard.Visible = reprice;
            _shelfCard.Visible = reprice;
            _inventoryPage.Visible = !reprice;
            if (_settingsCard.Parent != null)
                _settingsCard.Parent.Visible = reprice;
            if (_shelfCard.Parent != null)
                _shelfCard.Parent.Visible = reprice;
            if (_inventoryPage.Parent != null)
                _inventoryPage.Parent.Visible = !reprice;
            _configurationCaption.Visible = reprice;
            _unifiedSettingsButton.Visible = reprice;
            _separateSettingsButton.Visible = reprice;
            _scopeLabel.Visible = reprice;
            _currentTypeCaption.Visible = reprice;
            _zeroCdButton.Visible = reprice;
            _inventoryRentalButton.Visible = reprice;
            _repriceMasterBar.Visible = reprice;
            _inventoryMasterBar.Visible = !reprice;
            UpdateContextBarHeight(reprice);
            RenderPolicy();
            if (reprice)
                RenderPageState(_presenter.GetPageState(GetCurrentScope()));
            else
            {
                RenderInventoryPolicy();
                RenderInventoryPageState(_presenter.GetInventoryPageState());
            }
            RequestViewportRelayout();
        }

        private void UpdateContextBarHeight(bool reprice)
        {
            int barHeight = UIUtils.S(YouPinCcLandlordLayoutModel.GetContextBarLogicalHeight(reprice));
            _contextBar.Height = barHeight;
            if (_contextWrapper != null)
            {
                _contextWrapper.Height = barHeight + UIUtils.S(YouPinCcLandlordLayoutModel.ContextBarBottomGap);
                _contextWrapper.PerformLayout();
            }
        }

        private void SelectConfigurationMode(YouPinLandlordRepriceConfigurationMode mode)
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            if (current.RepriceConfigurationMode == mode)
                return;

            _presenter.ApplyPolicy(current with
            {
                PolicyVersion = current.PolicyVersion + 1,
                RepriceConfigurationMode = mode
            });
            SaveSettingsStoreToDisk();
            RenderPolicy();
            RenderPageState(_presenter.GetPageState(GetCurrentScope()));
        }

        private void SelectRentalType(YouPinRentalShelfType type)
        {
            _selectedRentalType = type;
            RenderPolicy();
            RenderPageState(_presenter.GetPageState(GetCurrentScope()));
        }

        private void SelectInventoryListMode(YouPinLandlordInventoryListMode mode)
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            if (current.InventoryAutoRent.ListMode == mode)
                return;
            _presenter.ApplyPolicy(current with
            {
                PolicyVersion = current.PolicyVersion + 1,
                InventoryAutoRent = current.InventoryAutoRent with { ListMode = mode }
            });
            SaveSettingsStoreToDisk();
            RenderInventoryPolicy();
            RenderInventoryPageState(_presenter.GetInventoryPageState());
        }

        private void SelectInventorySelectionScope(YouPinLandlordSelectionScope scope)
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            if (current.InventoryAutoRent.SelectionScope == scope)
                return;
            _presenter.ApplyPolicy(current with
            {
                PolicyVersion = current.PolicyVersion + 1,
                InventoryAutoRent = current.InventoryAutoRent with { SelectionScope = scope }
            });
            SaveSettingsStoreToDisk();
            RenderInventoryPolicy();
            RenderInventoryPageState(_presenter.GetInventoryPageState());
        }

        private void SelectRepriceSelectionScope(YouPinLandlordSelectionScope scope)
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            YouPinLandlordRentalPolicy selected = GetSelectedRentalPolicy(current);
            if (selected.Selection.Scope == scope)
                return;

            ApplySelectedRentalPolicy(
                current,
                selected with { Selection = selected.Selection with { Scope = scope } });
            SaveSettingsStoreToDisk();
            RenderPolicy();
            RenderPageState(_presenter.GetPageState(GetCurrentScope()));
        }

        private void OpenRepriceWeeklyFreeSettings()
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            YouPinLandlordRentalPolicy selected = GetSelectedRentalPolicy(current);
            string scopeText = current.RepriceConfigurationMode
                == YouPinLandlordRepriceConfigurationMode.Unified
                    ? "适用范围：0CD + 库存出租（统一设置）"
                    : _selectedRentalType == YouPinRentalShelfType.ZeroCd
                        ? "适用范围：0CD 出租"
                        : "适用范围：库存出租";
            if (!YouPinLandlordWeeklyFreeDialog.TryShow(
                    FindForm(),
                    scopeText,
                    selected.WeeklyFree,
                    out YouPinLandlordWeeklyFreeRule rule))
            {
                return;
            }

            ApplySelectedRentalPolicy(current, selected with { WeeklyFree = rule });
            SaveSettingsStoreToDisk();
            RenderPolicy();
        }

        private void OpenInventoryWeeklyFreeSettings()
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            if (!YouPinLandlordWeeklyFreeDialog.TryShow(
                    FindForm(),
                    "适用范围：库存自动出租",
                    current.InventoryAutoRent.WeeklyFree,
                    out YouPinLandlordWeeklyFreeRule rule))
            {
                return;
            }

            _presenter.ApplyPolicy(current with
            {
                PolicyVersion = current.PolicyVersion + 1,
                InventoryAutoRent = current.InventoryAutoRent with { WeeklyFree = rule }
            });
            SaveSettingsStoreToDisk();
            RenderInventoryPolicy();
        }

        private void RenderInventoryPolicy()
        {
            YouPinLandlordInventoryPolicy policy = _presenter.GetPolicy().InventoryAutoRent;
            _updatingPolicyControls = true;
            try
            {
                _inventoryEnabledSwitch.Checked = policy.Enabled;
                _inventoryIntervalInput.Value = Math.Clamp(policy.ScanIntervalMinutes, 20, 1440);
                _inventoryExecutionIntervalInput.Value = Math.Clamp(
                    policy.ExecutionIntervalMinutes,
                    20,
                    1440);
                _inventoryWeeklyFreeStatus.Text = policy.WeeklyFree.Enabled ? "已启用" : "已关闭";
                _inventoryWeeklyFreeStatus.ForeColor = policy.WeeklyFree.Enabled
                    ? UIColors.Positive
                    : UIColors.TextSub;
                _inventoryCooldownSwitch.Checked = policy.Cooldown.Enabled;
                _inventoryCooldownStartInput.Value = DateTime.Today.AddMinutes(policy.Cooldown.StartMinuteOfDay);
                _inventoryCooldownEndInput.Value = DateTime.Today.AddMinutes(policy.Cooldown.EndMinuteOfDay);
                _inventoryWhitelistButton.IsActive = policy.ListMode == YouPinLandlordInventoryListMode.Whitelist;
                _inventoryBlacklistButton.IsActive = policy.ListMode == YouPinLandlordInventoryListMode.Blacklist;
                _inventoryPerAssetButton.IsActive = policy.SelectionScope
                    == YouPinLandlordSelectionScope.PerAsset;
                _inventorySameNameButton.IsActive = policy.SelectionScope
                    == YouPinLandlordSelectionScope.SameItemName;
            }
            finally
            {
                _updatingPolicyControls = false;
            }
        }

        private void ApplyInventoryPolicyFromControls()
        {
            if (_updatingPolicyControls)
                return;
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            YouPinLandlordInventoryPolicy changed = current.InventoryAutoRent with
            {
                Enabled = _inventoryEnabledSwitch.Checked,
                ScanIntervalMinutes = decimal.ToInt32(_inventoryIntervalInput.Value),
                ExecutionIntervalMinutes = decimal.ToInt32(_inventoryExecutionIntervalInput.Value),
                Cooldown = new YouPinLandlordCooldownWindow(
                    _inventoryCooldownSwitch.Checked,
                    ToMinuteOfDay(_inventoryCooldownStartInput.Value),
                    ToMinuteOfDay(_inventoryCooldownEndInput.Value))
            };
            _presenter.ApplyPolicy(current with
            {
                PolicyVersion = current.PolicyVersion + 1,
                InventoryAutoRent = changed
            });
            SaveSettingsStoreToDisk();
            RenderInventoryPolicy();
        }

        private void HandleInventorySelectionChanged(string selectionKey, bool selected)
        {
            YouPinLandlordPolicy current = _presenter.GetPolicy();
            YouPinLandlordInventoryPolicy inventoryPolicy = current.InventoryAutoRent;
            var selections = (inventoryPolicy.SelectionScope
                    == YouPinLandlordSelectionScope.SameItemName
                        ? inventoryPolicy.SelectedItemNames
                        : inventoryPolicy.SelectedAssetIds)
                .ToHashSet(StringComparer.Ordinal);
            if (selected)
                selections.Add(selectionKey);
            else
                selections.Remove(selectionKey);
            YouPinLandlordInventoryPolicy changed = inventoryPolicy.SelectionScope
                == YouPinLandlordSelectionScope.SameItemName
                    ? inventoryPolicy with
                    {
                        SelectedItemNames = selections.OrderBy(
                            value => value,
                            StringComparer.Ordinal).ToArray()
                    }
                    : inventoryPolicy with
                    {
                        SelectedAssetIds = selections.OrderBy(
                            value => value,
                            StringComparer.Ordinal).ToArray()
                    };
            _presenter.ApplyPolicy(current with
            {
                PolicyVersion = current.PolicyVersion + 1,
                InventoryAutoRent = changed
            });
            SaveSettingsStoreToDisk();
            RenderInventoryPageState(_presenter.GetInventoryPageState());
        }

        private void HandleShelfSelectionChanged(string selectionKey, bool selected)
        {
            if (string.IsNullOrWhiteSpace(selectionKey))
                return;

            YouPinLandlordPolicy current = _presenter.GetPolicy();
            YouPinLandlordRentalPolicy rentalPolicy = GetSelectedRentalPolicy(current);
            YouPinLandlordSelectionRule selection = rentalPolicy.Selection;
            var selectedKeys = (selection.Scope == YouPinLandlordSelectionScope.SameItemName
                    ? selection.SelectedItemNames
                    : selection.SelectedAssetIds)
                .ToHashSet(StringComparer.Ordinal);
            if (!selection.Initialized)
            {
                selectedKeys.UnionWith(_shelfRows
                    .Select(row => row.SelectionKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)));
            }

            if (selected)
                selectedKeys.Add(selectionKey);
            else
                selectedKeys.Remove(selectionKey);

            YouPinLandlordSelectionRule changedSelection = selection.Scope
                == YouPinLandlordSelectionScope.SameItemName
                    ? selection with
                    {
                        Initialized = true,
                        SelectedItemNames = selectedKeys
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .ToArray()
                    }
                    : selection with
                    {
                        Initialized = true,
                        SelectedAssetIds = selectedKeys
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .ToArray()
                    };
            ApplySelectedRentalPolicy(
                current,
                rentalPolicy with { Selection = changedSelection });
            SaveSettingsStoreToDisk();
            RenderPolicy();
            RenderPageState(_presenter.GetPageState(GetCurrentScope()));
        }

        private YouPinLandlordRentalPolicy GetSelectedRentalPolicy(YouPinLandlordPolicy policy)
        {
            return policy.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified
                ? policy.UnifiedRental
                : policy.For(_selectedRentalType);
        }

        private void ApplySelectedRentalPolicy(
            YouPinLandlordPolicy current,
            YouPinLandlordRentalPolicy changed)
        {
            YouPinLandlordPolicy next = current.RepriceConfigurationMode
                == YouPinLandlordRepriceConfigurationMode.Unified
                    ? current with
                    {
                        PolicyVersion = current.PolicyVersion + 1,
                        UnifiedRental = changed
                    }
                    : _selectedRentalType == YouPinRentalShelfType.ZeroCd
                        ? current with
                        {
                            PolicyVersion = current.PolicyVersion + 1,
                            ZeroCd = changed
                        }
                        : current with
                        {
                            PolicyVersion = current.PolicyVersion + 1,
                            InventoryRental = changed
                        };
            _presenter.ApplyPolicy(next);
        }

        private void RenderInventoryPageState(YouPinLandlordInventoryPageStateViewModel state)
        {
            _inventoryRows = state.Rows;
            YouPinLandlordInventoryPolicy policy = _presenter.GetPolicy().InventoryAutoRent;
            bool failure = YouPinCcLandlordStatusFormatter.IsInventoryFailure(state.StatusText);
            _inventoryStatusLabel.Text = YouPinCcLandlordStatusFormatter.FormatInventoryMaster(state, policy);
            _inventoryStatusLabel.ForeColor = failure
                ? UIColors.TextWarn
                : state.IsRunning
                    ? UIColors.Primary
                    : policy.Enabled ? UIColors.Positive : UIColors.TextSub;
            _inventoryLastCheckedLabel.Text = "上次扫描：" + state.LastCheckedText;
            _inventoryLastExecutedLabel.Text = "上次执行：" + state.LastExecutedText;
            bool whitelist = policy.ListMode == YouPinLandlordInventoryListMode.Whitelist;
            bool sameName = policy.SelectionScope == YouPinLandlordSelectionScope.SameItemName;
            _inventoryListTitle.Text = sameName
                ? $"库存饰品 {state.TotalCount}  ·  可出租 {state.EligibleCount}  ·  同款 {state.Rows.Count}  ·  已勾选同款 {state.SelectedCount}"
                : $"库存饰品 {state.TotalCount}  ·  可出租 {state.EligibleCount}  ·  已勾选单件 {state.SelectedCount}";
            string modeMeaning = whitelist
                ? "白名单：勾选代表允许自动上架，未勾选会跳过。"
                : "黑名单：勾选代表禁止自动上架，未勾选仍可执行。";
            string scopeMeaning = sameName
                ? "按同款选择会作用于当前及以后所有完整名称相同的饰品；出租资格仍逐件判断。"
                : "逐件选择只作用于当前这件饰品，以后新增的同款不会自动继承。";
            _inventoryListHint.Text = modeMeaning + scopeMeaning + " 暂不可出租也可提前勾选。";
            _inventoryHeader.SetSelectionCaption(whitelist ? "允许自动上架" : "禁止自动上架");
            _inventoryScanButton.Enabled = !state.IsRunning;
            _inventoryExecuteButton.Enabled = !state.IsRunning && policy.Enabled;
            RenderInventoryRows();
        }

        private void RenderInventoryRows()
        {
            string keyword = _inventorySearchInput.Text.Trim();
            YouPinLandlordInventoryRowViewModel[] rows = _inventoryRows
                .Where(item => keyword.Length == 0
                    || item.ItemName.Contains(keyword, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();
            _inventoryList.SetRows(rows);
            _inventoryList.Visible = rows.Length > 0;
            _inventoryEmptyLabel.Visible = rows.Length == 0;
        }

        private void RenderPolicy()
        {
            YouPinLandlordPolicy policy = _presenter.GetPolicy();
            bool unified = policy.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified;
            YouPinLandlordRentalPolicy selected = unified
                ? policy.UnifiedRental
                : policy.For(_selectedRentalType);
            _updatingPolicyControls = true;
            try
            {
                _targetRankInput.Value = Math.Clamp(selected.TargetRank, 1, 20);
                _intervalInput.Value = Math.Clamp(selected.ScanIntervalMinutes, 20, 1440);
                _executionIntervalInput.Value = Math.Clamp(
                    selected.ExecutionIntervalMinutes,
                    20,
                    1440);
                _weeklyFreeStatus.Text = selected.WeeklyFree.Enabled ? "已启用" : "已关闭";
                _weeklyFreeStatus.ForeColor = selected.WeeklyFree.Enabled
                    ? UIColors.Positive
                    : UIColors.TextSub;
                _cooldownSwitch.Checked = selected.Cooldown.Enabled;
                _cooldownStartInput.Value = DateTime.Today.AddMinutes(selected.Cooldown.StartMinuteOfDay);
                _cooldownEndInput.Value = DateTime.Today.AddMinutes(selected.Cooldown.EndMinuteOfDay);
                _enabledSwitch.Checked = selected.Enabled;
                _zeroCdButton.IsActive = _selectedRentalType == YouPinRentalShelfType.ZeroCd;
                _inventoryRentalButton.IsActive = _selectedRentalType == YouPinRentalShelfType.InventoryRental;
                _repricePerAssetButton.IsActive = selected.Selection.Scope
                    == YouPinLandlordSelectionScope.PerAsset;
                _repriceSameNameButton.IsActive = selected.Selection.Scope
                    == YouPinLandlordSelectionScope.SameItemName;
                _unifiedSettingsButton.IsActive = unified;
                _separateSettingsButton.IsActive = !unified;
                _scopeLabel.Visible = unified && _showingReprice;
                _currentTypeCaption.Visible = !unified && _showingReprice;
                _zeroCdButton.Visible = !unified && _showingReprice;
                _inventoryRentalButton.Visible = !unified && _showingReprice;
            }
            finally
            {
                _updatingPolicyControls = false;
            }
        }

        private void ApplyPolicyFromControls()
        {
            if (_updatingPolicyControls)
                return;

            YouPinLandlordPolicy current = _presenter.GetPolicy();
            YouPinLandlordRentalPolicy selected = GetSelectedRentalPolicy(current);
            YouPinLandlordRentalPolicy changed = selected with
            {
                TargetRank = decimal.ToInt32(_targetRankInput.Value),
                ScanIntervalMinutes = decimal.ToInt32(_intervalInput.Value),
                ExecutionIntervalMinutes = decimal.ToInt32(_executionIntervalInput.Value),
                Enabled = _enabledSwitch.Checked,
                Cooldown = new YouPinLandlordCooldownWindow(
                    _cooldownSwitch.Checked,
                    ToMinuteOfDay(_cooldownStartInput.Value),
                    ToMinuteOfDay(_cooldownEndInput.Value))
            };
            ApplySelectedRentalPolicy(current, changed);
            SaveSettingsStoreToDisk();
            RenderPolicy();
        }

        private void HandleSnapshotChanged()
        {
            if (IsDisposed || !_snapshotSubscribed)
                return;

            YouPinLandlordPageStateViewModel state = _presenter.GetPageState(GetCurrentScope());
            if (!state.IsRunning)
            {
                if (_showingReprice)
                {
                    _refreshController.Request(UiRefreshReason.Now(
                        "运行快照变化",
                        "包租公服务"));
                }
                else
                {
                    _inventoryRefreshController.Request(UiRefreshReason.Now(
                        "运行快照变化",
                        "库存自动出租"));
                }
            }
        }

        private void RequestRepriceRun(YouPinLandlordRunMode mode, string trigger)
        {
            _scanButton.Enabled = false;
            _executeButton.Enabled = false;
            _statusLabel.Text = mode == YouPinLandlordRunMode.ScanOnly
                ? "● 扫描中"
                : "● 执行中";
            _statusLabel.ForeColor = UIColors.Primary;
            _refreshController.Request(UiRefreshReason.Now(
                trigger,
                BuildRepriceRunSource(mode, GetCurrentScope())));
        }

        private void RequestInventoryRun(YouPinLandlordRunMode mode, string trigger)
        {
            _inventoryScanButton.Enabled = false;
            _inventoryExecuteButton.Enabled = false;
            _inventoryStatusLabel.Text = mode == YouPinLandlordRunMode.ScanOnly
                ? "● 扫描中"
                : "● 执行中";
            _inventoryStatusLabel.ForeColor = UIColors.Primary;
            _inventoryRefreshController.Request(UiRefreshReason.Now(trigger, mode.ToString()));
        }

        private static string BuildRepriceRunSource(
            YouPinLandlordRunMode mode,
            YouPinRentalScanScope scope)
        {
            return $"{mode}|{scope}";
        }

        private static void ParseRepriceRunSource(
            string source,
            out YouPinLandlordRunMode mode,
            out YouPinRentalScanScope scope)
        {
            string[] parts = source.Split('|', 2, StringSplitOptions.TrimEntries);
            mode = parts.Length > 0
                && Enum.TryParse(parts[0], ignoreCase: false, out YouPinLandlordRunMode parsedMode)
                    ? parsedMode
                    : YouPinLandlordRunMode.ScanOnly;
            scope = parts.Length > 1
                && Enum.TryParse(parts[1], ignoreCase: false, out YouPinRentalScanScope parsedScope)
                    ? parsedScope
                    : YouPinRentalScanScope.ZeroCd;
        }

        private void SubscribeSnapshot()
        {
            if (_snapshotSubscribed)
                return;
            _presenter.SnapshotChanged += HandleSnapshotChanged;
            _snapshotSubscribed = true;
        }

        private void UnsubscribeSnapshot()
        {
            if (!_snapshotSubscribed)
                return;
            _presenter.SnapshotChanged -= HandleSnapshotChanged;
            _snapshotSubscribed = false;
        }

        private void RenderPageState(YouPinLandlordPageStateViewModel state)
        {
            YouPinLandlordPolicy policy = _presenter.GetPolicy();
            bool unified = policy.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified;
            YouPinLandlordRentalPolicy selected = unified
                ? policy.UnifiedRental
                : policy.For(_selectedRentalType);
            string scopeText = unified
                ? "全部类型"
                : _selectedRentalType == YouPinRentalShelfType.ZeroCd ? "0CD" : "库存出租";
            _statusLabel.Text = state.IsRunning
                ? $"● {scopeText}处理中"
                : $"● {scopeText}{(selected.Enabled ? "已开启" : "已关闭")} · {state.StatusText}";
            bool warning = state.StatusText.Contains("失败", StringComparison.Ordinal)
                || state.StatusText.Contains("未开启", StringComparison.Ordinal)
                || state.StatusText.Contains("冷却", StringComparison.Ordinal);
            _statusLabel.ForeColor = warning
                ? UIColors.TextWarn
                : (state.IsRunning ? UIColors.Primary : UIColors.Positive);
            _lastCheckedLabel.Text = "上次检查：" + state.LastCheckedText;
            _lastExecutedLabel.Text = "上次执行：" + state.LastExecutedText;
            _preferenceLabel.Text = state.PreferenceText;
            _shelfTitle.Text = $"当前货架 {state.TotalShelfCount}  ·  0CD {state.ZeroCdCount}  ·  库存出租 {state.InventoryRentalCount}";
            _shelfRows = state.ShelfRows;
            bool sameName = selected.Selection.Scope == YouPinLandlordSelectionScope.SameItemName;
            int selectedCount = state.ShelfRows.Count(row => row.IsSelected);
            _shelfHint.Text = sameName
                ? $"同款名单：完整饰品名称相同的货架合并选择 · 已勾选同款 {selectedCount}"
                : $"逐件名单：每件货架记录独立选择 · 已勾选单件 {selectedCount}";
            _shelfList.SetRows(state.ShelfRows);
            _emptyShelfLabel.Visible = state.ShelfRows.Count == 0;
            _shelfList.Visible = state.ShelfRows.Count > 0;
            _scanButton.Enabled = !state.IsRunning;
            _executeButton.Enabled = !state.IsRunning && selected.Enabled;
        }

        private YouPinRentalScanScope GetCurrentScope()
        {
            YouPinLandlordPolicy policy = _presenter.GetPolicy();
            if (policy.RepriceConfigurationMode == YouPinLandlordRepriceConfigurationMode.Unified)
                return YouPinRentalScanScope.All;
            return _selectedRentalType == YouPinRentalShelfType.ZeroCd
                ? YouPinRentalScanScope.ZeroCd
                : YouPinRentalScanScope.InventoryRental;
        }

        private void WireInputValidation()
        {
            foreach (LandlordNumberInput input in new[]
            {
                _targetRankInput,
                _intervalInput,
                _executionIntervalInput,
                _inventoryIntervalInput,
                _inventoryExecutionIntervalInput
            })
            {
                input.ValidationFailed += ShowInputValidationError;
            }

            foreach (LandlordTimeInput input in new[]
            {
                _cooldownStartInput,
                _cooldownEndInput,
                _inventoryCooldownStartInput,
                _inventoryCooldownEndInput
            })
            {
                input.ValidationFailed += ShowInputValidationError;
            }
        }

        private void ShowInputValidationError(string message)
        {
            Label status = _showingReprice ? _statusLabel : _inventoryStatusLabel;
            status.Text = "● 输入错误 · " + message;
            status.ForeColor = UIColors.TextWarn;
            System.Media.SystemSounds.Exclamation.Play();
        }

        private static LandlordNumberInput CreateNumberInput(int minimum, int maximum, int value)
        {
            string validationMessage = minimum == 20
                ? "包租公扫描间隔不能低于 20 分钟，以保护出租环境。"
                : $"请输入 {minimum} 至 {maximum} 之间的整数。";
            return new LandlordNumberInput(
                minimum,
                maximum,
                value,
                decimalPlaces: 0,
                validationMessage: validationMessage);
        }

        private static LandlordNumberInput CreateIntervalInput(bool execution)
        {
            string validationMessage = execution
                ? "包租公执行间隔不能低于 20 分钟，以保护出租环境。"
                : "包租公扫描间隔不能低于 20 分钟，以保护出租环境。";
            return new LandlordNumberInput(
                20,
                1440,
                30,
                decimalPlaces: 0,
                validationMessage: validationMessage);
        }

        private static LandlordTimeInput CreateTimeInput(TimeSpan initialValue) => new(initialValue);

        private static int ToMinuteOfDay(DateTime value) => (value.Hour * 60) + value.Minute;

        private static Label CreateLabel(
            string text,
            float size,
            FontStyle style,
            Color color,
            ContentAlignment alignment = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, style),
                TextAlign = alignment
            };
        }

        private static void ShowRepriceHelp(IWin32Window? owner)
        {
            ShowDetail(
                owner,
                "租赁自动改价 · 页面说明",
                YouPinCcLandlordHelpContent.Reprice);
        }

        private static void ShowInventoryHelp(IWin32Window? owner)
        {
            ShowDetail(
                owner,
                "库存自动出租 · 页面说明",
                YouPinCcLandlordHelpContent.InventoryAutoRent);
        }

        private void ShowPricingPreference()
        {
            LiteDetailDialog.ShowWithAction(
                FindForm(),
                "上架一键定价偏好",
                _presenter.BuildPricingPreferenceText(),
                "一键刷新",
                _presenter.RefreshPricingPreferenceAsync);
        }

        private static void ShowDetail(IWin32Window? owner, string title, string body)
        {
            LiteDetailDialog.Show(owner, title, Array.Empty<LiteDetailDialog.DetailRow>(), body);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                UnsubscribeSnapshot();
            base.Dispose(disposing);
        }
    }

    internal static class YouPinCcLandlordHelpContent
    {
        internal const string Reprice =
            "处理范围：只读取已经在悠悠货架上的 0CD 与库存出租商品，可统一配置，也可分别设置、扫描和判断。\r\n\r\n"
            + "名单方式：逐件名单按单件资产保存；同款名单按完整饰品名称保存，当前及以后所有同名货架共享选择。两套名单独立保存，未勾选项只显示在货架中，不读取市场、不自动改价。\r\n\r\n"
            + "出租位与防自压：按完整饰品名称查询同款市场；自有同名进入目标出租位后，其他自有同名不再继续压低自己。\r\n\r\n"
            + "一键定价偏好：来自悠悠云端，本软件只读取展示。\r\n\r\n"
            + "扫描与执行：扫描间隔和执行间隔彼此独立，均最低 20 分钟。扫描只读取并生成计划，不写入；自动执行到期时会先读取最新货架再执行。\r\n\r\n"
            + "立即执行一次：仅在当前类型开关开启时可用，不受两个间隔限制；会重置下一次扫描和自动执行时间。0CD、库存出租改价分别使用独立的 3 分钟重复执行保护，批内多件连续处理不等待。\r\n\r\n"
            + "执行授权：开启“自动改价”即持续授权，不再逐件审批。需要改价时使用悠悠一键定价，写前复核，写后回查；单件失败不会阻断同批其他饰品。\r\n\r\n"
            + "周周免租：启用后，命中价值区间的短租金必须严格小于 0.72。\r\n\r\n"
            + "冷却时段：只更新货架观察，不执行定价和改价写入。";

        internal const string InventoryAutoRent =
            "处理范围：只处理库存出租，不处理 0CD，也不提供出租类型切换。\r\n\r\n"
            + "名单模式：白名单只自动上架勾选商品；黑名单自动跳过勾选商品。只有本页使用黑白名单。\r\n\r\n"
            + "选择方式：逐件选择按单件资产保存，新增同款不会继承；按同款选择按完整饰品名称保存，当前及以后所有同名饰品共享勾选。两套名单独立保存，切换时不会互相转换。\r\n\r\n"
            + "出租资格：综合 Steam 可交易状态、悠悠禁租、库存状态、交易保护、店铺状态和悠悠上架资格判断。暂不可出租的饰品仍可提前加入名单。\r\n\r\n"
            + "防自压：上架前按完整饰品名称检查自有同名货架，禁止新商品压低自己的租金。\r\n\r\n"
            + "扫描与执行：扫描间隔和执行间隔彼此独立，均最低 20 分钟。扫描只更新资格和上架计划，不写入；自动执行到期时会重新读取最新库存再执行。\r\n\r\n"
            + "立即执行一次：仅在自动出租开关开启时可用，不受两个间隔限制；会重置下一次扫描和自动执行时间，并与自动执行共享 3 分钟重复执行保护。批内多件连续处理不等待。\r\n\r\n"
            + "执行授权：开启后即持续授权。命中名单且资格通过时，读取悠悠一键定价、写前复核、自动上架并强制回查，不再逐件询问；单件失败不会阻断其他饰品。\r\n\r\n"
            + "周周免租：启用后，命中价值区间的短租金必须严格小于 0.72。\r\n\r\n"
            + "冷却时段：只更新库存出租状态，不执行自动上架。";
    }

    internal static class YouPinCcLandlordStatusFormatter
    {
        internal static string FormatInventoryMaster(
            YouPinLandlordInventoryPageStateViewModel state,
            YouPinLandlordInventoryPolicy policy)
        {
            if (state.IsRunning)
                return "● 处理中";
            if (YouPinLandlordUserNotice.IsTradingNoticeFailure(state.StatusText))
                return YouPinLandlordUserNotice.TradingNoticeCompactStatus;
            if (IsInventoryFailure(state.StatusText))
                return "● 运行异常 · 请查看运行日志";
            if (state.StatusText.Contains("重复执行保护", StringComparison.Ordinal)
                || state.StatusText.Contains("未到执行时间", StringComparison.Ordinal)
                || state.StatusText.Contains("未开启", StringComparison.Ordinal))
            {
                return "● " + state.StatusText;
            }

            string enabled = policy.Enabled ? "已开启" : "已关闭";
            string listMode = policy.ListMode == YouPinLandlordInventoryListMode.Whitelist
                ? "白名单"
                : "黑名单";
            string selectionScope = policy.SelectionScope
                == YouPinLandlordSelectionScope.SameItemName
                    ? "按同款选择"
                    : "逐件选择";
            string scanSummary = state.LastCheckedText == "暂无"
                ? "尚未扫描"
                : $"可出租 {state.EligibleCount}/{state.TotalCount}";
            return $"● {enabled} · {listMode} · {selectionScope} · {scanSummary}";
        }

        internal static bool IsInventoryFailure(string statusText)
        {
            if (statusText.StartsWith("库存扫描失败", StringComparison.Ordinal)
                || statusText.Contains("审计日志写入异常", StringComparison.Ordinal))
            {
                return true;
            }

            int failureMarker = statusText.LastIndexOf("失败 ", StringComparison.Ordinal);
            if (failureMarker < 0)
                return false;

            ReadOnlySpan<char> countText = statusText.AsSpan(failureMarker + "失败 ".Length);
            int digitCount = 0;
            while (digitCount < countText.Length && char.IsAsciiDigit(countText[digitCount]))
                digitCount++;

            return digitCount > 0
                && int.TryParse(
                    countText[..digitCount],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int failedCount)
                && failedCount > 0;
        }
    }

    internal static class YouPinCcLandlordLayoutModel
    {
        internal const int CompactPageTopPadding = 6;
        internal const int ContextBarBottomGap = 10;

        internal static int GetContextBarLogicalHeight(bool reprice)
        {
            return reprice ? 88 : 44;
        }
    }

    internal sealed class LandlordNumberInput : LiteNumberInput
    {
        private readonly int _decimalPlaces;
        private readonly string _validationMessage;
        private decimal _value;

        public LandlordNumberInput(
            decimal minimum,
            decimal maximum,
            decimal value,
            int decimalPlaces,
            string validationMessage)
            : base(string.Empty, width: 84, maxLength: 12)
        {
            Minimum = minimum;
            Maximum = maximum;
            _decimalPlaces = decimalPlaces;
            _validationMessage = validationMessage;
            Value = Math.Clamp(value, minimum, maximum);
            SetBg(UIColors.InputBg);

            Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                CommitText();
            };
            Inner.Leave += (_, __) => CommitText();
        }

        public event EventHandler? ValueChanged;

        public event Action<string>? ValidationFailed;

        public decimal Minimum { get; }

        public decimal Maximum { get; }

        public decimal Value
        {
            get => _value;
            set
            {
                _value = Math.Clamp(value, Minimum, Maximum);
                Inner.Text = FormatValue(_value);
            }
        }

        private void CommitText()
        {
            if (!TryParseDecimal(Inner.Text, out decimal candidate)
                || candidate < Minimum
                || candidate > Maximum
                || decimal.Round(candidate, _decimalPlaces) != candidate)
            {
                Inner.Text = FormatValue(_value);
                ValidationFailed?.Invoke(_validationMessage);
                return;
            }

            candidate = decimal.Round(candidate, _decimalPlaces);
            Inner.Text = FormatValue(candidate);
            if (candidate == _value)
                return;

            _value = candidate;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private string FormatValue(decimal value)
        {
            return _decimalPlaces == 0
                ? decimal.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
                : value.ToString($"F{_decimalPlaces}", CultureInfo.InvariantCulture);
        }

        private static bool TryParseDecimal(string text, out decimal value)
        {
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }
    }

    internal sealed class LandlordTimeInput : LiteUnderlineInput
    {
        private DateTime _value;

        public LandlordTimeInput(TimeSpan initialValue)
            : base(string.Empty, width: 76, align: HorizontalAlignment.Center)
        {
            Inner.MaxLength = 5;
            Value = DateTime.Today.Add(initialValue);
            SetBg(UIColors.InputBg);

            Inner.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar) || char.IsDigit(e.KeyChar) || e.KeyChar == ':')
                    return;
                e.Handled = true;
            };
            Inner.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                CommitText();
            };
            Inner.Leave += (_, __) => CommitText();
        }

        public event EventHandler? ValueChanged;

        public event Action<string>? ValidationFailed;

        public DateTime Value
        {
            get => _value;
            set
            {
                _value = DateTime.Today.Add(value.TimeOfDay);
                Inner.Text = _value.ToString("HH:mm", CultureInfo.InvariantCulture);
            }
        }

        private void CommitText()
        {
            string[] formats = { "H:mm", "HH:mm" };
            if (!DateTime.TryParseExact(
                    Inner.Text.Trim(),
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsed))
            {
                Inner.Text = _value.ToString("HH:mm", CultureInfo.InvariantCulture);
                ValidationFailed?.Invoke("请输入 00:00 至 23:59 的时间。");
                return;
            }

            parsed = DateTime.Today.Add(parsed.TimeOfDay);
            Inner.Text = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (parsed.TimeOfDay == _value.TimeOfDay)
                return;

            _value = parsed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal sealed class YouPinLandlordInventoryListPanel : VirtualListPanel<YouPinLandlordInventoryRowViewModel>
    {
        public YouPinLandlordInventoryListPanel()
        {
            RowHeight = UIUtils.S(58);
            OverscanRowCount = 2;
            MaxNewRowsPerPass = 3;
        }

        public event Action<string, bool>? SelectionChanged;

        public void SetRows(IReadOnlyList<YouPinLandlordInventoryRowViewModel> rows)
        {
            SetItemsIncremental(rows, static (left, right) => left == right);
        }

        protected override Control CreateRowControl()
        {
            var row = new YouPinLandlordInventoryRowControl();
            row.SelectionChanged += (assetId, selected) => SelectionChanged?.Invoke(assetId, selected);
            return row;
        }

        protected override void OnRenderRow(
            Control rowControl,
            YouPinLandlordInventoryRowViewModel item,
            int index)
        {
            if (rowControl is YouPinLandlordInventoryRowControl row)
                row.Render(item, index);
        }
    }

    internal sealed class YouPinLandlordInventoryHeader : Panel
    {
        private readonly Label[] _labels =
        {
            CreateHeaderLabel("名单选择"),
            CreateHeaderLabel("饰品名称"),
            CreateHeaderLabel("出租资格")
        };

        public YouPinLandlordInventoryHeader()
        {
            BackColor = UIColors.ControlBg;
            Controls.AddRange(_labels);
        }

        public void SetSelectionCaption(string text)
        {
            _labels[0].Text = text;
            _labels[0].AccessibleDescription = text;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            YouPinLandlordInventoryColumns.Layout(_labels, ClientRectangle);
        }

        private static Label CreateHeaderLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = UIColors.TextSub,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(UIUtils.S(8), 0, UIUtils.S(4), 0)
            };
        }
    }

    internal sealed class YouPinLandlordInventoryRowControl : Panel
    {
        private readonly CheckBox _selection = new()
        {
            Text = string.Empty,
            AutoSize = false,
            ForeColor = UIColors.TextMain,
            BackColor = Color.Transparent
        };
        private readonly Label _name = CreateCellLabel();
        private readonly Label _eligibility = CreateCellLabel();
        private readonly ToolTip _toolTip = new();
        private string _selectionKey = string.Empty;
        private bool _rendering;

        public YouPinLandlordInventoryRowControl()
        {
            Controls.AddRange(new Control[] { _selection, _name, _eligibility });
            _selection.CheckedChanged += (_, __) =>
            {
                if (!_rendering && _selectionKey.Length > 0)
                    SelectionChanged?.Invoke(_selectionKey, _selection.Checked);
            };
        }

        public event Action<string, bool>? SelectionChanged;

        public void Render(YouPinLandlordInventoryRowViewModel row, int index)
        {
            _rendering = true;
            try
            {
                _selectionKey = row.SelectionKey;
                _selection.Checked = row.IsSelected;
                _name.Text = row.ItemName;
                _name.AccessibleDescription = row.ItemName;
                _toolTip.SetToolTip(_name, row.ItemName);
                _eligibility.Text = row.EligibilityText + " · " + row.EligibilityReason;
                _eligibility.ForeColor = row.IsEligible ? UIColors.Positive : UIColors.TextWarn;
                BackColor = index % 2 == 0 ? Color.Transparent : UIColors.ControlBg;
            }
            finally
            {
                _rendering = false;
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            YouPinLandlordInventoryColumns.Layout(
                new Control[] { _selection, _name, _eligibility },
                ClientRectangle);
        }

        private static Label CreateCellLabel()
        {
            return new Label
            {
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", 8.8F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(UIUtils.S(8), 0, UIUtils.S(4), 0)
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _toolTip.Dispose();
            base.Dispose(disposing);
        }
    }

    internal static class YouPinLandlordInventoryColumns
    {
        private static readonly int[] WidthPercent = { 14, 54, 32 };

        public static void Layout(IReadOnlyList<Control> controls, Rectangle bounds)
        {
            int x = bounds.Left;
            int remaining = bounds.Width;
            for (int index = 0; index < controls.Count; index++)
            {
                int width = index == controls.Count - 1
                    ? remaining
                    : Math.Max(1, bounds.Width * WidthPercent[index] / 100);
                controls[index].SetBounds(x, bounds.Top, width, bounds.Height);
                x += width;
                remaining = Math.Max(1, bounds.Right - x);
            }
        }
    }

    internal sealed class YouPinLandlordShelfListPanel : VirtualListPanel<YouPinLandlordShelfRowViewModel>
    {
        public YouPinLandlordShelfListPanel()
        {
            RowHeight = UIUtils.S(58);
            OverscanRowCount = 2;
            MaxNewRowsPerPass = 2;
        }

        public event Action<string, bool>? SelectionChanged;

        public void SetRows(IReadOnlyList<YouPinLandlordShelfRowViewModel> rows)
        {
            SetItemsIncremental(rows, static (left, right) => left == right);
        }

        protected override Control CreateRowControl()
        {
            var row = new YouPinLandlordShelfRowControl();
            row.SelectionChanged += (selectionKey, selected) =>
                SelectionChanged?.Invoke(selectionKey, selected);
            return row;
        }

        protected override void OnRenderRow(
            Control rowControl,
            YouPinLandlordShelfRowViewModel item,
            int index)
        {
            if (rowControl is YouPinLandlordShelfRowControl row)
                row.Render(item, index);
        }
    }

    internal sealed class YouPinLandlordShelfHeader : Panel
    {
        private static readonly string[] Captions =
        {
            "允许自动改价", "饰品名称", "出租类型", "当前短租价", "当前出租位", "目标出租位", "判断", "最近检查"
        };

        private readonly Label[] _labels;

        public YouPinLandlordShelfHeader()
        {
            BackColor = UIColors.ControlBg;
            _labels = Captions.Select(caption => CreateHeaderLabel(caption)).ToArray();
            Controls.AddRange(_labels);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            YouPinLandlordShelfColumns.Layout(_labels, ClientRectangle);
        }

        private static Label CreateHeaderLabel(string text)
        {
            return new Label
            {
                Text = text,
                BackColor = Color.Transparent,
                ForeColor = UIColors.TextSub,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(UIUtils.S(8), 0, UIUtils.S(4), 0)
            };
        }
    }

    internal sealed class YouPinLandlordShelfRowControl : Panel
    {
        private readonly CheckBox _selection = new()
        {
            Text = string.Empty,
            AutoSize = false,
            ForeColor = UIColors.TextMain,
            BackColor = Color.Transparent
        };
        private readonly Label[] _labels;
        private string _selectionKey = string.Empty;
        private bool _rendering;

        public YouPinLandlordShelfRowControl()
        {
            BackColor = Color.Transparent;
            _labels = Enumerable.Range(0, 7).Select(_ => CreateCellLabel()).ToArray();
            Controls.Add(_selection);
            Controls.AddRange(_labels);
            _selection.CheckedChanged += (_, __) =>
            {
                if (!_rendering && _selectionKey.Length > 0)
                    SelectionChanged?.Invoke(_selectionKey, _selection.Checked);
            };
        }

        public event Action<string, bool>? SelectionChanged;

        public void Render(YouPinLandlordShelfRowViewModel row, int index)
        {
            _rendering = true;
            try
            {
                _selectionKey = row.SelectionKey;
                _selection.Checked = row.IsSelected;
                _labels[0].Text = row.ItemCount > 1
                    ? $"{row.ItemName}（{row.ItemCount} 件）"
                    : row.ItemName;
                _labels[1].Text = row.RentalTypeText;
                _labels[2].Text = row.CurrentRentText;
                _labels[3].Text = row.CurrentRankText;
                _labels[4].Text = row.TargetRankText;
                _labels[5].Text = row.DecisionText;
                _labels[5].ForeColor = row.DecisionText == "无需处理"
                    ? UIColors.Positive
                    : row.DecisionText == "名单跳过" ? UIColors.TextSub : UIColors.TextWarn;
                _labels[6].Text = row.CheckedAtText;
                BackColor = index % 2 == 0 ? Color.Transparent : UIColors.ControlBg;
            }
            finally
            {
                _rendering = false;
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            YouPinLandlordShelfColumns.Layout(
                new Control[]
                {
                    _selection,
                    _labels[0], _labels[1], _labels[2], _labels[3],
                    _labels[4], _labels[5], _labels[6]
                },
                ClientRectangle);
        }

        private static Label CreateCellLabel()
        {
            return new Label
            {
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", 8.8F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(UIUtils.S(8), 0, UIUtils.S(4), 0)
            };
        }
    }

    internal static class YouPinLandlordShelfColumns
    {
        private static readonly int[] WidthPercent = { 12, 25, 10, 10, 10, 10, 14, 9 };

        public static void Layout(IReadOnlyList<Control> controls, Rectangle bounds)
        {
            int x = bounds.Left;
            int remaining = bounds.Width;
            for (int index = 0; index < controls.Count; index++)
            {
                int width = index == controls.Count - 1
                    ? remaining
                    : Math.Max(1, bounds.Width * WidthPercent[index] / 100);
                controls[index].SetBounds(x, bounds.Top, width, bounds.Height);
                x += width;
                remaining = Math.Max(1, bounds.Right - x);
            }
        }
    }
}
