using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class Cs2UpdateReminderHostPage : FrameworkSettingsHostPage<Cs2UpdateReminderPage>
    {
        public Cs2UpdateReminderHostPage()
            : base(new Cs2UpdateReminderPage(SystemPageRuntimeServices.Resolve()))
        {
        }
    }

    public sealed class Cs2UpdateReminderPage : FrameworkSettingsPageBase
    {
        private Label? _statusLabel;
        private Label? _lastCheckLabel;
        private Label? _enabledStateLabel;
        private Label? _failureReasonLabel;
        private Label? _baselineLabel;
        private Label? _latestLabel;
        private LiteCheck? _enabledCheck;
        private LiteNumberInput? _intervalInput;
        private Panel? _failureRow;
        private readonly ICs2UpdateReminderService _updateReminder;
        private bool _busy;

        public Cs2UpdateReminderPage()
            : this(SystemPageRuntimeServices.Resolve())
        {
        }

        internal Cs2UpdateReminderPage(SystemPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _updateReminder = runtimeServices.Cs2UpdateReminder;
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            RefreshStatus();
        }

        private void BuildPage()
        {
            var details = CreateDetailsCard();
            var history = CreateHistoryCard();
            var source = CreateSourceCard();
            var status = CreateStatusCard();

            Container.Controls.SetChildIndex(status, 0);
            Container.Controls.SetChildIndex(source, 1);
            Container.Controls.SetChildIndex(history, 2);
            Container.Controls.SetChildIndex(details, 3);
        }

        private Panel CreateStatusCard()
        {
            var group = new LiteSettingsGroup("CS2 更新提醒");
            group.AddFullItem(CreateTopStatusRow());
            _failureRow = CreateFailureRow();
            group.AddFullItem(_failureRow);

            RegisterRefresh(RefreshStatus);
            RegisterSave(() =>
            {
                if (_enabledCheck != null)
                    Set(nameof(Settings.Cs2UpdateReminderEnabled), _enabledCheck.Checked);
            });

            return AddGroupToPage(group);
        }

        private Control CreateTopStatusRow()
        {
            var row = new Panel
            {
                Height = UIUtils.S(132),
                BackColor = Color.Transparent
            };

            _statusLabel = CreateValueLabel(10F, true);
            _lastCheckLabel = CreateValueLabel();
            _enabledStateLabel = CreateValueLabel();

            var statusBlock = CreateStatusBlock("当前状态", _statusLabel);
            var lastCheckBlock = CreateStatusBlock("上次检查", _lastCheckLabel);
            var enabledBlock = CreateEnabledBlock("提醒开关", _enabledStateLabel);
            var sourceBlock = CreateStatusTextBlock("提醒范围", "发现 CS2 更新时提醒");

            var btnCheck = new LiteButton("立即检查", true) { Width = UIUtils.S(116), Height = UIUtils.S(34) };
            btnCheck.Click += async (_, __) => await RunCheckAsync(btnCheck, false);

            row.Controls.Add(statusBlock);
            row.Controls.Add(lastCheckBlock);
            row.Controls.Add(enabledBlock);
            row.Controls.Add(sourceBlock);
            row.Controls.Add(btnCheck);

            row.Layout += (_, __) =>
            {
                int gap = UIUtils.S(14);
                int actionW = UIUtils.S(128);
                int blockAreaW = Math.Max(UIUtils.S(420), row.Width - actionW - UIUtils.S(24));
                int colW = Math.Max(UIUtils.S(190), (blockAreaW - gap) / 2);
                int blockH = UIUtils.S(52);
                int left = 0;
                int top1 = UIUtils.S(10);
                int top2 = UIUtils.S(68);

                statusBlock.SetBounds(left, top1, colW, blockH);
                lastCheckBlock.SetBounds(left + colW + gap, top1, colW, blockH);
                enabledBlock.SetBounds(left, top2, colW, blockH);
                sourceBlock.SetBounds(left + colW + gap, top2, colW, blockH);
                btnCheck.SetBounds(row.Width - btnCheck.Width, top1, btnCheck.Width, btnCheck.Height);
            };

            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };

            return row;
        }

        private Panel CreateFailureRow()
        {
            var row = new Panel
            {
                Height = 0,
                Visible = false,
                BackColor = Color.Transparent
            };

            _failureReasonLabel = new Label
            {
                Text = "",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextWarn,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var retry = new LiteButton("重试", true) { Width = UIUtils.S(86), Height = UIUtils.S(30) };
            retry.Click += async (_, __) => await RunCheckAsync(retry, false);

            row.Controls.Add(_failureReasonLabel);
            row.Controls.Add(retry);
            row.Layout += (_, __) =>
            {
                int top = Math.Max(0, (row.Height - retry.Height) / 2);
                retry.SetBounds(row.Width - retry.Width, top, retry.Width, retry.Height);
                _failureReasonLabel.SetBounds(0, 0, Math.Max(1, retry.Left - UIUtils.S(14)), row.Height);
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private Panel CreateSourceCard()
        {
            var group = new LiteSettingsGroup("版本源");
            group.AddFullItem(CreateVersionSourceRow());
            return AddGroupToPage(group);
        }

        private Control CreateVersionSourceRow()
        {
            var row = new Panel
            {
                Height = UIUtils.S(58),
                BackColor = Color.Transparent
            };

            var title = CreatePlainLabel("FKBUFF CS2 更新记录", true);
            var detail = CreatePlainLabel("只读取公开更新记录；首次启用或重置基准时不会弹提醒，之后发现新记录再提醒。", false);
            detail.ForeColor = UIColors.TextSub;
            var btnOpen = new LiteButton("打开更新页面", false) { Width = UIUtils.S(124), Height = UIUtils.S(30) };
            btnOpen.Click += (_, __) => OpenUpdatePage();

            row.Controls.Add(title);
            row.Controls.Add(detail);
            row.Controls.Add(btnOpen);
            row.Layout += (_, __) =>
            {
                btnOpen.SetBounds(row.Width - btnOpen.Width, UIUtils.S(14), btnOpen.Width, btnOpen.Height);
                int textW = Math.Max(1, btnOpen.Left - UIUtils.S(16));
                title.SetBounds(0, UIUtils.S(7), textW, UIUtils.S(22));
                detail.SetBounds(0, UIUtils.S(30), textW, UIUtils.S(22));
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private Panel CreateHistoryCard()
        {
            var group = new LiteSettingsGroup("历史记录");
            _baselineLabel = CreateValueLabel();
            _latestLabel = CreateValueLabel();
            group.AddFullItem(CreateInfoRow("当前基准", _baselineLabel));
            group.AddFullItem(CreateInfoRow("最新记录", _latestLabel));
            return AddGroupToPage(group);
        }

        private Panel CreateDetailsCard()
        {
            var group = new LiteSettingsGroup("检测细节");
            _intervalInput = AddInt(group, "检查间隔", nameof(Settings.Cs2UpdateReminderRefreshSec), 600, "秒", 80,
                value => Math.Clamp(value <= 0 ? 600 : value, 60, 86400),
                _ => _updateReminder.ResetSchedule());
            AddToggle(group, "手机提醒", nameof(Settings.Cs2UpdateReminderWechatEnabled), true);
            AddToggle(group, "电脑提示音", nameof(Settings.Cs2UpdateReminderSoundEnabled), false);
            group.AddFullItem(CreateResetBaselineRow());
            AddHint(group, "手机提醒复用左侧“手机提醒”的已启用通道；检查失败时请先重试，仍失败再打开版本源确认页面是否可访问。");

            RegisterRefresh(() =>
            {
                if (_intervalInput != null)
                {
                    int value = Math.Clamp(Get(nameof(Settings.Cs2UpdateReminderRefreshSec), 600) <= 0
                        ? 600
                        : Get(nameof(Settings.Cs2UpdateReminderRefreshSec), 600), 60, 86400);
                    _intervalInput.Inner.Text = value.ToString();
                }
            });

            return AddGroupToPage(group);
        }

        private Control CreateResetBaselineRow()
        {
            var row = new Panel
            {
                Height = UIUtils.S(50),
                BackColor = Color.Transparent
            };

            var hint = CreatePlainLabel("重置基准只会把“最新记录”设为当前起点，不会弹出更新提醒。", false);
            hint.ForeColor = UIColors.TextSub;
            var btnReset = new LiteButton("重置基准", false) { Width = UIUtils.S(104), Height = UIUtils.S(30) };
            btnReset.Click += async (_, __) => await RunCheckAsync(btnReset, true);

            row.Controls.Add(hint);
            row.Controls.Add(btnReset);
            row.Layout += (_, __) =>
            {
                btnReset.SetBounds(row.Width - btnReset.Width, UIUtils.S(10), btnReset.Width, btnReset.Height);
                hint.SetBounds(0, 0, Math.Max(1, btnReset.Left - UIUtils.S(14)), row.Height);
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private Panel CreateStatusBlock(string caption, Label value)
        {
            var panel = new Panel
            {
                BackColor = Color.Transparent
            };

            var captionLabel = CreateCaptionLabel(caption);
            panel.Controls.Add(captionLabel);
            panel.Controls.Add(value);
            panel.Layout += (_, __) =>
            {
                captionLabel.SetBounds(0, UIUtils.S(2), panel.Width, UIUtils.S(18));
                value.SetBounds(0, UIUtils.S(20), panel.Width, UIUtils.S(28));
            };
            return panel;
        }

        private Panel CreateEnabledBlock(string caption, Label value)
        {
            var panel = CreateStatusBlock(caption, value);
            _enabledCheck = new LiteCheck(Get(nameof(Settings.Cs2UpdateReminderEnabled), true), "启用");
            _enabledCheck.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(nameof(Settings.Cs2UpdateReminderEnabled), _enabledCheck.Checked);
                _updateReminder.ResetSchedule();
                RefreshStatus();
            };
            panel.Controls.Add(_enabledCheck);
            panel.Layout += (_, __) =>
            {
                _enabledCheck.SetBounds(panel.Width - UIUtils.S(82), UIUtils.S(21), UIUtils.S(82), UIUtils.S(24));
                if (panel.Controls.Count > 1 && panel.Controls[1] is Label label)
                    label.SetBounds(0, UIUtils.S(20), Math.Max(1, _enabledCheck.Left - UIUtils.S(8)), UIUtils.S(28));
            };
            return panel;
        }

        private Panel CreateStatusTextBlock(string caption, string value)
        {
            var label = CreateValueLabel();
            label.Text = value;
            label.ForeColor = UIColors.TextSub;
            return CreateStatusBlock(caption, label);
        }

        private static Label CreateValueLabel(float size = 9F, bool strong = false)
        {
            return new Label
            {
                AutoSize = false,
                Height = UIUtils.S(30),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", size, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
        }

        private static Label CreateCaptionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Label CreatePlainLabel(string text, bool strong)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Control CreateInfoRow(string title, Label value)
        {
            var row = new Panel
            {
                Height = UIUtils.S(38),
                BackColor = Color.Transparent
            };

            var label = new Label
            {
                Text = title,
                AutoSize = false,
                Width = UIUtils.S(120),
                Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent
            };

            value.Dock = DockStyle.Fill;
            row.Controls.Add(value);
            row.Controls.Add(label);
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }

        private void RefreshStatus()
        {
            if (Config == null)
                return;

            bool enabled = Get(nameof(Settings.Cs2UpdateReminderEnabled), true);
            string lastStatus = Get(nameof(Settings.Cs2UpdateLastStatus), "未检查");
            var result = _updateReminder.LastResult;
            string failureReason = enabled ? BuildFailureReason(lastStatus, result) : "";
            bool failed = !string.IsNullOrWhiteSpace(failureReason);

            if (_enabledCheck != null && _enabledCheck.Checked != enabled)
                _enabledCheck.Checked = enabled;

            SetLabel(_statusLabel, BuildCurrentStatus(enabled, lastStatus, result, failed), GetStatusColor(enabled, lastStatus, result, failed));
            SetLabel(_lastCheckLabel, Cs2UpdateReminderService.FormatTime(Get(nameof(Settings.Cs2UpdateLastCheckTime), 0L)),
                Get(nameof(Settings.Cs2UpdateLastCheckTime), 0L) > 0 ? UIColors.TextMain : UIColors.TextSub);
            SetLabel(_enabledStateLabel, enabled ? "已启用" : "已关闭", enabled ? UIColors.Positive : UIColors.TextSub);
            RefreshFailureRow(failed, failureReason);

            if (_baselineLabel != null)
            {
                string baselineTitle = string.IsNullOrWhiteSpace(Get(nameof(Settings.Cs2UpdateBaselineTitle), ""))
                    ? "暂无基准"
                    : Get(nameof(Settings.Cs2UpdateBaselineTitle), "").Trim();
                string baselineTime = Cs2UpdateReminderService.FormatTime(Get(nameof(Settings.Cs2UpdateBaselinePublishedAt), 0L));
                _baselineLabel.Text = baselineTitle == "暂无基准" ? baselineTitle : $"{baselineTitle}  {baselineTime}";
                _baselineLabel.ForeColor = baselineTitle == "暂无基准" ? UIColors.TextSub : UIColors.TextMain;
            }

            var latest = result.Latest ?? _updateReminder.RecentItems.FirstOrDefault();
            if (_latestLabel != null)
            {
                if (latest != null)
                {
                    _latestLabel.Text = $"{latest.Title}  {Cs2UpdateReminderService.FormatTime(latest.PublishedAt)}";
                    _latestLabel.ForeColor = UIColors.TextMain;
                }
                else if (!string.IsNullOrWhiteSpace(Get(nameof(Settings.Cs2UpdateBaselineTitle), "")))
                {
                    _latestLabel.Text = $"{Get(nameof(Settings.Cs2UpdateBaselineTitle), "").Trim()}  {Cs2UpdateReminderService.FormatTime(Get(nameof(Settings.Cs2UpdateBaselinePublishedAt), 0L))}";
                    _latestLabel.ForeColor = UIColors.TextMain;
                }
                else
                {
                    _latestLabel.Text = "暂无数据";
                    _latestLabel.ForeColor = UIColors.TextSub;
                }
            }
        }

        private void RefreshFailureRow(bool visible, string reason)
        {
            if (_failureRow == null || _failureReasonLabel == null)
                return;

            int targetHeight = visible ? UIUtils.S(46) : 0;
            bool changed = _failureRow.Visible != visible || _failureRow.Height != targetHeight;
            _failureRow.Visible = visible;
            _failureRow.Height = targetHeight;
            _failureReasonLabel.Text = visible ? $"失败原因：{reason}" : "";
            if (changed)
                RequestRelayoutGroups();
        }

        private static void SetLabel(Label? label, string text, Color color)
        {
            if (label == null)
                return;

            label.Text = text;
            label.ForeColor = color;
        }

        private static string BuildCurrentStatus(bool enabled, string lastStatus, Cs2UpdateCheckResult result, bool failed)
        {
            if (!enabled)
                return "已关闭";
            if (failed)
                return "检查失败";
            if (result.HasNewUpdate || LooksLikeNewUpdateStatus(lastStatus))
                return string.IsNullOrWhiteSpace(lastStatus) ? "CS2 已有最新更新" : lastStatus;
            if (string.IsNullOrWhiteSpace(lastStatus) || string.Equals(lastStatus, "未检查", StringComparison.OrdinalIgnoreCase))
                return "等待首次检查";

            return lastStatus;
        }

        private static Color GetStatusColor(bool enabled, string lastStatus, Cs2UpdateCheckResult result, bool failed)
        {
            if (!enabled)
                return UIColors.TextSub;
            if (failed)
                return UIColors.TextWarn;
            if (result.HasNewUpdate || LooksLikeNewUpdateStatus(lastStatus))
                return UIColors.TextWarn;
            if (string.IsNullOrWhiteSpace(lastStatus) || string.Equals(lastStatus, "未检查", StringComparison.OrdinalIgnoreCase))
                return UIColors.TextSub;

            return UIColors.Positive;
        }

        private static string BuildFailureReason(string lastStatus, Cs2UpdateCheckResult result)
        {
            if (result.CheckedAt != DateTime.MinValue && !result.Success && !IsNotChecked(result.Message))
                return NormalizeFailure(result.Message);

            if (!IsNotChecked(lastStatus) && LooksLikeFailure(lastStatus))
                return NormalizeFailure(lastStatus);

            return "";
        }

        private static bool LooksLikeFailure(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || text.Contains("不可用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("异常", StringComparison.OrdinalIgnoreCase)
                || text.Contains("HTTP", StringComparison.OrdinalIgnoreCase)
                || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || text.Contains("timed out", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeNewUpdateStatus(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("发现", StringComparison.OrdinalIgnoreCase)
                || text.Contains("已有最新更新", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNotChecked(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                || string.Equals(text.Trim(), "未检查", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFailure(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "未知错误";

            text = text.Trim();
            const int maxLen = 120;
            return text.Length > maxLen ? text[..maxLen] + "..." : text;
        }

        private async Task RunCheckAsync(LiteButton button, bool resetBaseline)
        {
            if (_busy || Config == null)
                return;

            Save();
            _busy = true;
            button.Enabled = false;
            string oldText = button.Text;
            button.Text = resetBaseline ? "重置中..." : "检查中...";
            SetLabel(_statusLabel, resetBaseline ? "正在重置基准..." : "正在检查...", UIColors.TextSub);

            try
            {
                await _updateReminder.ManualCheckAsync(Config, resetBaseline);
                Set(nameof(Settings.Cs2UpdateBaselineKey), Config.Cs2UpdateBaselineKey);
                Set(nameof(Settings.Cs2UpdateBaselineTitle), Config.Cs2UpdateBaselineTitle);
                Set(nameof(Settings.Cs2UpdateBaselinePublishedAt), Config.Cs2UpdateBaselinePublishedAt);
                Set(nameof(Settings.Cs2UpdateLastCheckTime), Config.Cs2UpdateLastCheckTime);
                Set(nameof(Settings.Cs2UpdateLastStatus), Config.Cs2UpdateLastStatus);
                RefreshStatus();
            }
            finally
            {
                button.Text = oldText;
                button.Enabled = true;
                _busy = false;
            }

        }

        private void OpenUpdatePage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Cs2UpdateReminderService.PageLink) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("CS2Update", "Open FKBUFF CS2 update page failed.", ex);
                SetLabel(_statusLabel, "打开版本源失败，请稍后重试。", UIColors.TextWarn);
                SetLabel(_failureReasonLabel, "版本源链接无法打开：" + ex.Message, UIColors.TextWarn);
                if (_failureRow != null)
                {
                    _failureRow.Visible = true;
                    _failureRow.Height = UIUtils.S(54);
                    _failureRow.Parent?.PerformLayout();
                }
            }
        }
    }
}
