using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinLandlordHistoryDialog : Form
    {
        private readonly YouPinLandlordPagePresenter _presenter;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly DateTimePicker _from = CreateDateFilter();
        private readonly DateTimePicker _to = CreateDateFilter();
        private readonly ComboBox _workflow = CreateCombo("全部工作流", "租赁自动改价", "库存自动出租");
        private readonly ComboBox _rentalType = CreateCombo("全部类型", "0CD", "普通出租");
        private readonly TextBox _itemName = CreateTextBox("饰品名称");
        private readonly TextBox _result = CreateTextBox("结果关键词");
        private readonly TextBox _runId = CreateTextBox("RunId 前缀");
        private readonly TextBox _actionId = CreateTextBox("ActionId 前缀");
        private readonly Label _health = CreateLabel();
        private readonly Label _summary = CreateLabel();
        private readonly RichTextBox _content = new()
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UIColors.InputBg,
            ForeColor = UIColors.TextMain,
            Font = new Font("Microsoft YaHei UI", 9F),
            DetectUrls = false
        };
        private readonly LiteButton _query = new("查询", true) { Width = UIUtils.S(92) };

        private YouPinLandlordHistoryDialog(
            YouPinLandlordPagePresenter presenter,
            YouPinLandlordWorkflow? initialWorkflow)
        {
            _presenter = presenter;
            Text = "包租公操作历史";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = UIUtils.S(new Size(980, 680));
            Size = UIUtils.S(new Size(1120, 760));
            BackColor = UIColors.MainBg;
            ForeColor = UIColors.TextMain;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            if (initialWorkflow.HasValue)
                _workflow.SelectedIndex = initialWorkflow == YouPinLandlordWorkflow.RentalReprice ? 1 : 2;

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = UIUtils.S(112),
                Padding = new Padding(UIUtils.S(14), UIUtils.S(12), UIUtils.S(14), UIUtils.S(6)),
                WrapContents = true,
                AutoScroll = false,
                BackColor = UIColors.ControlBg
            };
            AddFilter(filters, "开始", _from, 150);
            AddFilter(filters, "结束", _to, 150);
            AddFilter(filters, "工作流", _workflow, 132);
            AddFilter(filters, "出租类型", _rentalType, 116);
            AddFilter(filters, "饰品", _itemName, 170);
            AddFilter(filters, "结果", _result, 132);
            AddFilter(filters, "Run", _runId, 132);
            AddFilter(filters, "Action", _actionId, 132);
            _query.Height = UIUtils.S(30);
            _query.Margin = new Padding(UIUtils.S(8), UIUtils.S(20), 0, 0);
            filters.Controls.Add(_query);

            var status = new Panel { Dock = DockStyle.Top, Height = UIUtils.S(42), BackColor = UIColors.CardBg };
            status.Controls.AddRange(new Control[] { _health, _summary });
            status.Layout += (_, __) =>
            {
                int pad = UIUtils.S(16);
                _health.SetBounds(pad, 0, UIUtils.S(390), status.Height);
                _summary.SetBounds(_health.Right + UIUtils.S(12), 0, Math.Max(1, status.Width - _health.Right - pad), status.Height);
            };
            _content.Dock = DockStyle.Fill;
            _content.Margin = new Padding(UIUtils.S(14));
            var contentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UIUtils.S(14)) };
            contentHost.Controls.Add(_content);
            Controls.Add(contentHost);
            Controls.Add(status);
            Controls.Add(filters);

            _query.Click += async (_, __) => await QueryAsync().ConfigureAwait(true);
            Shown += async (_, __) => await QueryAsync().ConfigureAwait(true);
        }

        public static void Show(
            IWin32Window? owner,
            YouPinLandlordPagePresenter presenter,
            YouPinLandlordWorkflow? initialWorkflow)
        {
            using var dialog = new YouPinLandlordHistoryDialog(presenter, initialWorkflow);
            dialog.ShowDialog(owner);
        }

        private async Task QueryAsync()
        {
            _query.Enabled = false;
            _summary.Text = "正在读取操作历史…";
            try
            {
                var query = new YouPinLandlordAuditQuery(
                    _from.Checked ? _from.Value : null,
                    _to.Checked ? _to.Value : null,
                    _workflow.SelectedIndex switch
                    {
                        1 => YouPinLandlordWorkflow.RentalReprice,
                        2 => YouPinLandlordWorkflow.InventoryAutoRent,
                        _ => null
                    },
                    _rentalType.SelectedIndex switch
                    {
                        1 => YouPinRentalShelfType.ZeroCd,
                        2 => YouPinRentalShelfType.InventoryRental,
                        _ => null
                    },
                    _itemName.Text,
                    _result.Text,
                    _runId.Text,
                    _actionId.Text,
                    500);
                IReadOnlyList<YouPinLandlordOperationRecord> records = await _presenter
                    .QueryHistoryAsync(query, _lifetime.Token)
                    .ConfigureAwait(true);
                _content.Text = YouPinLandlordPagePresenter.FormatHistoryRecords(records);
                _content.SelectionStart = 0;
                _content.ScrollToCaret();
                _summary.Text = $"已显示 {records.Count} 条，最多 500 条；查询不会长期占用日志文件。";
                RenderHealth();
            }
            catch (OperationCanceledException)
            {
                _summary.Text = "查询已取消";
            }
            catch (Exception ex)
            {
                _summary.Text = "查询失败：" + ex.Message;
            }
            finally
            {
                if (!IsDisposed)
                    _query.Enabled = true;
            }
        }

        private void RenderHealth()
        {
            YouPinLandlordAuditHealth health = _presenter.GetAuditHealth();
            _health.Text = health.IsHealthy
                ? "● 审计日志正常"
                : "● 审计日志写入异常：" + health.LastError;
            _health.ForeColor = health.IsHealthy ? UIColors.Positive : UIColors.TextWarn;
        }

        private static void AddFilter(
            FlowLayoutPanel panel,
            string caption,
            Control control,
            int width)
        {
            var host = new Panel
            {
                Width = UIUtils.S(width),
                Height = UIUtils.S(42),
                Margin = new Padding(0, 0, UIUtils.S(8), UIUtils.S(4))
            };
            var label = CreateLabel();
            label.Text = caption;
            label.ForeColor = UIColors.TextSub;
            label.SetBounds(0, 0, UIUtils.S(52), UIUtils.S(30));
            control.SetBounds(UIUtils.S(54), 0, Math.Max(1, host.Width - UIUtils.S(54)), UIUtils.S(30));
            host.Controls.AddRange(new[] { label, control });
            panel.Controls.Add(host);
        }

        private static DateTimePicker CreateDateFilter()
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "MM-dd HH:mm",
                ShowCheckBox = true,
                Checked = false,
                CalendarMonthBackground = UIColors.InputBg,
                CalendarForeColor = UIColors.TextMain
            };
        }

        private static ComboBox CreateCombo(params string[] items)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = UIColors.InputBg,
                ForeColor = UIColors.TextMain
            };
            combo.Items.AddRange(items);
            combo.SelectedIndex = 0;
            return combo;
        }

        private static TextBox CreateTextBox(string placeholder)
        {
            return new TextBox
            {
                PlaceholderText = placeholder,
                BackColor = UIColors.InputBg,
                ForeColor = UIColors.TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static Label CreateLabel()
        {
            return new Label
            {
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", 8.8F),
                BackColor = Color.Transparent
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
