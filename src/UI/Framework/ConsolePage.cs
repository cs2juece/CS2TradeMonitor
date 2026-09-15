using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class ConsoleHostPage : FrameworkSettingsHostPage<ConsolePage>
    {
        public ConsoleHostPage()
            : base(new ConsolePage())
        {
        }
    }

    public sealed class ConsolePage : FrameworkSettingsPageBase
    {
        private const int ActivityRowCount = 6;
        private const int UpdateRowCount = 2;

        private readonly MonitoringConsoleSnapshotBuilder _snapshotBuilder;
        private readonly Dictionary<string, ConsoleModuleTile> _moduleTiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConsoleReadinessRow> _readinessRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConsoleActivityRow> _activityRows = new();
        private readonly List<ConsoleUpdateRow> _updateRows = new();

        private TableLayoutPanel? _root;
        private ConsoleHealthBanner? _healthBanner;
        private ConsoleCardPanel? _moduleCard;
        private ConsoleCardPanel? _activityCard;
        private ConsoleCardPanel? _readinessCard;
        private ConsoleCardPanel? _updatesCard;
        private Panel? _moduleTilesHost;
        private Panel? _readinessRowsHost;
        private Panel? _mainHost;
        private Label? _capturedLabel;
        private ConsoleIconButton? _refreshButton;
        private ConsoleSegmentedFilter? _activityFilterControl;
        private MonitoringConsoleSnapshot? _lastSnapshot;
        private ConsoleActivityFilter _activityFilter = ConsoleActivityFilter.All;
        private bool _refreshing;
        private bool _widthSyncQueued;

        private Rectangle ContentBounds
        {
            get
            {
                Rectangle bounds = GetVisibleContentBounds(FrameworkSettingsPageLayoutHelper.WideContentMinimumWidth);
                return new Rectangle(
                    bounds.Left + UIUtils.S(3),
                    bounds.Top,
                    bounds.Width + UIUtils.S(5),
                    bounds.Height);
            }
        }

        private int ContentWidth => ContentBounds.Width;

        public ConsolePage()
            : this(ConsolePageRuntimeServices.Resolve())
        {
        }

        internal ConsolePage(ConsolePageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);
            _snapshotBuilder = runtimeServices.SnapshotBuilder;
            Container.SizeChanged += (_, __) => QueueDeferredContentWidthSync();
        }

        protected override void OnStoreAttached()
        {
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            QueueDeferredContentWidthSync();
            _ = RefreshSnapshotAsync();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            BuildPage();
            if (_lastSnapshot != null)
                ApplySnapshot(_lastSnapshot);
        }

        protected override int GetTopLevelContentWidth()
        {
            return ContentWidth;
        }

        private void BuildPage()
        {
            ClearPage();
            _moduleTiles.Clear();
            _readinessRows.Clear();
            _activityRows.Clear();
            _updateRows.Clear();

            Rectangle bounds = ContentBounds;
            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                AutoSize = false,
                ColumnCount = 1,
                RowCount = 0,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = UIColors.MainBg
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Container.Controls.Add(_root);

            AddRootRow(CreateHeaderPanel());
            AddRootRow(CreateHealthBanner());
            AddRootRow(CreateModuleCard());
            AddRootRow(CreateMainHost());

            QueueDeferredContentWidthSync();
            if (_lastSnapshot != null)
                ApplySnapshot(_lastSnapshot);
        }

        private Control CreateHeaderPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(75),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(14))
            };
            var title = ConsoleUi.Label("控制台", 18F, FontStyle.Bold, UIColors.TextMain);
            var subtitle = ConsoleUi.Label("监控链路与提醒动态，一处掌握。", 10.5F, FontStyle.Regular, UIColors.TextSub);
            _capturedLabel = ConsoleUi.Label("更新时间：正在读取", 9.5F, FontStyle.Regular, UIColors.TextSub, ContentAlignment.MiddleRight);
            _refreshButton = new ConsoleIconButton(ConsoleIconGlyphs.Refresh, "刷新状态")
            {
                Width = UIUtils.S(144),
                Height = UIUtils.S(48),
                Name = "Console.RefreshStatus"
            };
            _refreshButton.Click += async (_, __) => await RefreshSnapshotAsync();

            panel.Controls.AddRange(new Control[] { title, subtitle, _capturedLabel, _refreshButton });
            panel.Layout += (_, __) =>
            {
                int top = UIUtils.S(11);
                int actionTop = UIUtils.S(15);
                int gap = UIUtils.S(16);
                _refreshButton.SetBounds(panel.Width - _refreshButton.Width, actionTop, _refreshButton.Width, _refreshButton.Height);
                _capturedLabel.SetBounds(
                    Math.Max(UIUtils.S(280), _refreshButton.Left - UIUtils.S(244) - gap),
                    actionTop,
                    UIUtils.S(244),
                    _refreshButton.Height);
                int textWidth = Math.Max(1, _capturedLabel.Left - gap);
                title.SetBounds(0, top, textWidth, UIUtils.S(30));
                subtitle.SetBounds(0, title.Bottom + UIUtils.S(3), textWidth, UIUtils.S(24));
            };
            return panel;
        }

        private Control CreateHealthBanner()
        {
            _healthBanner = new ConsoleHealthBanner
            {
                Height = UIUtils.S(98),
                Margin = new Padding(0, 0, 0, UIUtils.S(15))
            };
            return _healthBanner;
        }

        private Control CreateModuleCard()
        {
            _moduleCard = CreateCard(UIUtils.S(144), 15);
            var title = ConsoleUi.Label("监控链路", 11.5F, FontStyle.Bold, UIColors.TextMain);
            _moduleTilesHost = new ConsoleCardPanel
            {
                FillColorOverride = UIColors.ControlBg,
                BorderColorOverride = UIColors.Border,
                Radius = UIUtils.S(4)
            };

            foreach (MonitorModuleDescriptor descriptor in MonitorModuleRegistry.Descriptors)
            {
                var tile = new ConsoleModuleTile(descriptor);
                _moduleTiles[descriptor.Id] = tile;
                _moduleTilesHost.Controls.Add(tile);
            }
            _moduleTilesHost.Paint += (_, e) =>
            {
                int tileCount = _moduleTiles.Count;
                if (tileCount <= 1)
                    return;

                using var pen = new Pen(UIColors.Border);
                for (int i = 1; i < tileCount; i++)
                {
                    int x = _moduleTilesHost.Width * i / tileCount;
                    e.Graphics.DrawLine(pen, x, UIUtils.S(15), x, _moduleTilesHost.Height - UIUtils.S(15));
                }
            };

            _moduleCard.Controls.AddRange(new Control[] { title, _moduleTilesHost });
            _moduleCard.Layout += (_, __) =>
            {
                int pad = UIUtils.S(16);
                title.SetBounds(pad, UIUtils.S(8), Math.Max(1, _moduleCard.Width - pad * 2), UIUtils.S(30));
                _moduleTilesHost.SetBounds(pad, UIUtils.S(47), Math.Max(1, _moduleCard.Width - pad * 2), UIUtils.S(80));
            };
            _moduleTilesHost.Layout += (_, __) => LayoutModuleTiles();
            return _moduleCard;
        }

        private Control CreateMainHost()
        {
            _mainHost = new Panel
            {
                Height = UIUtils.S(608),
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            _activityCard = CreateActivityCard();
            _readinessCard = CreateReadinessCard();
            _updatesCard = CreateUpdatesCard();
            _mainHost.Controls.AddRange(new Control[] { _activityCard, _readinessCard, _updatesCard });
            _mainHost.Layout += (_, __) => LayoutMainCards();
            return _mainHost;
        }

        private ConsoleCardPanel CreateActivityCard()
        {
            var card = CreateCard(UIUtils.S(608), 0);
            var title = ConsoleUi.Label("实时动态", 12F, FontStyle.Bold, UIColors.TextMain);
            _activityFilterControl = new ConsoleSegmentedFilter
            {
                Width = UIUtils.S(208),
                Height = UIUtils.S(30)
            };
            _activityFilterControl.Select(_activityFilter);
            _activityFilterControl.SelectedFilterChanged += filter =>
            {
                _activityFilter = filter;
                if (_lastSnapshot != null)
                    ApplyActivityRows(_lastSnapshot);
            };
            var divider = new Panel { BackColor = UIColors.Border };
            for (int i = 0; i < ActivityRowCount; i++)
            {
                var row = new ConsoleActivityRow();
                _activityRows.Add(row);
                card.Controls.Add(row);
            }

            card.Controls.AddRange(new Control[] { title, _activityFilterControl, divider });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(16);
                title.SetBounds(pad, UIUtils.S(7), UIUtils.S(180), UIUtils.S(36));
                _activityFilterControl.SetBounds(
                    Math.Max(pad, card.Width - pad - _activityFilterControl.Width),
                    UIUtils.S(12),
                    _activityFilterControl.Width,
                    _activityFilterControl.Height);
                divider.SetBounds(pad, UIUtils.S(49), Math.Max(1, card.Width - pad * 2), 1);
                int rowsTop = UIUtils.S(51);
                int rowHeight = Math.Max(UIUtils.S(82), (card.Height - rowsTop - UIUtils.S(8)) / ActivityRowCount);
                for (int i = 0; i < _activityRows.Count; i++)
                    _activityRows[i].SetBounds(pad, rowsTop + i * rowHeight, Math.Max(1, card.Width - pad * 2), rowHeight);
            };
            return card;
        }

        private ConsoleCardPanel CreateReadinessCard()
        {
            var card = CreateCard(UIUtils.S(374), 0);
            var title = ConsoleUi.Label("提醒准备度", 12F, FontStyle.Bold, UIColors.TextMain);
            var divider = new Panel { BackColor = UIColors.Border };
            _readinessRowsHost = new Panel { BackColor = Color.Transparent };
            card.Controls.AddRange(new Control[] { title, divider, _readinessRowsHost });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(16);
                title.SetBounds(pad, UIUtils.S(7), Math.Max(1, card.Width - pad * 2), UIUtils.S(36));
                divider.SetBounds(pad, UIUtils.S(49), Math.Max(1, card.Width - pad * 2), 1);
                _readinessRowsHost.SetBounds(0, UIUtils.S(51), card.Width, Math.Max(1, card.Height - UIUtils.S(58)));
            };
            _readinessRowsHost.Layout += (_, __) => LayoutReadinessRows();
            return card;
        }

        private ConsoleCardPanel CreateUpdatesCard()
        {
            var card = CreateCard(UIUtils.S(220), 0);
            var title = ConsoleUi.Label("最近 CS2 更新", 12F, FontStyle.Bold, UIColors.TextMain);
            var divider = new Panel { BackColor = UIColors.Border };
            for (int i = 0; i < UpdateRowCount; i++)
            {
                var row = new ConsoleUpdateRow();
                row.NavigateRequested += () => SwitchPage("Cs2UpdatePhoneReminder");
                _updateRows.Add(row);
                card.Controls.Add(row);
            }

            card.Controls.AddRange(new Control[] { title, divider });
            card.Layout += (_, __) =>
            {
                int pad = UIUtils.S(16);
                title.SetBounds(pad, UIUtils.S(7), Math.Max(1, card.Width - pad * 2), UIUtils.S(36));
                divider.SetBounds(pad, UIUtils.S(49), Math.Max(1, card.Width - pad * 2), 1);
                int rowsTop = UIUtils.S(51);
                int rowHeight = Math.Max(UIUtils.S(58), (card.Height - rowsTop - UIUtils.S(8)) / UpdateRowCount);
                for (int i = 0; i < _updateRows.Count; i++)
                    _updateRows[i].SetBounds(0, rowsTop + i * rowHeight, card.Width, rowHeight);
            };
            return card;
        }

        private async Task RefreshSnapshotAsync()
        {
            if (_refreshing || Config == null)
                return;

            _refreshing = true;
            SetRefreshButtonState(enabled: false, "读取中");
            try
            {
                MonitoringConsoleSnapshot snapshot = await _snapshotBuilder.BuildAsync(Config, PageToken);
                if (IsDisposed || PageToken.IsCancellationRequested)
                    return;
                ApplySnapshot(snapshot);
            }
            catch (OperationCanceledException)
            {
                // Page deactivation cancels a read-only refresh; no user-facing error is needed.
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored(
                    "MonitoringConsole",
                    "RefreshSnapshot",
                    ex,
                    retryable: true,
                    category: "UI");
                _healthBanner?.ApplyFailure();
                if (_capturedLabel != null)
                    _capturedLabel.Text = "更新时间：读取失败";
            }
            finally
            {
                _refreshing = false;
                SetRefreshButtonState(enabled: true, "刷新状态");
            }
        }

        private void ApplySnapshot(MonitoringConsoleSnapshot snapshot)
        {
            _lastSnapshot = snapshot;
            _healthBanner?.Apply(snapshot);
            if (_capturedLabel != null)
                _capturedLabel.Text = "更新时间： " + snapshot.CapturedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (MonitorModuleHealth health in snapshot.Modules)
            {
                if (_moduleTiles.TryGetValue(health.Id, out ConsoleModuleTile? tile))
                    tile.Apply(health);
            }

            EnsureReadinessRows(snapshot.AlertReadiness);
            foreach (AlertReadinessSnapshot readiness in snapshot.AlertReadiness)
            {
                if (_readinessRows.TryGetValue(readiness.Id, out ConsoleReadinessRow? row))
                    row.Apply(readiness);
            }

            ApplyActivityRows(snapshot);
            ApplyUpdateRows(snapshot);
            _moduleCard?.Invalidate(true);
            _activityCard?.Invalidate(true);
            _readinessCard?.Invalidate(true);
            _updatesCard?.Invalidate(true);
        }

        private void EnsureReadinessRows(IReadOnlyList<AlertReadinessSnapshot> readiness)
        {
            if (_readinessRowsHost == null)
                return;

            string[] currentIds = _readinessRows.Keys.ToArray();
            string[] nextIds = readiness.Select(item => item.Id).ToArray();
            if (currentIds.SequenceEqual(nextIds, StringComparer.OrdinalIgnoreCase))
                return;

            _readinessRows.Clear();
            _readinessRowsHost.Controls.Clear();
            foreach (AlertReadinessSnapshot item in readiness)
            {
                var row = new ConsoleReadinessRow(item);
                row.NavigateRequested += SwitchPage;
                _readinessRows[item.Id] = row;
                _readinessRowsHost.Controls.Add(row);
            }
            LayoutReadinessRows();
        }

        private void ApplyActivityRows(MonitoringConsoleSnapshot snapshot)
        {
            IReadOnlyList<ConsoleActivityItem> items = ConsolePageModel.BuildActivityItems(snapshot, _activityFilter, ActivityRowCount);
            for (int i = 0; i < _activityRows.Count; i++)
            {
                ConsoleActivityItem? item = i < items.Count ? items[i] : null;
                _activityRows[i].Apply(item, first: i == 0, last: i == _activityRows.Count - 1);
                _activityRows[i].Visible = i < items.Count || i == 0;
            }
        }

        private void ApplyUpdateRows(MonitoringConsoleSnapshot snapshot)
        {
            for (int i = 0; i < _updateRows.Count; i++)
            {
                if (i >= snapshot.RecentUpdates.Count)
                {
                    _updateRows[i].Apply(
                        i == 0 ? "暂无更新" : string.Empty,
                        string.Empty,
                        i == 0 ? "最近没有 CS2 更新记录" : string.Empty,
                        visible: i == 0);
                    continue;
                }

                ConsoleUpdateSnapshot update = snapshot.RecentUpdates[i];
                string formattedTime = global::CS2TradeMonitor.Application.Notify.Cs2UpdateReminderService.FormatTime(update.PublishedAt);
                string time = formattedTime.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? formattedTime;
                string title = string.IsNullOrWhiteSpace(update.Title) ? update.Summary : update.Title;
                string source = update.Source is "code" or "official" ? "CS2 官方" : update.Source;
                _updateRows[i].Apply(time, source, title, visible: true);
            }
        }

        private void LayoutModuleTiles()
        {
            if (_moduleTilesHost == null)
                return;

            ConsoleModuleTile[] tiles = _moduleTiles.Values.ToArray();
            if (tiles.Length == 0)
                return;

            int cellWidth = Math.Max(1, _moduleTilesHost.Width / tiles.Length);
            for (int i = 0; i < tiles.Length; i++)
            {
                int left = i * cellWidth;
                int width = i == tiles.Length - 1 ? _moduleTilesHost.Width - left : cellWidth;
                tiles[i].SetBounds(left, UIUtils.S(9), width, Math.Max(1, _moduleTilesHost.Height - UIUtils.S(18)));
            }
        }

        private void LayoutReadinessRows()
        {
            if (_readinessRowsHost == null)
                return;

            ConsoleReadinessRow[] rows = _readinessRows.Values.ToArray();
            if (rows.Length == 0)
                return;

            int rowHeight = Math.Max(UIUtils.S(54), _readinessRowsHost.Height / rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                int top = i * rowHeight;
                int height = i == rows.Length - 1 ? _readinessRowsHost.Height - top : rowHeight;
                rows[i].SetBounds(0, top, _readinessRowsHost.Width, Math.Max(1, height));
            }
        }

        private void LayoutMainCards()
        {
            if (_mainHost == null || _activityCard == null || _readinessCard == null || _updatesCard == null)
                return;

            ConsoleMainLayout layout = ConsolePageModel.BuildMainLayout(
                _mainHost.Width,
                UIUtils.S(608),
                UIUtils.S(14),
                UIUtils.S(16),
                UIUtils.S(820),
                UIUtils.S(560),
                UIUtils.S(360),
                UIUtils.S(220));
            if (_mainHost.Height != layout.HostHeight)
                _mainHost.Height = layout.HostHeight;
            _activityCard.Bounds = layout.ActivityBounds;
            _readinessCard.Bounds = layout.ReadinessBounds;
            _updatesCard.Bounds = layout.UpdatesBounds;
            QueueDeferredContentWidthSync();
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
            SyncContentWidth();
        }

        private void SyncContentWidth()
        {
            if (_root == null || _root.IsDisposed)
                return;

            Rectangle bounds = ContentBounds;
            int width = bounds.Width;
            bool changed = false;
            if (_root.Left != bounds.Left || _root.Top != bounds.Top || _root.Width != width)
            {
                _root.SetBounds(bounds.Left, bounds.Top, width, _root.Height);
                changed = true;
            }

            foreach (Control child in _root.Controls)
            {
                if (child.Width == width)
                    continue;
                child.Width = width;
                changed = true;
            }

            int height = Math.Max(UIUtils.S(1), _root.GetPreferredSize(new Size(width, 0)).Height);
            if (_root.Height != height)
            {
                _root.Height = height;
                changed = true;
            }

            if (changed)
                _root.PerformLayout();
            HideHorizontalScroll(Container);
        }

        private void QueueDeferredContentWidthSync()
        {
            if (_widthSyncQueued || IsDisposed)
                return;

            _widthSyncQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _widthSyncQueued = false;
                    SyncContentWidth();
                }));
            }
            catch
            {
                _widthSyncQueued = false;
                SyncContentWidth();
            }
        }

        private void SetRefreshButtonState(bool enabled, string text)
        {
            if (_refreshButton == null || _refreshButton.IsDisposed)
                return;
            _refreshButton.Enabled = enabled;
            _refreshButton.Text = text;
            _refreshButton.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            _refreshButton.Invalidate();
        }

        private void SwitchPage(string pageKey)
        {
            if (FindForm() is CS2TradeMonitor.src.UI.SettingsForm form)
                form.SwitchPage(pageKey);
        }

        private static ConsoleCardPanel CreateCard(int height, int bottomMargin)
        {
            return new ConsoleCardPanel
            {
                Height = height,
                Radius = UIUtils.S(6),
                FillColorOverride = UIColors.CardBg,
                BorderColorOverride = UIColors.Border,
                Margin = new Padding(0, 0, 0, UIUtils.S(bottomMargin))
            };
        }
    }
}
