using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.SystemServices.InfoService;
using CS2TradeMonitor.src.UI.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CS2TradeMonitor
{
    public class UIController : IDisposable
    {
        private readonly Settings _cfg;
        private readonly Form _form;
        private readonly ICs2UpdateReminderService _cs2UpdateReminder;
        private readonly IMarketAlertService _marketAlerts;
        private readonly IPhoneAlertDispatchService _phoneAlerts;
        private readonly IRenderScheduler _renderScheduler;
        private readonly IInfoService _infoService;
        private const int BackgroundRefreshIntervalMs = 1000;
        private UiThreadDispatcher? _refreshDispatcher;
        private System.Threading.Timer? _backgroundRefreshTimer;
        private int _backgroundRefreshRunning;
        private int _refreshIntervalMs;
        private bool _disposed;

        private UILayout _layout;
        private bool _layoutDirty = true;
        private bool _dragging = false;
        private string _lastLayoutSignature = "";

        private List<GroupLayoutInfo> _groups = new();
        private List<Column> _hxColsHorizontal = new();
        private List<Column> _hxColsTaskbar = new();
        private HorizontalLayout? _hxLayout;
        private bool _appliedHorizontalMode;
        private bool _layoutApplyQueued;
        private long _renderVersion;
        private Dictionary<string, DateTime> _overheatStartTimes = new Dictionary<string, DateTime>();
        public MainForm MainForm => (MainForm)_form;

        // 辅助判断当前已应用到 UI 的渲染模式，不能依赖 _hxLayout 是否创建。
        public bool IsLayoutHorizontal => _appliedHorizontalMode;

        public event Action<UiSnapshot>? RefreshSnapshotApplied;

        public List<Column> GetTaskbarColumns() => _hxColsTaskbar;
        public List<GroupLayoutInfo> GetMainGroups() => _groups;
        public long RenderVersion => Interlocked.Read(ref _renderVersion);

        public UIController(Settings cfg, Form form)
            : this(cfg, form, UIControllerRuntimeServices.Resolve())
        {
        }

        internal UIController(Settings cfg, Form form, UIControllerRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _cfg = cfg;
            _form = form;
            _cs2UpdateReminder = runtimeServices.Cs2UpdateReminder;
            _marketAlerts = runtimeServices.MarketAlerts;
            _phoneAlerts = runtimeServices.PhoneAlerts;
            _renderScheduler = runtimeServices.RenderScheduler;
            _infoService = runtimeServices.InfoService;

            _layout = new UILayout(ThemeManager.Current);

            AppNotificationHub.Instance.NotificationRequested += OnAppNotificationRequested;
            _marketAlerts.AlertRequested += OnMarketAlertRequested;
            _cs2UpdateReminder.UpdateDetected += OnCs2UpdateDetected;
            MarketDataSourceManager.DataUpdated += OnMarketDataUpdated;
            ApplyTheme(_cfg.Skin);
            StartRefreshDispatcher();
            StartBackgroundRefreshTimer();
        }

        public float GetCurrentDpiScale()
        {
            using (Graphics g = _form.CreateGraphics())
            {
                return g.DpiX / 96f;
            }
        }

        public void ApplyTheme(string name, bool retainData = false)
        {
            bool targetHorizontalMode = _cfg.HorizontalMode;

            // 1. 先保留旧主题的引用 (为了稍后释放)
            var oldTheme = ThemeManager.Current;

            // 2. 清理全局画刷缓存 (这不会影响 ThemeManager 的字体了，因为解耦了)
            UIRenderer.ClearCache();
            UIUtils.ClearBrushCache();

            // 3. 加载新主题 (Current 指向新对象，包含全新的字体)
            // 如果主题名相同且要求保留数据，可以考虑跳过 Load，但为了应用 Scale 还是重新 Load 比较稳妥
            // 或者优化：ThemeManager.Load 内部判断是否已加载
            ThemeManager.Load(name);
            var t = ThemeManager.Current;

            // 4. 安全释放旧主题的字体
            // 此时 Current 已经是新主题了，Paint 事件只会用新字体，所以释放旧的是安全的
            if (oldTheme != null && oldTheme != t)
            {
                oldTheme.DisposeFonts();
            }

            // ... 后续缩放逻辑保持不变 ...
            float dpiScale = GetCurrentDpiScale();
            float userScale = (float)_cfg.UIScale;
            float finalScale = dpiScale * userScale;

            t.Scale(dpiScale, userScale); // Scale 内部现在会自动清理旧缩放字体

            // ... 边距修复逻辑 ...
            if (!_cfg.HorizontalMode)
            {
                t.Layout.Width = (int)(_cfg.PanelWidth * finalScale);
            }

            TaskbarRenderer.ReloadStyle(_cfg);

            _layout = new UILayout(t);
            _hxLayout = null;
            _appliedHorizontalMode = targetHorizontalMode;

            if (!retainData)
            {
                BuildMetrics();
                BuildHorizontalColumns();
            }
            else
            {
                // [Safety Check] Even if retaining data, ensure we have content.
                // This handles cases where we switch modes but data wasn't built for that mode yet.
                if (_groups.Count == 0) BuildMetrics();
                if (_hxColsHorizontal.Count == 0) BuildHorizontalColumns();
            }

            _layoutDirty = true;
            ApplyPendingLayout();

            _form.BackColor = ThemeManager.ParseColor(GetPanelBackgroundColor(t));

            RestartRefreshDispatcherIfNeeded();
            MarkRenderDirty();
            RequestFormRender();
        }

        public void RebuildLayout()
        {
            BuildMetrics();
            BuildHorizontalColumns();
            _layoutDirty = true;
            ApplyPendingLayout();
            MarkRenderDirty();
            RequestFormRender();
        }

        public void SetDragging(bool dragging) => _dragging = dragging;

        private void ApplyPendingLayout()
        {
            if (!_layoutDirty) return;

            var t = ThemeManager.Current;
            _layout ??= new UILayout(t);

            Size targetSize;
            if (_cfg.HorizontalMode)
            {
                _cfg.HorizontalSingleLine = true;
                _hxLayout ??= new HorizontalLayout(t, GetDefaultPanelWidth(t), LayoutMode.Horizontal, _cfg);
                int h = _hxLayout.Build(_hxColsHorizontal);
                targetSize = new Size(Math.Max(1, _hxLayout.PanelWidth), Math.Max(1, h));
            }
            else
            {
                t.Layout.Width = IsMarketDisplayOnlyPanel()
                    ? MeasureMarketDisplayPanelWidth(t)
                    : GetDefaultPanelWidth(t);

                int h = _layout.Build(_groups);
                targetSize = new Size(Math.Max(1, t.Layout.Width), Math.Max(1, h));
            }

            if (_form.ClientSize != targetSize)
            {
                _form.ClientSize = targetSize;
                ClampFormToScreen();
            }

            _layoutDirty = false;
            MarkRenderDirty();
        }

        private void ClampFormToScreen()
        {
            if (!_cfg.ClampToScreen) return;

            var area = Screen.FromControl(_form).WorkingArea;
            int maxLeft = Math.Max(area.Left, area.Right - _form.Width);
            int maxTop = Math.Max(area.Top, area.Bottom - _form.Height);
            int left = Math.Clamp(_form.Left, area.Left, maxLeft);
            int top = Math.Clamp(_form.Top, area.Top, maxTop);

            if (_form.Left != left || _form.Top != top)
            {
                _form.Location = new Point(left, top);
            }
        }

        private void QueuePendingLayoutApply()
        {
            if (_layoutApplyQueued || !_form.IsHandleCreated || _form.IsDisposed) return;

            _layoutApplyQueued = true;
            _form.BeginInvoke(new Action(() =>
            {
                _layoutApplyQueued = false;
                ApplyPendingLayout();
                RequestFormRender();
            }));
        }

        private void RequestFormRender()
        {
            if (_form is MainForm mainForm)
                mainForm.RequestLayeredRender();
            else
                _renderScheduler.RequestRender(_form);
        }

        private void StartRefreshDispatcher()
        {
            if (_disposed)
            {
                return;
            }

            _refreshIntervalMs = GetRefreshIntervalMs();
            _refreshDispatcher = CreateRefreshDispatcher(_refreshIntervalMs);
            _refreshDispatcher.Start();
        }

        private UiThreadDispatcher CreateRefreshDispatcher(int refreshMs)
        {
            var dispatcher = new UiThreadDispatcher(
                refreshMs,
                BuildUiSnapshot,
                ApplyRefreshSnapshot,
                SynchronizationContext.Current);
            dispatcher.Producer.Faulted += OnRefreshProducerFaulted;
            return dispatcher;
        }

        private void RestartRefreshDispatcherIfNeeded()
        {
            if (_disposed || _refreshDispatcher == null)
            {
                return;
            }

            int refreshMs = GetRefreshIntervalMs();
            if (refreshMs == _refreshIntervalMs)
            {
                return;
            }

            _refreshDispatcher.Producer.Faulted -= OnRefreshProducerFaulted;
            _refreshDispatcher.Dispose();
            _refreshIntervalMs = refreshMs;
            _refreshDispatcher = CreateRefreshDispatcher(refreshMs);
            _refreshDispatcher.Start();
        }

        private int GetRefreshIntervalMs()
        {
            return Math.Max(80, _cfg.RefreshMs);
        }

        private UiSnapshot BuildUiSnapshot()
        {
            return BuildUiSnapshot(forceLayoutRebuild: false, forceRender: false);
        }

        private UiSnapshot BuildUiSnapshot(bool forceLayoutRebuild, bool forceRender)
        {
            var textValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in GetSnapshotKeys())
            {
                textValues[key] = GetTextSourceValue(key);
            }

            return new UiSnapshot(
                textValues: textValues,
                forceLayoutRebuild: forceLayoutRebuild,
                forceRender: forceRender);
        }

        private void StartBackgroundRefreshTimer()
        {
            _backgroundRefreshTimer = new System.Threading.Timer(
                _ => EvaluateBackgroundRefreshServicesSafe(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(BackgroundRefreshIntervalMs));
        }

        private void EvaluateBackgroundRefreshServicesSafe()
        {
            if (_disposed)
                return;

            if (Interlocked.Exchange(ref _backgroundRefreshRunning, 1) != 0)
                return;

            try
            {
                EvaluateBackgroundRefreshServices();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Render", "Background refresh service evaluation failed.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _backgroundRefreshRunning, 0);
            }
        }

        private void EvaluateBackgroundRefreshServices()
        {
            using (UiJankProfiler.Measure(
                "UIController.BackgroundRefreshServices",
                "Alert/CS2Update/InfoService",
                thresholdMs: 4))
            {
                CheckTemperatureAlert();
                _marketAlerts.Evaluate(_cfg);
                _cs2UpdateReminder.Tick(_cfg);
                _infoService.Update();
            }
        }

        private List<string> GetSnapshotKeys()
        {
            return YouPinInventoryTrendDisplayMetric.IncludeConfigured(_cfg, _cfg.MonitorItems)
                .Where(item => item.VisibleInPanel || item.VisibleInTaskbar)
                .Select(item => item.Key)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string? GetTextSourceValue(string key)
        {
            if (key.StartsWith("DASH.", StringComparison.OrdinalIgnoreCase))
            {
                return _infoService.GetValue(key.Substring(5));
            }

            if (MarketDisplayFormatter.IsMarketDisplayKey(key))
            {
                return MarketDisplayFormatter.GetValueText(key, _cfg);
            }

            return null;
        }

        private void EnqueueSnapshot(UiSnapshot snapshot)
        {
            if (_disposed || _refreshDispatcher == null)
            {
                return;
            }

            _refreshDispatcher.Queue.Enqueue(snapshot);
        }

        private static void OnRefreshProducerFaulted(object? sender, Exception e)
        {
            DiagnosticsLogger.Error("Render", "UI snapshot producer failed.", e);
        }

        public void Render(Graphics g)
        {
            g.Clear(Color.Transparent);
            var t = ThemeManager.Current;
            _layout ??= new UILayout(t);

            // === 横屏模式 ===
            if (_cfg.HorizontalMode)
            {
                _cfg.HorizontalSingleLine = true;
                _hxLayout ??= new HorizontalLayout(t, GetDefaultPanelWidth(t), LayoutMode.Horizontal, _cfg);

                if (_layoutDirty)
                {
                    QueuePendingLayoutApply();
                }
                HorizontalRenderer.Render(g, t, _hxColsHorizontal, _hxLayout.PanelWidth, _cfg);
                return;
            }

            // === 竖屏模式 ===
            if (_layoutDirty)
            {
                QueuePendingLayoutApply();
            }

            UIRenderer.Render(g, _groups, t, _cfg);
        }

        private bool IsMarketDisplayOnlyPanel()
        {
            var items = _groups.SelectMany(g => g.Items).ToList();
            return UIControllerMarketDisplayConfig.IsMarketDisplayOnlyKeys(items.Select(x => x.Key));
        }

        private int GetDefaultPanelWidth(Theme t)
        {
            return Math.Max(1, (int)Math.Round(_cfg.PanelWidth * t.Layout.LayoutScale));
        }

        private int MeasureMarketDisplayPanelWidth(Theme t)
        {
            int padX = 8;
            int textSafetyPadding = Math.Max(8, UIUtils.S(10));
            int labelGap = MarketDisplayFormatter.LabelGap;
            int valueGap = MarketDisplayFormatter.ValueGap;
            int minWidth = 1;
            int maxWidth = Math.Max(1, Screen.FromControl(_form).WorkingArea.Width / 2);
            int textWidth = 0;
            var items = _groups.SelectMany(g => g.Items).ToList();

            foreach (var item in items)
            {
                var segments = MarketDisplayFormatter.GetSegments(item.Key, _cfg);
                int labelW = TextRenderer.MeasureText(segments.Label, t.FontItem,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

                int primaryW = TextRenderer.MeasureText(segments.PrimaryText, t.FontItem,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

                int itemWidth = labelW + labelGap + primaryW;
                if (segments.HasData && !string.IsNullOrWhiteSpace(segments.SecondaryText))
                {
                    int secondaryW = TextRenderer.MeasureText(segments.SecondaryText, t.FontItem,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    itemWidth += valueGap + secondaryW;
                }

                textWidth = Math.Max(textWidth, itemWidth);
            }

            return Math.Min(maxWidth, Math.Max(minWidth, textWidth + padX * 2 + textSafetyPadding));
        }

        private bool _busy = false;

        private void ApplyRefreshSnapshot(UiSnapshot snapshot)
        {
            if (_disposed || _form.IsDisposed) return;

            if (_form.InvokeRequired)
            {
                try { _form.BeginInvoke(new Action(() => ApplyRefreshSnapshot(snapshot))); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
                return;
            }

            if (_dragging || _busy) return;
            _busy = true;

            try
            {
                bool dataChanged = false;

                if (snapshot.ForceLayoutRebuild)
                {
                    BuildMetrics();
                    BuildHorizontalColumns();
                    _layoutDirty = true;
                    ReloadTaskbarWindows();
                }

                // ① 更新竖屏 items
                foreach (var g in _groups)
                    foreach (var it in g.Items)
                    {
                        if (ApplySnapshotToItem(snapshot, it))
                        {
                            dataChanged = true;
                        }
                    }

                // ② 更新横版 / 任务栏 (清理了冗余代码)
                void UpdateCol(Column col)
                {
                    void UpdateItem(MetricItem? it)
                    {
                        if (it == null) return;

                        if (ApplySnapshotToItem(snapshot, it))
                        {
                            dataChanged = true;
                        }
                    }
                    UpdateItem(col.Top);
                    UpdateItem(col.Bottom);
                }

                foreach (var col in _hxColsHorizontal) UpdateCol(col);
                foreach (var col in _hxColsTaskbar) UpdateCol(col);

                // HardwareHistoryLogger.RecordSnapshot removed; // (_cfg, key => _mon.Get(key));

                // [优化] 在数据更新后检查布局签名 (每秒一次)
                // 只有当充电状态等导致样本变化时，才标记 Dirty
                if (_cfg.HorizontalMode && _hxLayout != null)
                {
                    string currentLayoutSig = _hxLayout.GetLayoutSignature(_hxColsHorizontal);
                    if (currentLayoutSig != _lastLayoutSignature)
                    {
                        _lastLayoutSignature = currentLayoutSig;
                        _layoutDirty = true;
                    }
                }
                else if (IsMarketDisplayOnlyPanel() && dataChanged)
                {
                    _layoutDirty = true;
                }

                bool shouldRender = snapshot.ForceRender || snapshot.ForceLayoutRebuild || dataChanged || _layoutDirty;
                if (_layoutDirty) ApplyPendingLayout();
                if (shouldRender)
                {
                    MarkRenderDirty();
                    RequestFormRender();
                }

                NotifyRefreshSnapshotApplied(snapshot);
            }
            finally
            {
                _busy = false;
            }
        }

        private void NotifyRefreshSnapshotApplied(UiSnapshot snapshot)
        {
            Action<UiSnapshot>? handlers = RefreshSnapshotApplied;
            if (handlers == null) return;

            foreach (Action<UiSnapshot> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(snapshot);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Error("Render", "Taskbar snapshot subscriber failed.", ex);
                }
            }
        }

        private void MarkRenderDirty()
        {
            Interlocked.Increment(ref _renderVersion);
        }

        private bool ApplySnapshotToItem(UiSnapshot snapshot, MetricItem item)
        {
            string? text;
            if (snapshot.TryGetTextValue(item.Key, out text) && text != null)
            {
                if (string.Equals(item.TextValue, text, StringComparison.Ordinal))
                {
                    return false;
                }

                item.TextValue = text;
                return true;
            }

            item.Value = null; // HW removed
            item.TickSmooth(_cfg.AnimationSpeed);
            return false;
        }

        private void ReloadTaskbarWindows()
        {
            foreach (var tf in System.Windows.Forms.Application.OpenForms.OfType<TaskbarForm>().ToList())
            {
                tf.ReloadLayout();
            }
        }

        private void BuildMetrics()
        {
            _groups = UIControllerMetricBuilder.BuildGroups(_cfg, _infoService);
        }

        private void BuildHorizontalColumns()
        {
            var columns = UIControllerMetricBuilder.BuildColumns(_cfg, _infoService, _form.Width);
            _hxColsHorizontal = columns.Horizontal;
            _hxColsTaskbar = columns.Taskbar;
        }
        private string GetPanelBackgroundColor(Theme t)
        {
            if (!string.IsNullOrWhiteSpace(_cfg.PanelBackgroundColor))
                return _cfg.PanelBackgroundColor;

            if (IsMarketDisplayOnlyPanel() && !string.IsNullOrWhiteSpace(_cfg.SteamDtBackgroundColor))
                return _cfg.SteamDtBackgroundColor;

            return t.Color.Background;
        }

        private void CheckTemperatureAlert()
        {
            if (!_cfg.AlertTempEnabled || _overheatStartTimes.Count > 0)
            {
                _overheatStartTimes.Clear();
            }
        }
        public void Dispose()
        {
            _disposed = true;
            _backgroundRefreshTimer?.Dispose();
            _backgroundRefreshTimer = null;
            AppNotificationHub.Instance.NotificationRequested -= OnAppNotificationRequested;
            _marketAlerts.AlertRequested -= OnMarketAlertRequested;
            _cs2UpdateReminder.UpdateDetected -= OnCs2UpdateDetected;
            MarketDataSourceManager.DataUpdated -= OnMarketDataUpdated;
            if (_refreshDispatcher != null)
            {
                _refreshDispatcher.Producer.Faulted -= OnRefreshProducerFaulted;
                _refreshDispatcher.Dispose();
                _refreshDispatcher = null;
            }
        }

        private void OnMarketDataUpdated()
        {
            if (_disposed || _form.IsDisposed) return;

            try
            {
                EnqueueSnapshot(BuildUiSnapshot(forceLayoutRebuild: true, forceRender: true));
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Render", "Failed to enqueue market data snapshot.", ex);
            }
        }

        private void OnMarketAlertRequested(object? sender, MarketAlertNotificationEventArgs e)
        {
            ShowMarketAlertNotification(_cfg, e.Title, e.Message, ToolTipIcon.Warning);
        }

        private void OnAppNotificationRequested(object? sender, AppNotificationEventArgs e)
        {
            if (_form.IsDisposed || _disposed)
                return;

            _ = PhoneAlertNotificationDelivery.SendIfRequestedAsync(_cfg, _phoneAlerts, e);

            void ShowLocal()
            {
                if (_form.IsDisposed || _disposed)
                    return;

                bool doNotDisturb = _cfg.DoNotDisturbEnabled;
                if (e.ShowToast)
                {
                    GlobalPromptService.Notify(
                        e.Title,
                        e.Message,
                        GlobalPromptService.MapSeverity(e.Severity),
                        source: e.Source ?? "系统通知",
                        dedupKey: e.DedupKey,
                        playSound: false,
                        placement: e.Placement,
                        owner: _form);
                }

                if (e.PlaySound && !doNotDisturb)
                {
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
                }
            }

            if (_form.InvokeRequired)
            {
                try { _form.BeginInvoke(new Action(ShowLocal)); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
            }
            else
            {
                ShowLocal();
            }
        }

        private void OnCs2UpdateDetected(object? sender, Cs2UpdateDetectedEventArgs e)
        {
            if (_form.IsDisposed) return;
            if (_cfg.DoNotDisturbEnabled) return;

            void ShowLocal()
            {
                if (_form.IsDisposed) return;
                GlobalPromptService.Notify(
                    e.Title,
                    e.Message,
                    GlobalPromptKind.Info,
                    source: "CS2 更新提醒",
                    dedupKey: "CS2Update:" + e.Title,
                    playSound: false,
                    owner: _form);
                if (_cfg.Cs2UpdateReminderSoundEnabled)
                {
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
                }
            }

            if (_form.InvokeRequired)
            {
                try { _form.BeginInvoke(new Action(ShowLocal)); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
            }
            else
            {
                ShowLocal();
            }

            if (!_cfg.Cs2UpdateReminderWechatEnabled || !_phoneAlerts.IsConfigured(_cfg))
                return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    await _phoneAlerts.SendConfiguredAsync(_cfg, e.Title, e.Message).ConfigureAwait(false);
                }
                catch
                {
                    // Phone push records expected failures; update checks must keep running.
                }
            });
        }

        public bool ShowMarketAlertNotification(Settings cfg, string title, string message, ToolTipIcon icon)
        {
            if (_form.IsDisposed) return false;

            bool Show()
            {
                if (_form is MainForm mainForm && !mainForm.IsDisposed)
                {
                    return MarketAlertNotificationDispatcher.Show(cfg, mainForm, _phoneAlerts, title, message, icon);
                }

                return false;
            }

            if (_form.InvokeRequired)
            {
                try
                {
                    _form.BeginInvoke(new Action(() => Show()));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return Show();
            }
        }
    }
}
