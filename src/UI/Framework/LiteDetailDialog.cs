using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class LiteDetailDialog : Form
    {
        private readonly Label _titleLabel;
        private readonly Panel _contentPanel;
        private readonly TextBox _bodyBox;
        private readonly LiteButton _closeButton;
        private readonly LiteButton? _actionButton;
        private readonly Label _actionStatusLabel;
        private readonly Func<CancellationToken, Task<string>>? _action;
        private readonly CancellationTokenSource _actionCancellation = new();
        private readonly string _actionButtonText;
        private readonly List<RowControls> _rows = new();
        public sealed record DetailRow(string Label, string Value);

        private LiteDetailDialog(
            string title,
            IEnumerable<DetailRow> rows,
            string body,
            bool warning,
            string actionButtonText = "",
            Func<CancellationToken, Task<string>>? action = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(UIUtils.S(620), UIUtils.S(520));
            BackColor = UIColors.MainBg;
            ForeColor = UIColors.TextMain;
            Font = UIUtils.GetFont("Microsoft YaHei UI", 9f, false);

            _titleLabel = new Label
            {
                AutoSize = false,
                Text = title,
                Font = UIUtils.GetFont("Microsoft YaHei UI", 11f, true),
                ForeColor = warning ? UIColors.TextWarn : UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _contentPanel = new Panel
            {
                AutoScroll = true,
                BackColor = UIColors.CardBg
            };
            _contentPanel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, _contentPanel.Width - 1, _contentPanel.Height - 1);
            };

            foreach (var row in rows.Where(x => !string.IsNullOrWhiteSpace(x.Label)))
            {
                var key = new Label
                {
                    AutoSize = false,
                    Text = row.Label.Trim(),
                    ForeColor = UIColors.TextSub,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.TopLeft
                };
                var value = new Label
                {
                    AutoSize = false,
                    Text = string.IsNullOrWhiteSpace(row.Value) ? "暂无" : row.Value.Trim(),
                    ForeColor = UIColors.TextMain,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.TopLeft
                };
                _rows.Add(new RowControls(key, value));
                _contentPanel.Controls.Add(key);
                _contentPanel.Controls.Add(value);
            }

            _bodyBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.InputBg,
                ForeColor = UIColors.TextMain,
                Text = body ?? "",
                Font = Font,
                TabStop = false
            };
            _contentPanel.Controls.Add(_bodyBox);

            _action = action;
            _actionButtonText = string.IsNullOrWhiteSpace(actionButtonText)
                ? "执行"
                : actionButtonText.Trim();
            _actionStatusLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = action != null
            };
            _closeButton = new LiteButton("关闭", action == null)
            {
                Width = UIUtils.S(90),
                Height = UIUtils.S(32)
            };
            _closeButton.Click += (_, __) =>
            {
                _actionCancellation.Cancel();
                DialogResult = DialogResult.OK;
                Close();
            };

            if (action != null)
            {
                _actionButton = new LiteButton(_actionButtonText, true)
                {
                    Width = UIUtils.S(100),
                    Height = UIUtils.S(32)
                };
                _actionButton.Click += HandleActionClick;
            }

            Controls.Add(_titleLabel);
            Controls.Add(_contentPanel);
            Controls.Add(_actionStatusLabel);
            Controls.Add(_closeButton);
            if (_actionButton != null)
                Controls.Add(_actionButton);

            Layout += (_, __) => LayoutDialog();
            _contentPanel.Resize += (_, __) => LayoutContent();
            FormClosed += (_, __) => _actionCancellation.Cancel();
            UIColors.ApplyNativeThemeRecursively(this);
        }

        public static void Show(IWin32Window? owner, string title, IEnumerable<DetailRow> rows, string body = "", bool warning = false)
        {
            using var dialog = new LiteDetailDialog(title, rows, body, warning);
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        public static void ShowWithAction(
            IWin32Window? owner,
            string title,
            string body,
            string actionButtonText,
            Func<CancellationToken, Task<string>> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            using var dialog = new LiteDetailDialog(
                title,
                Array.Empty<DetailRow>(),
                body,
                false,
                actionButtonText,
                action);
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        private void LayoutDialog()
        {
            int pad = UIUtils.S(18);
            _titleLabel.SetBounds(pad, pad, ClientSize.Width - pad * 2, UIUtils.S(32));
            int footerTop = ClientSize.Height - pad - _closeButton.Height;
            int footerRight = ClientSize.Width - pad;
            if (_actionButton != null)
            {
                _actionButton.SetBounds(
                    footerRight - _actionButton.Width,
                    footerTop,
                    _actionButton.Width,
                    _actionButton.Height);
                _closeButton.SetBounds(
                    _actionButton.Left - UIUtils.S(10) - _closeButton.Width,
                    footerTop,
                    _closeButton.Width,
                    _closeButton.Height);
            }
            else
            {
                _closeButton.SetBounds(
                    footerRight - _closeButton.Width,
                    footerTop,
                    _closeButton.Width,
                    _closeButton.Height);
            }
            _actionStatusLabel.SetBounds(
                pad,
                footerTop,
                Math.Max(1, _closeButton.Left - pad - UIUtils.S(12)),
                _closeButton.Height);
            _contentPanel.SetBounds(pad, _titleLabel.Bottom + UIUtils.S(10), ClientSize.Width - pad * 2, _closeButton.Top - _titleLabel.Bottom - UIUtils.S(24));
            LayoutContent();
        }

        private async void HandleActionClick(object? sender, EventArgs e)
        {
            if (_action == null || _actionButton == null)
                return;

            _actionButton.Enabled = false;
            _actionButton.Text = "刷新中…";
            SetActionStatus("正在读取悠悠云端…", UIColors.Primary);
            try
            {
                string refreshedBody = await _action(_actionCancellation.Token);
                if (IsDisposed || _actionCancellation.IsCancellationRequested)
                    return;

                _bodyBox.Text = refreshedBody ?? string.Empty;
                _bodyBox.SelectionStart = 0;
                _bodyBox.ScrollToCaret();
                _contentPanel.AutoScrollPosition = Point.Empty;
                LayoutContent();
                SetActionStatus("刷新成功", UIColors.Positive);
            }
            catch (OperationCanceledException) when (_actionCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    string error = AppActions.SanitizeError(ex.Message);
                    SetActionStatus(
                        string.IsNullOrWhiteSpace(error) ? "刷新失败" : "刷新失败：" + error,
                        UIColors.TextWarn);
                }
            }
            finally
            {
                if (!IsDisposed && !_actionCancellation.IsCancellationRequested)
                {
                    _actionButton.Text = _actionButtonText;
                    _actionButton.Enabled = true;
                }
            }
        }

        private void SetActionStatus(string text, Color color)
        {
            _actionStatusLabel.Text = text;
            _actionStatusLabel.ForeColor = color;
        }

        private void LayoutContent()
        {
            if (_contentPanel.Width <= 0)
                return;

            int pad = UIUtils.S(16);
            int y = pad + _contentPanel.AutoScrollPosition.Y;
            int keyW = UIUtils.S(104);
            int gap = UIUtils.S(14);
            int valueW = Math.Max(UIUtils.S(120), _contentPanel.ClientSize.Width - pad * 2 - keyW - gap);
            foreach (var row in _rows)
            {
                int valueH = Math.Max(UIUtils.S(24), Math.Min(UIUtils.S(72), TextRenderer.MeasureText(row.Value.Text, row.Value.Font, new Size(valueW, UIUtils.S(80)), TextFormatFlags.WordBreak).Height + UIUtils.S(6)));
                row.Key.SetBounds(pad, y + UIUtils.S(2), keyW, valueH);
                row.Value.SetBounds(pad + keyW + gap, y + UIUtils.S(2), valueW, valueH);
                y += valueH + UIUtils.S(8);
            }

            if (!string.IsNullOrWhiteSpace(_bodyBox.Text))
            {
                y += UIUtils.S(4);
                int bodyH = Math.Max(UIUtils.S(150), Math.Min(UIUtils.S(260), _contentPanel.ClientSize.Height - (y - _contentPanel.AutoScrollPosition.Y) - pad));
                _bodyBox.SetBounds(pad, y, _contentPanel.ClientSize.Width - pad * 2, bodyH);
                y += bodyH + pad;
                _bodyBox.Visible = true;
            }
            else
            {
                _bodyBox.Visible = false;
            }

            _contentPanel.AutoScrollMinSize = new Size(0, Math.Max(0, y - _contentPanel.AutoScrollPosition.Y));
            _contentPanel.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _actionCancellation.Cancel();
                _actionCancellation.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed record RowControls(Label Key, Label Value);
    }
}
