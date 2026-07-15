using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class RefreshIntervalDialog : Form
    {
        private readonly Action<int> _onSaved;
        private readonly LiteNumberInput _intervalInput;
        private readonly Label _validationLabel;

        public RefreshIntervalDialog(int currentInterval, Action<int> onSaved)
        {
            _onSaved = onSaved ?? throw new ArgumentNullException(nameof(onSaved));

            Text = "库存涨跌刷新设置";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UIColors.MainBg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(UIUtils.S(420), UIUtils.S(214));

            var container = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UIUtils.S(18)),
                BackColor = UIColors.MainBg
            };
            Controls.Add(container);

            var group = new LiteSettingsGroup("页面刷新设置");
            group.AddHint("调节库存涨跌页面自动刷新并重新渲染数据显示的时间间隔。");

            var row = new Panel { Height = UIUtils.S(40), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var label = new Label
            {
                Text = "此页面刷新间隔",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _intervalInput = new LiteNumberInput(Math.Max(5, currentInterval).ToString(), "秒", "", 120);

            row.Controls.Add(label);
            row.Controls.Add(_intervalInput);
            row.Layout += (_, __) =>
            {
                int mid = row.Height / 2;
                label.Location = new Point(0, mid - label.Height / 2);
                _intervalInput.Location = new Point(UIUtils.S(140), mid - _intervalInput.Height / 2);
            };
            group.AddFullItem(row);

            _validationLabel = new Label
            {
                Height = UIUtils.S(24),
                Dock = DockStyle.Top,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextWarn,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            group.AddFullItem(_validationLabel);

            var actions = new Panel { Height = UIUtils.S(50), Dock = DockStyle.Top, BackColor = Color.Transparent };
            var save = new LiteButton("保存", true) { Width = UIUtils.S(90), Height = UIUtils.S(30) };
            var cancel = new LiteButton("取消", false) { Width = UIUtils.S(90), Height = UIUtils.S(30) };

            save.Click += (_, __) =>
            {
                int value = _intervalInput.ValueInt;
                if (value < 5)
                {
                    _validationLabel.Text = "刷新间隔不能低于 5 秒。";
                    _validationLabel.Visible = true;
                    return;
                }

                _validationLabel.Visible = false;
                _onSaved(value);
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (_, __) => Close();

            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            actions.Layout += (_, __) =>
            {
                int mid = actions.Height / 2;
                save.Location = new Point(0, mid - save.Height / 2);
                cancel.Location = new Point(save.Right + UIUtils.S(10), mid - cancel.Height / 2);
            };
            group.AddFullItem(actions);

            container.Controls.Add(group);
            group.Dock = DockStyle.Fill;
            UIColors.ApplyNativeThemeRecursively(this);
        }
    }
}
