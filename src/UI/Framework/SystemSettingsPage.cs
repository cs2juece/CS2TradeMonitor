using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.Helpers;
using CS2TradeMonitor.src.UI.SettingsPage;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class SystemSettingsHostPage : SettingsPageBase
    {
        private readonly PageHost _pageHost;
        private readonly SettingsTransaction _settingsTransaction;
        private readonly SystemSettingsPage _systemSettingsPage;
        private bool _hosted;

        public SystemSettingsHostPage()
            : this(SystemPageRuntimeServices.Resolve())
        {
        }

        internal SystemSettingsHostPage(SystemPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _pageHost = new PageHost();
            _settingsTransaction = new SettingsTransaction(() => Config);
            _systemSettingsPage = new SystemSettingsPage(runtimeServices);

            Controls.Add(_pageHost);
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config is null)
                return;

            _settingsTransaction.Rebase();
            if (!_hosted)
            {
                _pageHost.AttachSettings(_settingsTransaction.Draft);
                _pageHost.ShowPage(_systemSettingsPage);
                _hosted = true;
            }
            else
            {
                _systemSettingsPage.Activate();
            }
        }

        public override void OnThemeChanged()
        {
            base.OnThemeChanged();
            _systemSettingsPage.ApplySystemTheme();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            _pageHost.Bounds = ClientRectangle;
            _pageHost.PerformLayout();
            _pageHost.RequestCurrentPageRelayout();
        }

        public override void Save()
        {
            if (!_hosted || Config is null)
                return;

            _pageHost.SaveCurrentPage();
            _settingsTransaction.Commit();
        }
    }

    public sealed class SystemSettingsPage : UserControl, IUiPage
    {
        private readonly BufferedPanel _container;
        private readonly List<Action> _refreshActions = new List<Action>();
        private readonly List<Action> _saveActions = new List<Action>();
        private readonly List<Control> _pageBlocks = new List<Control>();
        private readonly List<SystemSettingsSection> _sections = new List<SystemSettingsSection>();
        private SettingsStore? _settingsStore;
        private Label? _startupStatusHint;
        private Label? _softwareUpdateStatusHint;
        private Label? _supportStatusHint;
        private Label? _detailedDiagnosticsStatusHint;
        private SystemSettingsToggleRow? _detailedDiagnosticsToggle;
        private SystemSettingsActionRow? _diagnosticExportRow;
        private SystemSettingsPageHeader? _pageHeader;
        private readonly ISoftwareUpdateService _softwareUpdates;
        private readonly IMonitorModuleHost _moduleHost;
        private readonly IDetailedDiagnosticsService _detailedDiagnostics;
        private readonly IDetailedDiagnosticsExportService _diagnosticsExport;
        private bool _startupStatusUpdateQueued;
        private bool _softwareUpdateBusy;
        private bool _updatingDetailedDiagnosticsToggle;
        private bool _diagnosticExportBusy;

        public SystemSettingsPage()
            : this(SystemPageRuntimeServices.Resolve())
        {
        }

        internal SystemSettingsPage(SystemPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _softwareUpdates = runtimeServices.SoftwareUpdates;
            _moduleHost = runtimeServices.ModuleHost;
            _detailedDiagnostics = runtimeServices.DetailedDiagnostics;
            _diagnosticsExport = runtimeServices.DiagnosticsExport;
            BackColor = UIColors.MainBg;
            Dock = DockStyle.Fill;
            Padding = new Padding(0);

            _container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = UIUtils.S(new Padding(22, 14, 22, 4)),
                BackColor = UIColors.MainBg
            };
            _container.HandleCreated += (_, __) => FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(_container);
            _container.Layout += (_, __) => LayoutPage();
            _container.Resize += (_, __) => LayoutPage();
            Controls.Add(_container);

            InitializeUI();
            ConfigureTransparentTextSurfaces(_container);
        }

        public void Initialize(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            RefreshFromStore();
        }

        public void Activate()
        {
            _container.AutoScrollPosition = Point.Empty;
            RefreshFromStore();
            QueueStartupStatusHintUpdate();
            LayoutPage();
        }

        public void Deactivate()
        {
        }

        public void Save()
        {
            foreach (Action saveAction in _saveActions)
            {
                saveAction();
            }
        }

        public void ApplySystemTheme()
        {
            BackColor = UIColors.MainBg;
            _container.BackColor = UIColors.MainBg;
            _pageHeader?.RefreshTheme();
            foreach (SystemSettingsSection section in _sections)
                section.RefreshTheme();
            if (_startupStatusHint != null)
            {
                _startupStatusHint.ForeColor = UIColors.TextSub;
            }
            if (_detailedDiagnosticsStatusHint != null)
                _detailedDiagnosticsStatusHint.ForeColor = UIColors.TextSub;

            ConfigureTransparentTextSurfaces(_container);
            PerformLayout();
            _container.Invalidate(true);
            _container.Update();
        }

        private void InitializeUI()
        {
            _container.SuspendLayout();
            try
            {
                _pageHeader = new SystemSettingsPageHeader();
                AddPageBlock(_pageHeader);
                CreateSystemSections();
            }
            finally
            {
                _container.ResumeLayout(false);
                LayoutPage();
            }
        }

        private void CreateSystemSections()
        {
            using (UiJankProfiler.Measure("SystemPage.BuildGroup", "Startup", thresholdMs: 1))
            {
                var section = CreateSection("启动与显示", UIUtils.S(168));
                var autoStart = CreateToggleRow(
                    "开机启动",
                    "打开电脑后自动启动 CS2 交易监控。",
                    nameof(Settings.AutoStart),
                    fallback: false);
                autoStart.DrawRightBorder = true;
                var taskbar = CreateToggleRow(
                    "显示 Windows 任务栏按钮",
                    "在任务栏中显示软件图标。",
                    nameof(Settings.ShowMainWindowInTaskbar),
                    fallback: false,
                    "作者直播专用");
                var tray = CreateToggleRow(
                    "隐藏托盘图标",
                    "隐藏后，软件仍会在后台运行。",
                    nameof(Settings.HideTrayIcon),
                    fallback: false);
                tray.CheckedChanged += (_, __) => EnsureSafeVisibility(tray);
                _startupStatusHint = CreateInfoLabel("开机启动状态：正在读取...");

                section.Body.Controls.Add(autoStart);
                section.Body.Controls.Add(taskbar);
                section.Body.Controls.Add(tray);
                section.Body.Controls.Add(_startupStatusHint);
                section.Body.Layout += (_, __) =>
                {
                    int firstRowHeight = UIUtils.S(68);
                    bool stacked = ShouldStackStartupRows(section.Body.Width);
                    int desiredBodyHeight = stacked ? UIUtils.S(236) : UIUtils.S(168);
                    if (section.BodyHeight != desiredBodyHeight)
                    {
                        section.SetBodyHeight(desiredBodyHeight);
                        return;
                    }

                    if (stacked)
                    {
                        autoStart.DrawRightBorder = false;
                        autoStart.SetBounds(0, 0, section.Body.Width, firstRowHeight);
                        taskbar.SetBounds(0, firstRowHeight, section.Body.Width, firstRowHeight);
                        tray.SetBounds(0, firstRowHeight * 2, section.Body.Width, UIUtils.S(60));
                        _startupStatusHint.SetBounds(UIUtils.S(22), firstRowHeight * 2 + UIUtils.S(60),
                            Math.Max(1, section.Body.Width - UIUtils.S(44)), UIUtils.S(40));
                        return;
                    }

                    autoStart.DrawRightBorder = true;
                    int half = section.Body.Width / 2;
                    autoStart.SetBounds(0, 0, half, firstRowHeight);
                    taskbar.SetBounds(half, 0, section.Body.Width - half, firstRowHeight);
                    tray.SetBounds(0, firstRowHeight, section.Body.Width, UIUtils.S(60));
                    _startupStatusHint.SetBounds(UIUtils.S(22), firstRowHeight + UIUtils.S(60),
                        Math.Max(1, section.Body.Width - UIUtils.S(44)), UIUtils.S(40));
                };
            }

            using (UiJankProfiler.Measure("SystemPage.BuildGroup", "Update", thresholdMs: 1))
            {
                var section = CreateSection("软件更新", UIUtils.S(44));
                var row = new SystemSettingsUpdateRow(GetDisplayVersion(), CheckSoftwareUpdate, OpenGitHubPage)
                {
                    Dock = DockStyle.Fill
                };
                _softwareUpdateStatusHint = row.StatusLabel;
                section.Body.Controls.Add(row);
            }

            using (UiJankProfiler.Measure("SystemPage.BuildGroup", "Diagnostics", thresholdMs: 1))
            {
                var section = CreateSection("诊断工具", 0);
                var diagnosticsToggle = new LiteButton("⌄", false)
                {
                    Width = UIUtils.S(42),
                    Height = UIUtils.S(28),
                    Font = new Font("Segoe UI Symbol", 11F)
                };
                section.AddHeaderAction(diagnosticsToggle);

                _detailedDiagnosticsToggle = new SystemSettingsToggleRow(
                    "详细诊断模式",
                    "当前关闭；开启后记录所有模块并在 48 小时后自动关闭。",
                    value: false);
                _detailedDiagnosticsToggle.CheckedChanged += (_, __) => ChangeDetailedDiagnosticsMode();
                _detailedDiagnosticsStatusHint = CreateInfoLabel("详细诊断状态：正在读取...");

                var logRow = new SystemSettingsActionRow(
                    "",
                    "打开日志",
                    "查看常规错误日志；详细诊断会另外保存为 JSON Lines。",
                    "打开常规日志",
                    OpenLogFile,
                    primary: false,
                    accentOutline: true);
                var detailedLogRow = new SystemSettingsActionRow(
                    "",
                    "打开详细日志",
                    "查看当前或最近一次保留的详细诊断会话。",
                    "打开详细日志",
                    OpenDetailedLogFile,
                    primary: false,
                    accentOutline: true,
                    secondaryText: "日志目录",
                    secondaryAction: OpenDetailedLogDirectory,
                    actionLeftRatio: 0.76F,
                    secondaryLeftRatio: 0.61F);
                _diagnosticExportRow = new SystemSettingsActionRow(
                    "",
                    "导出诊断包",
                    "生成普通 ZIP 到你选择的位置；软件不会自动上传或发送。",
                    "导出诊断包",
                    ExportDiagnosticsPackage,
                    primary: true);
                var resetRow = new SystemSettingsActionRow(
                    "",
                    "恢复默认设置",
                    "清除本机设置并恢复初始状态，操作前会再次确认。",
                    "恢复默认设置",
                    () => SettingsForm.PerformFactoryReset(FindForm()),
                    primary: false,
                    danger: true);
                logRow.MaximumTextWidth = UIUtils.S(520);
                detailedLogRow.MaximumTextWidth = UIUtils.S(520);
                _diagnosticExportRow.MaximumTextWidth = UIUtils.S(520);
                resetRow.MaximumTextWidth = UIUtils.S(520);
                section.Body.Controls.Add(_detailedDiagnosticsToggle);
                section.Body.Controls.Add(_detailedDiagnosticsStatusHint);
                section.Body.Controls.Add(logRow);
                section.Body.Controls.Add(detailedLogRow);
                section.Body.Controls.Add(_diagnosticExportRow);
                section.Body.Controls.Add(resetRow);
                section.Body.Layout += (_, __) =>
                {
                    int toggleHeight = UIUtils.S(76);
                    int statusHeight = UIUtils.S(54);
                    int rowHeight = UIUtils.S(58);
                    int top = 0;
                    _detailedDiagnosticsToggle.SetBounds(0, top, section.Body.Width, toggleHeight);
                    top += toggleHeight;
                    _detailedDiagnosticsStatusHint.SetBounds(
                        UIUtils.S(22),
                        top,
                        Math.Max(1, section.Body.Width - UIUtils.S(44)),
                        statusHeight);
                    top += statusHeight;
                    logRow.SetBounds(0, top, section.Body.Width, rowHeight);
                    top += rowHeight;
                    detailedLogRow.SetBounds(0, top, section.Body.Width, rowHeight);
                    top += rowHeight;
                    _diagnosticExportRow.SetBounds(0, top, section.Body.Width, rowHeight);
                    top += rowHeight;
                    resetRow.SetBounds(0, top, section.Body.Width, Math.Max(rowHeight, section.Body.Height - top));
                };

                _refreshActions.Add(RefreshDetailedDiagnosticsUi);

                bool expanded = false;
                diagnosticsToggle.Click += (_, __) =>
                {
                    expanded = !expanded;
                    section.SetBodyHeight(expanded ? UIUtils.S(362) : 0);
                    diagnosticsToggle.Text = expanded ? "⌃" : "⌄";
                    if (expanded)
                        RefreshDetailedDiagnosticsUi();
                    LayoutPage();
                };
            }

            using (UiJankProfiler.Measure("SystemPage.BuildGroup", "Support", thresholdMs: 1))
            {
                var section = CreateSection("官方支持", UIUtils.S(140));
                var groupRow = new SystemSettingsActionRow(
                    "",
                    "官方反馈群",
                    $"QQ群号：{SupportInfo.QqGroupNumber}",
                    "一键加入",
                    OpenQqGroup,
                    primary: true,
                    accentOutline: false,
                    secondaryText: "复制",
                    secondaryAction: CopyQqGroupNumber,
                    detail: "加入官方群，获取使用帮助和版本更新通知。",
                    actionLeftRatio: 0.65F,
                    secondaryLeftRatio: 0.265F,
                    compactSecondary: true);
                var authorRow = new SystemSettingsActionRow(
                    "",
                    "编者的话",
                    "查看作者介绍与项目说明。",
                    "编者的话",
                    OpenAuthorNote,
                    primary: false,
                    actionLeftRatio: 0.65F);
                groupRow.ActionButton.Width = UIUtils.S(130);
                authorRow.ActionButton.Width = UIUtils.S(130);
                _supportStatusHint = groupRow.Controls.OfType<Label>()
                    .FirstOrDefault(label => label.Text.StartsWith("QQ群号：", StringComparison.Ordinal));
                section.Body.Controls.Add(groupRow);
                section.Body.Controls.Add(authorRow);
                section.Body.Layout += (_, __) =>
                {
                    int firstRowHeight = UIUtils.S(80);
                    groupRow.SetBounds(0, 0, section.Body.Width, firstRowHeight);
                    authorRow.SetBounds(0, firstRowHeight, section.Body.Width, section.Body.Height - firstRowHeight);
                };
            }
        }

        private static string GetDisplayVersion()
        {
            string version = System.Windows.Forms.Application.ProductVersion ?? "";
            int metadataIndex = version.IndexOf('+');
            if (metadataIndex >= 0)
                version = version[..metadataIndex];

            version = version.Trim();
            return string.IsNullOrWhiteSpace(version) ? "1.0" : version;
        }

        internal static bool ShouldStackStartupRows(int bodyWidth)
        {
            return bodyWidth < UIUtils.S(960);
        }

        private SystemSettingsToggleRow CreateToggleRow(
            string title,
            string description,
            string settingKey,
            bool fallback,
            string? tag = null)
        {
            var row = new SystemSettingsToggleRow(title, description, Get(settingKey, fallback), tag);
            row.CheckedChanged += (_, __) => _settingsStore?.Set(settingKey, row.Checked);
            _refreshActions.Add(() => row.Checked = Get(settingKey, fallback));
            _saveActions.Add(() => _settingsStore?.Set(settingKey, row.Checked));
            return row;
        }

        private T Get<T>(string key, T fallback) where T : notnull
        {
            return _settingsStore != null ? _settingsStore.Get(key, fallback) : fallback;
        }

        private void RefreshFromStore()
        {
            foreach (Action refreshAction in _refreshActions)
            {
                refreshAction();
            }
        }

        private void EnsureSafeVisibility(SystemSettingsToggleRow hideTrayToggle)
        {
            bool hideTray = hideTrayToggle.Checked;
            var candidate = new Settings
            {
                HideMainForm = Get(nameof(Settings.HideMainForm), fallback: false),
                HideTrayIcon = hideTray,
                ShowTaskbar = Get(nameof(Settings.ShowTaskbar), fallback: true),
                ClickThrough = Get(nameof(Settings.ClickThrough), fallback: false),
                TaskbarClickThrough = Get(nameof(Settings.TaskbarClickThrough), fallback: false)
            };

            if (!Settings.HasNoInteractiveEntry(candidate))
                return;

            hideTrayToggle.Checked = false;
            _settingsStore?.Set(nameof(Settings.HideTrayIcon), false);
            SetStartupStatusHintText("已自动保留托盘图标，避免软件失去所有可交互入口。");
        }

        private void QueueStartupStatusHintUpdate()
        {
            if (_startupStatusHint == null || _startupStatusHint.IsDisposed)
                return;

            if (_startupStatusUpdateQueued)
                return;

            _startupStatusUpdateQueued = true;
            SetStartupStatusHintText("正在读取开机启动状态...");

            _ = Task.Run(AutoStart.GetStatusSummary)
                .ContinueWith(task =>
                {
                    string text = task.Status == TaskStatus.RanToCompletion
                        ? ToUserFacingStartupStatus(task.Result)
                        : "无法读取开机启动状态，请稍后重试。";

                    if (IsDisposed || Disposing || !IsHandleCreated)
                    {
                        _startupStatusUpdateQueued = false;
                        return;
                    }

                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _startupStatusUpdateQueued = false;
                            SetStartupStatusHintText(text);
                        }));
                    }
                    catch
                    {
                        _startupStatusUpdateQueued = false;
                    }
                }, TaskScheduler.Default);
        }

        private void SetStartupStatusHintText(string text)
        {
            if (_startupStatusHint == null || _startupStatusHint.IsDisposed)
                return;

            _startupStatusHint.Text = text;
        }

        private static string ToUserFacingStartupStatus(string status)
        {
            if (status.Contains("已指向当前程序", StringComparison.Ordinal))
                return "开机启动已启用。";
            if (status.Contains("未启用", StringComparison.Ordinal))
                return "开机启动未启用。";
            if (status.Contains("路径不是当前程序", StringComparison.Ordinal))
                return "开机启动需要重新启用。";
            if (status.Contains("旧启动项", StringComparison.Ordinal))
                return "检测到旧启动设置，建议重新启用开机启动。";
            if (status.Contains("读取失败", StringComparison.Ordinal))
                return "无法读取开机启动状态，请稍后重试。";
            return status;
        }

        internal IReadOnlyList<string> GetDisplayedGroupTitlesForTesting()
        {
            return _sections.Select(section => section.Title).ToArray();
        }

        internal IReadOnlyList<string> GetDisplayedButtonTextsForTesting()
        {
            return EnumerateControls(_container)
                .Where(control => control is LiteButton)
                .Select(control => control.Text)
                .ToArray();
        }

        internal IReadOnlyList<string> GetDisplayedTextsForTesting()
        {
            return EnumerateControls(_container)
                .OfType<Label>()
                .Select(label => label.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
        }

        private static IEnumerable<Control> EnumerateControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in EnumerateControls(child))
                    yield return descendant;
            }
        }

        private static void ConfigureTransparentTextSurfaces(Control root)
        {
            foreach (Control control in EnumerateControls(root))
            {
                if (control is not Label label)
                    continue;

                label.BackColor = Color.Transparent;
                label.UseCompatibleTextRendering = false;
                label.Invalidate();
            }
        }

        private static void OpenLogFile()
        {
            DiagnosticsLogFileOpener.OpenLogFile();
        }

        private void ChangeDetailedDiagnosticsMode()
        {
            if (_detailedDiagnosticsToggle == null || _updatingDetailedDiagnosticsToggle)
                return;

            try
            {
                if (_detailedDiagnosticsToggle.Checked)
                {
                    const string confirmation =
                        "开启后会记录所有模块的详细运行信息，并跨软件重启持续最多 48 小时。\n\n" +
                        "每个目录实例最多占用 200 MB，结束会话自动保留 7 天。网络请求体和响应体会先脱敏；无法确认安全的内容不会保留。\n\n" +
                        "你可以随时关闭，关闭后立即停止记录。是否开启？";
                    Form? owner = FindForm();
                    DialogResult result = owner != null
                        ? MessageBox.Show(owner, confirmation, "开启详细诊断模式", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                        : MessageBox.Show(confirmation, "开启详细诊断模式", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result != DialogResult.Yes)
                    {
                        SetDetailedDiagnosticsToggle(checkedValue: false);
                        return;
                    }

                    _detailedDiagnostics.Enable();
                    DiagnosticsLogger.Info("DiagnosticsUI", "Detailed diagnostics enabled by user confirmation.");
                }
                else
                {
                    _detailedDiagnostics.Disable();
                    DiagnosticsLogger.Info("DiagnosticsUI", "Detailed diagnostics disabled by user.");
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("DiagnosticsUI", "Changing detailed diagnostics mode failed.", ex);
                SetDetailedDiagnosticsToggle(checkedValue: false);
                SetDetailedDiagnosticsStatus("详细诊断状态不可用，请查看常规日志。", UIColors.TextWarn);
                return;
            }

            RefreshDetailedDiagnosticsUi();
        }

        private void RefreshDetailedDiagnosticsUi()
            => RefreshDetailedDiagnosticsUi(statusNotice: null);

        private void RefreshDetailedDiagnosticsUi(string? statusNotice)
        {
            if (_detailedDiagnosticsToggle == null || _detailedDiagnosticsStatusHint == null)
                return;

            try
            {
                DetailedDiagnosticsStatus status = _detailedDiagnostics.GetStatus();
                SetDetailedDiagnosticsToggle(status.IsEnabled);
                if (status.IsEnabled && status.StartedAtUtc is not null && status.ExpiresAtUtc is not null)
                {
                    DateTime start = status.StartedAtUtc.Value.ToLocalTime();
                    DateTime expires = status.ExpiresAtUtc.Value.ToLocalTime();
                    TimeSpan remaining = status.ExpiresAtUtc.Value - DateTime.UtcNow;
                    _detailedDiagnosticsToggle.Description = "正在记录所有模块；无需重启，关闭后立即停止。";
                    string text =
                        $"开始 {start:MM-dd HH:mm} · 自动关闭 {expires:MM-dd HH:mm}（剩余 {FormatRemaining(remaining)}） · " +
                        $"占用 {FormatDiagnosticBytes(status.TotalBytes)} / {FormatDiagnosticBytes(status.MaximumBytes)}";
                    if (status.DroppedEventCount > 0)
                        text += $" · 已丢弃 {status.DroppedEventCount} 条低优先级事件";
                    if (!string.IsNullOrWhiteSpace(status.LastError))
                        text += " · 部分诊断能力不可用";
                    SetDetailedDiagnosticsStatus(
                        CombineDetailedDiagnosticsStatus(statusNotice, text),
                        string.IsNullOrWhiteSpace(status.LastError) ? UIColors.Positive : UIColors.TextWarn);
                    return;
                }

                _detailedDiagnosticsToggle.Description = "当前关闭；开启后跨重启持续 48 小时，结束会话自动保留 7 天。";
                string ended = status.LastSessionEndedAtUtc is null
                    ? "最近会话：暂无"
                    : $"最近会话结束：{status.LastSessionEndedAtUtc.Value.ToLocalTime():MM-dd HH:mm}";
                string suffix = status.TotalBytes > 0
                    ? $" · 已保留 {FormatDiagnosticBytes(status.TotalBytes)}"
                    : "";
                if (!string.IsNullOrWhiteSpace(status.LastError))
                    suffix += " · 状态存在异常，请查看常规日志";
                SetDetailedDiagnosticsStatus(
                    CombineDetailedDiagnosticsStatus(statusNotice, ended + suffix),
                    string.IsNullOrWhiteSpace(status.LastError) ? UIColors.TextSub : UIColors.TextWarn);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("DiagnosticsUI", "Refreshing detailed diagnostics status failed.", ex);
                SetDetailedDiagnosticsToggle(checkedValue: false);
                _detailedDiagnosticsToggle.Description = "详细诊断服务暂时不可用。";
                SetDetailedDiagnosticsStatus("无法读取详细诊断状态，请查看常规日志。", UIColors.TextWarn);
            }
        }

        private void SetDetailedDiagnosticsToggle(bool checkedValue)
        {
            if (_detailedDiagnosticsToggle == null)
                return;
            _updatingDetailedDiagnosticsToggle = true;
            try
            {
                _detailedDiagnosticsToggle.Checked = checkedValue;
            }
            finally
            {
                _updatingDetailedDiagnosticsToggle = false;
            }
        }

        private void SetDetailedDiagnosticsStatus(string text, Color color)
            => SetHintText(_detailedDiagnosticsStatusHint, text, color);

        private void OpenDetailedLogFile()
        {
            try
            {
                string? path = _diagnosticsExport.GetPreferredLogFilePath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    SetDetailedDiagnosticsStatus("当前没有可打开的详细诊断日志。", UIColors.TextSub);
                    return;
                }

                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                DiagnosticsLogger.Info("DiagnosticsUI", "Detailed diagnostics log opened by user.");
                RefreshDetailedDiagnosticsUi("已打开当前或最近一次详细诊断日志。");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("DiagnosticsUI", "Opening detailed diagnostics log failed.", ex);
                SetDetailedDiagnosticsStatus("打开详细诊断日志失败，请查看常规日志。", UIColors.TextWarn);
            }
        }

        private void OpenDetailedLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(_detailedDiagnostics.DiagnosticsDirectory);
                Process.Start(new ProcessStartInfo(_detailedDiagnostics.DiagnosticsDirectory) { UseShellExecute = true });
                DiagnosticsLogger.Info("DiagnosticsUI", "Detailed diagnostics directory opened by user.");
                RefreshDetailedDiagnosticsUi("已打开详细诊断日志目录。");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("DiagnosticsUI", "Opening detailed diagnostics directory failed.", ex);
                SetDetailedDiagnosticsStatus("打开日志目录失败，请查看常规日志。", UIColors.TextWarn);
            }
        }

        private async void ExportDiagnosticsPackage()
        {
            if (_diagnosticExportBusy)
                return;

            using var dialog = new SaveFileDialog
            {
                Title = "导出诊断包",
                Filter = "ZIP 压缩包 (*.zip)|*.zip",
                DefaultExt = "zip",
                AddExtension = true,
                FileName = $"CS2TradeMonitor-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            _diagnosticExportBusy = true;
            if (_diagnosticExportRow != null)
                _diagnosticExportRow.ActionButton.Enabled = false;
            SetDetailedDiagnosticsStatus("正在生成诊断包，请稍候...", UIColors.TextSub);
            try
            {
                var configuration = new Dictionary<string, object?>
                {
                    ["autoStart"] = Get(nameof(Settings.AutoStart), false),
                    ["showTaskbarButton"] = Get(nameof(Settings.ShowMainWindowInTaskbar), false),
                    ["hideTrayIcon"] = Get(nameof(Settings.HideTrayIcon), false),
                    ["settingsDarkMode"] = Get(nameof(Settings.SettingsPanelDarkMode), true),
                    ["marketRefreshMilliseconds"] = Get(nameof(Settings.RefreshMs), 1000),
                    ["steamDtRefreshSeconds"] = Get(nameof(Settings.SteamDtRefreshSec), Settings.DefaultMarketRefreshSec),
                    ["csqaqRefreshSeconds"] = Get(nameof(Settings.CsqaqRefreshSec), Settings.DefaultMarketRefreshSec),
                    ["displayScale"] = UIUtils.DpiScale * UIUtils.UserScale,
                    ["screenCount"] = Screen.AllScreens.Length,
                    ["moduleHealth"] = _moduleHost.GetHealthSnapshot()
                        .Select(health => new { health.Id, State = health.State.ToString() })
                        .ToArray()
                };
                await _diagnosticsExport.ExportAsync(dialog.FileName, configuration);
                DiagnosticsLogger.Info("DiagnosticsUI", "Diagnostics package exported by user.");
                RefreshDetailedDiagnosticsUi("诊断包已导出");
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("DiagnosticsUI", "Exporting diagnostics package failed.", ex);
                SetDetailedDiagnosticsStatus("导出诊断包失败，请查看常规日志。", UIColors.TextWarn);
            }
            finally
            {
                _diagnosticExportBusy = false;
                if (_diagnosticExportRow != null)
                    _diagnosticExportRow.ActionButton.Enabled = true;
            }
        }

        private static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
                return "即将关闭";
            if (remaining < TimeSpan.FromMinutes(1))
                return "不足 1 分钟";
            if (remaining.TotalHours >= 24)
                return $"{(int)remaining.TotalDays} 天 {remaining.Hours} 小时";
            return $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分钟";
        }

        internal static string CombineDetailedDiagnosticsStatus(string? statusNotice, string lifecycleStatus)
            => string.IsNullOrWhiteSpace(statusNotice)
                ? lifecycleStatus
                : $"{statusNotice} · {lifecycleStatus}";

        private static string FormatDiagnosticBytes(long bytes)
        {
            return bytes >= 1024L * 1024
                ? $"{bytes / 1024d / 1024d:F1} MB"
                : bytes >= 1024
                    ? $"{bytes / 1024d:F1} KB"
                    : $"{bytes} B";
        }

        private async void CheckSoftwareUpdate()
        {
            if (_softwareUpdateBusy)
                return;

            _softwareUpdateBusy = true;
            try
            {
                Form? owner = FindForm();
                Cursor.Current = Cursors.WaitCursor;
                SetSoftwareUpdateStatus(SoftwareUpdateStatusTextFormatter.Checking());
                var result = await _softwareUpdates.CheckAsync();
                SetSoftwareUpdateStatus(SoftwareUpdateStatusTextFormatter.FromResult(result));

                switch (result.State)
                {
                    case SoftwareUpdateState.Available:
                        using (var dialog = new SoftwareUpdateDialog(result, _softwareUpdates))
                        {
                            if (owner != null)
                                dialog.ShowDialog(owner);
                            else
                                dialog.ShowDialog();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("SoftwareUpdate", "Manual update check failed.", ex);
                SetSoftwareUpdateStatus(SoftwareUpdateStatusTextFormatter.Failed(ex));
            }
            finally
            {
                Cursor.Current = Cursors.Default;
                _softwareUpdateBusy = false;
            }
        }

        private static void OpenGitHubPage()
        {
            SystemActions.OpenUrl(SupportInfo.GetReleaseDownloadPage());
        }

        private void OpenAuthorNote()
        {
            using var dialog = new AuthorNoteDialog();
            Form? owner = FindForm();
            if (owner != null)
                dialog.ShowDialog(owner);
            else
                dialog.ShowDialog();
        }

        private void OpenQqGroup()
        {
            try
            {
                SupportInfo.OpenQqGroup();
                SetSupportStatusText("已打开官方群加群页面。", UIColors.Positive);
            }
            catch (Exception ex)
            {
                SetSupportStatusText("打开官方群链接失败：" + ex.Message, UIColors.TextWarn);
            }
        }

        private void CopyQqGroupNumber()
        {
            try
            {
                SupportInfo.CopyQqGroupNumber();
                SetSupportStatusText("官方群号已复制。", UIColors.Positive);
            }
            catch (Exception ex)
            {
                SetSupportStatusText("复制官方群号失败：" + ex.Message, UIColors.TextWarn);
            }
        }

        private void SetSoftwareUpdateStatus(SoftwareUpdateStatusText status)
        {
            Color color = status.Tone switch
            {
                SoftwareUpdateStatusTone.Positive => UIColors.Positive,
                SoftwareUpdateStatusTone.Warning => UIColors.TextWarn,
                SoftwareUpdateStatusTone.Critical => UIColors.TextCrit,
                _ => UIColors.TextSub
            };
            SetHintText(_softwareUpdateStatusHint, "更新状态：" + status.Text, color);
        }

        private void SetSupportStatusText(string text, Color color)
        {
            SetHintText(_supportStatusHint, text, color);
        }

        private static void SetHintText(Label? hint, string text, Color color)
        {
            if (hint == null || hint.IsDisposed)
                return;

            hint.Text = text;
            hint.ForeColor = color;
        }

        private SystemSettingsSection CreateSection(string title, int bodyHeight)
        {
            var section = new SystemSettingsSection(title, bodyHeight);
            _sections.Add(section);
            AddPageBlock(section);
            return section;
        }

        private void AddPageBlock(Control control)
        {
            _pageBlocks.Add(control);
            _container.Controls.Add(control);
        }

        private static Label CreateInfoLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UIFonts.Regular(8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent
            };
        }

        private void LayoutPage()
        {
            if (_container.IsDisposed || _pageBlocks.Count == 0)
                return;

            int availableWidth = Math.Max(
                UIUtils.S(680),
                _container.ClientSize.Width - _container.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - UIUtils.S(4));
            int scrollX = _container.AutoScrollPosition.X;
            int scrollY = _container.AutoScrollPosition.Y;
            int left = _container.Padding.Left + scrollX;
            int virtualTop = _container.Padding.Top;
            int gap = UIUtils.S(14);

            for (int index = 0; index < _pageBlocks.Count; index++)
            {
                Control block = _pageBlocks[index];
                block.SetBounds(left, virtualTop + scrollY, availableWidth, block.Height);
                block.PerformLayout();
                int blockGap = index == 1 ? UIUtils.S(19) : gap;
                virtualTop += block.Height + blockGap;
            }

            _container.AutoScrollMinSize = new Size(0, virtualTop + _container.Padding.Bottom);
            FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(_container);
        }
    }
}
