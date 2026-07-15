using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class YouPinProfitLossRedesignHostPage : FrameworkSettingsHostPage<YouPinProfitLossRedesignPage>
    {
        public YouPinProfitLossRedesignHostPage()
            : base(new YouPinProfitLossRedesignPage(YouPinPageRuntimeServices.Resolve()))
        {
        }
    }

    public sealed class YouPinProfitLossRedesignPage : FrameworkSettingsPageBase
    {
        private readonly IYouPinProfitLossService _profitLossService;
        private readonly List<LiteButton> _filterButtons = new();
        private TableLayoutPanel? _root;
        private FlatPanel? _overviewCard;
        private FlatPanel? _listCard;
        private ProfitLossOverviewPanel? _overviewPanel;
        private ProfitLossRowsPanel? _rowsPanel;
        private LiteButton? _syncButton;
        private LiteUnderlineInput? _searchInput;
        private YouPinProfitLossRedesignFilter _filter = YouPinProfitLossRedesignFilter.Matched;
        private YouPinProfitLossState? _state;
        private YouPinProfitLossRedesignView _view = new();
        private bool _busy;
        private bool _disposed;
        private bool _widthSyncQueued;

        private Rectangle ContentBounds => GetVisibleContentBounds(FrameworkSettingsPageLayoutHelper.StandardContentMinimumWidth);
        private int ContentWidth => ContentBounds.Width;

        public YouPinProfitLossRedesignPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinProfitLossRedesignPage(YouPinPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _profitLossService = runtimeServices.ProfitLoss;
            _profitLossService.DataUpdated += OnDataUpdated;
            Container.SizeChanged += (_, __) => QueueDeferredWidthSync();
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            RefreshState();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            BackColor = UIColors.MainBg;
            Container.BackColor = UIColors.MainBg;
            RefreshFilterButtons();
            RefreshState();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            QueueDeferredWidthSync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _profitLossService.DataUpdated -= OnDataUpdated;
            }

            base.Dispose(disposing);
        }

        private void BuildPage()
        {
            ClearPage();
            _filterButtons.Clear();
            Rectangle bounds = ContentBounds;

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Container.Controls.Add(_root);

            AddRootRow(CreatePageHeader());
            AddRootRow(CreateOverviewCard());
            AddRootRow(CreateListCard());
            RefreshState();
        }

        private void AddRootRow(Control control)
        {
            if (_root == null)
                return;

            int row = _root.RowCount;
            _root.RowCount = row + 1;
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            control.Width = ContentWidth;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _root.Controls.Add(control, 0, row);
        }

        private Control CreatePageHeader()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(58),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(8))
            };

            var title = CreateTextLabel("吃米/亏米统计", 16F, FontStyle.Bold, UIColors.TextMain);
            var desc = CreateTextLabel("按悠悠已完成买卖记录估算；无法可靠匹配的记录归为记录缺失。", 8.8F, FontStyle.Regular, UIColors.TextSub);
            var readonlyButton = new LiteButton("只读统计", false) { Width = UIUtils.S(88), Height = UIUtils.S(30) };
            readonlyButton.Click += (_, __) => RefreshState();
            _syncButton = new LiteButton("同步已完成成交", true) { Width = UIUtils.S(180), Height = UIUtils.S(36) };
            _syncButton.Click += async (_, __) => await RunSyncAsync();

            panel.Controls.Add(title);
            panel.Controls.Add(desc);
            panel.Controls.Add(readonlyButton);
            panel.Controls.Add(_syncButton);
            panel.Layout += (_, __) =>
            {
                int gap = UIUtils.S(12);
                int titleW = UIUtils.S(158);
                int buttonW = UIUtils.S(180);
                int readW = UIUtils.S(88);
                int actionsW = buttonW + readW + gap;
                bool compact = panel.Width < UIUtils.S(760);
                title.SetBounds(0, compact ? 0 : UIUtils.S(8), titleW, UIUtils.S(34));
                if (compact)
                {
                    bool showReadonly = panel.Width >= UIUtils.S(520);
                    readonlyButton.Visible = showReadonly;
                    _syncButton.SetBounds(Math.Max(0, panel.Width - buttonW), UIUtils.S(5), buttonW, UIUtils.S(34));
                    if (showReadonly)
                        readonlyButton.SetBounds(Math.Max(0, _syncButton.Left - gap - readW), UIUtils.S(7), readW, UIUtils.S(30));
                    desc.SetBounds(0, title.Bottom, Math.Max(1, panel.Width), UIUtils.S(22));
                }
                else
                {
                    readonlyButton.Visible = true;
                    _syncButton.SetBounds(panel.Width - buttonW, UIUtils.S(8), buttonW, UIUtils.S(36));
                    readonlyButton.SetBounds(_syncButton.Left - gap - readW, UIUtils.S(11), readW, UIUtils.S(30));
                    int descLeft = title.Right + UIUtils.S(10);
                    int descRight = Math.Max(descLeft + 1, panel.Width - actionsW - UIUtils.S(20));
                    desc.SetBounds(descLeft, UIUtils.S(11), descRight - descLeft, UIUtils.S(28));
                }
            };
            return panel;
        }

        private Control CreateOverviewCard()
        {
            _overviewCard = new FlatPanel
            {
                Padding = UIUtils.S(new Padding(22, 16, 22, 20)),
                Margin = new Padding(0, 0, 0, UIUtils.S(18))
            };
            var title = CreateTextLabel("统计结果", 11F, FontStyle.Bold, UIColors.TextMain);
            var desc = CreateTextLabel("购买记录 + 出售记录；按模板/名称/磨损/时间顺序估算匹配，取消和待处理订单不参与统计。", 8.5F, FontStyle.Regular, UIColors.TextSub);
            var lastSync = CreateTextLabel("", 8.5F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleRight);
            _overviewPanel = new ProfitLossOverviewPanel();

            _overviewCard.Controls.Add(title);
            _overviewCard.Controls.Add(desc);
            _overviewCard.Controls.Add(lastSync);
            _overviewCard.Controls.Add(_overviewPanel);
            _overviewCard.Layout += (_, __) =>
            {
                int left = _overviewCard.Padding.Left;
                int top = _overviewCard.Padding.Top;
                int innerW = Math.Max(1, _overviewCard.Width - _overviewCard.Padding.Horizontal);
                bool compact = innerW < UIUtils.S(700);
                title.SetBounds(left, top, UIUtils.S(150), UIUtils.S(28));
                if (compact)
                {
                    lastSync.SetBounds(left, top + UIUtils.S(28), innerW, UIUtils.S(22));
                    desc.SetBounds(left, lastSync.Bottom, innerW, UIUtils.S(22));
                }
                else
                {
                    lastSync.SetBounds(left + innerW - UIUtils.S(260), top, UIUtils.S(260), UIUtils.S(26));
                    desc.SetBounds(title.Right + UIUtils.S(8), top + UIUtils.S(1), Math.Max(1, lastSync.Left - title.Right - UIUtils.S(16)), UIUtils.S(24));
                }

                int bodyTop = compact ? desc.Bottom + UIUtils.S(10) : top + UIUtils.S(46);
                int bodyH = _overviewPanel.GetDesiredHeight(innerW);
                _overviewPanel.SetBounds(left, bodyTop, innerW, bodyH);
                int desiredH = bodyTop + bodyH + _overviewCard.Padding.Bottom;
                if (_overviewCard.Height != desiredH)
                    _overviewCard.Height = desiredH;
                lastSync.Text = BuildLastSyncText(_state);
            };
            return _overviewCard;
        }

        private Control CreateListCard()
        {
            _listCard = new FlatPanel
            {
                Padding = UIUtils.S(new Padding(22, 16, 22, 20)),
                Margin = new Padding(0, 0, 0, UIUtils.S(18))
            };
            var title = CreateTextLabel("成交明细", 11F, FontStyle.Bold, UIColors.TextMain);
            var desc = CreateTextLabel("资金列紧凑排布，记录缺失原因显示在饰品名下方。", 8.5F, FontStyle.Regular, UIColors.TextSub);
            _searchInput = new LiteUnderlineInput("", "", "", 150) { Placeholder = "搜索饰品 / 模板ID" };
            _searchInput.SetBg(UIColors.ControlBg);
            _searchInput.Inner.TextChanged += (_, __) => RefreshRows();
            _rowsPanel = new ProfitLossRowsPanel();

            _listCard.Controls.Add(title);
            _listCard.Controls.Add(desc);
            _listCard.Controls.Add(_searchInput);
            foreach (var (text, filter) in new[]
            {
                ("已匹配", YouPinProfitLossRedesignFilter.Matched),
                ("吃米", YouPinProfitLossRedesignFilter.Profit),
                ("亏米", YouPinProfitLossRedesignFilter.Loss),
                ("记录缺失", YouPinProfitLossRedesignFilter.Failed)
            })
            {
                var button = new LiteButton(text, false)
                {
                    Width = UIUtils.S(text.Length >= 4 ? 88 : text.Length >= 3 ? 72 : 58),
                    Height = UIUtils.S(30),
                    Tag = filter
                };
                button.Click += (_, __) =>
                {
                    _filter = filter;
                    RefreshFilterButtons();
                    RefreshRows();
                };
                _filterButtons.Add(button);
                _listCard.Controls.Add(button);
            }

            _listCard.Controls.Add(_rowsPanel);
            _listCard.Layout += (_, __) =>
            {
                int left = _listCard.Padding.Left;
                int top = _listCard.Padding.Top;
                int innerW = Math.Max(1, _listCard.Width - _listCard.Padding.Horizontal);
                bool compact = innerW < UIUtils.S(960);
                title.SetBounds(left, top, UIUtils.S(150), UIUtils.S(28));
                int actionsTop = top;
                if (compact)
                {
                    desc.SetBounds(left, title.Bottom, innerW, UIUtils.S(22));
                    actionsTop = desc.Bottom + UIUtils.S(8);
                    _searchInput.SetBounds(left, actionsTop, Math.Min(UIUtils.S(240), innerW), UIUtils.S(28));
                    actionsTop = LayoutFilterButtons(left, _searchInput.Bottom + UIUtils.S(8), innerW, wrap: true);
                }
                else
                {
                    int filterW = _filterButtons.Sum(button => button.Width) + Math.Max(0, _filterButtons.Count - 1) * UIUtils.S(8);
                    int searchX = left + innerW - filterW - UIUtils.S(170);
                    _searchInput.SetBounds(searchX, top, UIUtils.S(150), UIUtils.S(28));
                    LayoutFilterButtons(_searchInput.Right + UIUtils.S(10), top, filterW, wrap: false);
                    int descLeft = title.Right + UIUtils.S(8);
                    desc.SetBounds(descLeft, top + UIUtils.S(1), Math.Max(1, searchX - descLeft - UIUtils.S(16)), UIUtils.S(24));
                }

                int rowsTop = compact ? actionsTop + UIUtils.S(14) : top + UIUtils.S(52);
                int rowsH = _rowsPanel.GetDesiredHeight(innerW);
                int desiredH = rowsTop + rowsH + _listCard.Padding.Bottom;
                int minimumH = GetMinimumListCardHeight(desiredH);
                if (minimumH > desiredH)
                {
                    rowsH += minimumH - desiredH;
                    desiredH = minimumH;
                }

                _rowsPanel.SetBounds(left, rowsTop, innerW, rowsH);
                if (_listCard.Height != desiredH)
                    _listCard.Height = desiredH;
            };
            RefreshFilterButtons();
            return _listCard;
        }

        private int LayoutFilterButtons(int x, int y, int width, bool wrap)
        {
            int gap = UIUtils.S(8);
            int currentX = x;
            int currentY = y;
            int maxRight = x + Math.Max(1, width);
            foreach (var button in _filterButtons)
            {
                if (wrap && currentX + button.Width > maxRight && currentX > x)
                {
                    currentX = x;
                    currentY += button.Height + UIUtils.S(6);
                }

                button.SetBounds(currentX, currentY, button.Width, button.Height);
                currentX += button.Width + gap;
            }

            return currentY + (_filterButtons.Count == 0 ? 0 : _filterButtons.Max(button => button.Height));
        }

        private int GetMinimumListCardHeight(int desiredHeight)
        {
            if (_root == null || _listCard == null)
                return desiredHeight;

            int topInContainer = _root.Top + _listCard.Top;
            int available = Container.ClientSize.Height - topInContainer - UIUtils.S(20);
            return Math.Max(desiredHeight, available);
        }

        private async Task RunSyncAsync()
        {
            if (_busy)
                return;

            _busy = true;
            if (_syncButton != null)
            {
                _syncButton.Enabled = false;
                _syncButton.Text = "同步中...";
            }

            try
            {
                await _profitLossService.RefreshAsync(Config, PageToken);
                RefreshState();
            }
            catch (OperationCanceledException)
            {
                // 用户切换页面或关闭窗口时取消同步，finally 会恢复 busy 状态。
            }
            finally
            {
                _busy = false;
                if (_syncButton != null && !_syncButton.IsDisposed)
                {
                    _syncButton.Enabled = true;
                    _syncButton.Text = "同步已完成成交";
                }
            }
        }

        private void RefreshState()
        {
            _state = _profitLossService.GetState(Config);
            _view = YouPinProfitLossRedesignProjection.Build(_state.Records);
            if (_overviewPanel != null)
            {
                _overviewPanel.View = _view;
                _overviewPanel.State = _state;
                _overviewPanel.Invalidate();
            }

            RefreshRows();
            _overviewCard?.PerformLayout();
            _listCard?.PerformLayout();
        }

        private void RefreshRows()
        {
            string keyword = _searchInput?.Inner.Text ?? "";
            var rows = YouPinProfitLossRedesignProjection.Filter(_view.Rows, _filter, keyword);
            if (_rowsPanel != null)
            {
                _rowsPanel.View = _view;
                _rowsPanel.Rows = rows;
                _rowsPanel.Filter = _filter;
                _rowsPanel.Keyword = keyword;
                _rowsPanel.Invalidate();
            }

            _listCard?.PerformLayout();
        }

        private void RefreshFilterButtons()
        {
            foreach (var button in _filterButtons)
                button.IsActive = button.Tag is YouPinProfitLossRedesignFilter filter && filter == _filter;
        }

        private void OnDataUpdated()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(RefreshState));
            }
            catch
            {
                // Page may be closing while a background sync finishes.
            }
        }

        private void QueueDeferredWidthSync()
        {
            if (_widthSyncQueued || !IsHandleCreated || IsDisposed)
                return;

            _widthSyncQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _widthSyncQueued = false;
                    if (_root == null || IsDisposed)
                        return;

                    Rectangle bounds = ContentBounds;
                    int width = bounds.Width;
                    if (_root.Left != bounds.Left || _root.Top != bounds.Top || _root.Width != width)
                    {
                        _root.SetBounds(bounds.Left, bounds.Top, width, _root.Height);
                        foreach (Control child in _root.Controls)
                            child.Width = width;
                    }

                    _root.PerformLayout();
                    HideHorizontalScroll(Container);
                }));
            }
            catch
            {
                _widthSyncQueued = false;
            }
        }

        private static Label CreateTextLabel(string text, float size, FontStyle style, Color color, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = align,
                AutoEllipsis = true
            };
        }

        private static string BuildLastSyncText(YouPinProfitLossState? state)
        {
            if (state == null || state.LastSync == DateTime.MinValue)
                return "上次同步  未同步";

            return "上次同步  " + state.LastSync.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private sealed class ProfitLossOverviewPanel : Control
        {
            public YouPinProfitLossRedesignView View { get; set; } = new();
            public YouPinProfitLossState? State { get; set; }

            public ProfitLossOverviewPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = UIColors.CardBg;
            }

            public int GetDesiredHeight(int width)
            {
                if (width >= UIUtils.S(900))
                    return UIUtils.S(82);
                if (width >= UIUtils.S(520))
                    return UIUtils.S(174);
                return UIUtils.S(348);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(ResolveParentBackColor(this));
                e.Graphics.FillRectangle(bg, ClientRectangle);

                if (Width >= UIUtils.S(900))
                    DrawWide(e.Graphics);
                else
                    DrawCompact(e.Graphics);
            }

            private void DrawWide(Graphics g)
            {
                int tileGap = UIUtils.S(14);
                var metrics = BuildMetrics();
                for (int i = 0; i < metrics.Count; i++)
                {
                    int x = i * ((Width - tileGap * 3) / 4 + tileGap);
                    int w = i == metrics.Count - 1
                        ? Math.Max(1, Width - x)
                        : Math.Max(1, (Width - tileGap * 3) / 4);
                    DrawMetric(g, new Rectangle(x, 0, w, UIUtils.S(82)), metrics[i]);
                }
            }

            private void DrawCompact(Graphics g)
            {
                int cols = Width >= UIUtils.S(520) ? 2 : 1;
                int gap = UIUtils.S(10);
                int colW = Math.Max(1, (Width - gap * (cols - 1)) / cols);
                var metrics = BuildMetrics();
                for (int i = 0; i < metrics.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    int x = col * (colW + gap);
                    int y = row * (UIUtils.S(82) + gap);
                    DrawMetric(g, new Rectangle(x, y, colW, UIUtils.S(82)), metrics[i]);
                }
            }

            private List<MetricTile> BuildMetrics()
            {
                return new List<MetricTile>
                {
                    new("按饰品匹配盈亏", FormatSignedMoney(View.NetTotal), View.NetTotal >= 0 ? ProfitColor : LossColor, false),
                    new("吃米合计", FormatMoney(View.ProfitTotal), ProfitColor, false),
                    new("亏米合计", FormatMoney(View.LossTotal), LossColor, false),
                    new("记录缺失", $"{View.FailedCount} 条", UIColors.TextWarn, true)
                };
            }

            private void DrawMetric(Graphics g, Rectangle rect, MetricTile metric)
            {
                DrawRound(g, rect, UIUtils.S(5), UIColors.IsDark ? Color.FromArgb(23, 31, 40) : UIColors.CardBg, metric.Warn ? Color.FromArgb(150, UIColors.TextWarn) : UIColors.Border);
                var textRect = Inset(rect, UIUtils.S(16), UIUtils.S(9));
                DrawText(g, metric.Title, UIFonts.Regular(8F), UIColors.TextSub, new Rectangle(textRect.X, textRect.Y, textRect.Width, UIUtils.S(18)));
                DrawText(g, metric.Value, UIFonts.Bold(16F), metric.Color, new Rectangle(textRect.X, textRect.Y + UIUtils.S(23), textRect.Width, UIUtils.S(30)));
                string hint = metric.Title == "记录缺失"
                    ? "不计入盈亏"
                    : metric.Title == "按饰品匹配盈亏" ? "只含已匹配记录" : metric.Title.Contains("吃米", StringComparison.Ordinal) ? "匹配后盈利" : "匹配后亏损";
                DrawText(g, hint, UIFonts.Regular(7.8F), UIColors.TextSub, new Rectangle(textRect.X, textRect.Y + UIUtils.S(55), textRect.Width, UIUtils.S(18)));
            }
        }

        private sealed class ProfitLossRowsPanel : Control
        {
            public List<YouPinProfitLossRedesignRow> Rows { get; set; } = new();
            public YouPinProfitLossRedesignView View { get; set; } = new();
            public YouPinProfitLossRedesignFilter Filter { get; set; }
            public string Keyword { get; set; } = "";

            public ProfitLossRowsPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = UIColors.CardBg;
            }

            public int GetDesiredHeight(int width)
            {
                int header = UIUtils.S(40);
                int rowH = width >= UIUtils.S(900) ? UIUtils.S(56) : UIUtils.S(78);
                int empty = Rows.Count == 0 ? UIUtils.S(116) : 0;
                return header + Math.Max(empty, Rows.Count * rowH) + UIUtils.S(10) + GetFailureSummaryHeight(width);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var bg = new SolidBrush(ResolveParentBackColor(this));
                e.Graphics.FillRectangle(bg, ClientRectangle);

                if (Width >= UIUtils.S(900))
                    DrawWide(e.Graphics);
                else
                    DrawCompact(e.Graphics);
            }

            private void DrawWide(Graphics g)
            {
                int headerH = UIUtils.S(40);
                DrawHeader(g, new Rectangle(0, 0, Width, headerH), wide: true);
                int rowH = UIUtils.S(56);
                int y = headerH;
                if (Rows.Count == 0)
                {
                    DrawEmpty(g, new Rectangle(0, y, Width, UIUtils.S(116)));
                    y += UIUtils.S(116);
                }
                else
                {
                    for (int i = 0; i < Rows.Count; i++)
                    {
                        DrawWideRow(g, Rows[i], new Rectangle(0, y, Width, rowH), i);
                        y += rowH;
                    }
                }

                int summaryH = GetFailureSummaryHeight(Width);
                int summaryY = Math.Max(y + UIUtils.S(8), Height - summaryH);
                DrawFailureSummary(g, new Rectangle(0, summaryY, Width, summaryH));
            }

            private void DrawCompact(Graphics g)
            {
                int headerH = UIUtils.S(40);
                DrawHeader(g, new Rectangle(0, 0, Width, headerH), wide: false);
                int rowH = UIUtils.S(78);
                int y = headerH;
                if (Rows.Count == 0)
                {
                    DrawEmpty(g, new Rectangle(0, y, Width, UIUtils.S(116)));
                    y += UIUtils.S(116);
                }
                else
                {
                    for (int i = 0; i < Rows.Count; i++)
                    {
                        DrawCompactRow(g, Rows[i], new Rectangle(0, y, Width, rowH), i);
                        y += rowH;
                    }
                }

                int summaryH = GetFailureSummaryHeight(Width);
                int summaryY = Math.Max(y + UIUtils.S(8), Height - summaryH);
                DrawFailureSummary(g, new Rectangle(0, summaryY, Width, summaryH));
            }

            private void DrawHeader(Graphics g, Rectangle rect, bool wide)
            {
                using var fill = new SolidBrush(UIColors.IsDark ? Color.FromArgb(29, 38, 48) : UIColors.GroupHeader);
                g.FillRectangle(fill, rect);
                if (wide)
                {
                    var cols = CalculateColumns(rect.Width);
                    DrawText(g, "饰品", UIFonts.Bold(8.6F), UIColors.TextMain, new Rectangle(cols.NameX, rect.Y, cols.NameW, rect.Height));
                    DrawText(g, "买入", UIFonts.Bold(8.6F), UIColors.TextMain, new Rectangle(cols.BuyX, rect.Y, cols.AmountW, rect.Height), TextFormatFlags.Left);
                    DrawText(g, "卖出", UIFonts.Bold(8.6F), UIColors.TextMain, new Rectangle(cols.SellX, rect.Y, cols.AmountW, rect.Height), TextFormatFlags.Left);
                    DrawText(g, "估算盈亏", UIFonts.Bold(8.6F), UIColors.TextMain, new Rectangle(cols.NetX, rect.Y, cols.NetW, rect.Height), TextFormatFlags.Left);
                    return;
                }

                DrawText(g, "饰品 / 买入 / 卖出 / 估算盈亏", UIFonts.Bold(8.6F), UIColors.TextMain, rect);
            }

            private void DrawWideRow(Graphics g, YouPinProfitLossRedesignRow row, Rectangle rect, int index)
            {
                Color rowBg = row.IsFailed
                    ? Color.FromArgb(30, 32, 33)
                    : index % 2 == 0 ? UIColors.CardBg : Color.FromArgb(25, 33, 43);
                using (var fill = new SolidBrush(rowBg))
                    g.FillRectangle(fill, rect);
                if (row.IsFailed)
                {
                    using var accent = new SolidBrush(UIColors.TextWarn);
                    g.FillRectangle(accent, rect.X, rect.Y, UIUtils.S(3), rect.Height);
                }

                using (var pen = new Pen(UIColors.Border))
                    g.DrawLine(pen, rect.X, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

                var cols = CalculateColumns(rect.Width);
                DrawText(g, row.Name, UIFonts.Bold(9F), UIColors.TextMain, new Rectangle(cols.NameX, rect.Y + UIUtils.S(6), cols.NameW, UIUtils.S(22)));
                Color tone = ResolveRowTone(row);
                DrawText(g, row.MatchStatus, UIFonts.Bold(8.5F), tone, new Rectangle(cols.NameX, rect.Y + UIUtils.S(30), cols.NameW, UIUtils.S(20)));
                DrawText(g, row.BuyCount == 0 ? "-" : FormatMoney(row.BuyAmount), UIFonts.Bold(9F), row.BuyCount == 0 ? UIColors.TextSub : UIColors.TextMain, new Rectangle(cols.BuyX, rect.Y, cols.AmountW, rect.Height), TextFormatFlags.Left);
                DrawText(g, row.SellCount == 0 ? "-" : FormatMoney(row.SellAmount), UIFonts.Bold(9F), row.SellCount == 0 ? UIColors.TextSub : UIColors.TextMain, new Rectangle(cols.SellX, rect.Y, cols.AmountW, rect.Height), TextFormatFlags.Left);
                DrawText(g, row.IsFailed ? "不计入" : FormatSignedMoney(row.NetProfit), UIFonts.Bold(9F), tone, new Rectangle(cols.NetX, rect.Y, cols.NetW, rect.Height), TextFormatFlags.Left);
            }

            private void DrawCompactRow(Graphics g, YouPinProfitLossRedesignRow row, Rectangle rect, int index)
            {
                Color rowBg = row.IsFailed
                    ? Color.FromArgb(30, 32, 33)
                    : index % 2 == 0 ? UIColors.CardBg : Color.FromArgb(25, 33, 43);
                using (var fill = new SolidBrush(rowBg))
                    g.FillRectangle(fill, rect);
                if (row.IsFailed)
                {
                    using var accent = new SolidBrush(UIColors.TextWarn);
                    g.FillRectangle(accent, rect.X, rect.Y, UIUtils.S(3), rect.Height);
                }

                using (var pen = new Pen(UIColors.Border))
                    g.DrawLine(pen, rect.X, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

                int left = UIUtils.S(12);
                int textX = left;
                Color tone = ResolveRowTone(row);
                DrawText(g, row.Name, UIFonts.Bold(9F), UIColors.TextMain, new Rectangle(textX, rect.Y + UIUtils.S(8), rect.Width - textX - UIUtils.S(12), UIUtils.S(20)));
                DrawText(g, row.MatchStatus, UIFonts.Bold(8.5F), tone, new Rectangle(textX, rect.Y + UIUtils.S(30), rect.Width - textX - UIUtils.S(12), UIUtils.S(18)));
                string amounts = $"买 {FormatOptionalMoney(row.BuyAmount, row.BuyCount)}  卖 {FormatOptionalMoney(row.SellAmount, row.SellCount)}  {(row.IsFailed ? "不计入" : FormatSignedMoney(row.NetProfit))}";
                DrawText(g, amounts, UIFonts.Regular(8.4F), row.IsFailed ? UIColors.TextWarn : UIColors.TextSub, new Rectangle(textX, rect.Y + UIUtils.S(52), rect.Width - textX - UIUtils.S(12), UIUtils.S(18)));
            }

            private void DrawEmpty(Graphics g, Rectangle rect)
            {
                DrawText(g,
                    string.IsNullOrWhiteSpace(Keyword) && View.RecordCount == 0
                        ? "暂无成交数据，点击右上角“同步已完成成交”后显示估算盈亏。"
                        : "当前筛选没有匹配的成交记录。",
                    UIFonts.Bold(10F),
                    UIColors.TextSub,
                    rect,
                    TextFormatFlags.HorizontalCenter);
            }

            private void DrawFailureSummary(Graphics g, Rectangle rect)
            {
                DrawRound(g, rect, UIUtils.S(5), UIColors.IsDark ? Color.FromArgb(23, 31, 40) : UIColors.CardBg, UIColors.Border);
                DrawText(g, "记录缺失", UIFonts.Bold(11F), UIColors.TextWarn, new Rectangle(rect.X + UIUtils.S(20), rect.Y + UIUtils.S(12), UIUtils.S(92), UIUtils.S(24)));
                DrawText(g, "只买未售、只售无买、同名多件无法可靠判断时，暂不计入盈亏。", UIFonts.Regular(8.5F), UIColors.TextSub, new Rectangle(rect.X + UIUtils.S(116), rect.Y + UIUtils.S(14), rect.Width - UIUtils.S(136), UIUtils.S(20)));

                int top = rect.Y + UIUtils.S(50);
                int gap = UIUtils.S(16);
                int cols = rect.Width >= UIUtils.S(760) ? 3 : 1;
                int boxW = cols == 1 ? rect.Width - UIUtils.S(40) : Math.Max(1, (rect.Width - UIUtils.S(40) - gap * 2) / 3);
                var items = new[]
                {
                    ("只买未售", $"{View.FailedBuyCount} 条 / {FormatMoney(View.FailedBuyAmount)}", true),
                    ("只售无买", $"{View.FailedSellCount} 条 / {FormatMoney(View.FailedSellAmount)}", true),
                    ("处理方式", "不计入盈亏，后续同步到对应记录后自动重算", false)
                };
                for (int i = 0; i < items.Length; i++)
                {
                    int x = rect.X + UIUtils.S(20) + (i % cols) * (boxW + gap);
                    int y = top + (i / cols) * UIUtils.S(34);
                    DrawRound(g, new Rectangle(x, y, boxW, UIUtils.S(28)), UIUtils.S(5), UIColors.ControlBg, items[i].Item3 ? Color.FromArgb(100, UIColors.TextWarn) : UIColors.Border);
                    DrawText(g, items[i].Item1, UIFonts.Regular(8F), UIColors.TextSub, new Rectangle(x + UIUtils.S(12), y, UIUtils.S(88), UIUtils.S(28)));
                    DrawText(g, items[i].Item2, items[i].Item3 ? UIFonts.Bold(8.5F) : UIFonts.Regular(8F), items[i].Item3 ? UIColors.TextWarn : UIColors.TextSub, new Rectangle(x + UIUtils.S(96), y, boxW - UIUtils.S(106), UIUtils.S(28)));
                }
            }

            private static int GetFailureSummaryHeight(int width)
            {
                return width >= UIUtils.S(760) ? UIUtils.S(94) : UIUtils.S(156);
            }

            private static RowColumns CalculateColumns(int width)
            {
                int amountW = UIUtils.S(138);
                int netW = UIUtils.S(136);
                int gap = UIUtils.S(20);
                int netX = width - UIUtils.S(12) - netW;
                int sellX = netX - gap - amountW;
                int buyX = sellX - gap - amountW;
                int nameX = UIUtils.S(18);
                int nameW = Math.Max(UIUtils.S(260), buyX - nameX - UIUtils.S(28));
                return new RowColumns(nameX, nameW, buyX, sellX, amountW, netX, netW);
            }

            private sealed record RowColumns(
                int NameX,
                int NameW,
                int BuyX,
                int SellX,
                int AmountW,
                int NetX,
                int NetW);
        }

        private sealed record MetricTile(string Title, string Value, Color Color, bool Warn);

        private static readonly Color ProfitColor = Color.FromArgb(220, 70, 90);
        private static readonly Color LossColor = Color.FromArgb(80, 160, 135);

        private static Color ResolveParentBackColor(Control control)
        {
            Control? current = control.Parent;
            while (current != null && current.BackColor == Color.Transparent)
                current = current.Parent;
            return current?.BackColor ?? UIColors.CardBg;
        }

        private static Rectangle Inset(Rectangle rect, int x, int y)
        {
            return new Rectangle(rect.X + x, rect.Y + y, Math.Max(1, rect.Width - x * 2), Math.Max(1, rect.Height - y * 2));
        }

        private static void DrawRound(Graphics g, Rectangle rect, int radius, Color fill, Color border)
        {
            using var path = UIUtils.RoundRect(new Rectangle(rect.X, rect.Y, Math.Max(1, rect.Width - 1), Math.Max(1, rect.Height - 1)), radius);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border);
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, Rectangle rect, TextFormatFlags align = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(
                g,
                text ?? string.Empty,
                font,
                rect,
                color,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static Color ResolveRowTone(YouPinProfitLossRedesignRow row)
        {
            if (row.IsFailed)
                return UIColors.TextWarn;
            if (row.NetProfit > 0)
                return ProfitColor;
            if (row.NetProfit < 0)
                return LossColor;
            return UIColors.TextSub;
        }

        private static string FormatMoney(double value) => "¥" + value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string FormatOptionalMoney(double value, int count) => count <= 0 ? "-" : FormatMoney(value);

        private static string FormatSignedMoney(double value)
        {
            string sign = value > 0 ? "+" : "";
            return sign + "¥" + value.ToString("0.00", CultureInfo.InvariantCulture);
        }

    }
}
