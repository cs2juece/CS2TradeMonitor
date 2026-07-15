using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum BadgeTone
    {
        Info,
        Profit,
        Loss,
        Warn,
        Subtle
    }

    internal sealed record StopProfitLossDisplayRow(
        string Name,
        string TemplateId,
        bool Enabled,
        bool MissingCost,
        bool IsPreview,
        string Badge,
        string SecondaryBadge,
        string Detail,
        string ProfitText,
        string LossText,
        string ProfitTargetText,
        string LossTargetText,
        string MissingWarning,
        string Source)
    {
        public double Percent { get; init; }
        public bool IsNearTrigger { get; init; }
        public bool IsTriggered { get; init; }
        public bool ManualCost { get; init; }

        public BadgeTone BadgeTone => MissingCost
            ? BadgeTone.Warn
            : Source == "预览" ? BadgeTone.Subtle : BadgeTone.Info;

        public BadgeTone SecondaryBadgeTone => SecondaryBadge.Contains("只盯", StringComparison.Ordinal)
            ? BadgeTone.Profit
            : SecondaryBadge.Contains("手填", StringComparison.Ordinal)
                ? BadgeTone.Warn
            : BadgeTone.Info;

        public static StopProfitLossDisplayRow FromGroup(
            YouPinStopProfitLossMonitorGroup group,
            YouPinStopProfitLossItemRule? rule,
            double globalProfitPercent,
            double globalLossPercent,
            bool manualCost = false)
        {
            double percent = (group.CurrentUnitPrice - group.CostUnitPrice) / group.CostUnitPrice * 100.0;
            string sign = percent >= 0 ? "+" : "";
            bool profitEnabled = rule?.ProfitEnabled ?? true;
            bool lossEnabled = rule?.LossEnabled ?? true;
            double? profitPercent = rule?.ProfitPercent;
            double? lossPercent = rule?.LossPercent;
            string secondary = BuildSecondaryBadge(profitEnabled, lossEnabled, profitPercent, lossPercent);
            if (manualCost)
                secondary = string.IsNullOrWhiteSpace(secondary) ? "手填成本" : secondary + " · 手填成本";
            double effectiveProfit = profitPercent ?? globalProfitPercent;
            double effectiveLoss = lossPercent ?? globalLossPercent;
            bool enabled = rule?.Enabled ?? true;
            bool triggered = enabled
                && ((profitEnabled && percent >= effectiveProfit)
                    || (lossEnabled && percent <= -effectiveLoss));
            bool nearTrigger = enabled
                && !triggered
                && ((profitEnabled && percent < effectiveProfit && percent >= effectiveProfit - 5)
                    || (lossEnabled && percent > -effectiveLoss && percent <= -effectiveLoss + 5));
            return new StopProfitLossDisplayRow(
                group.Name,
                group.TemplateId,
                Enabled: enabled,
                MissingCost: false,
                IsPreview: false,
                Badge: group.Quantity > 1 ? $"×{group.Quantity} 件 · 均价" : "单件",
                SecondaryBadge: secondary,
                Detail: $"{(group.Quantity > 1 ? "均价" : "购入")} ¥{group.CostUnitPrice:0.##} → 现价 ¥{group.CurrentUnitPrice:0.##}    {sign}{percent:0.#}%",
                ProfitText: BuildDirectionText(profitEnabled, profitPercent, globalProfitPercent, positive: true),
                LossText: BuildDirectionText(lossEnabled, lossPercent, globalLossPercent, positive: false),
                ProfitTargetText: profitEnabled ? "→ ¥" + (group.CostUnitPrice * (1 + (profitPercent ?? globalProfitPercent) / 100.0)).ToString("0.##", CultureInfo.InvariantCulture) : "不触发止盈",
                LossTargetText: lossEnabled ? "→ ¥" + (group.CostUnitPrice * (1 - (lossPercent ?? globalLossPercent) / 100.0)).ToString("0.##", CultureInfo.InvariantCulture) : "不触发止损",
                MissingWarning: "",
                Source: "库存")
            {
                Percent = percent,
                IsNearTrigger = nearTrigger,
                IsTriggered = triggered,
                ManualCost = manualCost
            };
        }

        public static StopProfitLossDisplayRow FromMissingTrendRow(
            YouPinInventoryTrendRow row,
            double globalProfitPercent,
            double globalLossPercent)
        {
            return new StopProfitLossDisplayRow(
                row.Name,
                row.TemplateId,
                Enabled: false,
                MissingCost: true,
                IsPreview: false,
                Badge: "缺购入价",
                SecondaryBadge: "",
                Detail: row.CurrentPrice > 0 ? $"现价 ¥{row.CurrentPrice:0.##}    盈亏 —" : "暂无当前价    盈亏 —",
                ProfitText: $"+{globalProfitPercent:0.#}%",
                LossText: $"-{globalLossPercent:0.#}%",
                ProfitTargetText: "",
                LossTargetText: "",
                MissingWarning: "您尚未记录成本，手动填写后开始监控",
                Source: "库存")
            {
                Percent = 0,
                IsNearTrigger = false,
                IsTriggered = false,
                ManualCost = false
            };
        }

        private static string BuildDirectionText(bool enabled, double? customPercent, double globalPercent, bool positive)
        {
            if (!enabled)
                return "关";

            string sign = positive ? "+" : "-";
            return customPercent.HasValue
                ? $"{sign}{customPercent.Value:0.#}%"
                : $"{sign}{globalPercent:0.#}%";
        }

        private static string BuildSecondaryBadge(bool profitEnabled, bool lossEnabled, double? profitPercent, double? lossPercent)
        {
            if (profitEnabled && !lossEnabled)
                return "只盯止盈";
            if (!profitEnabled && lossEnabled)
                return "只盯止损";
            if (profitPercent.HasValue || lossPercent.HasValue)
                return "自定义";
            return "";
        }
    }

    internal static class YouPinStopProfitLossRedesignPalette
    {
        public static readonly Color ProfitColor = Color.FromArgb(255, 82, 105);
        public static readonly Color LossColor = Color.FromArgb(48, 198, 158);
    }

    internal sealed class YouPinStopProfitLossManualCostEntry
    {
        public string Name { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public double Cost { get; set; }
    }

    internal static class YouPinStopProfitLossManualCostStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static List<YouPinStopProfitLossManualCostEntry> LoadCosts(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<YouPinStopProfitLossManualCostEntry>();

            try
            {
                var costs = JsonSerializer.Deserialize<List<YouPinStopProfitLossManualCostEntry>>(json, JsonOptions);
                if (costs == null)
                    return new List<YouPinStopProfitLossManualCostEntry>();

                return costs
                    .Select(Normalize)
                    .Where(cost => cost.Cost > 0
                        && (!string.IsNullOrWhiteSpace(cost.Name) || !string.IsNullOrWhiteSpace(cost.TemplateId)))
                    .GroupBy(BuildIdentity, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch
            {
                return new List<YouPinStopProfitLossManualCostEntry>();
            }
        }

        public static string SaveCosts(IEnumerable<YouPinStopProfitLossManualCostEntry> costs)
        {
            var normalized = (costs ?? Enumerable.Empty<YouPinStopProfitLossManualCostEntry>())
                .Select(Normalize)
                .Where(cost => cost.Cost > 0
                    && (!string.IsNullOrWhiteSpace(cost.Name) || !string.IsNullOrWhiteSpace(cost.TemplateId)))
                .GroupBy(BuildIdentity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            return normalized.Count == 0 ? "" : JsonSerializer.Serialize(normalized, JsonOptions);
        }

        public static List<YouPinStopProfitLossManualCostEntry> UpsertCost(
            string? json,
            string name,
            string templateId,
            double cost)
        {
            var costs = LoadCosts(json);
            var entry = new YouPinStopProfitLossManualCostEntry
            {
                Name = name,
                TemplateId = templateId,
                Cost = cost
            };
            string identity = BuildIdentity(Normalize(entry));
            costs = costs
                .Where(item => !string.Equals(BuildIdentity(item), identity, StringComparison.OrdinalIgnoreCase))
                .ToList();
            costs.Add(Normalize(entry));
            return costs;
        }

        public static bool TryFindCost(
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> costs,
            string name,
            string templateId,
            out double cost)
        {
            cost = 0;
            if (costs == null || costs.Count == 0)
                return false;

            var match = costs.FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(item.TemplateId)
                    && !string.IsNullOrWhiteSpace(templateId)
                    && string.Equals(item.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(name)
                    && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
            if (match == null || match.Cost <= 0)
                return false;

            cost = match.Cost;
            return true;
        }

        public static List<YouPinInventoryItem> ApplyManualCostsToItems(
            IEnumerable<YouPinInventoryItem> items,
            IReadOnlyList<YouPinStopProfitLossManualCostEntry> costs)
        {
            return (items ?? Enumerable.Empty<YouPinInventoryItem>())
                .Select(item =>
                {
                    var clone = new YouPinInventoryItem
                    {
                        AssetId = item.AssetId,
                        TemplateId = item.TemplateId,
                        Name = item.Name,
                        Price = item.Price,
                        PurchasePrice = item.PurchasePrice,
                        Quantity = item.Quantity,
                        RawStatus = item.RawStatus
                    };
                    if (clone.PurchasePrice <= 0
                        && TryFindCost(costs, clone.Name, clone.TemplateId, out double manualCost))
                    {
                        clone.PurchasePrice = manualCost;
                    }

                    return clone;
                })
                .ToList();
        }

        private static YouPinStopProfitLossManualCostEntry Normalize(YouPinStopProfitLossManualCostEntry? entry)
        {
            entry ??= new YouPinStopProfitLossManualCostEntry();
            entry.Name = (entry.Name ?? string.Empty).Trim();
            entry.TemplateId = (entry.TemplateId ?? string.Empty).Trim();
            entry.Cost = Math.Round(Math.Clamp(entry.Cost, 0.01, 10_000_000), 2);
            return entry;
        }

        private static string BuildIdentity(YouPinStopProfitLossManualCostEntry entry)
        {
            return !string.IsNullOrWhiteSpace(entry.TemplateId)
                ? "T:" + entry.TemplateId
                : "N:" + entry.Name;
        }
    }

    internal static class YouPinStopProfitLossRedesignUiFactory
    {
        public static Label CreateHeaderDescription(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Height = UIUtils.S(24),
                Font = UIFonts.Regular(8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Control CreateStatusTile(string title, Label value, Control? leftControl = null)
        {
            var tile = new FlatPanel
            {
                Padding = UIUtils.S(new Padding(12, 8, 12, 8))
            };
            var titleLabel = CreateSubLabel(title);
            tile.Controls.Add(titleLabel);
            if (leftControl != null)
                tile.Controls.Add(leftControl);
            tile.Controls.Add(value);
            tile.Layout += (_, __) =>
            {
                titleLabel.SetBounds(UIUtils.S(10), UIUtils.S(8), tile.Width - UIUtils.S(20), UIUtils.S(20));
                int valueTop = UIUtils.S(40);
                if (leftControl != null)
                {
                    leftControl.SetBounds(UIUtils.S(10), valueTop - UIUtils.S(4), leftControl.Width, leftControl.Height);
                    value.SetBounds(leftControl.Right + UIUtils.S(10), valueTop, Math.Max(1, tile.Width - leftControl.Right - UIUtils.S(20)), UIUtils.S(28));
                }
                else
                {
                    value.SetBounds(UIUtils.S(10), valueTop, tile.Width - UIUtils.S(20), UIUtils.S(28));
                }
            };
            return tile;
        }

        public static Control CreateKpiTile(string title, Label value, bool warn = false)
        {
            var tile = new FlatPanel
            {
                Margin = UIUtils.S(new Padding(0, 0, 10, 0)),
                Padding = UIUtils.S(new Padding(12, 8, 12, 8))
            };
            if (warn)
                tile.BorderColorOverride = Color.FromArgb(150, 190, 125, 15);
            var titleLabel = CreateSubLabel(title);
            tile.Controls.Add(titleLabel);
            tile.Controls.Add(value);
            tile.Layout += (_, __) =>
            {
                titleLabel.SetBounds(UIUtils.S(10), UIUtils.S(8), tile.Width - UIUtils.S(20), UIUtils.S(20));
                value.SetBounds(UIUtils.S(10), UIUtils.S(32), tile.Width - UIUtils.S(20), UIUtils.S(34));
            };
            return tile;
        }

        public static Control CreateRecentAlertRow(YouPinStopProfitLossAlert alert)
        {
            var row = new FlatPanel
            {
                Height = UIUtils.S(42),
                Padding = UIUtils.S(new Padding(10, 5, 10, 5))
            };
            var direction = CreateBadge(alert.Direction, alert.Direction == "止盈" ? BadgeTone.Profit : BadgeTone.Loss);
            var name = CreateStrongLabel(alert.Name);
            var detail = CreateSubLabel($"均价 ¥{alert.OldUnitPrice:0.##} → ¥{alert.NewUnitPrice:0.##}   {YouPinStopProfitLossPageModel.FormatSignedPercent(alert.Percent)}");
            detail.ForeColor = alert.Percent >= 0
                ? YouPinStopProfitLossRedesignPalette.ProfitColor
                : YouPinStopProfitLossRedesignPalette.LossColor;
            var time = CreateSubLabel(alert.Time.ToString("HH:mm", CultureInfo.InvariantCulture));
            time.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.Add(direction);
            row.Controls.Add(name);
            row.Controls.Add(detail);
            row.Controls.Add(time);
            row.Layout += (_, __) =>
            {
                direction.SetBounds(UIUtils.S(8), UIUtils.S(9), direction.Width, direction.Height);
                int nameLeft = direction.Right + UIUtils.S(10);
                name.SetBounds(nameLeft, UIUtils.S(6), Math.Max(UIUtils.S(160), row.Width / 3), UIUtils.S(26));
                time.SetBounds(row.Width - UIUtils.S(56), UIUtils.S(6), UIUtils.S(48), UIUtils.S(26));
                detail.SetBounds(row.Width - UIUtils.S(360), UIUtils.S(6), UIUtils.S(292), UIUtils.S(26));
            };
            return row;
        }

        public static Label CreateValueLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = UIFonts.Bold(11F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Label CreateKpiValueLabel(string text)
        {
            var label = CreateValueLabel(text);
            label.Font = UIFonts.Bold(14F);
            return label;
        }

        public static Label CreateSubLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = UIFonts.Regular(8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        public static Label CreateHintLabel(string text)
        {
            var label = CreateSubLabel("ⓘ " + text);
            label.Height = UIUtils.S(28);
            label.Dock = DockStyle.Top;
            return label;
        }

        public static Label CreatePillLabel(string text)
        {
            var label = new PillLabel
            {
                Text = text,
                Height = UIUtils.S(28),
                Font = UIFonts.Bold(9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleCenter
            };
            return label;
        }

        public static Label CreateBadge(string text, BadgeTone tone)
        {
            var badge = new BadgeLabel(tone)
            {
                Text = text,
                Width = Math.Max(UIUtils.S(52), TextRenderer.MeasureText(text, UIFonts.Bold(8F)).Width + UIUtils.S(14)),
                Height = UIUtils.S(22),
                Font = UIFonts.Bold(8F),
                TextAlign = ContentAlignment.MiddleCenter
            };
            return badge;
        }

        public static Label CreateChip(string text)
        {
            var chip = new PillLabel
            {
                Text = text,
                Width = Math.Max(UIUtils.S(128), TextRenderer.MeasureText(text, UIFonts.Regular(9F)).Width + UIUtils.S(18)),
                Height = UIUtils.S(28),
                Font = UIFonts.Regular(9F),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Margin = UIUtils.S(new Padding(0, 0, 8, 0))
            };
            return chip;
        }

        public static Control CreateDivider()
        {
            var panel = new Panel { Height = UIUtils.S(10), BackColor = Color.Transparent };
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, panel.Height / 2, panel.Width, panel.Height / 2);
            };
            return panel;
        }

        private static Label CreateStrongLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = UIFonts.Bold(9F),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }
    }

    internal sealed class FlatPanel : Panel
    {
        public Color? BorderColorOverride { get; set; }

        public FlatPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(6));
            using var fill = new SolidBrush(UIColors.IsDark ? Color.FromArgb(18, 25, 34) : UIColors.CardBg);
            using var border = new Pen(BorderColorOverride ?? UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }

    internal sealed class SummaryTilesPanel : Panel
    {
        public SummaryTilesPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
        }

        public Action<Graphics, Size>? PaintTiles { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new SolidBrush(UIColors.IsDark ? Color.FromArgb(18, 25, 34) : UIColors.CardBg);
            e.Graphics.FillRectangle(fill, ClientRectangle);
            PaintTiles?.Invoke(e.Graphics, ClientSize);
        }
    }

    internal sealed class PillLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var parent = new SolidBrush(Parent?.BackColor == Color.Transparent ? UIColors.CardBg : Parent?.BackColor ?? UIColors.CardBg);
            e.Graphics.FillRectangle(parent, ClientRectangle);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(5));
            using var fill = new SolidBrush(UIColors.ControlBg);
            using var border = new Pen(UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class BadgeLabel : Label
    {
        private readonly BadgeTone _tone;

        public BadgeLabel(BadgeTone tone)
        {
            _tone = tone;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var colors = ResolveColors(_tone);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(4));
            using var fill = new SolidBrush(colors.Fill);
            using var border = new Pen(colors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, colors.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static (Color Fill, Color Border, Color Text) ResolveColors(BadgeTone tone)
        {
            return tone switch
            {
                BadgeTone.Profit => (Color.FromArgb(45, 120, 36, 50), Color.FromArgb(170, YouPinStopProfitLossRedesignPalette.ProfitColor), YouPinStopProfitLossRedesignPalette.ProfitColor),
                BadgeTone.Loss => (Color.FromArgb(45, 26, 108, 90), Color.FromArgb(170, YouPinStopProfitLossRedesignPalette.LossColor), YouPinStopProfitLossRedesignPalette.LossColor),
                BadgeTone.Warn => (Color.FromArgb(45, 155, 103, 0), Color.FromArgb(180, 214, 151, 20), UIColors.TextWarn),
                BadgeTone.Subtle => (UIColors.ControlBg, UIColors.Border, UIColors.TextSub),
                _ => (Color.FromArgb(45, UIColors.Primary), Color.FromArgb(160, UIColors.Primary), UIColors.Primary)
            };
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = UIUtils.S(58);
            Height = UIUtils.S(30);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(0, UIUtils.S(2), Width - 1, Height - UIUtils.S(4));
            using var path = UIUtils.RoundRect(track, track.Height / 2);
            using var fill = new SolidBrush(Checked ? UIColors.Primary : UIColors.ControlBg);
            using var border = new Pen(Checked ? UIColors.Primary : UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            int thumb = UIUtils.S(22);
            int x = Checked ? Width - thumb - UIUtils.S(5) : UIUtils.S(5);
            var thumbRect = new Rectangle(x, (Height - thumb) / 2, thumb, thumb);
            using var thumbBrush = new SolidBrush(Color.White);
            e.Graphics.FillEllipse(thumbBrush, thumbRect);
        }
    }

    internal sealed class ThresholdSlider : Control
    {
        private bool _dragging;
        private int _value;

        public event EventHandler? ValueChanged;

        public ThresholdSlider(Color accent)
        {
            Accent = accent;
            Minimum = 1;
            Maximum = 100;
            Height = UIUtils.S(24);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public Color Accent { get; }
        public int Minimum { get; set; }
        public int Maximum { get; set; }

        public int Value
        {
            get => _value;
            set
            {
                int normalized = Math.Clamp(value, Minimum, Maximum);
                if (_value == normalized)
                    return;

                _value = normalized;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
                return;

            _dragging = true;
            Capture = true;
            SetValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
                SetValueFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(ResolveParentBackColor()))
                e.Graphics.FillRectangle(background, ClientRectangle);

            int y = Height / 2;
            int left = UIUtils.S(4);
            int right = Width - UIUtils.S(4);
            Color trackColor = UIColors.IsDark ? Color.FromArgb(41, 52, 65) : UIColors.ControlBg;
            Color thumbColor = UIColors.IsDark ? Color.FromArgb(225, 234, 242) : UIColors.TextMain;
            using var trackPen = new Pen(trackColor, UIUtils.S(6)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var fillPen = new Pen(Accent, UIUtils.S(6)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(trackPen, left, y, right, y);
            int thumbX = ValueToX();
            e.Graphics.DrawLine(fillPen, left, y, thumbX, y);
            using var thumb = new SolidBrush(thumbColor);
            e.Graphics.FillEllipse(thumb, thumbX - UIUtils.S(8), y - UIUtils.S(8), UIUtils.S(16), UIUtils.S(16));
        }

        private Color ResolveParentBackColor()
        {
            Control? current = Parent;
            while (current != null && current.BackColor == Color.Transparent)
                current = current.Parent;
            return current?.BackColor ?? UIColors.CardBg;
        }

        private void SetValueFromMouse(int x)
        {
            int left = UIUtils.S(4);
            int right = Math.Max(left + 1, Width - UIUtils.S(4));
            double ratio = Math.Clamp((x - left) / (double)(right - left), 0, 1);
            Value = (int)Math.Round(Minimum + ratio * (Maximum - Minimum));
        }

        private int ValueToX()
        {
            int left = UIUtils.S(4);
            int right = Math.Max(left + 1, Width - UIUtils.S(4));
            double ratio = Maximum == Minimum ? 0 : (Value - Minimum) / (double)(Maximum - Minimum);
            return left + (int)Math.Round((right - left) * ratio);
        }
    }

    internal sealed class StepperButton : Control
    {
        private readonly Color _accent;

        public event EventHandler? DecreaseClicked;
        public event EventHandler? ValueClicked;
        public event EventHandler? IncreaseClicked;

        public StepperButton(string text, Color accent)
        {
            _accent = accent;
            Text = text;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            ForeColor = accent;
            Font = UIFonts.Bold(8.5F);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!Enabled || e.Button != MouseButtons.Left)
                return;

            if (!ClientRectangle.Contains(e.Location))
                return;

            int third = Math.Max(1, Width / 3);
            if (e.X < third)
            {
                DecreaseClicked?.Invoke(this, EventArgs.Empty);
            }
            else if (e.X >= Width - third)
            {
                IncreaseClicked?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ValueClicked?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(5));
            using var fill = new SolidBrush(Enabled ? UIColors.ControlBg : UIColors.ControlDisabledBg);
            using var border = new Pen(Enabled ? Color.FromArgb(120, _accent) : UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            using var divider = new Pen(Enabled ? Color.FromArgb(55, UIColors.Border) : Color.FromArgb(35, UIColors.Border));
            int third = Math.Max(1, Width / 3);
            e.Graphics.DrawLine(divider, third, UIUtils.S(5), third, Height - UIUtils.S(5));
            e.Graphics.DrawLine(divider, Width - third, UIUtils.S(5), Width - third, Height - UIUtils.S(5));

            TextRenderer.DrawText(e.Graphics, "-", Font, new Rectangle(0, 0, third, Height), Enabled ? _accent : UIColors.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, ExtractValueText(Text), Font, new Rectangle(third, 0, Width - third * 2, Height), Enabled ? _accent : UIColors.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, "+", Font, new Rectangle(Width - third, 0, third, Height), Enabled ? _accent : UIColors.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static string ExtractValueText(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.StartsWith("-", StringComparison.Ordinal))
                value = value[1..].TrimStart();
            if (value.EndsWith("+", StringComparison.Ordinal))
                value = value[..^1].TrimEnd();
            return value.Trim();
        }
    }
}
