using System;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    public sealed class YouPinAuthDialog : Form
    {
        private readonly Func<Settings?> _getConfig;
        private readonly Action? _credentialChanged;
        private readonly YouPinAuthCard _authCard;
        private readonly YouPinAuthRuntimeServices _runtimeServices;

        public YouPinAuthDialog(Func<Settings?> getConfig, Action? credentialChanged = null)
            : this(getConfig, YouPinAuthRuntimeServices.Resolve(), credentialChanged)
        {
        }

        internal YouPinAuthDialog(
            Func<Settings?> getConfig,
            YouPinAuthRuntimeServices runtimeServices,
            Action? credentialChanged = null)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _getConfig = getConfig;
            _credentialChanged = credentialChanged;
            _runtimeServices = runtimeServices;

            Text = "悠悠有品登录";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UIColors.MainBg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(UIUtils.S(760), UIUtils.S(430));

            var container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(UIUtils.S(18), UIUtils.S(18), UIUtils.S(18), UIUtils.S(18)),
                BackColor = UIColors.MainBg
            };
            Controls.Add(container);

            _authCard = new YouPinAuthCard(_getConfig, _runtimeServices, () =>
            {
                _credentialChanged?.Invoke();
            }, ShowManualCredentialDialog);
            AddGroup(container, _authCard);

            var footer = new Panel { Height = UIUtils.S(48), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var close = new LiteButton("关闭", false) { Width = UIUtils.S(90), Height = UIUtils.S(30) };
            close.Click += (_, __) => Close();
            footer.Controls.Add(close);
            footer.Layout += (_, __) =>
            {
                close.Location = new Point(Math.Max(0, footer.Width - close.Width), UIUtils.S(8));
            };
            container.Controls.Add(footer);
            container.Controls.SetChildIndex(footer, 0);
            UIColors.ApplyNativeThemeRecursively(this);
        }

        private void ShowManualCredentialDialog()
        {
            using var dialog = new Form
            {
                Text = "手动填写悠悠有品凭据",
                Font = new Font("Microsoft YaHei UI", 9F),
                BackColor = UIColors.MainBg,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(UIUtils.S(680), UIUtils.S(250))
            };

            var group = new LiteSettingsGroup("高级备用凭据");
            group.AddHint("仅在短信登录不可用时使用。保存后会自动应用到当前设置。");
            var tokenRow = CreateInputRow("登录凭据", out var tokenInput, width: 470);
            tokenInput.Inner.UseSystemPasswordChar = true;
            var deviceRow = CreateInputRow("设备凭据", out var deviceInput, width: 470);
            deviceInput.Inner.UseSystemPasswordChar = true;

            var cfg = _getConfig();
            if (cfg == null)
            {
                GlobalPromptService.Show(this, "当前设置不可用。", "悠悠有品登录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            tokenInput.Inner.Text = cfg.YouPinInventoryToken ?? "";
            deviceInput.Inner.Text = cfg.YouPinInventoryDeviceToken ?? "";
            group.AddFullItem(tokenRow);
            group.AddFullItem(deviceRow);

            var actions = new Panel { Height = UIUtils.S(50), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var save = new LiteButton("保存", true) { Width = UIUtils.S(90), Height = UIUtils.S(30) };
            var cancel = new LiteButton("取消", false) { Width = UIUtils.S(90), Height = UIUtils.S(30) };
            var status = new Label
            {
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft
            };
            save.Click += (_, __) =>
            {
                cfg.YouPinInventoryToken = tokenInput.Inner.Text.Trim();
                cfg.YouPinInventoryDeviceToken = deviceInput.Inner.Text.Trim();
                status.Text = "已写入当前设置，稍后会自动应用。";
                status.ForeColor = Color.FromArgb(0, 150, 80);
                _credentialChanged?.Invoke();
                _authCard.RefreshState();
                dialog.Close();
            };
            cancel.Click += (_, __) => dialog.Close();
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            actions.Controls.Add(status);
            actions.Layout += (_, __) =>
            {
                int mid = actions.Height / 2;
                save.Location = new Point(0, mid - save.Height / 2);
                cancel.Location = new Point(save.Right + UIUtils.S(10), mid - cancel.Height / 2);
                status.SetBounds(cancel.Right + UIUtils.S(12), UIUtils.S(5), Math.Max(1, actions.Width - cancel.Right - UIUtils.S(20)), UIUtils.S(40));
            };
            group.AddFullItem(actions);

            var wrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(UIUtils.S(18)), BackColor = UIColors.MainBg };
            wrapper.Controls.Add(group);
            group.Dock = DockStyle.Top;
            dialog.Controls.Add(wrapper);
            UIColors.ApplyNativeThemeRecursively(dialog);
            dialog.ShowDialog(this);
        }

        private static void AddGroup(Control container, Control group)
        {
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 0, 0, UIUtils.S(14))
            };
            wrapper.Resize += (_, __) => ClampGroupWidth(wrapper, group);
            wrapper.Layout += (_, __) => ClampGroupWidth(wrapper, group);
            wrapper.Controls.Add(group);
            container.Controls.Add(wrapper);
            container.Controls.SetChildIndex(wrapper, 0);
        }

        private static Panel CreateInputRow(string labelText, out LiteUnderlineInput input, int width)
        {
            var row = new Panel { Height = UIUtils.S(40), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var label = CreateRowLabel(labelText);
            input = new LiteUnderlineInput("", "", "", width, null, HorizontalAlignment.Left);
            var inputControl = input;
            row.Controls.Add(label);
            row.Controls.Add(inputControl);
            row.Layout += (_, __) =>
            {
                int mid = row.Height / 2;
                label.Location = new Point(0, mid - label.Height / 2);
                inputControl.Width = Math.Max(UIUtils.S(260), row.Width - UIUtils.S(170));
                inputControl.Location = new Point(UIUtils.S(150), mid - inputControl.Height / 2);
            };
            row.Paint += PaintBottomLine;
            return row;
        }

        private static Label CreateRowLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void ClampGroupWidth(Control wrapper, Control group)
        {
            int targetWidth = Math.Max(1, wrapper.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - UIUtils.S(6));
            if (group.Width != targetWidth) group.Width = targetWidth;
        }

        private static void PaintBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control) return;
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }
    }
}
