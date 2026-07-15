using AntdSwitch = AntdUI.Switch;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinLandlordWeeklyFreeDialog : Form
    {
        private readonly AntdSwitch _enabled = new()
        {
            Size = UIUtils.S(new Size(48, 26))
        };
        private readonly LandlordNumberInput _minimum;
        private readonly LandlordNumberInput _maximum;
        private readonly LiteButton _cancel = new("取消", false) { Width = UIUtils.S(88) };
        private readonly LiteButton _save = new("保存", true) { Width = UIUtils.S(88) };

        private YouPinLandlordWeeklyFreeDialog(
            string scopeText,
            YouPinLandlordWeeklyFreeRule current)
        {
            Text = "周周免租设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = UIUtils.S(new Size(520, 286));
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UIColors.MainBg;
            ForeColor = UIColors.TextMain;
            Font = UIUtils.GetFont("Microsoft YaHei UI", 9F, false);

            _enabled.Checked = current.Enabled;
            _minimum = CreateMoneyInput(current.MinimumItemValue);
            _maximum = CreateMoneyInput(current.MaximumItemValue);

            var title = CreateLabel("周周免租", 13F, FontStyle.Bold, UIColors.TextMain);
            var scope = CreateLabel(scopeText, 8.8F, FontStyle.Regular, UIColors.TextSub);
            var enabledCaption = CreateLabel("启用规则", 9F, FontStyle.Regular, UIColors.TextSub);
            var rangeCaption = CreateLabel("饰品价值", 9F, FontStyle.Regular, UIColors.TextSub);
            var separator = CreateLabel("元 至", 9F, FontStyle.Regular, UIColors.TextSub);
            var unit = CreateLabel("元", 9F, FontStyle.Regular, UIColors.TextSub);
            var note = CreateLabel(
                "命中价值区间时，短租金必须严格低于 0.72；详细定价仍读取悠悠一键定价。",
                8.8F,
                FontStyle.Regular,
                UIColors.TextSub);
            note.AutoEllipsis = false;
            var divider = new Panel { BackColor = UIColors.Border };

            Controls.AddRange(new Control[]
            {
                title, scope, enabledCaption, _enabled,
                rangeCaption, _minimum, separator, _maximum, unit,
                note, divider, _cancel, _save
            });
            Layout += (_, __) =>
            {
                int pad = UIUtils.S(22);
                title.SetBounds(pad, UIUtils.S(18), ClientSize.Width - pad * 2, UIUtils.S(30));
                scope.SetBounds(pad, UIUtils.S(48), ClientSize.Width - pad * 2, UIUtils.S(24));
                enabledCaption.SetBounds(pad, UIUtils.S(86), UIUtils.S(92), UIUtils.S(30));
                _enabled.SetBounds(enabledCaption.Right, UIUtils.S(88), UIUtils.S(48), UIUtils.S(26));
                rangeCaption.SetBounds(pad, UIUtils.S(132), UIUtils.S(92), UIUtils.S(30));
                _minimum.SetBounds(rangeCaption.Right, UIUtils.S(130), UIUtils.S(92), UIUtils.S(32));
                separator.SetBounds(_minimum.Right + UIUtils.S(10), UIUtils.S(132), UIUtils.S(48), UIUtils.S(30));
                _maximum.SetBounds(separator.Right + UIUtils.S(8), UIUtils.S(130), UIUtils.S(92), UIUtils.S(32));
                unit.SetBounds(_maximum.Right + UIUtils.S(8), UIUtils.S(132), UIUtils.S(36), UIUtils.S(30));
                note.SetBounds(pad, UIUtils.S(174), ClientSize.Width - pad * 2, UIUtils.S(42));
                divider.SetBounds(0, UIUtils.S(224), ClientSize.Width, 1);
                _save.SetBounds(ClientSize.Width - pad - _save.Width, UIUtils.S(240), _save.Width, UIUtils.S(34));
                _cancel.SetBounds(_save.Left - UIUtils.S(10) - _cancel.Width, UIUtils.S(240), _cancel.Width, UIUtils.S(34));
            };

            _cancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            _save.Click += (_, __) => SaveRule();
            AcceptButton = _save;
            CancelButton = _cancel;
            UIColors.ApplyNativeThemeRecursively(this);
        }

        public YouPinLandlordWeeklyFreeRule ResultRule { get; private set; }
            = YouPinLandlordWeeklyFreeRule.Disabled;

        public static bool TryShow(
            IWin32Window? owner,
            string scopeText,
            YouPinLandlordWeeklyFreeRule current,
            out YouPinLandlordWeeklyFreeRule result)
        {
            using var dialog = new YouPinLandlordWeeklyFreeDialog(scopeText, current);
            DialogResult dialogResult = owner == null
                ? dialog.ShowDialog()
                : dialog.ShowDialog(owner);
            result = dialog.ResultRule;
            return dialogResult == DialogResult.OK;
        }

        private void SaveRule()
        {
            if (_maximum.Value < _minimum.Value)
            {
                GlobalPromptService.Show(
                    this,
                    "结束价值不能低于起始价值。",
                    "周周免租设置",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ResultRule = new YouPinLandlordWeeklyFreeRule(
                _enabled.Checked,
                _minimum.Value,
                _maximum.Value);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static LandlordNumberInput CreateMoneyInput(decimal value)
        {
            return new LandlordNumberInput(
                minimum: 0m,
                maximum: 1_000_000m,
                value: value,
                decimalPlaces: 2,
                validationMessage: "请输入 0 至 1000000 之间的金额，最多保留 2 位小数。")
            {
                Width = UIUtils.S(92)
            };
        }

        private static Label CreateLabel(
            string text,
            float size,
            FontStyle style,
            Color color)
        {
            return new Label
            {
                Text = text,
                AutoEllipsis = true,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = UIUtils.GetFont("Microsoft YaHei UI", size, style == FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }
    }
}
