using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinGridStrategyDialog : Form
    {
        private readonly LiteUnderlineInput _itemName;
        private readonly LiteUnderlineInput _templateId;
        private readonly NumericUpDown _basePrice;
        private readonly NumericUpDown _gridPercent;
        private readonly NumericUpDown _quantityPerGrid;
        private readonly NumericUpDown _minimumPrice;
        private readonly NumericUpDown _maximumPrice;
        private readonly NumericUpDown _minimumHoldings;
        private readonly NumericUpDown _maximumHoldings;
        private readonly NumericUpDown _maxCapital;
        private readonly NumericUpDown _maxBatchQuantity;
        private readonly LiteCheck _enabled;
        private readonly LiteCheck _crossGrid;
        private readonly LiteCheck _automaticExecution;
        private readonly AutoQuoteBadgeLabel _executionBadge;
        private readonly Label _executionHint;
        private readonly ListBox _inventoryList;
        private readonly IReadOnlyList<InventoryChoice> _inventoryChoices;
        private readonly string _strategyId;
        private readonly bool _wasAutomaticExecution;

        private YouPinGridStrategyDialog(
            YouPinGridStrategy? current,
            IReadOnlyList<YouPinInventoryItem> inventoryItems)
        {
            _strategyId = string.IsNullOrWhiteSpace(current?.Id)
                ? Guid.NewGuid().ToString("N")
                : current.Id;
            _wasAutomaticExecution = current?.ObserveOnly == false;
            _inventoryChoices = (inventoryItems ?? Array.Empty<YouPinInventoryItem>())
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.TemplateId))
                .GroupBy(item => item.TemplateId.Trim() + "\n" + item.Name.Trim(), StringComparer.Ordinal)
                .Select(group => new InventoryChoice(
                    group.First().Name.Trim(),
                    group.First().TemplateId.Trim(),
                    group.Sum(item => Math.Max(1, item.Quantity)),
                    group.Where(item => item.Price > 0).Select(item => item.Price).DefaultIfEmpty(0d).Min()))
                .OrderBy(choice => choice.Name, StringComparer.Ordinal)
                .ToArray();

            Text = current == null ? "新建交易网格策略" : "编辑交易网格策略";
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = UIUtils.S(new Size(820, 700));
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UIColors.MainBg;
            ForeColor = UIColors.TextMain;
            Font = UIUtils.GetFont("Microsoft YaHei UI", 9F, false);

            _itemName = new LiteUnderlineInput(current?.ItemName ?? string.Empty);
            _itemName.Placeholder = "完整饰品名称（包含品质与磨损）";
            _templateId = new LiteUnderlineInput(current?.TemplateId ?? string.Empty);
            _templateId.Placeholder = "悠悠模板 ID";
            _basePrice = CreateMoneyInput(current?.BasePrice ?? 0m);
            _gridPercent = CreateDecimalInput(current?.GridPercent ?? 5m, 0.01m, 99.99m, 2);
            _quantityPerGrid = CreateIntegerInput(current?.QuantityPerGrid ?? 1, 1, 100);
            _minimumPrice = CreateMoneyInput(current?.MinimumPrice ?? 0m);
            _maximumPrice = CreateMoneyInput(current?.MaximumPrice ?? 0m);
            _minimumHoldings = CreateIntegerInput(current?.MinimumHoldings ?? 0, 0, 10000);
            _maximumHoldings = CreateIntegerInput(current?.MaxHoldings ?? 5, 1, 10000);
            _maxCapital = CreateMoneyInput(current?.MaxCapital ?? 0m);
            _maxBatchQuantity = CreateIntegerInput(current?.MaxBatchQuantity ?? 3, 1, 100);
            _enabled = new LiteCheck(current?.Enabled ?? true, "启用策略") { Width = UIUtils.S(118) };
            _crossGrid = new LiteCheck(current?.CrossGridMultiplierEnabled ?? false, "跨格时按格数放大数量") { Width = UIUtils.S(214) };
            _automaticExecution = new LiteCheck(_wasAutomaticExecution, "自动执行真实买卖") { Width = UIUtils.S(170) };
            _executionBadge = new AutoQuoteBadgeLabel
            {
                Font = UIFonts.Bold(8F)
            };
            _executionHint = Label(string.Empty, 8.2F, FontStyle.Regular, UIColors.TextSub);
            _automaticExecution.CheckedChanged += (_, __) => UpdateExecutionModeUi();
            _inventoryList = new ListBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.InputBg,
                ForeColor = UIColors.TextMain,
                Font = Font,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                ItemHeight = UIUtils.S(32)
            };
            foreach (InventoryChoice choice in _inventoryChoices)
                _inventoryList.Items.Add(choice);
            _inventoryList.HorizontalExtent = _inventoryChoices.Count == 0
                ? 0
                : _inventoryChoices.Max(choice =>
                    TextRenderer.MeasureText(choice.ToString(), _inventoryList.Font).Width) + UIUtils.S(12);
            _inventoryList.DoubleClick += (_, __) => ApplySelectedInventory();

            BuildUi(current == null);
            UpdateExecutionModeUi();
            UIColors.ApplyNativeThemeRecursively(this);
        }

        public YouPinGridStrategy ResultStrategy { get; private set; } = new();

        public static bool TryShow(
            IWin32Window? owner,
            YouPinGridStrategy? current,
            IReadOnlyList<YouPinInventoryItem> inventoryItems,
            out YouPinGridStrategy result)
        {
            using var dialog = new YouPinGridStrategyDialog(current, inventoryItems);
            Form? stableOwner = GlobalPromptPositioning.ResolveStableOwner(owner, dialog.Size);
            Screen screen = GlobalPromptPositioning.ResolveScreen(owner, stableOwner);
            dialog.Location = GlobalPromptPositioning.CalculateLocation(
                screen.WorkingArea,
                dialog.Size,
                stableOwner?.Bounds);
            DialogResult dialogResult = stableOwner != null
                ? dialog.ShowDialog(stableOwner)
                : dialog.ShowDialog();
            result = dialog.ResultStrategy;
            return dialogResult == DialogResult.OK;
        }

        private void BuildUi(bool isNew)
        {
            var title = Label(Text, 12F, FontStyle.Bold, UIColors.TextMain);
            var subtitle = Label(
                "同款判断只认悠悠模板 ID 与完整饰品名称；每次交易前会重新读取悠悠行情和库存。",
                8.8F,
                FontStyle.Regular,
                UIColors.TextSub);
            var inventoryCard = CreateCard();
            var settingsCard = CreateCard();
            var inventoryTitle = Label("从当前悠悠库存带入", 10F, FontStyle.Bold, UIColors.TextMain);
            var inventoryHint = Label(
                "双击库存项，自动填入完整名称、模板 ID 与当前估值。",
                8.4F,
                FontStyle.Regular,
                UIColors.TextSub);
            var useInventory = new LiteButton("使用选中库存", false)
            {
                Width = UIUtils.S(130),
                Height = UIUtils.S(32),
                Enabled = _inventoryChoices.Count > 0
            };
            useInventory.Click += (_, __) => ApplySelectedInventory();
            var emptyInventory = Label(
                _inventoryChoices.Count == 0 ? "当前没有可带入的悠悠库存，仍可在右侧手动填写。" : string.Empty,
                8.5F,
                FontStyle.Regular,
                UIColors.TextWarn,
                ContentAlignment.MiddleCenter);
            emptyInventory.Visible = _inventoryChoices.Count == 0;

            var settingsTitle = Label("网格参数", 10F, FontStyle.Bold, UIColors.TextMain);
            var itemNameLabel = Label("完整饰品名称", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var templateLabel = Label("悠悠模板 ID", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var baseLabel = Label("初始基准价", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var gridLabel = Label("每格涨跌比例", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var quantityLabel = Label("每格数量", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var batchLabel = Label("跨格最大批量", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var minimumPriceLabel = Label("有效最低价（0=不限）", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var maximumPriceLabel = Label("有效最高价（0=不限）", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var minimumHoldingsLabel = Label("最低保留件数", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var maximumHoldingsLabel = Label("最大持有件数", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var capitalLabel = Label("最大占用资金（估算，0=不限）", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var cancel = new LiteButton("取消", false) { Width = UIUtils.S(90), Height = UIUtils.S(34) };
            var save = new LiteButton(isNew ? "创建策略" : "保存修改", true) { Width = UIUtils.S(100), Height = UIUtils.S(34) };
            cancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            save.Click += (_, __) => SaveStrategy();
            AcceptButton = save;
            CancelButton = cancel;

            Controls.AddRange(new Control[]
            {
                title, subtitle, inventoryCard, settingsCard, cancel, save
            });
            inventoryCard.Controls.AddRange(new Control[]
            {
                inventoryTitle, inventoryHint, _inventoryList, emptyInventory, useInventory
            });
            settingsCard.Controls.AddRange(new Control[]
            {
                settingsTitle,
                itemNameLabel, _itemName,
                templateLabel, _templateId,
                baseLabel, _basePrice,
                gridLabel, _gridPercent,
                quantityLabel, _quantityPerGrid,
                batchLabel, _maxBatchQuantity,
                minimumPriceLabel, _minimumPrice,
                maximumPriceLabel, _maximumPrice,
                minimumHoldingsLabel, _minimumHoldings,
                maximumHoldingsLabel, _maximumHoldings,
                capitalLabel, _maxCapital,
                _enabled, _crossGrid, _automaticExecution, _executionBadge, _executionHint
            });

            inventoryCard.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                inventoryTitle.SetBounds(pad, UIUtils.S(16), inventoryCard.Width - pad * 2, UIUtils.S(26));
                inventoryHint.SetBounds(pad, UIUtils.S(42), inventoryCard.Width - pad * 2, UIUtils.S(42));
                _inventoryList.SetBounds(pad, UIUtils.S(90), inventoryCard.Width - pad * 2, UIUtils.S(348));
                emptyInventory.SetBounds(pad + UIUtils.S(10), UIUtils.S(170), inventoryCard.Width - pad * 2 - UIUtils.S(20), UIUtils.S(64));
                useInventory.SetBounds(inventoryCard.Width - pad - useInventory.Width, UIUtils.S(460), useInventory.Width, useInventory.Height);
            };

            settingsCard.Layout += (_, __) =>
            {
                int pad = UIUtils.S(18);
                int width = settingsCard.Width - pad * 2;
                int gap = UIUtils.S(14);
                int half = Math.Max(UIUtils.S(120), (width - gap) / 2);
                settingsTitle.SetBounds(pad, UIUtils.S(15), width, UIUtils.S(26));
                itemNameLabel.SetBounds(pad, UIUtils.S(48), width, UIUtils.S(20));
                _itemName.SetBounds(pad, UIUtils.S(68), width, UIUtils.S(30));
                templateLabel.SetBounds(pad, UIUtils.S(105), width, UIUtils.S(20));
                _templateId.SetBounds(pad, UIUtils.S(125), width, UIUtils.S(30));
                LayoutPair(baseLabel, _basePrice, gridLabel, _gridPercent, pad, UIUtils.S(164), half, gap);
                LayoutPair(quantityLabel, _quantityPerGrid, batchLabel, _maxBatchQuantity, pad, UIUtils.S(220), half, gap);
                LayoutPair(minimumPriceLabel, _minimumPrice, maximumPriceLabel, _maximumPrice, pad, UIUtils.S(276), half, gap);
                LayoutPair(minimumHoldingsLabel, _minimumHoldings, maximumHoldingsLabel, _maximumHoldings, pad, UIUtils.S(332), half, gap);
                capitalLabel.SetBounds(pad, UIUtils.S(392), half, UIUtils.S(20));
                _maxCapital.SetBounds(pad, UIUtils.S(412), half, UIUtils.S(32));
                _enabled.SetBounds(pad, UIUtils.S(452), UIUtils.S(118), UIUtils.S(24));
                _crossGrid.SetBounds(pad + UIUtils.S(128), UIUtils.S(452), Math.Max(UIUtils.S(170), width - UIUtils.S(128)), UIUtils.S(24));
                _automaticExecution.SetBounds(pad, UIUtils.S(478), UIUtils.S(170), UIUtils.S(24));
                _executionBadge.SetBounds(pad, UIUtils.S(508), width, UIUtils.S(26));
                _executionHint.SetBounds(pad, UIUtils.S(535), width, UIUtils.S(20));
            };

            Layout += (_, __) =>
            {
                YouPinGridStrategyDialogLayout layout = YouPinGridStrategyDialogLayoutModel.Build(
                    ClientSize,
                    cancel.Size,
                    save.Size,
                    UIUtils.ScaleFactor);
                title.Bounds = layout.Title;
                subtitle.Bounds = layout.Subtitle;
                inventoryCard.Bounds = layout.InventoryCard;
                settingsCard.Bounds = layout.SettingsCard;
                cancel.Bounds = layout.CancelButton;
                save.Bounds = layout.SaveButton;
            };
        }

        private void ApplySelectedInventory()
        {
            if (_inventoryList.SelectedItem is not InventoryChoice selected)
            {
                if (_inventoryChoices.Count == 1)
                    selected = _inventoryChoices[0];
                else
                    return;
            }

            _itemName.Inner.Text = selected.Name;
            _templateId.Inner.Text = selected.TemplateId;
            if (selected.Price > 0d)
                _basePrice.Value = ClampDecimal((decimal)selected.Price, _basePrice.Minimum, _basePrice.Maximum);
            _maximumHoldings.Value = ClampDecimal(
                Math.Max(1, selected.Quantity + 1),
                _maximumHoldings.Minimum,
                _maximumHoldings.Maximum);
        }

        private void SaveStrategy()
        {
            string itemName = _itemName.Inner.Text.Trim();
            string templateId = _templateId.Inner.Text.Trim();
            if (itemName.Length == 0 || templateId.Length == 0)
            {
                ShowValidation("请填写完整饰品名称和悠悠模板 ID。");
                return;
            }
            if (_basePrice.Value <= 0m)
            {
                ShowValidation("初始基准价必须大于 0。");
                return;
            }
            if (_maximumPrice.Value > 0m
                && _minimumPrice.Value > 0m
                && _maximumPrice.Value < _minimumPrice.Value)
            {
                ShowValidation("有效最高价不能低于有效最低价。");
                return;
            }
            if (_maximumHoldings.Value < _minimumHoldings.Value)
            {
                ShowValidation("最大持有件数不能小于最低保留件数。");
                return;
            }
            if (_automaticExecution.Checked && !_wasAutomaticExecution)
            {
                DialogResult confirm = GlobalPromptService.Show(
                    this,
                    "开启后，满足网格条件时会使用悠悠余额自动买入，或把符合规则的同款库存自动上架出售。\n\n" +
                    "系统只认悠悠模板 ID、完整饰品名称、悠悠商品/资产编号，并以悠悠订单状态回读为准。\n\n" +
                    "确认开启真实自动交易？",
                    "开启交易网格自动执行",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                    return;
            }

            ResultStrategy = new YouPinGridStrategy
            {
                Id = _strategyId,
                ItemName = itemName,
                TemplateId = templateId,
                Enabled = _enabled.Checked,
                ObserveOnly = !_automaticExecution.Checked,
                BasePrice = _basePrice.Value,
                GridPercent = _gridPercent.Value,
                QuantityPerGrid = decimal.ToInt32(_quantityPerGrid.Value),
                MinimumPrice = _minimumPrice.Value,
                MaximumPrice = _maximumPrice.Value,
                MinimumHoldings = decimal.ToInt32(_minimumHoldings.Value),
                MaxHoldings = decimal.ToInt32(_maximumHoldings.Value),
                MaxCapital = _maxCapital.Value,
                CrossGridMultiplierEnabled = _crossGrid.Checked,
                MaxBatchQuantity = decimal.ToInt32(_maxBatchQuantity.Value)
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateExecutionModeUi()
        {
            if (_automaticExecution.Checked)
            {
                _executionBadge.Text = "自动执行｜余额买入 · 库存上架 · 悠悠订单回读";
                _executionBadge.Tone = AutoQuoteBadgeTone.Success;
                _executionHint.Text = "每次网格触发只处理 1 件；未完成订单仅回读，不会追加下单。";
            }
            else
            {
                _executionBadge.Text = "观察模式｜只计算，不下单";
                _executionBadge.Tone = AutoQuoteBadgeTone.Warn;
                _executionHint.Text = "只读取真实行情和计算触发条件，不会提交悠悠买卖订单。";
            }

            bool quantityControlsEnabled = !_automaticExecution.Checked;
            _quantityPerGrid.Enabled = quantityControlsEnabled;
            _maxBatchQuantity.Enabled = quantityControlsEnabled;
            _crossGrid.Enabled = quantityControlsEnabled;

            _executionBadge.Invalidate();
        }

        private void ShowValidation(string message)
        {
            GlobalPromptService.Show(
                this,
                message,
                "交易网格参数",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static void LayoutPair(
            Label leftLabel,
            NumericUpDown leftInput,
            Label rightLabel,
            NumericUpDown rightInput,
            int left,
            int top,
            int width,
            int gap)
        {
            leftLabel.SetBounds(left, top, width, UIUtils.S(20));
            leftInput.SetBounds(left, top + UIUtils.S(20), width, UIUtils.S(32));
            rightLabel.SetBounds(left + width + gap, top, width, UIUtils.S(20));
            rightInput.SetBounds(left + width + gap, top + UIUtils.S(20), width, UIUtils.S(32));
        }

        private static NumericUpDown CreateMoneyInput(decimal value)
        {
            return CreateDecimalInput(value, 0m, 1_000_000m, 2);
        }

        private static NumericUpDown CreateIntegerInput(int value, int minimum, int maximum)
        {
            return CreateDecimalInput(value, minimum, maximum, 0);
        }

        private static NumericUpDown CreateDecimalInput(
            decimal value,
            decimal minimum,
            decimal maximum,
            int decimalPlaces)
        {
            return new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                DecimalPlaces = decimalPlaces,
                Increment = decimalPlaces == 0 ? 1m : 0.01m,
                Value = ClampDecimal(value, minimum, maximum),
                ThousandsSeparator = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UIColors.InputBg,
                ForeColor = UIColors.TextMain,
                Font = UIUtils.GetFont("Microsoft YaHei UI", 9F, false),
                TextAlign = HorizontalAlignment.Right
            };
        }

        private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
        {
            return Math.Min(maximum, Math.Max(minimum, value));
        }

        private static YouPinCcRoundedPanel CreateCard()
        {
            return new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(7),
                FillOverride = UIColors.CardBg
            };
        }

        private static Label Label(
            string text,
            float size,
            FontStyle style,
            Color color,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return YouPinCcUi.Label(text, size, style, color, align);
        }

        private sealed record InventoryChoice(
            string Name,
            string TemplateId,
            int Quantity,
            double Price)
        {
            public override string ToString()
            {
                string price = Price > 0d ? $" · ¥{Price:0.00}" : string.Empty;
                return $"{Name}  ×{Quantity}{price}";
            }
        }
    }
}
