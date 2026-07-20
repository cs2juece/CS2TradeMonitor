using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class FrameworkTopTabHeader<TTab> : Panel where TTab : notnull
    {
        private readonly List<TabEntry> _entries = new();
        private readonly Font _regularFont = new("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        private readonly Font _activeFont = new("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
        private TTab _activeTab;

        public FrameworkTopTabHeader(
            IReadOnlyList<FrameworkTopTabItem<TTab>> items,
            TTab activeTab,
            string accessiblePrefix = "顶部导航")
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("At least one top tab item is required.", nameof(items));

            _activeTab = activeTab;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint, true);
            Dock = DockStyle.Top;
            Height = UIUtils.S(40);
            BackColor = Color.Transparent;
            Padding = new Padding(0, 0, 0, UIUtils.S(2));

            foreach (FrameworkTopTabItem<TTab> item in items)
            {
                var label = CreateTabLabel(item, accessiblePrefix);
                _entries.Add(new TabEntry(item, label));
                Controls.Add(label);
            }

            ApplyTabStyles();
        }

        public event Action<TTab>? TabSelected;

        public void SetActiveTab(TTab activeTab)
        {
            _activeTab = activeTab;
            ApplyTabStyles();
            Invalidate();
        }

        public void RefreshTheme()
        {
            ApplyTabStyles();
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int top = Math.Max(0, Height - UIUtils.S(38) - UIUtils.S(1));
            int left = 0;
            int gap = UIUtils.S(8);
            int[] preferredWidths = _entries
                .Select(entry => UIUtils.S(entry.Item.Width))
                .ToArray();
            int preferredTotal = preferredWidths.Sum() + gap * Math.Max(0, _entries.Count - 1);
            if (preferredTotal > Width)
                gap = Width >= _entries.Count + UIUtils.S(2) * Math.Max(0, _entries.Count - 1)
                    ? UIUtils.S(2)
                    : 0;
            IReadOnlyList<int> widths = FrameworkTopTabLayoutModel.FitWidths(
                preferredWidths,
                Math.Max(1, Width),
                gap,
                UIUtils.S(48));

            for (int index = 0; index < _entries.Count; index++)
            {
                TabEntry entry = _entries[index];
                int width = widths[index];
                entry.Label.SetBounds(left, top, width, UIUtils.S(38));
                left = entry.Label.Right + gap;
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            Label? activeLabel = FindActiveLabel();
            if (activeLabel == null)
                return;

            using var activePen = new Pen(UIColors.Primary, UIUtils.S(3));
            int underlineY = Math.Max(0, Height - UIUtils.S(2));
            e.Graphics.DrawLine(activePen, activeLabel.Left, underlineY, activeLabel.Right, underlineY);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _regularFont.Dispose();
                _activeFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private Label CreateTabLabel(FrameworkTopTabItem<TTab> item, string accessiblePrefix)
        {
            var label = new Label
            {
                Text = item.Text,
                AutoSize = false,
                Cursor = Cursors.Hand,
                Font = _regularFont,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                ForeColor = UIColors.TextSub,
                AccessibleName = accessiblePrefix + "-" + item.Text,
                AccessibleDescription = "切换到" + item.Text + "标签",
                AccessibleRole = AccessibleRole.PushButton,
                AutoEllipsis = true
            };

            label.Click += (_, __) => TabSelected?.Invoke(item.Tab);
            label.MouseEnter += (_, __) =>
            {
                if (!IsActive(item.Tab))
                    label.ForeColor = UIColors.TextMain;
            };
            label.MouseLeave += (_, __) => ApplyTabStyles();
            return label;
        }

        private void ApplyTabStyles()
        {
            foreach (TabEntry entry in _entries)
                ApplyTabStyle(entry.Label, entry.Item.Tab);
        }

        private void ApplyTabStyle(Label label, TTab tab)
        {
            bool active = IsActive(tab);
            label.BackColor = !UIColors.IsDark && active ? Color.FromArgb(240, 245, 255) : Color.Transparent;
            label.ForeColor = active ? UIColors.Primary : UIColors.TextSub;
            Font targetFont = active ? _activeFont : _regularFont;
            if (!ReferenceEquals(label.Font, targetFont))
                label.Font = targetFont;
            label.Invalidate();
        }

        private Label? FindActiveLabel()
        {
            foreach (TabEntry entry in _entries)
            {
                if (IsActive(entry.Item.Tab))
                    return entry.Label;
            }

            return null;
        }

        private bool IsActive(TTab tab)
        {
            return EqualityComparer<TTab>.Default.Equals(_activeTab, tab);
        }

        private sealed record TabEntry(FrameworkTopTabItem<TTab> Item, Label Label);
    }

    internal sealed record FrameworkTopTabItem<TTab>(TTab Tab, string Text, int Width) where TTab : notnull;

    internal static class FrameworkTopTabLayoutModel
    {
        public static IReadOnlyList<int> FitWidths(
            IReadOnlyList<int> preferredWidths,
            int availableWidth,
            int gap,
            int minimumWidth)
        {
            ArgumentNullException.ThrowIfNull(preferredWidths);
            if (preferredWidths.Count == 0)
                return Array.Empty<int>();

            int count = preferredWidths.Count;
            int usableWidth = Math.Max(count, availableWidth - Math.Max(0, gap) * (count - 1));
            int minimum = Math.Max(1, minimumWidth);
            int[] desired = preferredWidths.Select(width => Math.Max(minimum, width)).ToArray();
            if (desired.Sum() <= usableWidth)
                return desired;

            if (usableWidth < minimum * count)
            {
                int compactWidth = usableWidth / count;
                int remainder = usableWidth % count;
                return Enumerable.Range(0, count)
                    .Select(index => compactWidth + (index < remainder ? 1 : 0))
                    .ToArray();
            }

            int distributable = usableWidth - minimum * count;
            int desiredExtra = desired.Sum(width => width - minimum);
            var result = new int[count];
            int assignedExtra = 0;
            for (int index = 0; index < count; index++)
            {
                int extra = index == count - 1
                    ? distributable - assignedExtra
                    : desiredExtra == 0
                        ? 0
                        : distributable * (desired[index] - minimum) / desiredExtra;
                result[index] = minimum + extra;
                assignedExtra += extra;
            }
            return result;
        }
    }
}
