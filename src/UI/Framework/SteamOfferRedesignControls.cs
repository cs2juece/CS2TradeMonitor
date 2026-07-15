using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal sealed class SteamOfferRuleCard : Panel
    {
        private readonly Label _radio;
        private readonly Label _title;
        private readonly Label _desc;
        private bool _selected;

        public SteamOfferRuleCard(SteamOfferRuleOption option)
        {
            Option = option ?? throw new ArgumentNullException(nameof(option));
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _radio = CreateLabel("", 12F, FontStyle.Bold, UIColors.Primary);
            _title = CreateLabel(option.Title, 8.8F, FontStyle.Bold, UIColors.TextMain);
            _desc = CreateLabel(option.Description, 7.8F, FontStyle.Regular, UIColors.TextSub);
            Controls.AddRange(new Control[] { _radio, _title, _desc });
            foreach (Control child in Controls)
            {
                child.Cursor = Cursors.Hand;
                child.Click += (_, __) => TitleClicked?.Invoke();
            }
        }

        public SteamOfferRuleOption Option { get; }

        public event Action? TitleClicked;

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                    return;
                _selected = value;
                _radio.Text = value ? "●" : "○";
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int pad = UIUtils.S(10);
            _radio.SetBounds(pad, UIUtils.S(8), UIUtils.S(22), UIUtils.S(24));
            _title.SetBounds(_radio.Right + UIUtils.S(6), UIUtils.S(7), Math.Max(1, Width - _radio.Right - pad - UIUtils.S(6)), UIUtils.S(24));
            _desc.SetBounds(_title.Left, UIUtils.S(33), Math.Max(1, Width - _title.Left - pad), UIUtils.S(22));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(4));
            Color fill = Selected ? Color.FromArgb(UIColors.IsDark ? 35 : 24, UIColors.Primary) : UIColors.ControlBg;
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(Selected ? UIColors.Primary : UIColors.Border);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, style),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }
    }

    internal sealed class SteamOfferListSurface : Panel
    {
        private const int InitialVisibleOfferLimit = 20;
        internal static int EmptySurfaceHeight => UIUtils.S(116);
        private static int SurfaceVerticalPadding => UIUtils.S(20);
        private static int CollapsedRowHeight => UIUtils.S(72);
        private static int ExpandedRowHeight => UIUtils.S(132);
        private static int FooterHeight => UIUtils.S(42);

        private readonly Action<SteamOfferItem> _toggleDetails;
        private readonly Func<SteamOfferItem, Task> _acceptAsync;
        private readonly Label _empty;
        private readonly Label _footerLabel;
        private readonly LiteButton _toggleAllButton;
        private readonly Dictionary<string, SteamOfferRedesignRow> _rowCache = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<SteamOfferItem> _offers = Array.Empty<SteamOfferItem>();
        private string _expandedOfferId = "";
        private bool _showAllOffers;

        public SteamOfferListSurface(Action<SteamOfferItem> toggleDetails, Func<SteamOfferItem, Task> acceptAsync)
        {
            _toggleDetails = toggleDetails ?? throw new ArgumentNullException(nameof(toggleDetails));
            _acceptAsync = acceptAsync ?? throw new ArgumentNullException(nameof(acceptAsync));
            BackColor = UIColors.ControlBg;
            AutoScroll = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _empty = new Label
            {
                Text = SteamOfferRedesignModel.BuildEmptyOfferText(),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                AutoEllipsis = false
            };
            _footerLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Regular),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                Visible = false
            };
            _toggleAllButton = new LiteButton("显示全部", false)
            {
                Width = UIUtils.S(96),
                Height = UIUtils.S(30),
                Visible = false
            };
            _toggleAllButton.Click += (_, __) =>
            {
                _showAllOffers = !_showAllOffers;
                RenderRows();
                PerformLayout();
            };
            Controls.Add(_empty);
            Controls.Add(_footerLabel);
            Controls.Add(_toggleAllButton);
        }

        public int GetDesiredSurfaceHeight()
        {
            if (_offers.Count == 0)
                return EmptySurfaceHeight;

            int visibleCount = GetVisibleOfferCount();
            int rowsHeight = 0;
            for (int i = 0; i < visibleCount; i++)
            {
                SteamOfferItem offer = _offers[i];
                bool expanded = string.Equals(offer.TradeOfferId, _expandedOfferId, StringComparison.OrdinalIgnoreCase);
                rowsHeight += expanded ? ExpandedRowHeight : CollapsedRowHeight;
            }

            int desired = SurfaceVerticalPadding + rowsHeight + (_offers.Count > InitialVisibleOfferLimit ? FooterHeight : 0);
            return Math.Min(UIUtils.S(_showAllOffers ? 500 : 344), desired);
        }

        public void SetOffers(IReadOnlyList<SteamOfferItem> offers, string expandedOfferId)
        {
            _offers = offers ?? Array.Empty<SteamOfferItem>();
            _expandedOfferId = expandedOfferId ?? "";
            if (_offers.Count <= InitialVisibleOfferLimit)
                _showAllOffers = false;
            AutoScroll = _offers.Count > 0;
            RenderRows();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutRows();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = UIUtils.RoundRect(rect, UIUtils.S(4));
            using var fill = new SolidBrush(UIColors.ControlBg);
            using var border = new Pen(UIColors.Border);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private void RenderRows()
        {
            SuspendLayout();
            try
            {
                int visibleCount = GetVisibleOfferCount();
                var visibleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < visibleCount; i++)
                    visibleKeys.Add(BuildOfferRowKey(_offers[i], i));

                PruneRowCache(visibleKeys);

                _empty.Visible = _offers.Count == 0;
                _footerLabel.Visible = _offers.Count > InitialVisibleOfferLimit;
                _toggleAllButton.Visible = _offers.Count > InitialVisibleOfferLimit;
                _toggleAllButton.Text = _showAllOffers ? "收起" : "显示全部";
                _footerLabel.Text = _offers.Count > InitialVisibleOfferLimit
                    ? SteamOfferRedesignModel.BuildVisibleOfferSummary(visibleCount, _offers.Count, _showAllOffers)
                    : "";
                var desiredRows = new List<SteamOfferRedesignRow>(visibleCount);
                for (int i = 0; i < visibleCount; i++)
                {
                    SteamOfferItem offer = _offers[i];
                    string key = BuildOfferRowKey(offer, i);
                    bool expanded = string.Equals(offer.TradeOfferId, _expandedOfferId, StringComparison.OrdinalIgnoreCase);
                    if (!_rowCache.TryGetValue(key, out SteamOfferRedesignRow? row))
                    {
                        row = new SteamOfferRedesignRow(offer, expanded, _toggleDetails, _acceptAsync);
                        _rowCache[key] = row;
                    }
                    else
                    {
                        row.Render(offer, expanded);
                    }

                    desiredRows.Add(row);
                }

                for (int i = 0; i < desiredRows.Count; i++)
                {
                    SteamOfferRedesignRow row = desiredRows[i];
                    if (!ReferenceEquals(row.Parent, this))
                        Controls.Add(row);
                    Controls.SetChildIndex(row, i);
                }
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private void LayoutRows()
        {
            if (_empty == null || _footerLabel == null || _toggleAllButton == null)
                return;

            int pad = UIUtils.S(10);
            int clientWidth = Math.Max(1, ClientSize.Width);
            _empty.SetBounds(pad, pad, Math.Max(1, clientWidth - pad * 2), Math.Max(1, ClientSize.Height - pad * 2));
            int y = pad;
            foreach (Control control in Controls)
            {
                if (ReferenceEquals(control, _empty)
                    || ReferenceEquals(control, _footerLabel)
                    || ReferenceEquals(control, _toggleAllButton))
                    continue;

                int rowHeight = control is SteamOfferRedesignRow row && row.Expanded ? ExpandedRowHeight : CollapsedRowHeight;
                control.SetBounds(pad, y, Math.Max(1, clientWidth - pad * 2), rowHeight);
                y += rowHeight;
            }

            if (_footerLabel.Visible || _toggleAllButton.Visible)
            {
                y += UIUtils.S(8);
                _toggleAllButton.SetBounds(Math.Max(pad, clientWidth - pad - _toggleAllButton.Width), y, _toggleAllButton.Width, _toggleAllButton.Height);
                _footerLabel.SetBounds(pad, y, Math.Max(1, _toggleAllButton.Left - pad - UIUtils.S(10)), _toggleAllButton.Height);
            }

            AutoScrollMinSize = _offers.Count == 0
                ? Size.Empty
                : new Size(0, y + pad + (_footerLabel.Visible || _toggleAllButton.Visible ? _toggleAllButton.Height : 0));
            HorizontalScroll.Enabled = false;
            HorizontalScroll.Visible = false;
        }

        private int GetVisibleOfferCount()
            => _showAllOffers ? _offers.Count : Math.Min(_offers.Count, InitialVisibleOfferLimit);

        private static string BuildOfferRowKey(SteamOfferItem offer, int index)
        {
            if (!string.IsNullOrWhiteSpace(offer.TradeOfferId))
                return "id:" + offer.TradeOfferId.Trim();

            return "index:" + index.ToString(CultureInfo.InvariantCulture);
        }

        private void PruneRowCache(ISet<string> visibleKeys)
        {
            var staleKeys = _rowCache.Keys
                .Where(key => !visibleKeys.Contains(key))
                .ToList();
            foreach (string key in staleKeys)
            {
                SteamOfferRedesignRow row = _rowCache[key];
                if (ReferenceEquals(row.Parent, this))
                    Controls.Remove(row);
                row.Dispose();
                _rowCache.Remove(key);
            }
        }
    }

    internal sealed class SteamOfferRedesignRow : Panel
    {
        private SteamOfferItem _offer;
        private readonly Action<SteamOfferItem> _toggleDetails;
        private readonly Func<SteamOfferItem, Task> _acceptAsync;
        private readonly SteamOfferThumbnailStrip _thumbs;
        private readonly Label _title;
        private readonly Label _tag;
        private readonly Label _direction;
        private readonly Label _meta;
        private readonly LiteButton _detail;
        private readonly LiteButton _accept;
        private readonly Label _details;

        public SteamOfferRedesignRow(
            SteamOfferItem offer,
            bool expanded,
            Action<SteamOfferItem> toggleDetails,
            Func<SteamOfferItem, Task> acceptAsync)
        {
            _offer = offer ?? throw new ArgumentNullException(nameof(offer));
            _toggleDetails = toggleDetails ?? throw new ArgumentNullException(nameof(toggleDetails));
            _acceptAsync = acceptAsync ?? throw new ArgumentNullException(nameof(acceptAsync));
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _thumbs = new SteamOfferThumbnailStrip();
            _title = CreateLabel("", 9.4F, FontStyle.Bold, UIColors.TextMain);
            _tag = CreateLabel("", 8F, FontStyle.Bold, UIColors.TextMain);
            _direction = CreateLabel("", 8.6F, FontStyle.Regular, UIColors.TextSub);
            _meta = CreateLabel("", 8.3F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleRight);
            _detail = new LiteButton("详情", false) { Width = UIUtils.S(86), Height = UIUtils.S(32) };
            _accept = new LiteButton("同意报价", true) { Width = UIUtils.S(104), Height = UIUtils.S(32) };
            _details = CreateLabel("", 8.4F, FontStyle.Regular, UIColors.TextSub);
            _details.AutoEllipsis = false;

            _detail.Click += (_, __) => _toggleDetails(_offer);
            _accept.Click += async (_, __) => await _acceptAsync(_offer);
            Click += (_, __) => _toggleDetails(_offer);
            foreach (Control child in new Control[] { _thumbs, _title, _tag, _direction, _meta, _details })
                child.Click += (_, __) => _toggleDetails(_offer);

            Controls.AddRange(new Control[] { _thumbs, _title, _tag, _direction, _meta, _detail, _accept, _details });
            Render(offer, expanded);
        }

        public bool Expanded { get; private set; }

        public void Render(SteamOfferItem offer, bool expanded)
        {
            _offer = offer ?? throw new ArgumentNullException(nameof(offer));
            Expanded = expanded;
            _title.Text = BuildTitle(offer);
            _tag.Text = SteamOfferRedesignModel.CategoryTagText(offer);
            _tag.ForeColor = ResolveTagColor(offer);
            _direction.Text = SteamOfferRedesignModel.BuildDirectionLine(offer);
            _direction.ForeColor = SteamOfferRedesignModel.LosesInventory(offer) ? UIColors.TextWarn : UIColors.TextSub;
            _meta.Text = BuildMeta(offer);
            _details.Text = BuildDetails(offer);
            _details.Visible = Expanded;
            _thumbs.SetAssets(offer.ItemsToReceive.Count > 0 ? offer.ItemsToReceive : offer.ItemsToGive);
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int pad = UIUtils.S(12);
            int thumbW = SteamOfferThumbnailStrip.RequiredWidth;
            int top = UIUtils.S(10);
            _thumbs.SetBounds(pad, top, thumbW, UIUtils.S(54));
            int textX = _thumbs.Right + UIUtils.S(14);
            _accept.SetBounds(Width - pad - _accept.Width, top + UIUtils.S(4), _accept.Width, _accept.Height);
            _detail.SetBounds(_accept.Left - UIUtils.S(12) - _detail.Width, _accept.Top, _detail.Width, _detail.Height);
            int contentRight = _detail.Left - UIUtils.S(12);
            int metaW = UIUtils.S(190);
            _meta.SetBounds(Math.Max(textX, contentRight - metaW), top + UIUtils.S(7), metaW, UIUtils.S(24));
            int headingRight = Math.Max(textX, _meta.Left - UIUtils.S(10));
            int headingW = Math.Max(1, headingRight - textX);
            int tagW = Math.Min(UIUtils.S(140), Math.Max(0, headingW / 2));
            int titleW = Math.Max(1, headingW - tagW - UIUtils.S(8));
            _title.SetBounds(textX, top, titleW, UIUtils.S(24));
            _tag.SetBounds(_title.Right + UIUtils.S(8), top + UIUtils.S(2), tagW, UIUtils.S(22));
            _direction.SetBounds(textX, _title.Bottom + UIUtils.S(4), Math.Max(1, contentRight - textX), UIUtils.S(22));
            _details.SetBounds(pad + UIUtils.S(8), UIUtils.S(74), Math.Max(1, Width - pad * 2 - UIUtils.S(16)), UIUtils.S(52));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool warning = SteamOfferRedesignModel.LosesInventory(_offer);
            if (warning)
            {
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using var path = UIUtils.RoundRect(rect, UIUtils.S(4));
                using var fill = new SolidBrush(Color.FromArgb(18, UIColors.TextWarn));
                using var border = new Pen(UIColors.TextWarn);
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
            else
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, UIUtils.S(8), Height - 1, Width - UIUtils.S(8), Height - 1);
            }
        }

        private static string BuildTitle(SteamOfferItem offer)
        {
            string partner = SteamOfferDisplayFormatter.FirstText(offer.PartnerName, offer.Source, "Steam 玩家");
            return "来自 " + partner;
        }

        private static string BuildMeta(SteamOfferItem offer)
        {
            string id = string.IsNullOrWhiteSpace(offer.TradeOfferId) ? "暂无报价号" : "#" + offer.TradeOfferId.TrimStart('#');
            string time = offer.CreatedAt == default ? "暂无时间" : offer.CreatedAt.ToString("今天 HH:mm");
            return $"{id} · {time}";
        }

        private static string BuildDetails(SteamOfferItem offer)
        {
            string partner = SteamOfferDisplayFormatter.FirstText(offer.PartnerName, offer.PartnerSteamId, "未知");
            string receive = SteamOfferDisplayFormatter.BuildAssetList(offer.ItemsToReceive, int.MaxValue);
            string give = SteamOfferDisplayFormatter.BuildAssetList(offer.ItemsToGive, int.MaxValue);
            string category = SteamOfferRedesignModel.CategoryTagText(offer);
            string id = string.IsNullOrWhiteSpace(offer.TradeOfferId) ? "暂无报价号" : "#" + offer.TradeOfferId.TrimStart('#');
            string action = SteamOfferRedesignModel.LosesInventory(offer)
                ? "可在本项目同意，执行前核对失去物品"
                : "可在本项目同意";
            return $"失去：{give}    收到：{receive}\n对方：{partner}    报价：{id}    来源：{category}    操作：{action}";
        }

        private static Color ResolveTagColor(SteamOfferItem offer)
        {
            return SteamOfferRedesignModel.Classify(offer) switch
            {
                SteamOfferRedesignCategory.Pure => UIColors.Positive,
                SteamOfferRedesignCategory.YouPinPurchase => Color.FromArgb(0, 190, 210),
                SteamOfferRedesignCategory.YouPinSale => UIColors.Primary,
                SteamOfferRedesignCategory.YouPinRental => Color.FromArgb(145, 120, 255),
                _ => UIColors.TextWarn
            };
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color, ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, style),
                TextAlign = align
            };
        }
    }

    internal sealed class SteamOfferThumbnailStrip : Panel
    {
        private const int MaxVisibleThumbnails = 3;
        private readonly List<PictureBox> _boxes = new();
        private string _assetSignature = "";
        private int _imageVersion;

        public SteamOfferThumbnailStrip()
        {
            BackColor = Color.Transparent;
        }

        public static int RequiredWidth
            => UIUtils.S(54 * MaxVisibleThumbnails + 6 * (MaxVisibleThumbnails - 1));

        public void SetAssets(IReadOnlyList<TradeAsset> assets)
        {
            string signature = BuildAssetSignature(assets);
            if (string.Equals(_assetSignature, signature, StringComparison.Ordinal))
                return;

            _assetSignature = signature;
            int version = Interlocked.Increment(ref _imageVersion);
            var oldControls = Controls.Cast<Control>().ToList();
            Controls.Clear();
            foreach (Control old in oldControls)
                old.Dispose();
            _boxes.Clear();
            foreach (TradeAsset asset in assets.Take(MaxVisibleThumbnails))
            {
                var box = new PictureBox
                {
                    BackColor = UIColors.InputBg,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = SteamOfferImageProvider.GetPlaceholder()
                };
                Controls.Add(box);
                _boxes.Add(box);
                if (!string.IsNullOrWhiteSpace(asset.IconUrl))
                    _ = LoadImageAsync(box, asset.IconUrl, version);
            }
            if (_boxes.Count == 0)
            {
                var box = new PictureBox
                {
                    BackColor = UIColors.InputBg,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = SteamOfferImageProvider.GetPlaceholder()
                };
                Controls.Add(box);
                _boxes.Add(box);
            }
            PerformLayout();
        }

        private static string BuildAssetSignature(IReadOnlyList<TradeAsset> assets)
        {
            return string.Join(
                "\u001f",
                assets
                    .Take(MaxVisibleThumbnails)
                    .Select(asset => string.Join(
                        "\u001e",
                        asset.AssetId,
                        asset.ClassId,
                        asset.InstanceId,
                        asset.IconUrl,
                        asset.MarketHashName)));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int size = UIUtils.S(54);
            int gap = UIUtils.S(6);
            int x = 0;
            foreach (PictureBox box in _boxes)
            {
                box.SetBounds(x, 0, size, size);
                x += size + gap;
            }
        }

        private async Task LoadImageAsync(PictureBox box, string url, int version)
        {
            Image? image = await SteamOfferImageProvider.GetAsync(url).ConfigureAwait(false);
            if (IsDisposed || version != Volatile.Read(ref _imageVersion))
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || version != Volatile.Read(ref _imageVersion))
                        return;
                    box.Image = image ?? SteamOfferImageProvider.GetPlaceholder();
                }));
            }
            catch
            {
                // 行控件可能已被虚拟列表回收或释放，图片回填失败可忽略。
            }
        }
    }

    internal sealed record SteamOfferConfirmResult(bool Confirmed, bool SkipNextTime);

    internal sealed class SteamOfferRedesignConfirmDialog : Form
    {
        private readonly LiteCheck _skip;
        private readonly TableLayoutPanel _grid;
        private readonly Panel _gridHost;
        private readonly IReadOnlyList<LiteDetailDialog.DetailRow> _rows;

        private SteamOfferRedesignConfirmDialog(string title, string body, IReadOnlyList<LiteDetailDialog.DetailRow> rows, string okText)
        {
            _rows = rows;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = UIColors.CardBg;
            ForeColor = UIColors.TextMain;
            ClientSize = BuildClientSize(rows, body);
            Font = new Font("Microsoft YaHei UI", 9F);

            var titleLabel = CreateLabel(title, 13F, FontStyle.Bold, UIColors.TextMain);
            var bodyLabel = CreateLabel(body, 9F, FontStyle.Regular, UIColors.TextSub);
            bodyLabel.AutoEllipsis = false;
            _grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 0,
                BackColor = UIColors.ControlBg,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UIUtils.S(128)));
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            foreach (LiteDetailDialog.DetailRow row in rows)
            {
                int index = _grid.RowCount;
                _grid.RowCount = index + 1;
                _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(38)));
                _grid.Controls.Add(CreateCell(row.Label, UIColors.TextSub), 0, index);
                _grid.Controls.Add(CreateCell(row.Value, row.Value.Contains("失去", StringComparison.OrdinalIgnoreCase) ? UIColors.TextWarn : UIColors.TextMain), 1, index);
            }
            _gridHost = new Panel
            {
                AutoScroll = true,
                BackColor = UIColors.ControlBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _gridHost.Controls.Add(_grid);

            _skip = new LiteCheck(false, "下次不再提醒") { Width = UIUtils.S(150) };
            var cancel = new LiteButton("取消", false) { Width = UIUtils.S(96), Height = UIUtils.S(34) };
            var ok = new LiteButton(okText, false) { Width = UIUtils.S(112), Height = UIUtils.S(34), ForeColor = UIColors.TextWarn };
            cancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            ok.Click += (_, __) =>
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { titleLabel, bodyLabel, _gridHost, _skip, cancel, ok });
            Layout += (_, __) =>
            {
                int pad = UIUtils.S(24);
                titleLabel.SetBounds(pad, UIUtils.S(22), ClientSize.Width - pad * 2, UIUtils.S(32));
                int bodyHeight = MeasureTextHeight(bodyLabel.Text, bodyLabel.Font, ClientSize.Width - pad * 2, UIUtils.S(42), UIUtils.S(72));
                bodyLabel.SetBounds(pad, titleLabel.Bottom + UIUtils.S(8), ClientSize.Width - pad * 2, bodyHeight);
                int gridTop = bodyLabel.Bottom + UIUtils.S(12);
                int gridWidth = ClientSize.Width - pad * 2;
                int valueWidth = Math.Max(UIUtils.S(220), gridWidth - UIUtils.S(128) - UIUtils.S(4));
                int neededGridHeight = LayoutGridRows(valueWidth);
                int maxGridHeight = Math.Max(
                    UIUtils.S(120),
                    ClientSize.Height - gridTop - UIUtils.S(14) - UIUtils.S(26) - UIUtils.S(66));
                int gridHostHeight = Math.Min(neededGridHeight + UIUtils.S(2), maxGridHeight);
                _gridHost.SetBounds(pad, gridTop, gridWidth, gridHostHeight);
                int gridContentWidth = Math.Max(1, _gridHost.ClientSize.Width);
                if (neededGridHeight > _gridHost.ClientSize.Height)
                    gridContentWidth = Math.Max(1, gridContentWidth - SystemInformation.VerticalScrollBarWidth);
                _grid.SetBounds(0, 0, gridContentWidth, neededGridHeight);
                _skip.SetBounds(pad, _gridHost.Bottom + UIUtils.S(14), UIUtils.S(170), UIUtils.S(26));
                ok.SetBounds(ClientSize.Width - pad - ok.Width, ClientSize.Height - UIUtils.S(54), ok.Width, ok.Height);
                cancel.SetBounds(ok.Left - UIUtils.S(12) - cancel.Width, ok.Top, cancel.Width, cancel.Height);
            };
        }

        public static SteamOfferConfirmResult ShowSingle(IWin32Window? owner, SteamOfferItem offer)
        {
            string body = "确认同意该 Steam 报价？请核对收到和失去的物品。";
            var rows = new[]
            {
                new LiteDetailDialog.DetailRow("将失去", BuildConfirmationAssetList(offer.ItemsToGive)),
                new LiteDetailDialog.DetailRow("将收到", BuildConfirmationAssetList(offer.ItemsToReceive)),
                new LiteDetailDialog.DetailRow("来源判定", SteamOfferRedesignModel.CategoryTagText(offer)),
                new LiteDetailDialog.DetailRow("操作", "Steam 同意报价")
            };
            using var dialog = new SteamOfferRedesignConfirmDialog("确认同意报价", body, rows, "确认同意");
            DialogResult result = dialog.ShowDialog(owner);
            return new SteamOfferConfirmResult(result == DialogResult.OK, dialog._skip.Checked);
        }

        internal static string BuildConfirmationAssetList(IReadOnlyList<TradeAsset>? assets)
        {
            if (assets == null || assets.Count == 0)
                return "无";

            return string.Join(Environment.NewLine, assets.Select(asset =>
            {
                string name = SteamOfferDisplayFormatter.FirstText(asset.MarketHashName, asset.AssetId, "未知物品");
                return asset.Amount > 1 ? $"{name} x{asset.Amount}" : name;
            }));
        }

        internal static Size BuildLogicalClientSize(IReadOnlyList<LiteDetailDialog.DetailRow> rows, string body)
        {
            int longestValue = rows.Count == 0
                ? 0
                : rows.Max(row => (row.Value ?? string.Empty).Length);
            int width = longestValue > 80 ? 760 : 700;
            int valueWidth = width - 48 - 128;
            int rowHeight = rows.Sum(row => EstimateLogicalRowHeight(row.Value, valueWidth));
            int bodyHeight = EstimateLogicalTextHeight(body, width - 48, min: 42, max: 72);
            int height = 22 + 32 + 8 + bodyHeight + 12 + rowHeight + 14 + 26 + 66;
            return new Size(width, Math.Clamp(height, 400, 620));
        }

        private static Size BuildClientSize(IReadOnlyList<LiteDetailDialog.DetailRow> rows, string body)
        {
            Size preferred = UIUtils.S(BuildLogicalClientSize(rows, body));
            Rectangle workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, preferred.Width, preferred.Height);
            int safeMargin = UIUtils.S(80);
            int minWidth = UIUtils.S(560);
            int minHeight = UIUtils.S(360);
            int maxWidth = Math.Max(minWidth, workingArea.Width - safeMargin);
            int maxHeight = Math.Max(minHeight, workingArea.Height - safeMargin);
            return new Size(
                Math.Min(preferred.Width, maxWidth),
                Math.Min(preferred.Height, maxHeight));
        }

        private int LayoutGridRows(int valueWidth)
        {
            int total = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                int rowHeight = MeasureTextHeight(
                    _rows[i].Value,
                    Font,
                    valueWidth - UIUtils.S(20),
                    UIUtils.S(38),
                    UIUtils.S(160));
                rowHeight = Math.Max(UIUtils.S(38), rowHeight + UIUtils.S(10));
                _grid.RowStyles[i].Height = rowHeight;
                total += rowHeight;
            }

            return Math.Max(UIUtils.S(120), total);
        }

        private static int EstimateLogicalRowHeight(string? text, int valueWidth)
        {
            int normalizedLength = string.IsNullOrWhiteSpace(text) ? 2 : text.Trim().Length;
            int charsPerLine = Math.Max(18, valueWidth / 9);
            int lines = Math.Clamp((int)Math.Ceiling(normalizedLength / (double)charsPerLine), 1, 5);
            return Math.Max(38, lines * 20 + 16);
        }

        private static int EstimateLogicalTextHeight(string? text, int width, int min, int max)
        {
            int normalizedLength = string.IsNullOrWhiteSpace(text) ? 0 : text.Trim().Length;
            int charsPerLine = Math.Max(24, width / 9);
            int lines = Math.Clamp((int)Math.Ceiling(Math.Max(1, normalizedLength) / (double)charsPerLine), 1, 3);
            return Math.Clamp(lines * 20 + 8, min, max);
        }

        private static int MeasureTextHeight(string? text, Font font, int width, int min, int max)
        {
            string value = string.IsNullOrWhiteSpace(text) ? "暂无" : text.Trim();
            Size measured = TextRenderer.MeasureText(
                value,
                font,
                new Size(Math.Max(1, width), max),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return Math.Clamp(measured.Height, min, max);
        }

        public static SteamOfferConfirmResult ShowBatch(IWin32Window? owner, BatchAcceptSummary summary)
        {
            string body = $"将同意当前列表全部 {summary.EligibleCount} 条报价，请核对会失去和会收到的物品。";
            var rows = new[]
            {
                new LiteDetailDialog.DetailRow("纳入范围", $"{summary.EligibleCount} 条"),
                new LiteDetailDialog.DetailRow("会失去库存", $"{summary.LosingInventoryCount} 条"),
                new LiteDetailDialog.DetailRow("来源不确定", $"{summary.UnknownSourceCount} 条"),
                new LiteDetailDialog.DetailRow("已排除", $"{summary.ExcludedCount} 条")
            };
            using var dialog = new SteamOfferRedesignConfirmDialog("一键同意所有报价", body, rows, "确认执行");
            DialogResult result = dialog.ShowDialog(owner);
            return new SteamOfferConfirmResult(result == DialogResult.OK, dialog._skip.Checked);
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                BackColor = Color.Transparent,
                ForeColor = color,
                Font = new Font("Microsoft YaHei UI", size, style),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label CreateCell(string text, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = false,
                BackColor = UIColors.ControlBg,
                Dock = DockStyle.Fill,
                ForeColor = color,
                Font = new Font("Microsoft YaHei UI", 9F),
                Margin = Padding.Empty,
                Padding = UIUtils.S(new Padding(10, 0, 10, 0)),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }
    }
}
