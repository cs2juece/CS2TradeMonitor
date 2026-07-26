using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

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
        private readonly MonitoringConsoleSnapshotBuilder _snapshotBuilder;
        private readonly Dictionary<string, ConsoleStatusRow> _moduleRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ConsoleStatusRow> _readinessRows = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConsoleHistoryRow> _historyRows = new();
        private readonly List<ConsoleHistoryRow> _updateRows = new();
        private Label? _overallLabel;
        private Label? _capturedLabel;
        private LiteButton? _refreshButton;
        private Panel? _readinessPanel;
        private bool _refreshing;

        public ConsolePage()
            : this(ConsolePageRuntimeServices.Resolve())
        {
        }

        internal ConsolePage(ConsolePageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);
            _snapshotBuilder = runtimeServices.SnapshotBuilder;
        }

        protected override void OnStoreAttached()
        {
            BuildPage();
        }

        public override void Activate()
        {
            base.Activate();
            _ = RefreshSnapshotAsync();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            foreach (ConsoleStatusRow row in _moduleRows.Values.Concat(_readinessRows.Values))
                row.ApplyTheme();
            foreach (ConsoleHistoryRow row in _historyRows.Concat(_updateRows))
                row.ApplyTheme();
        }

        private void BuildPage()
        {
            ClearPage();

            var overviewGroup = new LiteSettingsGroup("控制台");
            overviewGroup.AddFullItem(CreateOverviewPanel());

            var moduleGroup = new LiteSettingsGroup("监控模块");
            moduleGroup.AddHeaderInlineAction(CreateHeaderDescription("直接读取现有模块运行状态，不创建第二套状态。"));
            moduleGroup.AddFullItem(CreateModulePanel());

            var readinessGroup = new LiteSettingsGroup("触发准备状态");
            readinessGroup.AddHeaderInlineAction(CreateHeaderDescription("只说明当前条件，不阻止或改变提醒。"));
            readinessGroup.AddFullItem(CreateReadinessPanel());

            var alertGroup = new LiteSettingsGroup("最近提醒历史");
            alertGroup.AddHeaderInlineAction(CreateHeaderDescription("仅在本机保存非敏感摘要和投递结果，最多 500 条。"));
            alertGroup.AddFullItem(CreateHistoryPanel(_historyRows, 8));

            var updateGroup = new LiteSettingsGroup("最近 CS2 更新");
            updateGroup.AddHeaderInlineAction(CreateHeaderDescription("控制台显示最近 3 条，原页面保留 10 条。"));
            updateGroup.AddFullItem(CreateHistoryPanel(_updateRows, 3));

            // DockStyle.Top displays the most recently added group first.
            // Add bottom-to-top so the visual order remains overview-to-details.
            AddGroupToPage(updateGroup);
            AddGroupToPage(alertGroup);
            AddGroupToPage(readinessGroup);
            AddGroupToPage(moduleGroup);
            AddGroupToPage(overviewGroup);
        }

        private Control CreateOverviewPanel()
        {
            var panel = new Panel
            {
                Height = UIUtils.S(100),
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            var title = CreateLabel("控制台", 16F, FontStyle.Bold, UIColors.TextMain);
            var note = CreateLabel("集中查看运行状态、规则准备度、最近提醒和 CS2 更新。", 9F, FontStyle.Regular, UIColors.TextSub);
            _overallLabel = CreateLabel("正在读取…", 10F, FontStyle.Bold, UIColors.TextSub);
            _capturedLabel = CreateLabel(string.Empty, 8.5F, FontStyle.Regular, UIColors.TextSub);
            _refreshButton = new LiteButton("刷新状态", false)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(34)
            };
            _refreshButton.Click += async (_, __) => await RefreshSnapshotAsync();

            panel.Controls.AddRange(new Control[] { title, note, _overallLabel, _capturedLabel, _refreshButton });
            panel.Layout += (_, __) =>
            {
                int pad = UIUtils.S(4);
                int gap = UIUtils.S(12);
                int refreshX = panel.Width - _refreshButton.Width - pad;
                int capturedWidth = UIUtils.S(220);
                int capturedX = Math.Max(pad, refreshX - capturedWidth - gap);
                title.SetBounds(pad, UIUtils.S(4), Math.Max(1, panel.Width - UIUtils.S(140)), UIUtils.S(30));
                note.SetBounds(pad, UIUtils.S(36), Math.Max(1, panel.Width - UIUtils.S(140)), UIUtils.S(24));
                _overallLabel.SetBounds(pad, UIUtils.S(66), Math.Max(1, capturedX - pad - gap), UIUtils.S(26));
                _capturedLabel.SetBounds(capturedX, UIUtils.S(66), capturedWidth, UIUtils.S(26));
                _refreshButton.SetBounds(refreshX, UIUtils.S(33), _refreshButton.Width, _refreshButton.Height);
            };
            return panel;
        }

        private Control CreateModulePanel()
        {
            var panel = CreateRowsPanel(MonitorModuleRegistry.Descriptors.Count);
            foreach (MonitorModuleDescriptor descriptor in MonitorModuleRegistry.Descriptors)
            {
                var row = new ConsoleStatusRow(descriptor.DisplayName, string.Empty, showNavigation: false);
                _moduleRows[descriptor.Id] = row;
                panel.Controls.Add(row.Root);
            }

            AttachRowsLayout(panel, _moduleRows.Values.Select(row => row.Root));
            return panel;
        }

        private Control CreateReadinessPanel()
        {
            _readinessPanel = CreateRowsPanel(1);
            _readinessPanel.Layout += (_, __) => LayoutRows(
                _readinessPanel,
                _readinessRows.Values.Select(row => row.Root));
            return _readinessPanel;
        }

        private static Panel CreateRowsPanel(int count)
        {
            return new Panel
            {
                Height = UIUtils.S(Math.Max(1, count) * 48 + 4),
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
        }

        private static void AttachRowsLayout(Panel panel, IEnumerable<Control> rows)
        {
            Control[] rowArray = rows.ToArray();
            panel.Layout += (_, __) => LayoutRows(panel, rowArray);
        }

        private static void LayoutRows(Panel panel, IEnumerable<Control> rows)
        {
            Control[] rowArray = rows.ToArray();
            int rowHeight = UIUtils.S(48);
            for (int i = 0; i < rowArray.Length; i++)
                rowArray[i].SetBounds(0, i * rowHeight, panel.Width, rowHeight);
        }

        private static Control CreateHistoryPanel(ICollection<ConsoleHistoryRow> rows, int count)
        {
            var panel = new Panel
            {
                Height = UIUtils.S(count * 48 + 4),
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            for (int i = 0; i < count; i++)
            {
                var row = new ConsoleHistoryRow();
                rows.Add(row);
                panel.Controls.Add(row.Root);
            }

            ConsoleHistoryRow[] rowArray = rows.TakeLast(count).ToArray();
            panel.Layout += (_, __) =>
            {
                int rowHeight = UIUtils.S(48);
                for (int i = 0; i < rowArray.Length; i++)
                    rowArray[i].Root.SetBounds(0, i * rowHeight, panel.Width, rowHeight);
            };
            return panel;
        }

        private async Task RefreshSnapshotAsync()
        {
            if (_refreshing || Config == null)
                return;

            _refreshing = true;
            if (_refreshButton != null)
            {
                _refreshButton.Enabled = false;
                _refreshButton.Text = "读取中";
            }

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
                if (_overallLabel != null && !_overallLabel.IsDisposed)
                {
                    _overallLabel.Text = "状态读取失败，请稍后重试";
                    _overallLabel.ForeColor = UIColors.TextCrit;
                }
            }
            finally
            {
                _refreshing = false;
                if (_refreshButton != null && !_refreshButton.IsDisposed)
                {
                    _refreshButton.Enabled = true;
                    _refreshButton.Text = "刷新状态";
                }
            }
        }

        private void ApplySnapshot(MonitoringConsoleSnapshot snapshot)
        {
            if (_overallLabel != null)
            {
                _overallLabel.Text = snapshot.OverallText;
                _overallLabel.ForeColor = snapshot.IsHealthy ? UIColors.Positive : UIColors.TextWarn;
            }
            if (_capturedLabel != null)
                _capturedLabel.Text = "更新于 " + snapshot.CapturedAt.LocalDateTime.ToString("MM-dd HH:mm:ss");

            foreach (MonitorModuleHealth health in snapshot.Modules)
            {
                if (!_moduleRows.TryGetValue(health.Id, out ConsoleStatusRow? row))
                    continue;
                row.Apply(
                    SystemSettingsPageModel.FormatModuleState(health),
                    health.Message,
                    ResolveModuleColor(health.State));
            }

            EnsureReadinessRows(snapshot.AlertReadiness);
            foreach (AlertReadinessSnapshot readiness in snapshot.AlertReadiness)
            {
                if (!_readinessRows.TryGetValue(readiness.Id, out ConsoleStatusRow? row))
                    continue;
                row.Apply(FormatReadiness(readiness.State), readiness.Message, ResolveReadinessColor(readiness.State));
            }

            for (int i = 0; i < _historyRows.Count; i++)
            {
                if (i >= snapshot.RecentAlerts.Count)
                {
                    _historyRows[i].Apply(i == 0 ? "暂无提醒历史" : string.Empty, string.Empty, UIColors.TextSub);
                    continue;
                }

                AlertHistoryEntry entry = snapshot.RecentAlerts[i];
                string title = $"{entry.OccurredAt.LocalDateTime:MM-dd HH:mm} · {entry.Source} · {entry.Channel} · {FormatDeliveryStatus(entry.Status)}";
                string detail = string.IsNullOrWhiteSpace(entry.Summary)
                    ? entry.Title
                    : entry.Title + " · " + entry.Summary;
                if (!string.IsNullOrWhiteSpace(entry.Detail))
                    detail += " · " + entry.Detail;
                _historyRows[i].Apply(title, detail, ResolveDeliveryColor(entry.Status));
            }

            for (int i = 0; i < _updateRows.Count; i++)
            {
                if (i >= snapshot.RecentUpdates.Count)
                {
                    _updateRows[i].Apply(i == 0 ? "暂无更新记录" : string.Empty, string.Empty, UIColors.TextSub);
                    continue;
                }

                var item = snapshot.RecentUpdates[i];
                string time = global::CS2TradeMonitor.Application.Notify.Cs2UpdateReminderService.FormatTime(item.PublishedAt);
                _updateRows[i].Apply(
                    $"{time} · {item.Source}",
                    string.IsNullOrWhiteSpace(item.Title) ? item.Summary : item.Title,
                    UIColors.Primary);
            }
        }

        private void EnsureReadinessRows(IReadOnlyList<AlertReadinessSnapshot> readiness)
        {
            if (_readinessPanel == null)
                return;

            string[] currentIds = _readinessRows.Keys.ToArray();
            string[] nextIds = readiness.Select(item => item.Id).ToArray();
            if (currentIds.SequenceEqual(nextIds, StringComparer.OrdinalIgnoreCase))
                return;

            _readinessRows.Clear();
            _readinessPanel.Controls.Clear();
            foreach (AlertReadinessSnapshot item in readiness)
            {
                var row = new ConsoleStatusRow(item.DisplayName, item.PageKey, showNavigation: true);
                row.NavigateRequested += pageKey => SwitchPage(pageKey);
                _readinessRows[item.Id] = row;
                _readinessPanel.Controls.Add(row.Root);
            }

            _readinessPanel.Height = UIUtils.S(Math.Max(1, readiness.Count) * 48 + 4);
            _readinessPanel.PerformLayout();
        }

        private void SwitchPage(string pageKey)
        {
            if (FindForm() is CS2TradeMonitor.src.UI.SettingsForm form)
                form.SwitchPage(pageKey);
        }

        private static Label CreateHeaderDescription(string text)
        {
            return CreateLabel(text, 8.5F, FontStyle.Regular, UIColors.TextSub);
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", size, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
        }

        private static string FormatReadiness(AlertReadinessState state)
        {
            return state switch
            {
                AlertReadinessState.Disabled => "已关闭",
                AlertReadinessState.Waiting => "等待条件",
                AlertReadinessState.Ready => "已准备",
                AlertReadinessState.CoolingDown => "冷却中",
                AlertReadinessState.Faulted => "异常",
                _ => "未知"
            };
        }

        private static Color ResolveReadinessColor(AlertReadinessState state)
        {
            return state switch
            {
                AlertReadinessState.Ready => UIColors.Positive,
                AlertReadinessState.Faulted => UIColors.TextCrit,
                AlertReadinessState.Waiting or AlertReadinessState.CoolingDown => UIColors.TextWarn,
                _ => UIColors.TextSub
            };
        }

        private static Color ResolveModuleColor(MonitorModuleState state)
        {
            return state switch
            {
                MonitorModuleState.Running => UIColors.Positive,
                MonitorModuleState.Starting => UIColors.Primary,
                MonitorModuleState.Faulted => UIColors.TextCrit,
                MonitorModuleState.Paused => UIColors.TextWarn,
                _ => UIColors.TextSub
            };
        }

        private static Color ResolveDeliveryColor(AlertDeliveryStatus status)
        {
            return status switch
            {
                AlertDeliveryStatus.Succeeded => UIColors.Positive,
                AlertDeliveryStatus.Failed => UIColors.TextCrit,
                AlertDeliveryStatus.Skipped => UIColors.TextWarn,
                _ => UIColors.Primary
            };
        }

        private static string FormatDeliveryStatus(AlertDeliveryStatus status)
        {
            return status switch
            {
                AlertDeliveryStatus.Succeeded => "已投递",
                AlertDeliveryStatus.Skipped => "已跳过",
                AlertDeliveryStatus.Failed => "失败",
                _ => "未知"
            };
        }

        private sealed class ConsoleStatusRow
        {
            private readonly Label _name;
            private readonly Label _state;
            private readonly Label _detail;
            private readonly LiteButton? _navigateButton;

            public ConsoleStatusRow(string name, string pageKey, bool showNavigation)
            {
                Root = new Panel { BackColor = Color.Transparent };
                _name = CreateLabel(name, 9F, FontStyle.Bold, UIColors.TextMain);
                _state = CreateLabel("未读取", 9F, FontStyle.Bold, UIColors.TextSub);
                _detail = CreateLabel(string.Empty, 8.5F, FontStyle.Regular, UIColors.TextSub);
                Root.Controls.AddRange(new Control[] { _name, _state, _detail });
                if (showNavigation)
                {
                    _navigateButton = new LiteButton("查看", false)
                    {
                        Width = UIUtils.S(64),
                        Height = UIUtils.S(28),
                        Tag = pageKey
                    };
                    _navigateButton.Click += (_, __) => NavigateRequested?.Invoke(pageKey);
                    Root.Controls.Add(_navigateButton);
                }

                Root.Paint += (_, e) =>
                {
                    using var pen = new Pen(UIColors.Border);
                    e.Graphics.DrawLine(pen, 0, Root.Height - 1, Root.Width, Root.Height - 1);
                };
                Root.Layout += (_, __) =>
                {
                    int buttonWidth = _navigateButton?.Width ?? 0;
                    int right = Root.Width - buttonWidth - (_navigateButton == null ? 0 : UIUtils.S(8));
                    _name.SetBounds(UIUtils.S(4), 0, UIUtils.S(150), Root.Height);
                    _state.SetBounds(UIUtils.S(166), 0, UIUtils.S(88), Root.Height);
                    _detail.SetBounds(UIUtils.S(266), 0, Math.Max(1, right - UIUtils.S(266)), Root.Height);
                    _navigateButton?.SetBounds(Root.Width - buttonWidth, UIUtils.S(10), buttonWidth, UIUtils.S(28));
                };
            }

            public Panel Root { get; }

            public event Action<string>? NavigateRequested;

            public void Apply(string state, string detail, Color color)
            {
                _state.Text = state;
                _state.ForeColor = color;
                _detail.Text = detail;
            }

            public void ApplyTheme()
            {
                _name.ForeColor = UIColors.TextMain;
                _detail.ForeColor = UIColors.TextSub;
                _navigateButton?.RefreshTheme();
                Root.Invalidate();
            }
        }

        private sealed class ConsoleHistoryRow
        {
            private readonly Label _title;
            private readonly Label _detail;

            public ConsoleHistoryRow()
            {
                Root = new Panel { BackColor = Color.Transparent };
                _title = CreateLabel(string.Empty, 8.5F, FontStyle.Bold, UIColors.TextSub);
                _detail = CreateLabel(string.Empty, 8.5F, FontStyle.Regular, UIColors.TextMain);
                Root.Controls.AddRange(new Control[] { _title, _detail });
                Root.Paint += (_, e) =>
                {
                    using var pen = new Pen(UIColors.Border);
                    e.Graphics.DrawLine(pen, 0, Root.Height - 1, Root.Width, Root.Height - 1);
                };
                Root.Layout += (_, __) =>
                {
                    _title.SetBounds(UIUtils.S(4), 0, UIUtils.S(235), Root.Height);
                    _detail.SetBounds(UIUtils.S(251), 0, Math.Max(1, Root.Width - UIUtils.S(255)), Root.Height);
                };
            }

            public Panel Root { get; }

            public void Apply(string title, string detail, Color color)
            {
                _title.Text = title;
                _title.ForeColor = color;
                _detail.Text = detail;
            }

            public void ApplyTheme()
            {
                _detail.ForeColor = UIColors.TextMain;
                Root.Invalidate();
            }
        }
    }
}
