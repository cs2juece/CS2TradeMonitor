using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class YouPinAutoQuotePage : FrameworkSettingsPageBase
    {
        private readonly ISteamOfferService _steamOffers;
        private readonly Action _persistSettings;
        private LiteCheck? _purchaseCheck;
        private LiteCheck? _saleCheck;
        private LiteCheck? _rentalCheck;
        private LiteButton? _logButton;
        private Label? _statusValueLabel;
        private Label? _lastProcessValueLabel;
        private Label? _todayValueLabel;
        private Label? _todayFailureValueLabel;
        private YouPinCcRoundedPanel? _recordsFrame;
        private TableLayoutPanel? _recordsTable;
        private string? _lastRecordsSignature;

        public YouPinAutoQuotePage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinAutoQuotePage(YouPinPageRuntimeServices runtimeServices)
            : this(runtimeServices?.SteamOffers ?? throw new ArgumentNullException(nameof(runtimeServices)))
        {
        }

        internal YouPinAutoQuotePage(ISteamOfferService steamOffers)
            : this(steamOffers, null)
        {
        }

        internal YouPinAutoQuotePage(ISteamOfferService steamOffers, Action? persistSettings)
        {
            _steamOffers = steamOffers ?? throw new ArgumentNullException(nameof(steamOffers));
            _persistSettings = persistSettings ?? SaveSettingsStoreToDisk;
            _steamOffers.DataUpdated += OnSteamOffersDataUpdated;
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            RunWithUpdateGuard(RefreshControlsFromStore);
            RefreshStatusFromServices();
        }

        public override void ApplySystemTheme()
        {
            _lastRecordsSignature = null;
            base.ApplySystemTheme();
            RefreshStatusFromServices();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _steamOffers.DataUpdated -= OnSteamOffersDataUpdated;

            base.Dispose(disposing);
        }

        private void BuildPage()
        {
            ClearPage();
            Padding pagePadding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding();
            pagePadding.Top = 0;
            Container.Padding = pagePadding;

            var card = new YouPinCcRoundedPanel
            {
                Height = UIUtils.S(600),
                Padding = UIUtils.S(new Padding(28, 26, 28, 24))
            };

            var title = YouPinCcUi.Label("悠悠自动报价", 11F, FontStyle.Bold);
            var hint = YouPinCcUi.Label("按悠悠订单类型自动处理；Steam 手机确认作为后续独立阶段记录。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var statusPanel = new YouPinCcRoundedPanel { FillOverride = UIColors.CardBg, Radius = UIUtils.S(6) };
            var receivePanel = new YouPinCcRoundedPanel { FillOverride = UIColors.CardBg, Radius = UIUtils.S(6) };
            var sendPanel = new YouPinCcRoundedPanel { FillOverride = UIColors.CardBg, Radius = UIUtils.S(6) };
            var purchaseOptionPanel = CreateRuleOptionPanel();
            var saleOptionPanel = CreateRuleOptionPanel();
            var rentalOptionPanel = CreateRuleOptionPanel();
            var purchaseAccent = CreateRuleAccent();
            var saleAccent = CreateRuleAccent();
            var rentalAccent = CreateRuleAccent();
            var statusLabel = YouPinCcUi.Label("状态", 9F, FontStyle.Regular, UIColors.TextSub);
            var lastLabel = YouPinCcUi.Label("上次处理", 9F, FontStyle.Regular, UIColors.TextSub);
            var successLabel = YouPinCcUi.Label("今日成功", 9F, FontStyle.Regular, UIColors.TextSub);
            var failureLabel = YouPinCcUi.Label("今日失败", 9F, FontStyle.Regular, UIColors.TextSub);
            var receiveDivider = CreateSectionDivider();
            var sendDivider = CreateSectionDivider();
            var receiveTitle = YouPinCcUi.Label("自动接收报价", 9.5F, FontStyle.Bold);
            var sendTitle = YouPinCcUi.Label("自动发送报价", 9.5F, FontStyle.Bold);
            var purchaseDescription = YouPinCcUi.Label("匹配悠悠购买订单后自动接收收货报价。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var saleDescription = YouPinCcUi.Label("匹配悠悠出售订单后自动发送报价。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var rentalDescription = YouPinCcUi.Label("匹配悠悠出租订单后自动发送租赁报价。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var recordsTitle = YouPinCcUi.Label("自动处理记录", 9.5F, FontStyle.Bold);
            var recordsHint = YouPinCcUi.Label("默认显示最新 5 条，完整记录请打开日志查看。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var statusSep1 = new Panel { Width = 1, BackColor = UIColors.Border };
            var statusSep2 = new Panel { Width = 1, BackColor = UIColors.Border };
            var statusSep3 = new Panel { Width = 1, BackColor = UIColors.Border };

            _statusValueLabel = new AutoQuoteBadgeLabel
            {
                Text = "未启用",
                Font = UIFonts.Bold(8.5F),
                Tone = AutoQuoteBadgeTone.Subtle
            };
            _lastProcessValueLabel = YouPinCcUi.Label("--", 9F);
            _todayValueLabel = YouPinCcUi.Label("0", 9F);
            _todayFailureValueLabel = YouPinCcUi.Label("0", 9F);

            _purchaseCheck = CreateAutoCheck("悠悠购买自动处理");
            _saleCheck = CreateAutoCheck("悠悠出售自动处理");
            _rentalCheck = CreateAutoCheck("悠悠出租自动处理");
            _logButton = new LiteButton("打开日志", false) { Width = UIUtils.S(100), Height = UIUtils.S(34) };
            _logButton.Click += (_, __) => OpenLogFile();

            _recordsFrame = new YouPinCcRoundedPanel { FillOverride = UIColors.CardBg, Radius = UIUtils.S(6) };
            _recordsTable = new TableLayoutPanel
            {
                BackColor = UIColors.CardBg,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                ColumnCount = 6,
                RowCount = 1,
                Margin = Padding.Empty
            };
            _recordsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(110)));
            _recordsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(130)));
            _recordsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(78)));
            _recordsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _recordsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(160)));
            _recordsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(100)));

            card.Controls.Add(title);
            card.Controls.Add(hint);
            card.Controls.Add(_logButton);
            card.Controls.Add(statusPanel);
            card.Controls.Add(receivePanel);
            card.Controls.Add(sendPanel);
            card.Controls.Add(recordsTitle);
            card.Controls.Add(recordsHint);
            card.Controls.Add(_recordsFrame);

            statusPanel.Controls.Add(statusLabel);
            statusPanel.Controls.Add(_statusValueLabel);
            statusPanel.Controls.Add(lastLabel);
            statusPanel.Controls.Add(_lastProcessValueLabel);
            statusPanel.Controls.Add(successLabel);
            statusPanel.Controls.Add(failureLabel);
            statusPanel.Controls.Add(_todayValueLabel);
            statusPanel.Controls.Add(_todayFailureValueLabel);
            statusPanel.Controls.Add(statusSep1);
            statusPanel.Controls.Add(statusSep2);
            statusPanel.Controls.Add(statusSep3);

            receivePanel.Controls.Add(receiveTitle);
            receivePanel.Controls.Add(receiveDivider);
            receivePanel.Controls.Add(purchaseOptionPanel);
            purchaseOptionPanel.Controls.Add(purchaseAccent);
            purchaseOptionPanel.Controls.Add(_purchaseCheck);
            purchaseOptionPanel.Controls.Add(purchaseDescription);

            sendPanel.Controls.Add(sendTitle);
            sendPanel.Controls.Add(sendDivider);
            sendPanel.Controls.Add(saleOptionPanel);
            sendPanel.Controls.Add(rentalOptionPanel);
            saleOptionPanel.Controls.Add(saleAccent);
            saleOptionPanel.Controls.Add(_saleCheck);
            saleOptionPanel.Controls.Add(saleDescription);
            rentalOptionPanel.Controls.Add(rentalAccent);
            rentalOptionPanel.Controls.Add(_rentalCheck);
            rentalOptionPanel.Controls.Add(rentalDescription);

            _recordsFrame.Controls.Add(_recordsTable);

            statusPanel.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int contentW = Math.Max(1, statusPanel.Width - pad * 2);
                int colW = Math.Max(UIUtils.S(150), contentW / 4);
                int innerGap = UIUtils.S(18);
                int top = UIUtils.S(16);
                LayoutInlineStatus(statusLabel, _statusValueLabel, pad, top, colW, valueWidth: UIUtils.S(66));
                statusSep1.SetBounds(pad + colW, UIUtils.S(14), 1, UIUtils.S(30));
                LayoutInlineStatus(lastLabel, _lastProcessValueLabel, pad + colW + innerGap, top, colW - innerGap);
                statusSep2.SetBounds(pad + colW * 2, UIUtils.S(14), 1, UIUtils.S(30));
                LayoutInlineStatus(successLabel, _todayValueLabel, pad + colW * 2 + innerGap, top, colW - innerGap);
                statusSep3.SetBounds(pad + colW * 3, UIUtils.S(14), 1, UIUtils.S(30));
                LayoutInlineStatus(failureLabel, _todayFailureValueLabel, pad + colW * 3 + innerGap, top, colW - innerGap);
            };

            receivePanel.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int textW = Math.Max(1, receivePanel.Width - pad * 2);
                receiveTitle.SetBounds(pad, UIUtils.S(18), textW, UIUtils.S(26));
                receiveDivider.SetBounds(pad, UIUtils.S(50), textW, 1);
                purchaseOptionPanel.SetBounds(pad, UIUtils.S(62), textW, UIUtils.S(50));
                LayoutRuleOption(purchaseOptionPanel, purchaseAccent, _purchaseCheck, purchaseDescription);
            };

            sendPanel.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int textW = Math.Max(1, sendPanel.Width - pad * 2);
                sendTitle.SetBounds(pad, UIUtils.S(18), textW, UIUtils.S(26));
                sendDivider.SetBounds(pad, UIUtils.S(50), textW, 1);
                saleOptionPanel.SetBounds(pad, UIUtils.S(62), textW, UIUtils.S(50));
                rentalOptionPanel.SetBounds(pad, UIUtils.S(116), textW, UIUtils.S(50));
                LayoutRuleOption(saleOptionPanel, saleAccent, _saleCheck, saleDescription);
                LayoutRuleOption(rentalOptionPanel, rentalAccent, _rentalCheck, rentalDescription);
            };

            card.Layout += (_, __) =>
            {
                int padL = UIUtils.S(28);
                int padR = UIUtils.S(28);
                int right = card.Width - padR;
                int contentW = Math.Max(1, right - padL);

                title.SetBounds(padL, UIUtils.S(25), UIUtils.S(220), UIUtils.S(30));
                _logButton?.SetBounds(right - UIUtils.S(100), UIUtils.S(24), UIUtils.S(100), UIUtils.S(34));
                hint.SetBounds(padL, UIUtils.S(58), Math.Max(1, contentW - UIUtils.S(120)), UIUtils.S(24));

                statusPanel.SetBounds(padL, UIUtils.S(90), contentW, UIUtils.S(46));
                int groupGap = UIUtils.S(16);
                int groupTop = UIUtils.S(144);
                int groupHeight = UIUtils.S(176);
                int groupWidth = Math.Max(1, (contentW - groupGap) / 2);
                receivePanel.SetBounds(padL, groupTop, groupWidth, groupHeight);
                sendPanel.SetBounds(receivePanel.Right + groupGap, groupTop, groupWidth, groupHeight);
                recordsTitle.SetBounds(padL, receivePanel.Bottom + UIUtils.S(20), UIUtils.S(160), UIUtils.S(26));
                recordsHint.SetBounds(recordsTitle.Right + UIUtils.S(20), receivePanel.Bottom + UIUtils.S(22), Math.Max(1, right - recordsTitle.Right - UIUtils.S(20)), UIUtils.S(24));
                int recordsTop = recordsTitle.Bottom + UIUtils.S(8);
                _recordsFrame?.SetBounds(padL, recordsTop, contentW, Math.Max(UIUtils.S(138), card.Height - recordsTop - UIUtils.S(22)));
                _recordsTable.SetBounds(UIUtils.S(16), UIUtils.S(1), Math.Max(1, (_recordsFrame?.Width ?? contentW) - UIUtils.S(32)), Math.Max(1, (_recordsFrame?.Height ?? UIUtils.S(138)) - UIUtils.S(2)));
            };

            RegisterRefresh(RefreshControlsFromStore);
            RegisterSave(CaptureControlsToStore);
            YouPinCcUi.AddTopCard(Container, card);
            RenderRecords(Array.Empty<SteamAutoTradeRecordRow>());
        }

        private LiteCheck CreateAutoCheck(string text)
        {
            var check = new LiteCheck(false, text) { Width = UIUtils.S(240), Height = UIUtils.S(24) };
            check.CheckedChanged += (_, __) => CaptureControlsToStore();
            return check;
        }

        private static YouPinCcRoundedPanel CreateRuleOptionPanel()
        {
            return new YouPinCcRoundedPanel
            {
                FillOverride = UIColors.IsDark ? Color.FromArgb(22, 33, 43) : UIColors.CardBg,
                Radius = UIUtils.S(5)
            };
        }

        private static Panel CreateRuleAccent()
        {
            return new Panel
            {
                BackColor = UIColors.Primary,
                Width = UIUtils.S(3)
            };
        }

        private static Panel CreateSectionDivider()
        {
            return new Panel
            {
                Height = 1,
                BackColor = UIColors.Border
            };
        }

        private static void LayoutRuleOption(Control host, Control accent, LiteCheck? check, Label description)
        {
            accent.SetBounds(0, 0, UIUtils.S(3), host.Height);
            check?.SetBounds(UIUtils.S(16), UIUtils.S(5), Math.Max(1, host.Width - UIUtils.S(32)), UIUtils.S(24));
            description.SetBounds(UIUtils.S(44), UIUtils.S(27), Math.Max(1, host.Width - UIUtils.S(56)), UIUtils.S(20));
        }

        private static void LayoutInlineStatus(Label label, Label? value, int left, int top, int width, int? valueWidth = null)
        {
            int measuredLabelWidth = TextRenderer.MeasureText(label.Text, label.Font).Width + UIUtils.S(2);
            int maxLabelWidth = Math.Max(UIUtils.S(48), width - UIUtils.S(54));
            int labelWidth = Math.Clamp(measuredLabelWidth, UIUtils.S(42), maxLabelWidth);
            label.SetBounds(left, top, labelWidth, UIUtils.S(24));
            int resolvedValueWidth = valueWidth ?? Math.Max(1, width - labelWidth - UIUtils.S(8));
            value?.SetBounds(label.Right + UIUtils.S(4), top, Math.Min(resolvedValueWidth, Math.Max(1, width - labelWidth - UIUtils.S(8))), UIUtils.S(24));
        }

        private void RefreshControlsFromStore()
        {
            var settings = ReadAutoTradeSettings();
            if (_purchaseCheck != null)
                _purchaseCheck.Checked = settings.AcceptYouPinPurchaseEnabled;
            if (_saleCheck != null)
                _saleCheck.Checked = settings.SendYouPinSaleEnabled;
            if (_rentalCheck != null)
                _rentalCheck.Checked = settings.SendYouPinRentalEnabled;
        }

        private void CaptureControlsToStore()
        {
            if (IsUpdatingControls)
                return;

            SteamAutoTradeSettings current = ReadAutoTradeSettings();
            SteamAutoTradeSettings settings = BuildSettingsFromControls(current);
            if (SettingsMatch(current, settings))
                return;

            ApplySettingsToStore(settings);
            _persistSettings();
            if (settings.Enabled)
                _steamOffers.StartAutoTrade(settings);
            else
                _steamOffers.StopAutoConfirm();
            RefreshStatusFromServices();
        }

        private SteamAutoTradeSettings BuildSettingsFromControls()
        {
            return BuildSettingsFromControls(ReadAutoTradeSettings());
        }

        private SteamAutoTradeSettings BuildSettingsFromControls(SteamAutoTradeSettings current)
        {
            return SteamAutoTradeProjection.BuildYouPinSettings(
                current,
                _purchaseCheck?.Checked == true,
                _saleCheck?.Checked == true,
                _rentalCheck?.Checked == true);
        }

        private static bool SettingsMatch(SteamAutoTradeSettings left, SteamAutoTradeSettings right)
        {
            return left.Enabled == right.Enabled
                && left.AcceptPureIncomingEnabled == right.AcceptPureIncomingEnabled
                && left.AcceptYouPinPurchaseEnabled == right.AcceptYouPinPurchaseEnabled
                && left.SendYouPinSaleEnabled == right.SendYouPinSaleEnabled
                && left.SendYouPinRentalEnabled == right.SendYouPinRentalEnabled
                && left.IntervalSeconds == right.IntervalSeconds;
        }

        private SteamAutoTradeSettings ReadAutoTradeSettings()
        {
            return Config == null
                ? SteamAutoTradeSettingsPersistence.ReadFrom(Settings.Load())
                : SteamAutoTradeSettingsPersistence.ReadFrom(Config);
        }

        private void ApplySettingsToStore(SteamAutoTradeSettings settings)
        {
            Set(nameof(Settings.SteamAutoTradeAcceptPureIncomingEnabled), settings.AcceptPureIncomingEnabled);
            Set(nameof(Settings.SteamAutoTradeAcceptYouPinPurchaseEnabled), settings.AcceptYouPinPurchaseEnabled);
            Set(nameof(Settings.SteamAutoTradeSendYouPinSaleEnabled), settings.SendYouPinSaleEnabled);
            Set(nameof(Settings.SteamAutoTradeSendYouPinRentalEnabled), settings.SendYouPinRentalEnabled);
            Set(nameof(Settings.SteamAutoTradeEnabled), settings.Enabled);
            Set(nameof(Settings.SteamAutoTradeIntervalSeconds), settings.IntervalSeconds);
        }

        private void RefreshStatusFromServices()
        {
            if (IsDisposed || Disposing || _statusValueLabel == null)
                return;

            SteamAutoTradeState autoTrade = _steamOffers.GetState().AutoTrade;
            var settings = BuildSettingsFromControls();
            bool youPinEnabled = settings.AcceptYouPinPurchaseEnabled
                || settings.SendYouPinSaleEnabled
                || settings.SendYouPinRentalEnabled;

            _statusValueLabel.Text = youPinEnabled ? autoTrade.StatusText : "未启用";
            _statusValueLabel.ForeColor = youPinEnabled
                ? (autoTrade.StatusText.Contains("失败", StringComparison.Ordinal) ? UIColors.TextWarn : UIColors.Positive)
                : UIColors.TextSub;
            if (_statusValueLabel is AutoQuoteBadgeLabel statusBadge)
            {
                statusBadge.Tone = !youPinEnabled
                    ? AutoQuoteBadgeTone.Subtle
                    : autoTrade.StatusText.Contains("失败", StringComparison.Ordinal)
                        ? AutoQuoteBadgeTone.Warn
                        : AutoQuoteBadgeTone.Success;
            }
            if (_lastProcessValueLabel != null)
                _lastProcessValueLabel.Text = FormatTime(autoTrade.LastProcessTime);
            if (_todayValueLabel != null)
                _todayValueLabel.Text = autoTrade.TodaySuccess.ToString();
            if (_todayFailureValueLabel != null)
                _todayFailureValueLabel.Text = autoTrade.TodayFailure.ToString();

            RenderRecords(SteamAutoTradeProjection.BuildRecordRows(
                autoTrade.RecentRecords,
                SteamAutoTradeProjectionView.YouPinAutoQuote));
        }

        private void RenderRecords(System.Collections.Generic.IReadOnlyCollection<SteamAutoTradeRecordRow> records)
        {
            if (IsDisposed || Disposing || _recordsTable == null || _recordsTable.IsDisposed)
                return;

            string signature = BuildRecordSignature(records);
            if (string.Equals(_lastRecordsSignature, signature, StringComparison.Ordinal))
                return;

            _lastRecordsSignature = signature;

            _recordsTable.SuspendLayout();
            try
            {
                DisposeRecordTableCells(_recordsTable);
                _recordsTable.RowStyles.Clear();
                _recordsTable.RowCount = 6;
                AddRecordCell("时间", 0, 0, UIColors.TextSub, bold: true);
                AddRecordCell("类型", 1, 0, UIColors.TextSub, bold: true);
                AddRecordCell("方向", 2, 0, UIColors.TextSub, bold: true);
                AddRecordCell("饰品", 3, 0, UIColors.TextSub, bold: true);
                AddRecordCell("来源", 4, 0, UIColors.TextSub, bold: true);
                AddRecordCell("结果", 5, 0, UIColors.TextSub, bold: true);

                if (records.Count == 0)
                {
                    var empty = CreateRecordCell("暂无自动处理记录", UIColors.TextSub);
                    empty.TextAlign = ContentAlignment.MiddleCenter;
                    _recordsTable.Controls.Add(empty, 0, 1);
                    _recordsTable.SetColumnSpan(empty, 6);
                }
                else
                {
                    int row = 1;
                    foreach (SteamAutoTradeRecordRow record in records)
                    {
                        AddRecordCell(record.TimeText, 0, row);
                        AddRecordCell(record.TypeText, 1, row, GetRecordTypeColor(record));
                        AddRecordCell(record.DirectionText, 2, row, GetDirectionColor(record));
                        AddRecordCell(record.ItemsText, 3, row);
                        AddRecordCell(record.SourceText, 4, row);
                        AddRecordCell(record.ResultText, 5, row, GetRecordColor(record), badge: true);
                        row++;
                    }
                }

                for (int i = 0; i < _recordsTable.RowCount; i++)
                {
                    if (i > records.Count && records.Count > 0)
                    {
                        for (int column = 0; column < 6; column++)
                            AddRecordCell("", column, i, UIColors.TextSub);
                    }

                    _recordsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(32)));
                }
            }
            finally
            {
                if (!_recordsTable.IsDisposed)
                    _recordsTable.ResumeLayout(true);
            }
        }

        private static string BuildRecordSignature(
            System.Collections.Generic.IReadOnlyCollection<SteamAutoTradeRecordRow> records)
        {
            return string.Join("\u001f", records.Select(record => string.Join(
                "\u001e",
                record.Time.Ticks,
                record.Type,
                record.Direction,
                record.TimeText,
                record.TypeText,
                record.DirectionText,
                record.ItemsText,
                record.SourceText,
                record.ResultText,
                record.ResultTone)));
        }

        internal static void DisposeRecordTableCells(TableLayoutPanel table)
        {
            ArgumentNullException.ThrowIfNull(table);
            while (table.Controls.Count > 0)
            {
                Control control = table.Controls[0];
                table.Controls.RemoveAt(0);
                control.Dispose();
            }
        }

        private void AddRecordCell(string text, int column, int row, Color? color = null, bool bold = false, bool badge = false)
        {
            if (_recordsTable == null || _recordsTable.IsDisposed)
                return;

            var label = CreateRecordCell(text, color, bold, badge);
            label.BackColor = row == 0
                ? (UIColors.IsDark ? Color.FromArgb(20, 30, 40) : UIColors.ControlBg)
                : row % 2 == 0 && !string.IsNullOrEmpty(text)
                    ? (UIColors.IsDark ? Color.FromArgb(18, 28, 38) : Color.FromArgb(248, 250, 253))
                    : UIColors.CardBg;
            _recordsTable.Controls.Add(label, column, row);
        }

        private static AutoQuoteRecordCellLabel CreateRecordCell(string text, Color? color = null, bool bold = false, bool badge = false)
        {
            return new AutoQuoteRecordCellLabel
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = UIUtils.S(new Padding(10, 0, 8, 0)),
                Font = bold ? UIFonts.Bold(8.8F) : UIFonts.Regular(8.8F),
                ForeColor = color ?? UIColors.TextMain,
                UseBadge = badge && !string.IsNullOrWhiteSpace(text),
                BadgeTone = ResolveResultBadgeTone(text)
            };
        }

        private static Color GetRecordColor(SteamAutoTradeRecordRow record)
        {
            return record.ResultTone switch
            {
                SteamAutoTradeResultTone.Failure => UIColors.TextCrit,
                SteamAutoTradeResultTone.Warning => UIColors.TextWarn,
                SteamAutoTradeResultTone.Success => UIColors.Positive,
                _ => UIColors.TextSub
            };
        }

        private static Color GetRecordTypeColor(SteamAutoTradeRecordRow record)
        {
            return record.Type is SteamAutoTradeRecordType.Failed or SteamAutoTradeRecordType.TerminalFailure
                ? UIColors.TextCrit
                : UIColors.TextMain;
        }

        private static Color GetDirectionColor(SteamAutoTradeRecordRow record)
        {
            return record.Direction == SteamAutoTradeDirection.Outgoing
                ? UIColors.TextCrit
                : record.Direction == SteamAutoTradeDirection.Incoming ? UIColors.Positive : UIColors.TextSub;
        }

        private static string FormatTime(DateTime time)
        {
            return time == default ? "--" : time.ToString("HH:mm:ss");
        }

        private static AutoQuoteBadgeTone ResolveResultBadgeTone(string text)
        {
            if (text.Contains("失败", StringComparison.Ordinal))
                return AutoQuoteBadgeTone.Critical;
            if (text.Contains("待", StringComparison.Ordinal))
                return AutoQuoteBadgeTone.Warn;
            if (text.Contains("跳过", StringComparison.Ordinal))
                return AutoQuoteBadgeTone.Subtle;
            return AutoQuoteBadgeTone.Success;
        }

        private static void OpenLogFile()
        {
            try
            {
                string path = SteamOfferAuditLog.EnsureLogFile();
                using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开日志失败：" + SteamOfferAuditLog.RedactSecrets(ex.Message), "悠悠自动报价", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnSteamOffersDataUpdated()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(RefreshStatusFromServices));
            }
            catch (ObjectDisposedException)
            {
                // Page was closed while the background Steam state event was being marshalled.
            }
            catch (InvalidOperationException)
            {
                // Handle can disappear during tab switches; the next activation refreshes the state.
            }
        }
    }

    internal enum AutoQuoteBadgeTone
    {
        Success,
        Warn,
        Critical,
        Subtle
    }

    internal sealed class AutoQuoteBadgeLabel : Label
    {
        public AutoQuoteBadgeTone Tone { get; set; } = AutoQuoteBadgeTone.Subtle;

        public AutoQuoteBadgeLabel()
        {
            AutoSize = false;
            BackColor = Color.Transparent;
            TextAlign = ContentAlignment.MiddleCenter;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var colors = ResolveBadgeColors(Tone);
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var path = UIUtils.RoundRect(rect, Math.Max(1, Height / 2));
            using var fill = new SolidBrush(colors.Fill);
            using var border = new Pen(colors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                colors.Text,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
        }

        internal static (Color Fill, Color Border, Color Text) ResolveBadgeColors(AutoQuoteBadgeTone tone)
        {
            return tone switch
            {
                AutoQuoteBadgeTone.Success => (
                    Color.FromArgb(45, UIColors.Positive),
                    Color.FromArgb(150, UIColors.Positive),
                    UIColors.Positive),
                AutoQuoteBadgeTone.Warn => (
                    Color.FromArgb(45, 155, 103, 0),
                    Color.FromArgb(180, 214, 151, 20),
                    UIColors.TextWarn),
                AutoQuoteBadgeTone.Critical => (
                    Color.FromArgb(42, UIColors.TextCrit),
                    Color.FromArgb(150, UIColors.TextCrit),
                    UIColors.TextCrit),
                _ => (
                    UIColors.ControlBg,
                    UIColors.Border,
                    UIColors.TextSub)
            };
        }
    }

    internal sealed class AutoQuoteRecordCellLabel : Label
    {
        public bool UseBadge { get; set; }

        public AutoQuoteBadgeTone BadgeTone { get; set; } = AutoQuoteBadgeTone.Subtle;

        public AutoQuoteRecordCellLabel()
        {
            AutoSize = false;
            AutoEllipsis = true;
            TextAlign = ContentAlignment.MiddleLeft;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var background = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);

            if (UseBadge)
                DrawBadge(e.Graphics);
            else
                DrawCellText(e.Graphics);

            using var separator = new Pen(UIColors.IsDark ? Color.FromArgb(49, 62, 76) : UIColors.Border);
            e.Graphics.DrawLine(separator, Padding.Left, Height - 1, Math.Max(Padding.Left, Width - Padding.Right), Height - 1);
        }

        private void DrawCellText(Graphics graphics)
        {
            var flags = TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix;
            flags |= TextAlign == ContentAlignment.MiddleCenter
                ? TextFormatFlags.HorizontalCenter
                : TextFormatFlags.Left;
            var rect = new Rectangle(
                Padding.Left,
                0,
                Math.Max(1, Width - Padding.Left - Padding.Right),
                Height);
            TextRenderer.DrawText(graphics, Text, Font, rect, ForeColor, flags);
        }

        private void DrawBadge(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var colors = AutoQuoteBadgeLabel.ResolveBadgeColors(BadgeTone);
            int textWidth = TextRenderer.MeasureText(Text, Font).Width;
            int badgeWidth = Math.Clamp(textWidth + UIUtils.S(18), UIUtils.S(48), Math.Max(UIUtils.S(48), Width - Padding.Left - Padding.Right));
            int badgeHeight = UIUtils.S(22);
            var rect = new Rectangle(Padding.Left, Math.Max(0, (Height - badgeHeight) / 2), badgeWidth, badgeHeight);
            using var path = UIUtils.RoundRect(rect, Math.Max(1, badgeHeight / 2));
            using var fill = new SolidBrush(colors.Fill);
            using var border = new Pen(colors.Border);
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                rect,
                colors.Text,
                TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
        }
    }
}
