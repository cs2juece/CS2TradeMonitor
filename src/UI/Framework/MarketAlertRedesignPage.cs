using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class MarketAlertRedesignHostPage : FrameworkSettingsHostPage<MarketAlertRedesignPage>
    {
        public MarketAlertRedesignHostPage()
            : base(new MarketAlertRedesignPage())
        {
        }
    }

    public sealed class MarketAlertRedesignPage : FrameworkSettingsPageBase
    {
        private static readonly Color RiseColor = Color.FromArgb(220, 70, 90);
        private static readonly Color FallColor = Color.FromArgb(80, 160, 135);

        private readonly List<MarketAlertRuleCardBinding> _ruleCards = new();
        private readonly ToolTip _toolTip = new();
        private TableLayoutPanel? _root;
        private MarketAlertPill? _headerStatusPill;
        private Label? _testStatusLabel;
        private MarketAlertPill? _testStatusPill;
        private YouPinCcRoundedPanel? _testCard;
        private Panel? _advancedHost;
        private FlowLayoutPanel? _advancedList;
        private LiteButton? _advancedToggle;
        private LiteButton? _advancedAddButton;
        private bool _advancedExpanded;
        private bool _widthSyncQueued;
        private bool _refreshAllQueued;
        private EventHandler? _refreshAllHandleCreatedHandler;
        private bool _disposed;

        private int ContentWidth
        {
            get { return ContentBounds.Width; }
        }

        private Rectangle ContentBounds
        {
            get { return GetVisibleContentBounds(FrameworkSettingsPageLayoutHelper.StandardContentMinimumWidth); }
        }

        private List<MarketAlertRule> Rules => GetList<MarketAlertRule>(nameof(Settings.MarketAlertRules));

        public MarketAlertRedesignPage()
        {
            Container.SizeChanged += (_, __) => QueueDeferredContentWidthSync();
            MarketDataSourceManager.DataUpdated += OnMarketDataUpdated;
        }

        protected override void OnStoreAttached()
        {
            EnsureBuiltinRules();
            BuildPage();
        }

        public override void Activate()
        {
            EnsureBuiltinRules();
            base.Activate();
            QueueDeferredRefreshAll();
            QueueDeferredContentWidthSync();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            RefreshAll();
            _root?.Invalidate(true);
        }

        protected override int GetTopLevelContentWidth()
        {
            return ContentWidth;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (_refreshAllHandleCreatedHandler != null)
                {
                    HandleCreated -= _refreshAllHandleCreatedHandler;
                    _refreshAllHandleCreatedHandler = null;
                }

                MarketDataSourceManager.DataUpdated -= OnMarketDataUpdated;
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildPage()
        {
            ClearPage();
            _ruleCards.Clear();
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
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = UIColors.MainBg
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Container.Controls.Add(_root);

            AddRootRow(CreateHeaderPanel());
            AddRootRow(CreateControlCard());
            AddRootRow(CreateRulesHost());
            AddRootRow(CreateTestCard());
            RefreshFromStore();
            QueueDeferredRefreshAll();
            QueueDeferredContentWidthSync();
        }

        private Control CreateHeaderPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(66),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(2))
            };

            var avatar = new MarketAlertAvatar();
            var title = CreateTextLabel("大盘预警", 16F, FontStyle.Bold, UIColors.TextMain);
            _headerStatusPill = new MarketAlertPill { Width = UIUtils.S(66), Height = UIUtils.S(24) };
            var description = CreateTextLabel("监控 QAQ / SteamDT 指数点位与涨跌幅。", 9F, FontStyle.Regular, UIColors.TextSub);
            var test = new LiteButton("发送测试预警", true) { Width = UIUtils.S(132), Height = UIUtils.S(38) };
            var openData = new LiteButton("查看大盘数据源", false) { Width = UIUtils.S(142), Height = UIUtils.S(38) };
            test.Click += (_, __) => SendTestAlert();
            openData.Click += (_, __) => SwitchToDataPage();

            panel.Controls.AddRange(new Control[] { avatar, title, _headerStatusPill, description, test, openData });
            panel.Layout += (_, __) =>
            {
                int y = UIUtils.S(8);
                avatar.SetBounds(0, y, UIUtils.S(44), UIUtils.S(44));
                title.SetBounds(avatar.Right + UIUtils.S(14), y + UIUtils.S(2), UIUtils.S(132), UIUtils.S(34));
                _headerStatusPill.SetBounds(title.Right + UIUtils.S(8), y + UIUtils.S(10), UIUtils.S(66), UIUtils.S(24));
                openData.SetBounds(panel.Width - openData.Width, y + UIUtils.S(3), openData.Width, openData.Height);
                test.SetBounds(openData.Left - UIUtils.S(12) - test.Width, openData.Top, test.Width, test.Height);
                description.SetBounds(_headerStatusPill.Right + UIUtils.S(22), y + UIUtils.S(11), Math.Max(1, test.Left - _headerStatusPill.Right - UIUtils.S(34)), UIUtils.S(24));
            };
            return panel;
        }

        private Control CreateControlCard()
        {
            var card = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(172),
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(16))
            };
            var title = CreateTextLabel("预警总控", 11F, FontStyle.Bold, UIColors.TextMain);
            var divider = new Panel { BackColor = UIColors.Border };
            var globalLabel = CreateTextLabel("总开关", 9F, FontStyle.Regular, UIColors.TextSub);
            var modeLabel = CreateTextLabel("提醒方式", 9F, FontStyle.Regular, UIColors.TextSub);
            var deferLabel = CreateTextLabel("全屏/游戏时延迟汇总", 9F, FontStyle.Regular, UIColors.TextSub);
            var windowLabel = CreateTextLabel("默认统计窗口", 9F, FontStyle.Regular, UIColors.TextSub);
            var cooldownLabel = CreateTextLabel("同一规则冷却", 9F, FontStyle.Regular, UIColors.TextSub);
            var note = CreateTextLabel("ⓘ 总开关 + 单条规则同时启用后才会触发；点位规则需要穿越阈值，涨跌幅规则需要窗口内达到百分比。", 8.5F, FontStyle.Regular, UIColors.TextSub);

            var enabledSwitch = CreateBoundSwitch(nameof(Settings.MarketAlertsEnabled), false, RefreshHeaderStatus);
            var modeCombo = CreateNotificationModeCombo();
            var deferSwitch = CreateBoundSwitch(nameof(Settings.MarketAlertDeferWhenFullscreen), true, null);
            var windowInput = CreateBoundIntInput(nameof(Settings.MarketAlertDefaultWindowMinutes), 10, "分钟", 88, value => UpdateDefaultWindow(value));
            var cooldownInput = CreateBoundIntInput(nameof(Settings.MarketAlertDefaultCooldownMinutes), 5, "分钟", 88, value => UpdateDefaultCooldown(value));

            card.Controls.AddRange(new Control[]
            {
                title, divider,
                globalLabel, enabledSwitch,
                modeLabel, modeCombo,
                deferLabel, deferSwitch,
                windowLabel, windowInput,
                cooldownLabel, cooldownInput,
                note
            });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(20);
                bool compact = card.Width < UIUtils.S(760);
                int desiredHeight = compact ? UIUtils.S(224) : UIUtils.S(172);
                if (card.Height != desiredHeight)
                    card.Height = desiredHeight;

                title.SetBounds(pad, UIUtils.S(14), UIUtils.S(180), UIUtils.S(28));
                divider.SetBounds(pad, UIUtils.S(50), Math.Max(1, card.Width - pad * 2), 1);
                int y = UIUtils.S(72);
                globalLabel.SetBounds(pad, y, UIUtils.S(70), UIUtils.S(30));
                enabledSwitch.SetBounds(globalLabel.Right + UIUtils.S(16), y, UIUtils.S(58), UIUtils.S(30));
                modeLabel.SetBounds(enabledSwitch.Right + UIUtils.S(compact ? 30 : 48), y, UIUtils.S(74), UIUtils.S(30));
                modeCombo.SetBounds(modeLabel.Right + UIUtils.S(12), y - UIUtils.S(1), UIUtils.S(212), UIUtils.S(34));
                int deferY = compact ? UIUtils.S(112) : y;
                int deferX = compact ? pad : modeCombo.Right + UIUtils.S(34);
                deferLabel.SetBounds(deferX, deferY, UIUtils.S(156), UIUtils.S(30));
                deferSwitch.SetBounds(deferLabel.Right + UIUtils.S(10), deferY, UIUtils.S(58), UIUtils.S(30));

                int y2 = compact ? UIUtils.S(152) : UIUtils.S(112);
                windowLabel.SetBounds(pad, y2, UIUtils.S(106), UIUtils.S(30));
                windowInput.SetBounds(windowLabel.Right + UIUtils.S(10), y2 - UIUtils.S(1), UIUtils.S(88), UIUtils.S(34));
                cooldownLabel.SetBounds(windowInput.Right + UIUtils.S(34), y2, UIUtils.S(100), UIUtils.S(30));
                cooldownInput.SetBounds(cooldownLabel.Right + UIUtils.S(10), y2 - UIUtils.S(1), UIUtils.S(88), UIUtils.S(34));
                note.SetBounds(pad, compact ? UIUtils.S(194) : UIUtils.S(146), Math.Max(1, card.Width - pad * 2), UIUtils.S(22));
            };
            return card;
        }

        private Control CreateRulesHost()
        {
            var host = new Panel
            {
                Height = UIUtils.S(390),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UIUtils.S(16))
            };
            Control qaq = CreateSourceRuleCard("QAQ 指数规则", MarketDataSourceManager.QaqId, MarketDataSourceManager.QaqDisplayKey);
            Control steamDt = CreateSourceRuleCard("SteamDT 指数规则", MarketDataSourceManager.SteamDtId, MarketDataSourceManager.SteamDtDisplayKey);
            host.Controls.Add(qaq);
            host.Controls.Add(steamDt);
            host.Layout += (_, __) =>
            {
                bool stacked = host.Width < UIUtils.S(760);
                int gap = UIUtils.S(16);
                if (stacked)
                {
                    int cardHeight = UIUtils.S(370);
                    host.Height = cardHeight * 2 + gap;
                    qaq.SetBounds(0, 0, host.Width, cardHeight);
                    steamDt.SetBounds(0, cardHeight + gap, host.Width, cardHeight);
                }
                else
                {
                    int w = (host.Width - gap) / 2;
                    host.Height = UIUtils.S(390);
                    qaq.SetBounds(0, 0, w, host.Height);
                    steamDt.SetBounds(w + gap, 0, host.Width - w - gap, host.Height);
                }
            };
            return host;
        }

        private Control CreateSourceRuleCard(string titleText, string sourceId, string displayKey)
        {
            var card = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg
            };
            var title = CreateTextLabel(titleText, 11F, FontStyle.Bold, UIColors.TextMain);
            var indexPill = new MarketAlertPill { Width = UIUtils.S(112), Height = UIUtils.S(26) };
            var refreshPill = new MarketAlertPill { Width = UIUtils.S(108), Height = UIUtils.S(26) };
            var enabledPill = new MarketAlertPill { Width = UIUtils.S(86), Height = UIUtils.S(26) };
            var binding = new MarketAlertRuleCardBinding(sourceId, displayKey, indexPill, refreshPill, enabledPill);

            card.Controls.AddRange(new Control[] { title, indexPill, refreshPill, enabledPill });
            foreach (RuleRowSpec spec in MarketAlertPageModel.CreateSourceRuleSpecs(sourceId))
            {
                var row = CreateRuleRow(spec, binding);
                binding.Rows.Add(row);
                card.Controls.Add(row.Row);
            }

            _ruleCards.Add(binding);
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int y = UIUtils.S(16);
                int enabledW = UIUtils.S(86);
                int refreshW = UIUtils.S(92);
                int indexW = UIUtils.S(112);
                enabledPill.SetBounds(card.Width - pad - enabledW, y + UIUtils.S(1), enabledW, UIUtils.S(26));
                refreshPill.SetBounds(enabledPill.Left - UIUtils.S(8) - refreshW, enabledPill.Top, refreshW, UIUtils.S(26));
                indexPill.SetBounds(refreshPill.Left - UIUtils.S(8) - indexW, enabledPill.Top, indexW, UIUtils.S(26));
                int titleWidth = Math.Max(1, indexPill.Left - pad - UIUtils.S(10));
                string compactTitle = titleWidth < UIUtils.S(92)
                    ? titleText.Replace(" 指数规则", "", StringComparison.Ordinal)
                    : titleWidth < UIUtils.S(124)
                        ? titleText.Replace(" 指数规则", " 规则", StringComparison.Ordinal)
                        : titleText;
                if (!string.Equals(title.Text, compactTitle, StringComparison.Ordinal))
                    title.Text = compactTitle;
                title.SetBounds(pad, y, titleWidth, UIUtils.S(28));
                int rowY = UIUtils.S(70);
                foreach (RuleRowBinding row in binding.Rows)
                {
                    row.Row.SetBounds(pad, rowY, Math.Max(1, card.Width - pad * 2), UIUtils.S(72));
                    rowY += UIUtils.S(72);
                }
            };
            return card;
        }

        private RuleRowBinding CreateRuleRow(RuleRowSpec spec, MarketAlertRuleCardBinding card)
        {
            bool percent = MarketAlertRedesignPageModel.IsPercentRule(spec.RuleType);
            Color accent = MarketAlertRedesignPageModel.GetRuleAccentColor(spec.RuleType, RiseColor, FallColor);
            var row = new Panel { BackColor = Color.Transparent, Height = UIUtils.S(72), Margin = Padding.Empty };
            var icon = new MarketRuleIcon(spec.RuleType, accent) { Width = UIUtils.S(34), Height = UIUtils.S(34) };
            var title = CreateTextLabel(MarketAlertRedesignPageModel.GetShortRuleTitle(spec.RuleType), 9.2F, FontStyle.Bold, UIColors.TextMain);
            var hint = CreateTextLabel(MarketAlertRedesignPageModel.GetCompactRuleHint(spec.RuleType), 8F, FontStyle.Regular, UIColors.TextSub);
            _toolTip.SetToolTip(hint, spec.Hint);
            var toggle = new MarketAlertSwitch();
            var windowInput = new LiteNumberInput("", "分钟", "", 62) { Visible = percent };
            var thresholdInput = new LiteNumberInput("", spec.Unit, "", 82);
            windowInput.Placeholder = "10";
            thresholdInput.Placeholder = spec.Placeholder;
            windowInput.Padding = UIUtils.S(new Padding(0, 5, 0, 1));
            thresholdInput.Padding = UIUtils.S(new Padding(0, 5, 0, 1));

            var binding = new RuleRowBinding(spec, row, toggle, windowInput, thresholdInput);
            toggle.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                MarketAlertRule? rule = EnsureBuiltinRule(spec.SourceId, spec.RuleType);
                if (rule == null)
                    return;

                rule.Enabled = toggle.Checked;
                SaveRules();
                RefreshRuleRow(binding);
                RefreshRuleCardStatus(card);
                RefreshAdvancedRules();
            };
            thresholdInput.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (double.TryParse(thresholdInput.Inner.Text, out double value))
                {
                    MarketAlertRule? rule = EnsureBuiltinRule(spec.SourceId, spec.RuleType);
                    if (rule == null)
                        return;

                    rule.Threshold = Math.Max(0, value);
                    SaveRules();
                    RefreshAdvancedRules();
                }
            };
            windowInput.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls || !percent)
                    return;

                if (int.TryParse(windowInput.Inner.Text, out int value))
                {
                    MarketAlertRule? rule = EnsureBuiltinRule(spec.SourceId, spec.RuleType);
                    if (rule == null)
                        return;

                    rule.WindowMinutes = Math.Clamp(value, 1, 1440);
                    SaveRules();
                    hint.Text = MarketAlertRedesignPageModel.GetCompactRuleHint(spec.RuleType);
                    _toolTip.SetToolTip(hint, MarketAlertPageModel.GetRuleHint(spec.RuleType, rule.WindowMinutes));
                    RefreshAdvancedRules();
                }
            };

            row.Controls.AddRange(new Control[] { icon, title, hint, toggle, windowInput, thresholdInput });
            row.Layout += (_, __) =>
            {
                MarketAlertRuleRowLayout layout = MarketAlertRedesignPageModel.BuildRuleRowLayout(row.Width, row.Height, percent);
                icon.Bounds = layout.IconBounds;
                title.Bounds = layout.TitleBounds;
                hint.Bounds = layout.HintBounds;
                toggle.Bounds = layout.ToggleBounds;
                windowInput.Bounds = layout.WindowInputBounds;
                thresholdInput.Bounds = layout.ThresholdInputBounds;
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };

            return binding;
        }

        private Control CreateTestCard()
        {
            var card = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(184),
                Radius = UIUtils.S(6),
                FillOverride = UIColors.CardBg,
                Margin = Padding.Empty
            };
            _testCard = card;
            var title = CreateTextLabel("测试与说明", 11F, FontStyle.Bold, UIColors.TextMain);
            var divider = new Panel { BackColor = UIColors.Border };
            var statusTitle = CreateTextLabel("上次测试状态", 9F, FontStyle.Regular, UIColors.TextSub);
            _testStatusPill = new MarketAlertPill { Width = UIUtils.S(78), Height = UIUtils.S(28) };
            var button = new LiteButton("发送测试预警", true) { Width = UIUtils.S(132), Height = UIUtils.S(36) };
            button.Click += (_, __) => SendTestAlert();
            _testStatusLabel = CreateTextLabel("测试只检查提醒方式和弹窗样式，不改变真实规则状态。", 9F, FontStyle.Regular, UIColors.TextSub);

            _advancedHost = new Panel { BackColor = Color.Transparent };
            var advancedTitle = CreateTextLabel("高级规则", 9.5F, FontStyle.Bold, UIColors.TextMain);
            var advancedHint = CreateTextLabel("高级模式下可新增自定义规则，内置规则请在上方卡片编辑。", 8.5F, FontStyle.Regular, UIColors.TextSub);
            _advancedToggle = new LiteButton("展开", false) { Width = UIUtils.S(74), Height = UIUtils.S(30) };
            _advancedAddButton = new LiteButton("新增自定义规则", true) { Width = UIUtils.S(122), Height = UIUtils.S(30), Visible = false };
            _advancedList = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = Color.Transparent,
                Visible = false
            };
            _advancedToggle.Click += (_, __) =>
            {
                _advancedExpanded = !_advancedExpanded;
                RefreshAdvancedRules();
            };
            _advancedAddButton.Click += (_, __) => AddCustomRule();
            _advancedHost.Controls.AddRange(new Control[] { advancedTitle, advancedHint, _advancedToggle, _advancedAddButton, _advancedList });
            _advancedHost.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                advancedTitle.SetBounds(pad, UIUtils.S(8), UIUtils.S(92), UIUtils.S(28));
                advancedHint.SetBounds(advancedTitle.Right + UIUtils.S(12), UIUtils.S(9), Math.Max(1, _advancedHost.Width - UIUtils.S(320)), UIUtils.S(26));
                _advancedToggle.SetBounds(_advancedHost.Width - pad - _advancedToggle.Width, UIUtils.S(8), _advancedToggle.Width, _advancedToggle.Height);
                _advancedAddButton.SetBounds(_advancedToggle.Left - UIUtils.S(10) - _advancedAddButton.Width, _advancedToggle.Top, _advancedAddButton.Width, _advancedAddButton.Height);
                _advancedList.SetBounds(pad, UIUtils.S(46), Math.Max(1, _advancedHost.Width - pad * 2), Math.Max(1, _advancedHost.Height - UIUtils.S(54)));
                ResizeAdvancedRows();
            };

            card.Controls.AddRange(new Control[] { title, divider, statusTitle, _testStatusPill, button, _testStatusLabel, _advancedHost });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(20);
                title.SetBounds(pad, UIUtils.S(14), UIUtils.S(180), UIUtils.S(28));
                divider.SetBounds(pad, UIUtils.S(50), Math.Max(1, card.Width - pad * 2), 1);
                int y = UIUtils.S(75);
                statusTitle.SetBounds(pad, y, UIUtils.S(104), UIUtils.S(30));
                _testStatusPill.SetBounds(statusTitle.Right + UIUtils.S(10), y + UIUtils.S(1), UIUtils.S(78), UIUtils.S(28));
                button.SetBounds(_testStatusPill.Right + UIUtils.S(36), y - UIUtils.S(3), UIUtils.S(132), UIUtils.S(36));
                _testStatusLabel.SetBounds(button.Right + UIUtils.S(28), y, Math.Max(1, card.Width - button.Right - UIUtils.S(48)), UIUtils.S(30));
                _advancedHost.SetBounds(pad, UIUtils.S(126), Math.Max(1, card.Width - pad * 2), Math.Max(UIUtils.S(50), card.Height - UIUtils.S(140)));
            };
            return card;
        }

        private MarketAlertSwitch CreateBoundSwitch(string settingKey, bool fallback, Action? afterChanged)
        {
            var toggle = new MarketAlertSwitch { Checked = Get(settingKey, fallback) };
            toggle.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(settingKey, toggle.Checked);
                afterChanged?.Invoke();
            };
            RegisterRefresh(() => toggle.Checked = Get(settingKey, fallback));
            RegisterSave(() => Set(settingKey, toggle.Checked));
            return toggle;
        }

        private LiteComboBox CreateNotificationModeCombo()
        {
            var combo = new LiteComboBox { Width = UIUtils.S(212), Height = UIUtils.S(34) };
            combo.AddItem("系统托盘气泡", ((int)MarketAlertNotificationMode.TrayBalloon).ToString());
            combo.AddItem("桌面右下角弹窗", ((int)MarketAlertNotificationMode.DesktopToast).ToString());
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;
                if (int.TryParse(combo.SelectedValue, out int value) && Enum.IsDefined(typeof(MarketAlertNotificationMode), value))
                    Set(nameof(Settings.MarketAlertNotificationMode), (MarketAlertNotificationMode)value);
            };
            RegisterRefresh(() =>
            {
                var mode = Get(nameof(Settings.MarketAlertNotificationMode), MarketAlertNotificationMode.DesktopToast);
                if (mode == MarketAlertNotificationMode.InAppToast)
                    mode = MarketAlertNotificationMode.DesktopToast;
                combo.SelectValue(((int)mode).ToString());
            });
            RegisterSave(() =>
            {
                if (int.TryParse(combo.SelectedValue, out int value) && Enum.IsDefined(typeof(MarketAlertNotificationMode), value))
                    Set(nameof(Settings.MarketAlertNotificationMode), (MarketAlertNotificationMode)value);
            });
            return combo;
        }

        private LiteNumberInput CreateBoundIntInput(string settingKey, int fallback, string unit, int width, Action<int> afterChanged)
        {
            var input = new LiteNumberInput(Get(settingKey, fallback).ToString(), unit, "", width)
            {
                Height = UIUtils.S(34),
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                    afterChanged(Math.Clamp(value, 1, 1440));
            };
            RegisterRefresh(() => input.Inner.Text = Math.Clamp(Get(settingKey, fallback), 1, 1440).ToString());
            RegisterSave(() =>
            {
                if (int.TryParse(input.Inner.Text, out int value))
                    Set(settingKey, Math.Clamp(value, 1, 1440));
            });
            return input;
        }

        private void RefreshAll()
        {
            RefreshHeaderStatus();
            foreach (MarketAlertRuleCardBinding card in _ruleCards)
                RefreshRuleCard(card);
            RefreshTestStatus();
            RefreshAdvancedRules();
        }

        private void RefreshHeaderStatus()
        {
            bool enabled = Get(nameof(Settings.MarketAlertsEnabled), false);
            _headerStatusPill?.SetText(enabled ? "已启用" : "未启用", enabled ? UIColors.Positive : UIColors.TextSub);
        }

        private void RefreshRuleCard(MarketAlertRuleCardBinding card)
        {
            RefreshRuleCardSnapshot(card);
            RefreshRuleCardStatus(card);
            foreach (RuleRowBinding row in card.Rows)
                RefreshRuleRow(row);
        }

        private void RefreshRuleCardSnapshot(MarketAlertRuleCardBinding card)
        {
            MarketDisplaySnapshot snapshot = MarketDataSourceManager.GetDisplaySnapshot(card.DisplayKey);
            var snapshotView = MarketAlertRedesignPageModel.BuildSnapshotView(snapshot);
            card.IndexPill.SetText(snapshotView.IndexText, snapshotView.IndexColor);
            card.RefreshPill.SetText(snapshotView.RefreshText, UIColors.TextSub);
        }

        private void RefreshRuleCardStatus(MarketAlertRuleCardBinding card)
        {
            int enabled = MarketAlertRedesignPageModel.CountEnabledBuiltinRules(Rules, card.SourceId);
            card.EnabledPill.SetText($"已启用 {enabled}/4", enabled > 0 ? UIColors.Primary : UIColors.TextSub);
        }

        private void RefreshRuleRow(RuleRowBinding binding)
        {
            MarketAlertRule? rule = FindBuiltinRule(binding.Spec.SourceId, binding.Spec.RuleType) ?? EnsureBuiltinRule(binding.Spec.SourceId, binding.Spec.RuleType);
            if (rule == null)
                return;

            RunWithUpdateGuard(() =>
            {
                binding.Toggle.Checked = rule.Enabled;
                if (binding.WindowInput.Visible)
                    binding.WindowInput.Inner.Text = Math.Clamp(rule.WindowMinutes, 1, 1440).ToString();
                binding.ThresholdInput.Inner.Text = MarketAlertPageModel.FormatThreshold(rule);
            });
            ApplyRuleInputState(binding.WindowInput, rule.Enabled);
            ApplyRuleInputState(binding.ThresholdInput, rule.Enabled);
        }

        private static void ApplyRuleInputState(LiteNumberInput input, bool enabled)
        {
            input.Enabled = true;
            input.Inner.ReadOnly = !enabled;
            input.TabStop = enabled;
            input.Inner.TabStop = enabled;
            input.Cursor = enabled ? Cursors.IBeam : Cursors.Default;
            input.Inner.Cursor = enabled ? Cursors.IBeam : Cursors.Default;
            input.SetTextColor(enabled ? UIColors.TextMain : UIColors.TextSub);
        }

        private void RefreshTestStatus()
        {
            _testStatusPill?.SetText("未测试", UIColors.TextSub);
            if (_testStatusLabel != null && string.IsNullOrWhiteSpace(_testStatusLabel.Text))
                _testStatusLabel.Text = "测试只检查提醒方式和弹窗样式，不改变真实规则状态。";
        }

        private void RefreshAdvancedRules()
        {
            if (_advancedList == null || _advancedToggle == null || _advancedAddButton == null || _testCard == null)
                return;

            _advancedToggle.Text = _advancedExpanded ? "收起" : "展开";
            _advancedAddButton.Visible = _advancedExpanded;
            _advancedList.Visible = _advancedExpanded;
            _advancedList.Controls.Clear();

            if (_advancedExpanded)
            {
                var customRules = MarketAlertRedesignPageModel.SelectCustomRules(Rules).ToArray();
                if (customRules.Length == 0)
                {
                    _advancedList.Controls.Add(CreateEmptyAdvancedLabel());
                }
                else
                {
                    foreach (MarketAlertRule rule in customRules)
                    {
                        Control row = MarketAlertAdvancedRuleRowFactory.Create(
                            rule,
                            new MarketAlertAdvancedRuleRowCallbacks(
                                SaveRules,
                                deletedRule =>
                                {
                                    if (MarketAlertPageModel.IsBuiltinRule(deletedRule))
                                        return;

                                    Rules.Remove(deletedRule);
                                    SaveRules();
                                    RefreshAdvancedRules();
                                }));
                        _advancedList.Controls.Add(row);
                    }
                }
            }

            int rowCount = Math.Max(1, _advancedList.Controls.Count);
            _testCard.Height = _advancedExpanded ? UIUtils.S(228 + rowCount * 44) : UIUtils.S(184);
            _advancedHost!.Height = Math.Max(UIUtils.S(50), _testCard.Height - UIUtils.S(140));
            ResizeAdvancedRows();
            SyncContentWidth();
        }

        private Control CreateEmptyAdvancedLabel()
        {
            return new Label
            {
                Text = "暂无自定义规则，点击右侧按钮新增。",
                AutoSize = false,
                Height = UIUtils.S(34),
                Width = Math.Max(UIUtils.S(360), _advancedList?.Width ?? UIUtils.S(760)),
                Font = new Font("Microsoft YaHei UI", 8.8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void ResizeAdvancedRows()
        {
            if (_advancedList == null)
                return;

            int width = Math.Max(UIUtils.S(760), _advancedList.Width - UIUtils.S(4));
            foreach (Control row in _advancedList.Controls)
                row.Width = width;
        }

        private void AddCustomRule()
        {
            Rules.Add(new MarketAlertRule
            {
                Id = "custom:" + Guid.NewGuid().ToString("N"),
                Name = "自定义规则",
                Enabled = false,
                SourceId = MarketDataSourceManager.QaqId,
                RuleType = MarketAlertRuleType.CrossAbove,
                Threshold = 0,
                WindowMinutes = Math.Max(1, Get(nameof(Settings.MarketAlertDefaultWindowMinutes), 10)),
                CooldownMinutes = Math.Max(1, Get(nameof(Settings.MarketAlertDefaultCooldownMinutes), 5))
            });
            SaveRules();
            _advancedExpanded = true;
            RefreshAdvancedRules();
        }

        private void UpdateDefaultWindow(int value)
        {
            value = Math.Clamp(value, 1, 1440);
            Set(nameof(Settings.MarketAlertDefaultWindowMinutes), value);
            EnsureBuiltinRules();
            foreach (MarketAlertRule rule in Rules.Where(MarketAlertPageModel.IsBuiltinRule))
            {
                if (MarketAlertRedesignPageModel.IsPercentRule(rule.RuleType))
                    rule.WindowMinutes = value;
            }
            SaveRules();
            foreach (MarketAlertRuleCardBinding card in _ruleCards)
                foreach (RuleRowBinding row in card.Rows)
                    if (MarketAlertRedesignPageModel.IsPercentRule(row.Spec.RuleType))
                        RefreshRuleRow(row);
            RefreshAdvancedRules();
        }

        private void UpdateDefaultCooldown(int value)
        {
            value = Math.Clamp(value, 1, 1440);
            Set(nameof(Settings.MarketAlertDefaultCooldownMinutes), value);
            EnsureBuiltinRules();
            foreach (MarketAlertRule rule in Rules.Where(MarketAlertPageModel.IsBuiltinRule))
                rule.CooldownMinutes = value;
            SaveRules();
            RefreshAdvancedRules();
        }

        private void SendTestAlert()
        {
            if (Config == null || UI == null)
                return;

            bool delivered = UI.ShowMarketAlertNotification(Config, "大盘预警测试", "这是一条测试提醒，用于确认当前弹窗方式是否符合预期。", ToolTipIcon.Info);
            MarketAlertTextViewModel status = MarketAlertPagePresenter.BuildTestResultStatus(delivered);
            if (_testStatusLabel != null)
            {
                _testStatusLabel.Text = status.Text;
                _testStatusLabel.ForeColor = status.Color;
            }
            _testStatusPill?.SetText(delivered ? "已发送" : "失败", status.Color);
        }

        private void SwitchToDataPage()
        {
            if (FindForm() is CS2TradeMonitor.src.UI.SettingsForm settingsForm)
                settingsForm.SwitchPage("Data");
        }

        private void EnsureBuiltinRules()
        {
            if (MarketAlertRuleCatalog.EnsureBuiltinRules(Rules))
                SaveRules();
        }

        private MarketAlertRule? EnsureBuiltinRule(string sourceId, MarketAlertRuleType ruleType)
        {
            MarketAlertRule? rule = FindBuiltinRule(sourceId, ruleType);
            if (rule != null)
                return rule;

            EnsureBuiltinRules();
            return FindBuiltinRule(sourceId, ruleType);
        }

        private MarketAlertRule? FindBuiltinRule(string sourceId, MarketAlertRuleType ruleType)
        {
            string id = Settings.GetBuiltinMarketAlertRuleId(sourceId, ruleType);
            return Rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveRules()
        {
            Set(nameof(Settings.MarketAlertRules), Rules);
        }

        private void OnMarketDataUpdated()
        {
            if (_disposed || IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    foreach (MarketAlertRuleCardBinding card in _ruleCards)
                        RefreshRuleCardSnapshot(card);
                }));
            }
            catch
            {
                // 页面隐藏或销毁期间的规则卡片刷新可以安全丢弃。
            }
        }

        private void QueueDeferredRefreshAll()
        {
            if (_refreshAllQueued || _disposed || IsDisposed)
                return;

            _refreshAllQueued = true;

            try
            {
                if (IsHandleCreated)
                {
                    BeginInvoke((MethodInvoker)RunDeferredRefreshAll);
                }
                else
                {
                    _refreshAllHandleCreatedHandler ??= (_, __) =>
                    {
                        if (_refreshAllHandleCreatedHandler != null)
                        {
                            HandleCreated -= _refreshAllHandleCreatedHandler;
                            _refreshAllHandleCreatedHandler = null;
                        }

                        if (!_refreshAllQueued || _disposed || IsDisposed)
                            return;

                        try
                        {
                            BeginInvoke((MethodInvoker)RunDeferredRefreshAll);
                        }
                        catch
                        {
                            _refreshAllQueued = false;
                        }
                    };
                    HandleCreated += _refreshAllHandleCreatedHandler;
                }
            }
            catch
            {
                _refreshAllQueued = false;
            }
        }

        private void RunDeferredRefreshAll()
        {
            _refreshAllQueued = false;
            if (_disposed || IsDisposed || !IsHandleCreated || _root == null || _root.IsDisposed || Container.IsDisposed)
                return;

            RefreshAll();
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

        private sealed class MarketAlertRuleCardBinding
        {
            public MarketAlertRuleCardBinding(string sourceId, string displayKey, MarketAlertPill indexPill, MarketAlertPill refreshPill, MarketAlertPill enabledPill)
            {
                SourceId = sourceId;
                DisplayKey = displayKey;
                IndexPill = indexPill;
                RefreshPill = refreshPill;
                EnabledPill = enabledPill;
            }

            public string SourceId { get; }
            public string DisplayKey { get; }
            public MarketAlertPill IndexPill { get; }
            public MarketAlertPill RefreshPill { get; }
            public MarketAlertPill EnabledPill { get; }
            public List<RuleRowBinding> Rows { get; } = new();
        }

        private sealed class RuleRowBinding
        {
            public RuleRowBinding(RuleRowSpec spec, Panel row, MarketAlertSwitch toggle, LiteNumberInput windowInput, LiteNumberInput thresholdInput)
            {
                Spec = spec;
                Row = row;
                Toggle = toggle;
                WindowInput = windowInput;
                ThresholdInput = thresholdInput;
            }

            public RuleRowSpec Spec { get; }
            public Panel Row { get; }
            public MarketAlertSwitch Toggle { get; }
            public LiteNumberInput WindowInput { get; }
            public LiteNumberInput ThresholdInput { get; }
        }
    }

    internal static class MarketAlertRedesignPageModel
    {
        public static bool IsPercentRule(MarketAlertRuleType ruleType)
        {
            return ruleType == MarketAlertRuleType.RiseByPercent || ruleType == MarketAlertRuleType.FallByPercent;
        }

        public static bool IsRiseRule(MarketAlertRuleType ruleType)
        {
            return ruleType == MarketAlertRuleType.CrossAbove || ruleType == MarketAlertRuleType.RiseByPercent;
        }

        public static Color GetRuleAccentColor(MarketAlertRuleType ruleType, Color riseColor, Color fallColor)
        {
            return IsRiseRule(ruleType) ? riseColor : fallColor;
        }

        public static string GetShortRuleTitle(MarketAlertRuleType ruleType)
        {
            return ruleType switch
            {
                MarketAlertRuleType.CrossAbove => "突破点位",
                MarketAlertRuleType.CrossBelow => "跌破点位",
                MarketAlertRuleType.RiseByPercent => "规定时间内上涨",
                MarketAlertRuleType.FallByPercent => "规定时间内下跌",
                _ => "规则"
            };
        }

        public static string GetCompactRuleHint(MarketAlertRuleType ruleType)
        {
            return ruleType switch
            {
                MarketAlertRuleType.CrossAbove => "上穿点位时提醒。",
                MarketAlertRuleType.CrossBelow => "下穿点位时提醒。",
                MarketAlertRuleType.RiseByPercent => "上涨达到百分比时提醒。",
                MarketAlertRuleType.FallByPercent => "下跌达到百分比时提醒。",
                _ => ""
            };
        }

        public static MarketAlertRuleRowLayout BuildRuleRowLayout(int rowWidth, int rowHeight, bool percent)
        {
            int mid = rowHeight / 2;
            int iconSize = UIUtils.S(34);
            int switchW = UIUtils.S(52);
            int switchH = UIUtils.S(28);
            int thresholdW = UIUtils.S(82);
            int windowW = UIUtils.S(62);
            int inputH = UIUtils.S(34);
            int inputTop = mid - inputH / 2;
            int textLeftGap = UIUtils.S(12);
            int textRightGap = UIUtils.S(16);

            var iconBounds = new Rectangle(0, mid - iconSize / 2, iconSize, iconSize);
            var thresholdBounds = new Rectangle(
                Math.Max(0, rowWidth - thresholdW),
                inputTop,
                thresholdW,
                inputH);

            Rectangle windowBounds;
            Rectangle toggleBounds;
            if (percent)
            {
                windowBounds = new Rectangle(
                    thresholdBounds.Left - UIUtils.S(10) - windowW,
                    inputTop,
                    windowW,
                    inputH);
                toggleBounds = new Rectangle(
                    windowBounds.Left - UIUtils.S(18) - switchW,
                    mid - switchH / 2,
                    switchW,
                    switchH);
            }
            else
            {
                windowBounds = new Rectangle(
                    thresholdBounds.Left,
                    inputTop,
                    0,
                    inputH);
                toggleBounds = new Rectangle(
                    thresholdBounds.Left - UIUtils.S(20) - switchW,
                    mid - switchH / 2,
                    switchW,
                    switchH);
            }

            int textLeft = iconBounds.Right + textLeftGap;
            int textRight = Math.Max(textLeft + 1, toggleBounds.Left - textRightGap);
            var titleBounds = new Rectangle(textLeft, UIUtils.S(11), textRight - textLeft, UIUtils.S(22));
            var hintBounds = new Rectangle(textLeft, UIUtils.S(34), textRight - textLeft, UIUtils.S(22));
            return new MarketAlertRuleRowLayout(
                iconBounds,
                titleBounds,
                hintBounds,
                toggleBounds,
                windowBounds,
                thresholdBounds);
        }

        public static int CountEnabledBuiltinRules(IEnumerable<MarketAlertRule> rules, string sourceId)
        {
            ArgumentNullException.ThrowIfNull(rules);
            return rules.Count(rule =>
                rule.Enabled
                && MarketAlertPageModel.IsBuiltinRule(rule)
                && string.Equals(rule.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerable<MarketAlertRule> SelectCustomRules(IEnumerable<MarketAlertRule> rules)
        {
            ArgumentNullException.ThrowIfNull(rules);
            return rules.Where(rule => !MarketAlertPageModel.IsBuiltinRule(rule));
        }

        public static MarketAlertSnapshotView BuildSnapshotView(MarketDisplaySnapshot snapshot)
        {
            if (!snapshot.HasData)
            {
                return new MarketAlertSnapshotView(
                    string.IsNullOrWhiteSpace(snapshot.PlaceholderText) ? "当前 --" : "当前 " + snapshot.PlaceholderText,
                    UIColors.TextSub,
                    "刷新 --");
            }

            Color color = snapshot.Percent >= 0 ? Color.FromArgb(220, 70, 90) : Color.FromArgb(80, 160, 135);
            string refresh = snapshot.RetrievedAt == default ? "刷新 --" : "刷新 " + snapshot.RetrievedAt.ToString("HH:mm");
            return new MarketAlertSnapshotView("当前 " + MarketDisplayFormatter.FormatIndex(snapshot.Index), color, refresh);
        }
    }

    internal readonly record struct MarketAlertSnapshotView(string IndexText, Color IndexColor, string RefreshText);

    internal readonly record struct MarketAlertRuleRowLayout(
        Rectangle IconBounds,
        Rectangle TitleBounds,
        Rectangle HintBounds,
        Rectangle ToggleBounds,
        Rectangle WindowInputBounds,
        Rectangle ThresholdInputBounds);

    internal static class MarketAlertPaintHelper
    {
        public static Color ResolveSurfaceColor(Control? control)
        {
            Control? current = control;
            while (current != null)
            {
                if (current is YouPinCcRoundedPanel roundedPanel)
                    return roundedPanel.FillOverride ?? UIColors.CardBg;

                if (current.BackColor != Color.Transparent)
                    return current.BackColor;

                current = current.Parent;
            }

            return UIColors.CardBg;
        }
    }

    internal sealed class MarketAlertAvatar : Control
    {
        public MarketAlertAvatar()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Text = "MA";
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(UIColors.Primary);
            e.Graphics.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class MarketAlertPill : Control
    {
        private Color _textColor = UIColors.TextSub;

        public MarketAlertPill()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        }

        public void SetText(string text, Color textColor)
        {
            if (string.Equals(Text, text, StringComparison.Ordinal) && _textColor == textColor)
                return;

            Text = text;
            _textColor = textColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parent = new SolidBrush(MarketAlertPaintHelper.ResolveSurfaceColor(Parent));
            e.Graphics.FillRectangle(parent, ClientRectangle);

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fillColor = Color.FromArgb(UIColors.IsDark ? 32 : 22, _textColor);
            Color borderColor = Color.FromArgb(UIColors.IsDark ? 110 : 90, _textColor);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(4));
            using var fill = new SolidBrush(fillColor);
            using var border = new Pen(borderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            var textRect = new Rectangle(UIUtils.S(6), 0, Math.Max(1, Width - UIUtils.S(12)), Height);
            TextFormatFlags flags = TextRenderer.MeasureText(Text, Font).Width <= textRect.Width
                ? TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, _textColor, flags);
        }
    }

    internal sealed class MarketAlertSwitch : Control
    {
        private bool _checked;

        public MarketAlertSwitch()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = UIUtils.S(58);
            Height = UIUtils.S(30);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Text = "";
            AutoSize = false;
            TabStop = true;
        }

        public event EventHandler? CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value)
                    return;

                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled)
                Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter))
                return;

            Checked = !Checked;
            e.SuppressKeyPress = true;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var parent = new SolidBrush(MarketAlertPaintHelper.ResolveSurfaceColor(Parent)))
                e.Graphics.FillRectangle(parent, ClientRectangle);

            var track = new Rectangle(0, UIUtils.S(2), Width - 1, Height - UIUtils.S(4));
            using var path = UIUtils.RoundRect(track, track.Height / 2);
            Color fillColor;
            Color borderColor;
            if (!Enabled)
            {
                fillColor = UIColors.ControlDisabledBg;
                borderColor = UIColors.Border;
            }
            else if (Checked)
            {
                fillColor = UIColors.Primary;
                borderColor = UIColors.Primary;
            }
            else
            {
                fillColor = UIColors.ControlBg;
                borderColor = UIColors.Border;
            }

            using var fill = new SolidBrush(fillColor);
            using var border = new Pen(borderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            int thumb = UIUtils.S(22);
            int x = Checked ? Width - thumb - UIUtils.S(5) : UIUtils.S(5);
            var thumbRect = new Rectangle(x, (Height - thumb) / 2, thumb, thumb);
            Color thumbColor = Checked
                ? Color.White
                : Color.FromArgb(112, 126, 142);
            using var thumbBrush = new SolidBrush(thumbColor);
            e.Graphics.FillEllipse(thumbBrush, thumbRect);
        }
    }

    internal sealed class MarketRuleIcon : Control
    {
        private readonly MarketAlertRuleType _ruleType;
        private readonly Color _accent;

        public MarketRuleIcon(MarketAlertRuleType ruleType, Color accent)
        {
            _ruleType = ruleType;
            _accent = accent;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parent = new SolidBrush(MarketAlertPaintHelper.ResolveSurfaceColor(Parent));
            e.Graphics.FillRectangle(parent, ClientRectangle);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(4));
            using var fill = new SolidBrush(Color.FromArgb(UIColors.IsDark ? 22 : 18, _accent));
            using var border = new Pen(Color.FromArgb(105, _accent));
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            string text = _ruleType switch
            {
                MarketAlertRuleType.CrossAbove => "↗",
                MarketAlertRuleType.CrossBelow => "↘",
                MarketAlertRuleType.RiseByPercent => "%",
                MarketAlertRuleType.FallByPercent => "%",
                _ => "!"
            };
            TextRenderer.DrawText(e.Graphics, text, Font, ClientRectangle, _accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
