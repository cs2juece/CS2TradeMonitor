using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryStorageConfirmDialog : Form
    {
        private YouPinInventoryStorageConfirmDialog(string title, string message, string confirmText)
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = UIColors.MainBg;
            ClientSize = new Size(UIUtils.S(500), UIUtils.S(270));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = title;

            var card = new YouPinCcRoundedPanel
            {
                DrawBorder = true,
                FillOverride = UIColors.CardBg
            };
            var icon = YouPinCcUi.Label("!", 16F, FontStyle.Bold, UIColors.TextWarn, ContentAlignment.MiddleCenter);
            icon.BackColor = UIColors.IsDark ? Color.FromArgb(66, 54, 31) : Color.FromArgb(255, 248, 224);
            var heading = YouPinCcUi.Label(title, 13F, FontStyle.Bold);
            var body = YouPinCcUi.Label(message, 9.5F, FontStyle.Regular, UIColors.TextSub);
            body.AutoEllipsis = false;
            var cancel = new LiteButton("取消");
            var confirm = new LiteButton(confirmText, true);
            cancel.DialogResult = DialogResult.Cancel;
            confirm.DialogResult = DialogResult.OK;
            AcceptButton = confirm;
            CancelButton = cancel;

            card.Controls.Add(icon);
            card.Controls.Add(heading);
            card.Controls.Add(body);
            card.Controls.Add(cancel);
            card.Controls.Add(confirm);
            Controls.Add(card);
            card.Layout += (_, _) =>
            {
                int pad = UIUtils.S(24);
                icon.SetBounds(pad, UIUtils.S(22), UIUtils.S(44), UIUtils.S(44));
                heading.SetBounds(icon.Right + UIUtils.S(14), UIUtils.S(20), Math.Max(1, card.Width - icon.Right - pad - UIUtils.S(14)), UIUtils.S(34));
                body.SetBounds(icon.Right + UIUtils.S(14), UIUtils.S(58), Math.Max(1, card.Width - icon.Right - pad - UIUtils.S(14)), UIUtils.S(108));
                confirm.SetBounds(card.Width - pad - UIUtils.S(108), card.Height - pad - UIUtils.S(36), UIUtils.S(108), UIUtils.S(36));
                cancel.SetBounds(confirm.Left - UIUtils.S(12) - UIUtils.S(96), confirm.Top, UIUtils.S(96), UIUtils.S(36));
            };
            card.SetBounds(UIUtils.S(16), UIUtils.S(16), ClientSize.Width - UIUtils.S(32), ClientSize.Height - UIUtils.S(32));
        }

        public static bool Confirm(IWin32Window owner, string title, string message, string confirmText)
        {
            using var dialog = new YouPinInventoryStorageConfirmDialog(title, message, confirmText);
            return dialog.ShowDialog(owner) == DialogResult.OK;
        }
    }
}
