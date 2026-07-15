using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum YouPinSaleReminderOrderListEntryKind
    {
        Empty,
        Section,
        Order
    }

    internal sealed class YouPinSaleReminderOrderListActions
    {
        public YouPinSaleReminderOrderListActions(
            Action<YouPinSaleOrder> showDetail,
            Func<YouPinSaleOrder, Task> runPrimaryActionAsync,
            Func<YouPinSaleOrder, Task> queryStatusAsync,
            Action<LiteButton> trackActionButton)
        {
            ShowDetail = showDetail ?? throw new ArgumentNullException(nameof(showDetail));
            RunPrimaryActionAsync = runPrimaryActionAsync ?? throw new ArgumentNullException(nameof(runPrimaryActionAsync));
            QueryStatusAsync = queryStatusAsync ?? throw new ArgumentNullException(nameof(queryStatusAsync));
            TrackActionButton = trackActionButton ?? throw new ArgumentNullException(nameof(trackActionButton));
        }

        public Action<YouPinSaleOrder> ShowDetail { get; }
        public Func<YouPinSaleOrder, Task> RunPrimaryActionAsync { get; }
        public Func<YouPinSaleOrder, Task> QueryStatusAsync { get; }
        public Action<LiteButton> TrackActionButton { get; }
    }

    internal sealed class YouPinSaleReminderOrderListPanel : VirtualListPanel<YouPinSaleReminderOrderListEntry>
    {
        private readonly YouPinSaleReminderOrderListActions _actions;
        private readonly bool _waitDeliverActions;
        private readonly bool _compactQuoteStyle;
        private readonly bool _groupCompactQuotes;
        private readonly int _normalRowHeight;
        private string _lastSignature = string.Empty;
        private bool _rowPoolPrewarmed;

        public YouPinSaleReminderOrderListPanel(
            YouPinSaleReminderOrderListActions actions,
            bool waitDeliverActions,
            bool compactQuoteStyle = false,
            bool groupCompactQuotes = true)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _waitDeliverActions = waitDeliverActions;
            _compactQuoteStyle = compactQuoteStyle;
            _groupCompactQuotes = groupCompactQuotes;
            _normalRowHeight = UIUtils.S(compactQuoteStyle ? 78 : waitDeliverActions ? 116 : 96);
            RowHeight = _normalRowHeight;
            MaxNewRowsPerPass = 1;
            OverscanRowCount = 2;
        }

        public void SetOrders(IReadOnlyList<YouPinSaleOrder> orders, string emptyText)
        {
            if (_compactQuoteStyle && _groupCompactQuotes && orders.Count == 0)
                emptyText = "暂无待发货或报价处理订单\n点击“立即刷新”读取悠悠待办。";
            int targetRowHeight = orders.Count == 0
                ? (_compactQuoteStyle ? Math.Max(UIUtils.S(118), Height - UIUtils.S(2)) : UIUtils.S(52))
                : _normalRowHeight;
            string signature = YouPinSaleReminderOrderDisplay.BuildListSignature(orders, emptyText, targetRowHeight);
            if (string.Equals(_lastSignature, signature, StringComparison.Ordinal))
                return;

            _lastSignature = signature;
            var entries = new List<YouPinSaleReminderOrderListEntry>();
            if (orders.Count == 0)
            {
                RowHeight = targetRowHeight;
                entries.Add(YouPinSaleReminderOrderListEntry.CreateEmpty(emptyText));
            }
            else if (_compactQuoteStyle && _waitDeliverActions && _groupCompactQuotes)
            {
                RowHeight = targetRowHeight;
                BuildGroupedQuoteEntries(orders, entries);
            }
            else
            {
                RowHeight = targetRowHeight;
                foreach (YouPinSaleOrder order in orders)
                    entries.Add(YouPinSaleReminderOrderListEntry.CreateOrder(order));
            }

            SetItemsIncremental(entries, OrderEntriesEquivalent);
            QueueRowPoolPrewarm();
        }

        private static void BuildGroupedQuoteEntries(IReadOnlyList<YouPinSaleOrder> orders, List<YouPinSaleReminderOrderListEntry> entries)
        {
            var actionable = new List<YouPinSaleOrder>();
            var passive = new List<YouPinSaleOrder>();
            foreach (YouPinSaleOrder order in orders)
            {
                if (YouPinSaleReminderOrderDisplay.IsActionableQuoteOrder(order))
                    actionable.Add(order);
                else
                    passive.Add(order);
            }

            if (actionable.Count > 0)
            {
                entries.Add(YouPinSaleReminderOrderListEntry.CreateSection(
                    "需要处理 " + actionable.Count,
                    "需要你点击确认或发送报价",
                    actionable: true));
                foreach (YouPinSaleOrder order in actionable)
                    entries.Add(YouPinSaleReminderOrderListEntry.CreateOrder(order));
            }

            if (passive.Count > 0)
            {
                entries.Add(YouPinSaleReminderOrderListEntry.CreateSection(
                    "无需处理 " + passive.Count,
                    "已发出或等待对方/平台同步",
                    actionable: false));
                foreach (YouPinSaleOrder order in passive)
                    entries.Add(YouPinSaleReminderOrderListEntry.CreateOrder(order));
            }
        }

        private void QueueRowPoolPrewarm()
        {
            if (_rowPoolPrewarmed || Items.Count == 0 || IsDisposed || Disposing || !IsHandleCreated)
                return;

            _rowPoolPrewarmed = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && !Disposing)
                        PrewarmRowPool(6);
                }));
            }
            catch
            {
                // 列表释放期间预热行池失败可忽略。
            }
        }

        private static bool OrderEntriesEquivalent(YouPinSaleReminderOrderListEntry left, YouPinSaleReminderOrderListEntry right)
        {
            return string.Equals(left.RenderSignature, right.RenderSignature, StringComparison.Ordinal);
        }

        protected override Control CreateRowControl()
        {
            return new YouPinSaleReminderOrderRowControl(_actions, _waitDeliverActions, _compactQuoteStyle);
        }

        protected override void OnRenderRow(Control rowControl, YouPinSaleReminderOrderListEntry item, int index)
        {
            if (rowControl is YouPinSaleReminderOrderRowControl row)
                row.Render(item);
        }
    }

    internal readonly struct YouPinSaleReminderOrderListEntry
    {
        public YouPinSaleReminderOrderListEntry(YouPinSaleOrder? order, string emptyText, string renderSignature)
            : this(
                  order == null ? YouPinSaleReminderOrderListEntryKind.Empty : YouPinSaleReminderOrderListEntryKind.Order,
                  order,
                  emptyText,
                  string.Empty,
                  string.Empty,
                  false,
                  renderSignature)
        {
        }

        private YouPinSaleReminderOrderListEntry(
            YouPinSaleReminderOrderListEntryKind kind,
            YouPinSaleOrder? order,
            string emptyText,
            string sectionTitle,
            string sectionSubtitle,
            bool sectionActionable,
            string renderSignature)
        {
            Kind = kind;
            Order = order;
            EmptyText = emptyText;
            SectionTitle = sectionTitle;
            SectionSubtitle = sectionSubtitle;
            SectionActionable = sectionActionable;
            RenderSignature = renderSignature;
        }

        public static YouPinSaleReminderOrderListEntry CreateEmpty(string emptyText)
        {
            return new YouPinSaleReminderOrderListEntry(
                YouPinSaleReminderOrderListEntryKind.Empty,
                null,
                emptyText,
                string.Empty,
                string.Empty,
                false,
                "empty|" + emptyText);
        }

        public static YouPinSaleReminderOrderListEntry CreateSection(string title, string subtitle, bool actionable)
        {
            return new YouPinSaleReminderOrderListEntry(
                YouPinSaleReminderOrderListEntryKind.Section,
                null,
                string.Empty,
                title,
                subtitle,
                actionable,
                "section|" + actionable + "|" + title + "|" + subtitle);
        }

        public static YouPinSaleReminderOrderListEntry CreateOrder(YouPinSaleOrder order)
        {
            return new YouPinSaleReminderOrderListEntry(
                YouPinSaleReminderOrderListEntryKind.Order,
                order,
                string.Empty,
                string.Empty,
                string.Empty,
                false,
                YouPinSaleReminderOrderDisplay.BuildRenderSignature(order));
        }

        public YouPinSaleReminderOrderListEntryKind Kind { get; }
        public YouPinSaleOrder? Order { get; }
        public string EmptyText { get; }
        public string SectionTitle { get; }
        public string SectionSubtitle { get; }
        public bool SectionActionable { get; }
        public string RenderSignature { get; }
        public bool IsEmpty => Kind == YouPinSaleReminderOrderListEntryKind.Empty;
        public bool IsSection => Kind == YouPinSaleReminderOrderListEntryKind.Section;
    }

    internal sealed class YouPinSaleReminderOrderRowControl : Panel
    {
        private readonly YouPinSaleReminderOrderListActions _actions;
        private readonly bool _waitDeliverActions;
        private readonly bool _compactQuoteStyle;
        private readonly PictureBox _thumbnail;
        private readonly Label _tag;
        private readonly Label _title;
        private readonly Label _item;
        private readonly Label _meta;
        private readonly Label _time;
        private readonly Label _price;
        private readonly LiteButton _detail;
        private readonly LiteButton? _sendOffer;
        private readonly LiteButton? _queryStatus;
        private readonly Font _titleFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private readonly Font _compactTitleFont = new("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        private readonly Font _compactPriceFont = new("Microsoft YaHei UI", 11F, FontStyle.Bold);
        private readonly Font _emptyFont = new("Microsoft YaHei UI", 8.5F);
        private int _imageVersion;
        private string _thumbnailUrl = string.Empty;
        private string _thumbnailLoadingUrl = string.Empty;
        private bool _thumbnailIsPlaceholder;
        private string _renderSignature = string.Empty;
        private YouPinSaleReminderOrderListEntry _entry;

        public YouPinSaleReminderOrderRowControl(YouPinSaleReminderOrderListActions actions, bool waitDeliverActions, bool compactQuoteStyle = false)
        {
            _actions = actions ?? throw new ArgumentNullException(nameof(actions));
            _waitDeliverActions = waitDeliverActions;
            _compactQuoteStyle = compactQuoteStyle;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = new Padding(0);

            _thumbnail = new PictureBox
            {
                BackColor = UIColors.ControlBg,
                SizeMode = PictureBoxSizeMode.Zoom,
                Visible = false
            };
            _tag = CreateTagLabel("交易订单");
            _title = CreateLabel(string.Empty, strong: true);
            var initialTitleFont = _title.Font;
            _title.Font = _titleFont;
            initialTitleFont.Dispose();
            _item = CreateMutedLabel(string.Empty);
            _meta = CreateMutedLabel(string.Empty);
            _time = CreateMutedLabel(string.Empty);
            _time.TextAlign = ContentAlignment.MiddleRight;
            _price = CreateLabel(string.Empty, strong: true);
            var initialPriceFont = _price.Font;
            _price.Font = _compactPriceFont;
            _price.TextAlign = ContentAlignment.MiddleRight;
            initialPriceFont.Dispose();
            _detail = new LiteButton(compactQuoteStyle ? "详情" : "查看详情", false)
            {
                Width = UIUtils.S(compactQuoteStyle ? 72 : 76),
                Height = UIUtils.S(compactQuoteStyle ? 36 : 26),
                Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            _detail.Click += (_, __) =>
            {
                if (_entry.Order != null)
                    _actions.ShowDetail(_entry.Order);
            };

            Controls.Add(_thumbnail);
            Controls.Add(_tag);
            Controls.Add(_title);
            Controls.Add(_item);
            Controls.Add(_meta);
            Controls.Add(_time);
            Controls.Add(_price);
            Controls.Add(_detail);

            if (waitDeliverActions)
            {
                _sendOffer = new LiteButton(compactQuoteStyle ? "发报价" : "发送报价", !compactQuoteStyle)
                {
                    Width = UIUtils.S(compactQuoteStyle ? 72 : 84),
                    Height = UIUtils.S(compactQuoteStyle ? 36 : 28)
                };
                _queryStatus = new LiteButton("查询状态", false) { Width = UIUtils.S(84), Height = UIUtils.S(28) };
                _sendOffer.Click += async (_, __) =>
                {
                    if (_entry.Order != null)
                        await _actions.RunPrimaryActionAsync(_entry.Order);
                };
                _queryStatus.Click += async (_, __) =>
                {
                    if (_entry.Order != null)
                        await _actions.QueryStatusAsync(_entry.Order);
                };
                _actions.TrackActionButton(_sendOffer);
                _actions.TrackActionButton(_queryStatus);
                Controls.Add(_sendOffer);
                Controls.Add(_queryStatus);
            }
        }

        public void Render(YouPinSaleReminderOrderListEntry entry)
        {
            if (string.Equals(_renderSignature, entry.RenderSignature, StringComparison.Ordinal))
            {
                _entry = entry;
                if (entry.Order != null && (_thumbnail.Image == null || _thumbnailIsPlaceholder))
                    LoadThumbnail(entry.Order.ImageUrl);
                return;
            }

            YouPinSaleReminderOrderListEntryKind previousKind = _entry.Kind;
            _entry = entry;
            _renderSignature = entry.RenderSignature;
            bool hasOrder = entry.Kind == YouPinSaleReminderOrderListEntryKind.Order && entry.Order != null;
            bool isSection = entry.Kind == YouPinSaleReminderOrderListEntryKind.Section;
            bool isEmpty = entry.Kind == YouPinSaleReminderOrderListEntryKind.Empty;
            bool layoutChanged = previousKind != entry.Kind;

            SuspendLayout();
            try
            {
                SetVisible(_tag, hasOrder, ref layoutChanged);
                SetVisible(_title, true, ref layoutChanged);
                SetVisible(_thumbnail, hasOrder, ref layoutChanged);
                SetVisible(_item, hasOrder || isSection, ref layoutChanged);
                SetVisible(_meta, hasOrder && !_compactQuoteStyle, ref layoutChanged);
                SetVisible(_time, hasOrder, ref layoutChanged);
                SetVisible(_price, hasOrder && _compactQuoteStyle, ref layoutChanged);
                SetVisible(_detail, hasOrder, ref layoutChanged);
                if (_sendOffer != null)
                {
                    bool showPrimaryAction = hasOrder && _waitDeliverActions;
                    if (showPrimaryAction && _compactQuoteStyle)
                        showPrimaryAction = YouPinSaleReminderOrderDisplay.IsActionableQuoteOrder(entry.Order!);
                    SetVisible(_sendOffer, showPrimaryAction, ref layoutChanged);
                }
                if (_queryStatus != null)
                    SetVisible(_queryStatus, false, ref layoutChanged);

                if (isSection)
                {
                    SetText(_title, entry.SectionTitle);
                    SetFont(_title, _titleFont);
                    SetForeColor(_title, entry.SectionActionable ? UIColors.TextMain : UIColors.TextSub);
                    SetText(_item, entry.SectionSubtitle);
                    SetForeColor(_item, UIColors.TextSub);
                    ClearThumbnail();
                }
                else if (isEmpty)
                {
                    SetText(_title, entry.EmptyText);
                    SetFont(_title, _emptyFont);
                    SetForeColor(_title, UIColors.TextSub);
                    SetText(_item, string.Empty);
                    ClearThumbnail();
                }
                else
                {
                    YouPinSaleOrder order = entry.Order!;
                    SetText(_title, _compactQuoteStyle ? BuildCompactTitle(order) : YouPinSaleReminderOrderDisplay.BuildTitle(order));
                    SetFont(_title, _compactQuoteStyle ? _compactTitleFont : _titleFont);
                    SetForeColor(_title, UIColors.TextMain);
                    SetText(_item, _compactQuoteStyle ? BuildCompactSubTitle(order) : YouPinSaleReminderOrderDisplay.BuildItemText(order));
                    SetText(_meta, YouPinSaleReminderOrderDisplay.BuildMeta(order));
                    SetText(_time, YouPinSaleReminderOrderDisplay.BuildTime(order));
                    SetText(_price, FormatPrice(order.Price));
                    if (_compactQuoteStyle)
                    {
                        SetText(_tag, YouPinSaleReminderOrderDisplay.BuildCompactQuoteStatusText(order));
                        ApplyTagTone(order);
                    }
                    else
                    {
                        SetText(_tag, "交易订单");
                        SetForeColor(_tag, UIColors.TextSub);
                        SetBackColor(_tag, UIColors.IsDark ? Color.FromArgb(24, 30, 37) : Color.FromArgb(242, 244, 247));
                    }
                    if (_sendOffer != null)
                    {
                        var action = YouPinSaleOrderActionResolver.Resolve(order);
                        string sendText = BuildCompactActionText(action);
                        if (!string.Equals(_sendOffer.Text, sendText, StringComparison.Ordinal))
                            _sendOffer.Text = sendText;
                        _sendOffer.Enabled = action.CanRun;
                        _sendOffer.IsActive = _compactQuoteStyle && YouPinSaleReminderOrderDisplay.IsActionableQuoteOrder(order);
                    }
                    if (_queryStatus != null)
                    {
                        if (!string.Equals(_queryStatus.Text, "查询状态", StringComparison.Ordinal))
                            _queryStatus.Text = "查询状态";
                    }
                    LoadThumbnail(order.ImageUrl);
                }
            }
            finally
            {
                ResumeLayout(false);
            }

            if (layoutChanged)
                PerformLayout();
            Invalidate();
        }

        private void LoadThumbnail(string imageUrl)
        {
            using (UiJankProfiler.Measure("YouPinSaleReminder.LoadThumbnail", string.IsNullOrWhiteSpace(imageUrl) ? "NoUrl" : "Url", thresholdMs: 1))
            {
                string normalizedUrl = imageUrl?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(normalizedUrl))
                {
                    _thumbnailUrl = string.Empty;
                    _thumbnailLoadingUrl = string.Empty;
                    Interlocked.Increment(ref _imageVersion);
                    ShowThumbnailPlaceholder();
                    return;
                }

                if (string.Equals(_thumbnailUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase)
                    && _thumbnail.Image != null
                    && !_thumbnailIsPlaceholder)
                {
                    _thumbnail.Visible = true;
                    return;
                }

                if (string.Equals(_thumbnailUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase)
                    && _thumbnailIsPlaceholder
                    && string.Equals(_thumbnailLoadingUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    _thumbnail.Visible = true;
                    return;
                }

                _thumbnail.Visible = true;
                _thumbnailUrl = normalizedUrl;
                _thumbnailLoadingUrl = normalizedUrl;
                int version = Interlocked.Increment(ref _imageVersion);

                if (YouPinSaleReminderOrderImages.TryGet(normalizedUrl, out Image? cached))
                {
                    _thumbnailLoadingUrl = string.Empty;
                    _thumbnailIsPlaceholder = false;
                    if (!ReferenceEquals(_thumbnail.Image, cached))
                        _thumbnail.Image = cached;
                    return;
                }

                ShowThumbnailPlaceholder();
                _ = LoadThumbnailAsync(normalizedUrl, version);
            }
        }

        private async Task LoadThumbnailAsync(string imageUrl, int version)
        {
            Image? image = await YouPinSaleReminderOrderImages.GetAsync(imageUrl).ConfigureAwait(false);
            if (IsDisposed)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || version != Volatile.Read(ref _imageVersion))
                        return;
                    if (!string.Equals(_thumbnailUrl, imageUrl, StringComparison.OrdinalIgnoreCase))
                        return;

                    _thumbnailLoadingUrl = string.Empty;
                    _thumbnailIsPlaceholder = image == null;
                    _thumbnail.Image = image ?? YouPinSaleReminderOrderImages.GetPlaceholder();
                }));
            }
            catch
            {
                // Row may have been disposed while a virtual-list image request was in flight.
            }
        }

        private void ClearThumbnail()
        {
            if (string.IsNullOrEmpty(_thumbnailUrl) && _thumbnail.Image == null && !_thumbnail.Visible)
                return;

            _thumbnailUrl = string.Empty;
            _thumbnailLoadingUrl = string.Empty;
            _thumbnailIsPlaceholder = false;
            Interlocked.Increment(ref _imageVersion);
            _thumbnail.Image = null;
            _thumbnail.Visible = false;
        }

        private void ShowThumbnailPlaceholder()
        {
            _thumbnail.Visible = true;
            Image placeholder = YouPinSaleReminderOrderImages.GetPlaceholder();
            _thumbnailIsPlaceholder = true;
            if (!ReferenceEquals(_thumbnail.Image, placeholder))
                _thumbnail.Image = placeholder;
        }

        private static Label CreateLabel(string text, bool strong = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", strong ? 9F : 8.8F, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Label CreateMutedLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Label CreateTagLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = UIColors.TextSub,
                BackColor = UIColors.IsDark ? Color.FromArgb(24, 30, 37) : Color.FromArgb(242, 244, 247),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private static void SetText(Label label, string text)
        {
            if (!string.Equals(label.Text, text, StringComparison.Ordinal))
                label.Text = text;
        }

        private static void SetVisible(Control control, bool visible, ref bool layoutChanged)
        {
            if (control.Visible == visible)
                return;

            control.Visible = visible;
            layoutChanged = true;
        }

        private static void SetFont(Label label, Font font)
        {
            if (!ReferenceEquals(label.Font, font))
                label.Font = font;
        }

        private static void SetForeColor(Label label, Color color)
        {
            if (label.ForeColor.ToArgb() != color.ToArgb())
                label.ForeColor = color;
        }

        private static void SetBackColor(Label label, Color color)
        {
            if (label.BackColor.ToArgb() != color.ToArgb())
                label.BackColor = color;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_compactQuoteStyle)
            {
                LayoutCompactQuoteRow();
                return;
            }

            if (_entry.Kind == YouPinSaleReminderOrderListEntryKind.Empty
                || _entry.Kind == YouPinSaleReminderOrderListEntryKind.Section)
            {
                _title.SetBounds(0, 0, Math.Max(1, Width), Height);
                if (_entry.Kind == YouPinSaleReminderOrderListEntryKind.Section)
                    _item.SetBounds(0, UIUtils.S(30), Math.Max(1, Width), UIUtils.S(22));
                return;
            }

            int gap = UIUtils.S(10);
            int padX = UIUtils.S(8);
            int right = Math.Max(padX, Width - padX);
            int detailWidth = UIUtils.S(76);
            int timeWidth = UIUtils.S(138);
            _time.SetBounds(Math.Max(padX, right - timeWidth), UIUtils.S(12), timeWidth, UIUtils.S(22));
            _detail.SetBounds(Math.Max(padX, right - detailWidth), Height - UIUtils.S(36), detailWidth, UIUtils.S(26));
            right = Math.Min(_time.Left, _detail.Left) - gap;

            if (_queryStatus != null && _sendOffer != null && _queryStatus.Visible)
            {
                _queryStatus.SetBounds(Math.Max(padX, right - _queryStatus.Width), Height - UIUtils.S(38), _queryStatus.Width, _queryStatus.Height);
                _sendOffer.SetBounds(Math.Max(padX, _queryStatus.Left - gap - _sendOffer.Width), Height - UIUtils.S(38), _sendOffer.Width, _sendOffer.Height);
                right = Math.Min(right, _sendOffer.Left - gap);
            }
            else if (_sendOffer != null)
            {
                _sendOffer.SetBounds(Math.Max(padX, right - _sendOffer.Width), Height - UIUtils.S(38), _sendOffer.Width, _sendOffer.Height);
                right = Math.Min(right, _sendOffer.Left - gap);
            }

            int tagWidth = UIUtils.S(76);
            int thumbnailSize = UIUtils.S(58);
            _thumbnail.SetBounds(padX, UIUtils.S(19), thumbnailSize, thumbnailSize);
            int textLeft = _thumbnail.Right + UIUtils.S(12);
            int textWidth = Math.Max(1, right - textLeft);
            _title.SetBounds(textLeft, UIUtils.S(10), textWidth, UIUtils.S(24));
            _item.SetBounds(textLeft, _title.Bottom + UIUtils.S(3), textWidth, UIUtils.S(22));
            _tag.SetBounds(textLeft, _item.Bottom + UIUtils.S(4), tagWidth, UIUtils.S(22));
            _meta.SetBounds(_tag.Right + UIUtils.S(10), _item.Bottom + UIUtils.S(4), Math.Max(1, textLeft + textWidth - _tag.Right - UIUtils.S(10)), UIUtils.S(22));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_compactQuoteStyle)
            {
                PaintCompactQuoteRow(e);
                return;
            }

            base.OnPaint(e);
            PaintBottomLine(this, e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _titleFont.Dispose();
                _compactTitleFont.Dispose();
                _compactPriceFont.Dispose();
                _emptyFont.Dispose();
            }
        }

        private void LayoutCompactQuoteRow()
        {
            if (_entry.Kind == YouPinSaleReminderOrderListEntryKind.Empty)
            {
                _title.SetBounds(0, 0, Math.Max(1, Width), Height);
                return;
            }
            if (_entry.Kind == YouPinSaleReminderOrderListEntryKind.Section)
            {
                int left = UIUtils.S(18);
                int sectionRight = Math.Max(left, Width - UIUtils.S(18));
                _title.SetBounds(left, UIUtils.S(13), Math.Max(1, sectionRight - left), UIUtils.S(24));
                _item.SetBounds(left, _title.Bottom + UIUtils.S(1), Math.Max(1, sectionRight - left), UIUtils.S(22));
                return;
            }

            bool compact = Width < UIUtils.S(720);
            int top = UIUtils.S(6);
            int cardHeight = Math.Max(1, Height - UIUtils.S(12));
            int padX = compact ? UIUtils.S(12) : UIUtils.S(18);
            int mid = top + cardHeight / 2;
            int gap = compact ? UIUtils.S(8) : UIUtils.S(14);
            int right = Math.Max(padX, Width - padX);
            int detailWidth = compact ? Math.Min(_detail.Width, UIUtils.S(62)) : _detail.Width;
            int actionWidth = compact ? UIUtils.S(62) : UIUtils.S(72);
            int priceWidth = compact ? UIUtils.S(86) : UIUtils.S(112);
            int statusWidth = compact ? UIUtils.S(94) : UIUtils.S(112);
            _detail.SetBounds(Math.Max(padX, right - detailWidth), mid - _detail.Height / 2, detailWidth, _detail.Height);
            right = _detail.Left - gap;
            if (_sendOffer != null && _sendOffer.Visible)
            {
                _sendOffer.SetBounds(Math.Max(padX, right - actionWidth), mid - _sendOffer.Height / 2, actionWidth, _sendOffer.Height);
                right = _sendOffer.Left - gap;
            }

            _price.SetBounds(Math.Max(padX, right - priceWidth), mid - UIUtils.S(14), priceWidth, UIUtils.S(28));
            right = _price.Left - gap;
            _tag.SetBounds(Math.Max(padX, right - statusWidth), mid - UIUtils.S(13), statusWidth, UIUtils.S(26));
            right = _tag.Left - UIUtils.S(22);

            int thumb = compact ? UIUtils.S(42) : UIUtils.S(48);
            _thumbnail.SetBounds(padX, mid - thumb / 2, thumb, thumb);
            int textLeft = _thumbnail.Right + (compact ? UIUtils.S(10) : UIUtils.S(14));
            int textWidth = Math.Max(1, right - textLeft);
            _title.SetBounds(textLeft, mid - UIUtils.S(25), textWidth, UIUtils.S(24));
            _item.SetBounds(textLeft, _title.Bottom + UIUtils.S(2), textWidth, UIUtils.S(22));
            _meta.SetBounds(0, 0, 0, 0);
            _time.SetBounds(0, 0, 0, 0);
        }

        private void PaintCompactQuoteRow(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var clear = new SolidBrush(Color.Transparent))
                e.Graphics.FillRectangle(clear, ClientRectangle);

            if (_entry.Kind == YouPinSaleReminderOrderListEntryKind.Empty)
            {
                var iconRect = new Rectangle((Width - UIUtils.S(30)) / 2, Math.Max(0, Height / 2 - UIUtils.S(36)), UIUtils.S(30), UIUtils.S(28));
                using var iconPen = new Pen(UIColors.TextSub, 1.6F);
                DrawEmptyInboxIcon(e.Graphics, iconPen, iconRect);
                TextRenderer.DrawText(
                    e.Graphics,
                    _entry.EmptyText,
                    _emptyFont,
                    new Rectangle(UIUtils.S(24), iconRect.Bottom + UIUtils.S(10), Math.Max(1, Width - UIUtils.S(48)), UIUtils.S(44)),
                    UIColors.TextSub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            if (_entry.IsSection)
            {
                using var sectionBack = new SolidBrush(Color.Transparent);
                e.Graphics.FillRectangle(sectionBack, ClientRectangle);
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, UIUtils.S(18), Height - 1, Math.Max(UIUtils.S(18), Width - UIUtils.S(18)), Height - 1);
                return;
            }

            var rect = new Rectangle(0, UIUtils.S(5), Math.Max(1, Width - 1), Math.Max(1, Height - UIUtils.S(10)));
            using var path = RoundedRect(rect, UIUtils.S(6));
            using var fill = new SolidBrush(UIColors.CardBg);
            using var border = new Pen(UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private static void DrawEmptyInboxIcon(Graphics graphics, Pen pen, Rectangle rect)
        {
            int radius = UIUtils.S(3);
            using (var path = RoundedRect(rect, radius))
                graphics.DrawPath(pen, path);

            int foldY = rect.Top + UIUtils.S(12);
            int centerX = rect.Left + rect.Width / 2;
            graphics.DrawLine(pen, rect.Left + UIUtils.S(4), foldY, rect.Left + UIUtils.S(10), foldY);
            graphics.DrawLine(pen, rect.Right - UIUtils.S(10), foldY, rect.Right - UIUtils.S(4), foldY);
            graphics.DrawLine(pen, rect.Left + UIUtils.S(10), foldY, centerX - UIUtils.S(3), foldY + UIUtils.S(5));
            graphics.DrawLine(pen, centerX + UIUtils.S(3), foldY + UIUtils.S(5), rect.Right - UIUtils.S(10), foldY);
            graphics.DrawLine(pen, centerX - UIUtils.S(3), foldY + UIUtils.S(5), centerX + UIUtils.S(3), foldY + UIUtils.S(5));
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static string BuildCompactTitle(YouPinSaleOrder order)
        {
            string name = YouPinSaleReminderOrderDisplay.BuildItemName(order);
            return string.IsNullOrWhiteSpace(name) ? "悠悠有品订单" : name;
        }

        private static string BuildCompactSubTitle(YouPinSaleOrder order)
        {
            return YouPinSaleReminderOrderDisplay.BuildCompactQuoteMeta(order);
        }

        private static string BuildCompactActionText(YouPinSaleOrderAction action)
        {
            return action.Kind switch
            {
                YouPinSaleOrderActionKind.SendOffer => "发报价",
                YouPinSaleOrderActionKind.QueryStatus => "查状态",
                _ => action.ButtonText
            };
        }

        private void ApplyTagTone(YouPinSaleOrder order)
        {
            string status = YouPinSaleReminderOrderDisplay.BuildCompactQuoteStatusText(order);
            if (status.Contains("确认", StringComparison.Ordinal) && !status.Contains("对方", StringComparison.Ordinal))
            {
                SetForeColor(_tag, Color.FromArgb(115, 186, 255));
                SetBackColor(_tag, UIColors.IsDark ? Color.FromArgb(30, 51, 75) : Color.FromArgb(232, 244, 255));
                return;
            }
            if (status.Contains("发送", StringComparison.Ordinal))
            {
                SetForeColor(_tag, UIColors.TextWarn);
                SetBackColor(_tag, UIColors.IsDark ? Color.FromArgb(55, 43, 22) : Color.FromArgb(255, 247, 230));
                return;
            }

            SetForeColor(_tag, UIColors.TextSub);
            SetBackColor(_tag, UIColors.IsDark ? Color.FromArgb(24, 30, 37) : Color.FromArgb(242, 244, 247));
        }

        private static string FormatPrice(double price)
        {
            return price <= 0 ? "—" : "¥" + price.ToString("#,0.##");
        }

        private static void PaintBottomLine(object? sender, PaintEventArgs e)
        {
            if (sender is not Control control)
                return;

            using var pen = new Pen(UIColors.Border);
            e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width, control.Height - 1);
        }
    }

    internal static class YouPinSaleReminderOrderImages
    {
        private static readonly RemoteImageCache OrderImages = RemoteImageCache.CreateDomestic(
            timeoutSeconds: 10,
            maxBytes: 2 * 1024 * 1024,
            failureTtl: TimeSpan.FromMinutes(30));
        private static readonly Lazy<Image> DarkOrderImagePlaceholder = new(() => CreateOrderImagePlaceholder(dark: true));
        private static readonly Lazy<Image> LightOrderImagePlaceholder = new(() => CreateOrderImagePlaceholder(dark: false));

        public static bool TryGet(string imageUrl, out Image? image)
        {
            return OrderImages.TryGet(imageUrl, out image);
        }

        public static Task<Image?> GetAsync(string imageUrl)
        {
            return OrderImages.GetAsync(imageUrl);
        }

        public static Image GetPlaceholder()
        {
            return UIColors.IsDark ? DarkOrderImagePlaceholder.Value : LightOrderImagePlaceholder.Value;
        }

        private static Image CreateOrderImagePlaceholder(bool dark)
        {
            var bitmap = new Bitmap(96, 96);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(dark ? Color.FromArgb(25, 31, 38) : Color.FromArgb(245, 247, 250));

            var card = new Rectangle(14, 16, 68, 64);
            using (var fill = new SolidBrush(dark ? Color.FromArgb(35, 43, 52) : Color.FromArgb(235, 239, 244)))
                graphics.FillRectangle(fill, card);
            using (var border = new Pen(dark ? Color.FromArgb(70, 82, 98) : Color.FromArgb(190, 199, 210), 2f))
                graphics.DrawRectangle(border, card);

            using (var accent = new Pen(dark ? Color.FromArgb(88, 164, 255) : Color.FromArgb(0, 120, 215), 4f))
            {
                graphics.DrawLine(accent, 28, 58, 44, 44);
                graphics.DrawLine(accent, 44, 44, 56, 54);
                graphics.DrawLine(accent, 56, 54, 70, 36);
            }

            using var brush = new SolidBrush(dark ? Color.FromArgb(145, 158, 174) : Color.FromArgb(108, 118, 132));
            using var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            SizeF textSize = graphics.MeasureString("无图", font);
            graphics.DrawString("无图", font, brush, (bitmap.Width - textSize.Width) / 2f, 68);
            return bitmap;
        }
    }
}
