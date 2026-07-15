using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class MarketAlertHostPage : FrameworkSettingsHostPage<MarketAlertPage>
    {
        public MarketAlertHostPage()
            : base(new MarketAlertPage())
        {
        }
    }

    public sealed class MarketAlertPage : FrameworkSettingsPageBase
    {
        private LiteSettingsGroup? _advancedGroup;
        private Panel? _advancedWrapper;
        private FlowLayoutPanel? _advancedList;
        private LiteSettingsGroup? _generalGroup;
        private Panel? _generalSettingsPanel;
        private Panel? _generalLeftColumn;
        private Panel? _generalRightColumn;
        private Panel? _testAlertRow;
        private Label? _testStatus;
        private readonly Dictionary<string, LiteSettingsGroup> _sourceRuleGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _builtSourceRuleRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<RuleRowSpec> _pendingRuleRows = new();
        private System.Windows.Forms.Timer? _deferredGeneralSettingsTimer;
        private System.Windows.Forms.Timer? _deferredRuleRowsTimer;
        private bool _generalSettingsBuilt;
        private int _generalSettingsBuildStep;
        private bool _themeRefreshQueued;

        public MarketAlertPage()
        {
            Container.SuspendLayout();
            try
            {
                using (UiJankProfiler.Measure("MarketAlert.BuildGroup", "ManualTest", thresholdMs: 1))
                    CreateManualTestCard();
                using (UiJankProfiler.Measure("MarketAlert.BuildGroup", "Advanced", thresholdMs: 1))
                    CreateAdvancedCard();
                using (UiJankProfiler.Measure("MarketAlert.BuildGroup", "SteamDtRules", thresholdMs: 1))
                    CreateSourceRuleCard("SteamDT 指数规则", MarketDataSourceManager.SteamDtId, deferRows: true);
                using (UiJankProfiler.Measure("MarketAlert.BuildGroup", "QaqRules", thresholdMs: 1))
                    CreateSourceRuleCard("QAQ 指数规则", MarketDataSourceManager.QaqId, deferRows: true);
                using (UiJankProfiler.Measure("MarketAlert.BuildGroup", "General", thresholdMs: 1))
                    CreateGeneralCard();
            }
            finally
            {
                Container.ResumeLayout(false);
            }
            QueueDeferredRuleRowsBuild();
        }

        protected override void OnStoreAttached()
        {
            EnsureBuiltinRules();
            QueueDeferredGeneralSettingsBuild();
        }

        public override void Activate()
        {
            EnsureBuiltinRules();
            base.Activate();
            RefreshAdvancedRules();
            QueueDeferredGeneralSettingsBuild();
            QueueDeferredRuleRowsBuild();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            ApplyMarketAlertTheme();
            QueueSettledThemeRefresh();
        }

        private void CreateGeneralCard()
        {
            var group = new LiteSettingsGroup("大盘预警");
            _generalGroup = group;
            group.AddFullItem(MarketAlertGeneralSettingsLayoutFactory.CreateIntroPanel());
            AddGroupToPage(group);
            QueueDeferredGeneralSettingsBuild();
        }

        private void QueueDeferredGeneralSettingsBuild()
        {
            if (_generalSettingsBuilt || IsDisposed || Disposing || !IsHandleCreated)
                return;

            _deferredGeneralSettingsTimer ??= CreateDeferredGeneralSettingsTimer();
            _deferredGeneralSettingsTimer.Stop();
            _deferredGeneralSettingsTimer.Interval = 16;
            _deferredGeneralSettingsTimer.Start();
        }

        private System.Windows.Forms.Timer CreateDeferredGeneralSettingsTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 16 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                BuildDeferredGeneralSettings();
            };
            return timer;
        }

        private void BuildDeferredGeneralSettings()
        {
            if (_generalSettingsBuilt || _generalGroup == null || IsDisposed || Disposing)
                return;

            using (UiJankProfiler.Measure("MarketAlert.BuildDeferredGeneralSettings", $"Step={_generalSettingsBuildStep}", thresholdMs: 1))
            {
                switch (_generalSettingsBuildStep)
                {
                    case 0:
                        _generalGroup.SuspendLayout();
                        try
                        {
                            _generalGroup.AddFullItem(CreateGeneralSettingsShell());
                        }
                        finally
                        {
                            _generalGroup.ResumeLayout(false);
                        }
                        _generalSettingsBuildStep = 1;
                        break;
                    case 1:
                        BuildGeneralSettingsLeftColumn();
                        _generalSettingsBuildStep = 2;
                        break;
                    case 2:
                        BuildGeneralNotificationModeRow();
                        _generalSettingsBuildStep = 3;
                        break;
                    case 3:
                        BuildGeneralWindowRow();
                        _generalSettingsBuildStep = 4;
                        break;
                    default:
                        BuildGeneralCooldownRow();
                        _generalSettingsBuilt = true;
                        _generalSettingsBuildStep = 5;
                        break;
                }

                RequestRelayoutGroups();
                RefreshFromStore();
            }

            if (!_generalSettingsBuilt)
                QueueDeferredGeneralSettingsBuild();
        }

        private Control CreateGeneralSettingsShell()
        {
            MarketAlertGeneralSettingsShell shell = MarketAlertGeneralSettingsLayoutFactory.CreateSettingsShell();
            _generalSettingsPanel = shell.Panel;
            _generalLeftColumn = shell.LeftColumn;
            _generalRightColumn = shell.RightColumn;
            return shell.Panel;
        }

        private void BuildGeneralSettingsLeftColumn()
        {
            if (_generalLeftColumn == null)
                return;

            _generalLeftColumn.SuspendLayout();
            try
            {
                var enabled = CreateBoundCheck(nameof(Settings.MarketAlertsEnabled), false);
                var defer = CreateBoundCheck(nameof(Settings.MarketAlertDeferWhenFullscreen), true);
                MarketAlertGeneralSettingsLayoutFactory.AddSectionRow(_generalLeftColumn, "总开关", "开启后才会检查已启用规则。", enabled);
                MarketAlertGeneralSettingsLayoutFactory.AddSectionRow(_generalLeftColumn, "全屏/游戏时延迟汇总", "避免游戏中频繁弹窗打扰。", defer);
            }
            finally
            {
                _generalLeftColumn.ResumeLayout(false);
            }
        }

        private void BuildGeneralNotificationModeRow()
        {
            if (_generalRightColumn == null)
                return;

            _generalRightColumn.SuspendLayout();
            try
            {
                var mode = CreateNotificationModeSelector();
                MarketAlertGeneralSettingsLayoutFactory.AddSectionRow(_generalRightColumn, "提醒方式", "桌面弹窗更醒目，托盘气泡更轻。", mode);
            }
            finally
            {
                _generalRightColumn.ResumeLayout(false);
            }
        }

        private void BuildGeneralWindowRow()
        {
            if (_generalRightColumn == null)
                return;

            _generalRightColumn.SuspendLayout();
            try
            {
                var window = CreateBoundInt(nameof(Settings.MarketAlertDefaultWindowMinutes), 10, "分钟", 84,
                    value => Math.Clamp(value, 1, 1440), UpdateDefaultWindow);
                MarketAlertGeneralSettingsLayoutFactory.AddSectionRow(_generalRightColumn, "涨跌幅统计时间", "越短越敏感。", window);
            }
            finally
            {
                _generalRightColumn.ResumeLayout(false);
            }
        }

        private void BuildGeneralCooldownRow()
        {
            if (_generalRightColumn == null)
                return;

            _generalRightColumn.SuspendLayout();
            try
            {
                var cooldown = CreateBoundInt(nameof(Settings.MarketAlertDefaultCooldownMinutes), 5, "分钟", 84,
                    value => Math.Clamp(value, 1, 1440), UpdateDefaultCooldown);
                MarketAlertGeneralSettingsLayoutFactory.AddSectionRow(_generalRightColumn, "同一规则冷却时间", "越长越少重复提醒。", cooldown);
            }
            finally
            {
                _generalRightColumn.ResumeLayout(false);
            }
        }

        private LiteCheck CreateBoundCheck(string settingKey, bool fallback)
        {
            var check = new LiteCheck(Get(settingKey, fallback), "启用");
            check.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(settingKey, check.Checked);
            };
            RegisterRefresh(() => check.Checked = Get(settingKey, fallback));
            RegisterSave(() => Set(settingKey, check.Checked));
            return check;
        }

        private Control CreateNotificationModeSelector()
        {
            var selector = new RedesignSegmentedControl("托盘气泡", "桌面弹窗")
            {
                Width = UIUtils.S(214),
                Height = UIUtils.S(32)
            };
            selector.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;
                Set(nameof(Settings.MarketAlertNotificationMode), selector.SelectedIndex == 0
                    ? MarketAlertNotificationMode.TrayBalloon
                    : MarketAlertNotificationMode.DesktopToast);
            };
            RegisterRefresh(() =>
            {
                var mode = Get(nameof(Settings.MarketAlertNotificationMode), MarketAlertNotificationMode.DesktopToast);
                if (mode == MarketAlertNotificationMode.InAppToast)
                    mode = MarketAlertNotificationMode.DesktopToast;
                selector.SelectedIndex = mode == MarketAlertNotificationMode.TrayBalloon ? 0 : 1;
            });
            RegisterSave(() =>
            {
                Set(nameof(Settings.MarketAlertNotificationMode), selector.SelectedIndex == 0
                    ? MarketAlertNotificationMode.TrayBalloon
                    : MarketAlertNotificationMode.DesktopToast);
            });
            return selector;
        }

        private LiteNumberInput CreateBoundInt(
            string settingKey,
            int fallback,
            string unit,
            int width,
            Func<int, int> normalize,
            Action<int> afterChanged)
        {
            var input = new LiteNumberInput(normalize(Get(settingKey, fallback)).ToString(), unit, "", width)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                {
                    int normalized = normalize(value);
                    Set(settingKey, normalized);
                    afterChanged(normalized);
                }
            };
            RegisterRefresh(() => input.Inner.Text = normalize(Get(settingKey, fallback)).ToString());
            RegisterSave(() =>
            {
                if (int.TryParse(input.Inner.Text, out int value))
                    Set(settingKey, normalize(value));
            });
            return input;
        }

        private void CreateManualTestCard()
        {
            var group = new LiteSettingsGroup("手动测试");
            AddHint(group, "低频操作：只在调整提醒方式或排查弹窗时使用。");
            MarketAlertTestCard card = MarketAlertTestCardFactory.Create(SendTestAlert);
            _testAlertRow = card.Row;
            _testStatus = card.StatusLabel;
            group.AddFullItem(card.Row);
            AddGroupToPage(group);
        }

        private void ApplyMarketAlertTheme()
        {
            if (IsDisposed)
                return;

            BackColor = UIColors.MainBg;
            Container.BackColor = UIColors.MainBg;
            if (_testAlertRow != null && !_testAlertRow.IsDisposed)
                _testAlertRow.BackColor = MarketAlertTestCardModel.GetRowBackColor();

            ClearLabelBackColors(Container);
            Container.Invalidate(true);
        }

        private void QueueSettledThemeRefresh()
        {
            if (_themeRefreshQueued || IsDisposed || !IsHandleCreated)
                return;

            _themeRefreshQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _themeRefreshQueued = false;
                    ApplyMarketAlertTheme();
                }));
            }
            catch (InvalidOperationException)
            {
                _themeRefreshQueued = false;
            }
        }

        private static void ClearLabelBackColors(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label && label.BackColor != Color.Transparent)
                    label.BackColor = Color.Transparent;

                if (child.HasChildren)
                    ClearLabelBackColors(child);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _deferredRuleRowsTimer?.Stop();
                _deferredRuleRowsTimer?.Dispose();
                _deferredRuleRowsTimer = null;
                _deferredGeneralSettingsTimer?.Stop();
                _deferredGeneralSettingsTimer?.Dispose();
                _deferredGeneralSettingsTimer = null;
            }

            base.Dispose(disposing);
        }

        private void CreateSourceRuleCard(string title, string sourceId, bool deferRows = false)
        {
            var group = new LiteSettingsGroup(title);
            _sourceRuleGroups[sourceId] = group;
            if (!deferRows)
                BuildSourceRuleRows(sourceId);
            AddGroupToPage(group);
        }

        private void QueueDeferredRuleRowsBuild()
        {
            if (_builtSourceRuleRows.Contains(MarketDataSourceManager.SteamDtId)
                && _builtSourceRuleRows.Contains(MarketDataSourceManager.QaqId))
                return;

            if (IsDisposed)
                return;

            if (!IsHandleCreated)
                return;

            EnqueueSourceRuleRows(MarketDataSourceManager.SteamDtId);
            EnqueueSourceRuleRows(MarketDataSourceManager.QaqId);
            if (_pendingRuleRows.Count == 0)
                return;

            _deferredRuleRowsTimer ??= CreateDeferredRuleRowsTimer();
            _deferredRuleRowsTimer.Stop();
            _deferredRuleRowsTimer.Interval = 16;
            _deferredRuleRowsTimer.Start();
        }

        private System.Windows.Forms.Timer CreateDeferredRuleRowsTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 16 };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                BuildDeferredRuleRows();
            };
            return timer;
        }

        private void BuildDeferredRuleRows()
        {
            if (_pendingRuleRows.Count == 0 || IsDisposed || Disposing)
                return;

            RuleRowSpec spec = _pendingRuleRows.Dequeue();
            using (UiJankProfiler.Measure("MarketAlert.BuildDeferredRuleRow", $"{spec.SourceId}:{spec.RuleType}", thresholdMs: 1))
            {
                if (_sourceRuleGroups.TryGetValue(spec.SourceId, out LiteSettingsGroup? group))
                {
                    group.SuspendLayout();
                    try
                    {
                        group.AddItem(CreateSourceRuleRow(spec));
                    }
                    finally
                    {
                        group.ResumeLayout(false);
                    }

                    RequestRelayoutGroups();
                    if (!_pendingRuleRows.Any(row => string.Equals(row.SourceId, spec.SourceId, StringComparison.OrdinalIgnoreCase)))
                        _builtSourceRuleRows.Add(spec.SourceId);
                }

                if (_pendingRuleRows.Count == 0)
                    RequestRelayoutGroups();
            }

            if (_pendingRuleRows.Count > 0 && _deferredRuleRowsTimer != null && !IsDisposed && !Disposing)
            {
                _deferredRuleRowsTimer.Interval = 16;
                _deferredRuleRowsTimer.Start();
            }
        }

        private void EnqueueSourceRuleRows(string sourceId)
        {
            if (_builtSourceRuleRows.Contains(sourceId) || _pendingRuleRows.Any(row => string.Equals(row.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)))
                return;

            foreach (RuleRowSpec spec in MarketAlertPageModel.CreateSourceRuleSpecs(sourceId))
                _pendingRuleRows.Enqueue(spec);
        }

        private void BuildSourceRuleRows(string sourceId)
        {
            if (_builtSourceRuleRows.Contains(sourceId))
                return;
            if (!_sourceRuleGroups.TryGetValue(sourceId, out LiteSettingsGroup? group))
                return;

            group.SuspendLayout();
            try
            {
                foreach (RuleRowSpec spec in MarketAlertPageModel.CreateSourceRuleSpecs(sourceId))
                    group.AddItem(CreateSourceRuleRow(spec));
            }
            finally
            {
                group.ResumeLayout(false);
            }
            _builtSourceRuleRows.Add(sourceId);
        }

        private Control CreateSourceRuleRow(RuleRowSpec spec)
        {
            return MarketAlertSourceRuleRowFactory.Create(
                spec.SourceId,
                spec.RuleType,
                spec.Title,
                spec.Unit,
                spec.Placeholder,
                spec.Hint,
                new MarketAlertSourceRuleRowCallbacks(
                    () => IsUpdatingControls,
                    EnsureBuiltinRule,
                    FindBuiltinRule,
                    SaveRules,
                    RefreshAdvancedRules,
                    RegisterRefresh,
                    RunWithUpdateGuard));
        }

        private void AddNotificationMode(LiteSettingsGroup group)
        {
            var combo = new LiteComboBox { Width = UIUtils.S(170) };
            combo.AddItem("托盘气泡", ((int)MarketAlertNotificationMode.TrayBalloon).ToString());
            combo.AddItem("桌面弹窗", ((int)MarketAlertNotificationMode.DesktopToast).ToString());
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
            group.AddItem(new LiteSettingsItem("提醒方式", combo));
        }

        private void UpdateDefaultWindow(int value)
        {
            value = Math.Clamp(value, 1, 1440);
            Set(nameof(Settings.MarketAlertDefaultWindowMinutes), value);
            EnsureBuiltinRules();
            foreach (var rule in Rules.Where(MarketAlertPageModel.IsBuiltinRule))
            {
                if (rule.RuleType == MarketAlertRuleType.RiseByPercent || rule.RuleType == MarketAlertRuleType.FallByPercent)
                    rule.WindowMinutes = value;
            }
            SaveRules();
            RefreshAdvancedRules();
        }

        private void UpdateDefaultCooldown(int value)
        {
            value = Math.Clamp(value, 1, 1440);
            Set(nameof(Settings.MarketAlertDefaultCooldownMinutes), value);
            EnsureBuiltinRules();
            foreach (var rule in Rules.Where(MarketAlertPageModel.IsBuiltinRule))
                rule.CooldownMinutes = value;
            SaveRules();
            RefreshAdvancedRules();
        }

        private void SendTestAlert()
        {
            if (Config == null || UI == null)
                return;
            bool delivered = UI.ShowMarketAlertNotification(Config, "大盘预警测试", "这是一条测试提醒，用于确认当前弹窗方式是否符合预期。", ToolTipIcon.Info);
            MarketAlertPagePresenter.ApplyLabel(_testStatus, MarketAlertPagePresenter.BuildTestResultStatus(delivered));
        }

        private void CreateAdvancedCard()
        {
            _advancedGroup = new LiteSettingsGroup("大盘预警 - 高级规则");
            AddHint(_advancedGroup, "高级模式下可编辑完整规则列表；普通用户建议使用上方内置规则。");
            var addButton = new LiteButton("新增规则", true) { Width = UIUtils.S(92), Height = UIUtils.S(28) };
            addButton.Click += (_, __) => AddCustomRule();
            _advancedGroup.AddHeaderAction(addButton);
            _advancedList = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            _advancedGroup.AddFullItem(_advancedList);
            RegisterRefresh(RefreshAdvancedRules);
            _advancedWrapper = AddGroupToPage(_advancedGroup);
            Container.Controls.Remove(_advancedWrapper);
        }

        private void AddCustomRule()
        {
            Rules.Add(new MarketAlertRule
            {
                Name = "自定义预警",
                SourceId = MarketDataSourceManager.QaqId,
                RuleType = MarketAlertRuleType.CrossAbove,
                Threshold = 0,
                Enabled = false,
                WindowMinutes = Math.Max(1, Get(nameof(Settings.MarketAlertDefaultWindowMinutes), 10)),
                CooldownMinutes = Math.Max(1, Get(nameof(Settings.MarketAlertDefaultCooldownMinutes), 5))
            });
            SaveRules();
            RefreshAdvancedRules();
        }

        private void RefreshAdvancedRules()
        {
            if (_advancedGroup == null || _advancedList == null)
                return;
            bool showAdvanced = Get(nameof(Settings.AdvancedMode), false);
            if (_advancedWrapper != null)
            {
                if (showAdvanced && _advancedWrapper.Parent == null)
                {
                    Container.Controls.Add(_advancedWrapper);
                    Container.Controls.SetChildIndex(_advancedWrapper, 0);
                }
                else if (!showAdvanced && _advancedWrapper.Parent != null)
                {
                    Container.Controls.Remove(_advancedWrapper);
                }
                _advancedWrapper.Visible = showAdvanced;
            }
            _advancedGroup.Visible = showAdvanced;
            _advancedList.Controls.Clear();
            if (!showAdvanced)
                return;
            _advancedList.Controls.Add(CreateAdvancedHeader());
            foreach (var rule in Rules.ToList())
                _advancedList.Controls.Add(CreateAdvancedRuleRow(rule));
        }

        private Control CreateAdvancedHeader()
        {
            return new Label
            {
                Text = "启用    名称              数据源      条件          阈值      窗口      冷却",
                AutoSize = false,
                Width = UIUtils.S(760),
                Height = UIUtils.S(24),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Control CreateAdvancedRuleRow(MarketAlertRule rule)
        {
            return MarketAlertAdvancedRuleRowFactory.Create(
                rule,
                new MarketAlertAdvancedRuleRowCallbacks(
                    SaveRules,
                    deletedRule =>
                    {
                        Rules.Remove(deletedRule);
                        SaveRules();
                        RefreshAdvancedRules();
                    }));
        }

        private void EnsureBuiltinRules()
        {
            if (MarketAlertPageModel.EnsureBuiltinRules(Rules, Settings.CreateDefaultMarketAlertRules()))
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

        private List<MarketAlertRule> Rules => GetList<MarketAlertRule>(nameof(Settings.MarketAlertRules));

    }
}
