using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    public interface IMainPanelSettingsPageHost
    {
        string ActiveTab { get; }
        bool IsHandleCreated { get; }
        bool IsDisposed { get; }
        bool Visible { get; }
        void SelectTab(string key);
    }

    public sealed class LegacyMainPanelHostPage : FrameworkSettingsHostPage<MainPanelPage>, IMainPanelSettingsPageHost
    {
        public LegacyMainPanelHostPage()
            : base(new MainPanelPage())
        {
        }

        public string ActiveTab => HostedPage.ActiveTab;

        public void SelectTab(string key)
        {
            HostedPage.SelectTab(key);
        }
    }

    public sealed class MainPanelPage : FrameworkSettingsPageBase
    {
        private readonly Dictionary<string, Panel> _tabPanels = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _builtTabs = new(StringComparer.OrdinalIgnoreCase);
        private readonly MainPanelPreviewController _previewController;
        private readonly MainPanelPageLayoutController _layoutController;
        private readonly MainPanelMonitorRefreshScheduler _monitorRefreshScheduler;
        private readonly MainPanelSettingsApplier _settingsApplier;
        private readonly MainPanelMonitorGroupsBuilder _monitorGroupsBuilder;
        private readonly MainPanelStyleGroupsBuilder _styleGroupsBuilder;
        private readonly MainPanelTaskbarGroupsBuilder _taskbarGroupsBuilder;
        private readonly MainPanelFloatGroupsBuilder _floatGroupsBuilder;
        private readonly MainPanelTabGroupBuildRouter _tabGroupBuildRouter;
        private readonly MainPanelSettingControlBinder _settingControlBinder;
        private readonly MainPanelTabPanelHost _tabPanelHost;
        private MainPanelTabBar? _tabBar;
        private string _activeTab = MainPanelTabKeys.Float;
        private string? _buildingTab;
        private bool _panelsBuilt;
        private bool _storeAttached;
        private bool _buildingPage;
        private bool _suppressLayoutQueue;
        private readonly UiDeferredActionScheduler _deferredActions;
        private readonly MainPanelDeferredTabGroupQueue _deferredTabGroups;
        private readonly MainPanelDeferredSpecialGroupCoordinator _deferredSpecialGroups;

        public MainPanelPage()
        {
            _previewController = new MainPanelPreviewController(
                Container,
                () => Config,
                (key, fallback) => Get(key, fallback),
                (key, fallback) => Get(key, fallback),
                (key, fallback) => Get(key, fallback),
                () => IsHandleCreated && !IsDisposed && !Disposing,
                () => !IsDisposed && !Disposing && Visible);
            _tabPanelHost = new MainPanelTabPanelHost(Container, _tabPanels);
            _layoutController = new MainPanelPageLayoutController(
                this,
                Container,
                _previewController,
                _tabPanels,
                () => _activeTab,
                () => _tabBar,
                () => !IsDisposed && !Disposing,
                control => HideHorizontalScroll(control));
            _monitorRefreshScheduler = new MainPanelMonitorRefreshScheduler(
                () => !IsDisposed && !Disposing,
                () => IsHandleCreated,
                () =>
                {
                    UI?.RebuildLayout();
                    MainForm?.RequestLayeredRender();
                });
            _settingsApplier = new MainPanelSettingsApplier(
                (key, fallback) => Get(key, fallback),
                (key, value) => Set(key, value));
            _settingControlBinder = new MainPanelSettingControlBinder(
                () => IsUpdatingControls,
                RegisterRefresh,
                RegisterSave,
                (group, title, settingKey, fallback, afterChanged) => AddColor(group, title, settingKey, fallback, afterChanged),
                (key, fallback) => Get(key, fallback),
                (key, value) => Set(key, value),
                RefreshMonitorDisplay);
            _monitorGroupsBuilder = new MainPanelMonitorGroupsBuilder(
                () => GetList<ItemMonitorConfig>(nameof(Settings.ItemConfigs)),
                (key, fallback) => Get(key, fallback),
                (key, value) => Set(key, value),
                _settingControlBinder.AddDirectInt,
                _settingControlBinder.AddDirectToggle,
                _settingControlBinder.AddDirectColor,
                AddInt,
                (group, title, settingKey, fallback, unit, normalize, markCustomLayout) =>
                    _settingControlBinder.AddFloat(group, title, settingKey, fallback, unit, normalize, markCustomLayout),
                (group, text) => AddHint(group, text),
                group => AddGroupToPage(group),
                RegisterSave,
                RefreshMonitorDisplay);
            _styleGroupsBuilder = new MainPanelStyleGroupsBuilder(
                (key, fallback) => Get(key, fallback),
                (key, fallback) => Get(key, fallback),
                (key, fallback) => Get(key, fallback),
                (key, value) => Set(key, value),
                AddToggle,
                _settingControlBinder.AddDirectInt,
                AddInt,
                (group, title, settingKey, fallback, unit, normalize, markCustomLayout) =>
                    _settingControlBinder.AddFloat(group, title, settingKey, fallback, unit, normalize, markCustomLayout),
                (group, title, items, getValue, setValue, fullWidth) =>
                    _settingControlBinder.AddMappedCombo(group, title, items, getValue, setValue, fullWidth),
                (group, title, settingKey, fallback, afterChanged) =>
                    _settingControlBinder.AddColor(group, title, settingKey, fallback, afterChanged),
                (group, text) => AddHint(group, text),
                group => AddGroupToPage(group),
                ApplyPreset,
                RefreshMonitorDisplay);
            _taskbarGroupsBuilder = new MainPanelTaskbarGroupsBuilder(
                (key, fallback) => Get(key, fallback),
                (key, fallback) => Get(key, fallback),
                (key, value) => Set(key, value),
                AddToggle,
                (group, title, items, getIndex, setIndex, fullWidth) =>
                    _settingControlBinder.AddMappedCombo(group, title, items, getIndex, setIndex, fullWidth),
                AddInt,
                (group, text) => AddHint(group, text),
                group => AddGroupToPage(group),
                InvalidateDeferredGroupLayout,
                ApplyTaskbarStylePreset,
                EnsureSafeVisibility,
                RefreshMonitorDisplay);
            _floatGroupsBuilder = new MainPanelFloatGroupsBuilder(
                (key, fallback) => Get(key, fallback),
                (key, value) => Set(key, value),
                AddToggle,
                (group, title, items, getIndex, setIndex, fullWidth) =>
                    _settingControlBinder.AddMappedCombo(group, title, items, getIndex, setIndex, fullWidth),
                group => AddGroupToPage(group),
                InvalidateDeferredGroupLayout,
                EnsureSafeVisibility,
                _settingsApplier.ResolveSafeVisibility,
                () => MainForm?.HideMainWindow(),
                () => MainForm?.ShowMainWindow(),
                RefreshMonitorDisplay,
                () => IsUpdatingControls,
                RegisterRefresh,
                RegisterSave);
            _deferredActions = new UiDeferredActionScheduler(() => !IsDisposed && !Disposing);
            _deferredTabGroups = new MainPanelDeferredTabGroupQueue(
                () => !IsDisposed && !Disposing && IsHandleCreated,
                BuildNextDeferredTabGroup);
            _deferredSpecialGroups = new MainPanelDeferredSpecialGroupCoordinator(
                () => !IsDisposed && !Disposing,
                () => IsHandleCreated,
                () => _panelsBuilt,
                () => _activeTab,
                () => _builtTabs,
                RunInTabBuildScope,
                () => _layoutController.MarkDirty(),
                () => LayoutMainPanel(force: true),
                () =>
                {
                    LayoutMainPanel(force: true);
                    RefreshPreview();
                },
                CreateFloatAppearanceModeGroupShell,
                CreateFloatAppearanceModeEditor,
                CreateFloatAppearanceWidthGroup,
                CreateFloatAppearanceOpacityGroup,
                CreateFloatAppearanceAdvancedGroup,
                CreateTaskbarAdvancedBehaviorGroup,
                CreateTaskbarAlignGroupShell,
                CreateTaskbarAlignEditor,
                CreateTaskbarOffsetGroup,
                QueueDeferredTabGroupBuild);
            _tabGroupBuildRouter = new MainPanelTabGroupBuildRouter(
                CreateFloatBehaviorGroup,
                CreateFloatLayoutGroup,
                CreateTaskbarGeneralGroup,
                CreateTaskbarFontGroup,
                CreateItemMonitorDisplayGroup,
                CreateInventoryTrendDisplayGroup,
                CreateTaskbarFontFamilyGroup,
                CreateTaskbarSpacingGroup,
                CreateTaskbarColorGroup,
                CreateTaskbarPresetGroup,
                CreateItemMonitorColorGroup,
                CreateInventoryTrendColorGroup,
                _deferredSpecialGroups.QueueDeferredFloatAppearanceBuild,
                _deferredSpecialGroups.QueueDeferredTaskbarAdvancedBuild);

            Container.SizeChanged += (_, __) =>
            {
                _layoutController.Queue(force: true);
            };
            Container.AutoScrollMargin = new Size(0, UIUtils.S(48));
        }

        public string ActiveTab => _activeTab;

        public void SelectTab(string key)
        {
            string normalized = MainPanelTabKeys.Normalize(key);
            if (_activeTab == normalized && _panelsBuilt && _builtTabs.Contains(normalized))
            {
                EnsureTabContentBuilt(_activeTab);
                ShowActiveTabContent();
                UpdateTabBarSelection();
                if (_layoutController.IsDirty)
                    LayoutMainPanel();
                QueueDeferredTabGroupsIfActive();
                return;
            }

            using (UiJankProfiler.Measure("MainPanel.SelectTab", $"From={_activeTab}; To={normalized}", thresholdMs: 1))
            {
                _activeTab = normalized;
                if (!_panelsBuilt)
                {
                    if (_storeAttached)
                        BuildPage();
                    return;
                }

                _tabPanelHost.ResetScrollToTop();
                Container.SuspendLayout();
                try
                {
                    _suppressLayoutQueue = true;
                    _tabPanelHost.EnsureTabPanel(_activeTab);
                    ShowActiveTabContent();
                    UpdateTabBarSelection();
                    InvalidateActiveTabLayoutCache();
                    _layoutController.MarkDirty();
                }
                finally
                {
                    _suppressLayoutQueue = false;
                    Container.ResumeLayout(false);
                }

                LayoutMainPanel();
                QueueActiveTabContentBuild();
                QueueDeferredTabGroupsIfActive();
            }
        }

        public override void Activate()
        {
            base.Activate();
            if (!_panelsBuilt)
            {
                if (!_storeAttached)
                    return;

                BuildPage();
                if (!_panelsBuilt)
                    return;
            }

            _tabPanelHost.EnsureTabPanel(_activeTab);
            ShowActiveTabContent();
            UpdateTabBarSelection();
            _tabPanelHost.ResetScrollToTop();
            InvalidateActiveTabLayoutCache();
            LayoutMainPanel(force: true);
            RefreshPreview();
            QueueActiveTabContentBuild();
            _deferredSpecialGroups.QueueFloatAppearanceBuildIfActive();
            _deferredSpecialGroups.QueueTaskbarAdvancedBuildIfActive();
        }

        public override void Save()
        {
            base.Save();
            if (Config is not null)
                TaskbarRenderer.ReloadStyle(Config);

            RefreshMonitorDisplay();
        }

        protected override void OnStoreAttached()
        {
            _storeAttached = true;
            _panelsBuilt = false;
            _layoutController.Reset();
            BuildPage();
        }

        public override void ApplySystemTheme()
        {
            base.ApplySystemTheme();
            RefreshPreview();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            QueueDeferredTabGroupsIfActive();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _monitorRefreshScheduler.Dispose();
                _previewController.Dispose();
                _deferredActions.Dispose();
                _deferredTabGroups.Dispose();
                _deferredSpecialGroups.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildPage()
        {
            if (!_panelsBuilt)
            {
                using (UiJankProfiler.Measure("MainPanel.BuildPage", thresholdMs: 1))
                {
                    Container.SuspendLayout();
                    try
                    {
                        ClearPage();
                        _tabPanels.Clear();
                        _builtTabs.Clear();
                        _previewController.Reset();
                        _tabBar = null;
                        _layoutController.Reset();
                        _floatGroupsBuilder.Reset();
                        _deferredSpecialGroups.Reset();
                        _deferredTabGroups.Clear();
                        _taskbarGroupsBuilder.Reset();

                        _buildingPage = true;
                        try
                        {
                            using (UiJankProfiler.Measure("MainPanel.CreatePreviewPanel", thresholdMs: 1))
                            {
                                _previewController.Create();
                            }

                            using (UiJankProfiler.Measure("MainPanel.CreateTabBar", thresholdMs: 1))
                            {
                                CreateTabBar();
                            }
                            _tabPanelHost.EnsureTabPanel(_activeTab);
                            _panelsBuilt = true;
                        }
                        finally
                        {
                            _buildingTab = null;
                            _buildingPage = false;
                        }
                    }
                    finally
                    {
                        Container.ResumeLayout(false);
                    }
                }
            }

            _tabPanelHost.EnsureTabPanel(_activeTab);
            ShowActiveTabContent();
            UpdateTabBarSelection();
            _tabPanelHost.ResetScrollToTop();
            InvalidateActiveTabLayoutCache();
            LayoutMainPanel(force: true);
            RefreshPreview();
            QueueActiveTabContentBuild();
            _deferredSpecialGroups.QueueFloatAppearanceBuildIfActive();
            _deferredSpecialGroups.QueueTaskbarAdvancedBuildIfActive();
        }

        private void QueueActiveTabContentBuild(int delayMs = 55)
        {
            if (!_panelsBuilt || _builtTabs.Contains(_activeTab) || IsDisposed || Disposing)
                return;

            if (!IsHandleCreated)
            {
                BuildActiveTabContentNow();
                return;
            }

            _deferredActions.Schedule("active-tab-content", delayMs, BuildActiveTabContentNow);
        }

        private void QueueDeferredTabGroupBuild(
            string key,
            string tab,
            string scope,
            Action build,
            Action? afterBuild = null,
            int delayMs = 90)
        {
            QueueDeferredTabGroupBuild(
                new DeferredTabGroupBuild(key, MainPanelTabKeys.Normalize(tab), scope, build, afterBuild),
                delayMs);
        }

        private void QueueDeferredTabGroupBuild(DeferredTabGroupBuild item, int delayMs)
        {
            _deferredTabGroups.Queue(item, delayMs);
        }

        private void BuildNextDeferredTabGroup()
        {
            _deferredTabGroups.Stop();
            if (IsDisposed || Disposing || !_panelsBuilt)
                return;

            while (_deferredTabGroups.TryDequeueForTab(_activeTab, out DeferredTabGroupBuild item))
            {
                string? previousBuildingTab = _buildingTab;
                _buildingTab = item.Tab;
                Container.SuspendLayout();
                try
                {
                    using (UiJankProfiler.Measure("MainPanel.BuildDeferredTabGroup", item.Scope, thresholdMs: 1))
                    using (UiJankProfiler.Measure("MainPanel.BuildTabGroup", item.Scope, thresholdMs: 1))
                    {
                        item.Build();
                    }

                    _deferredTabGroups.MarkCompleted(item.Key);
                    _layoutController.MarkDirty();
                }
                finally
                {
                    _buildingTab = previousBuildingTab;
                    Container.ResumeLayout(false);
                }

                LayoutMainPanel(force: true);
                RefreshPreview();
                item.AfterBuild?.Invoke();
                break;
            }

            if (_deferredTabGroups.HasPending && !IsDisposed && !Disposing)
                _deferredTabGroups.Start();
        }

        private void BuildActiveTabContentNow()
        {
            if (!_panelsBuilt || IsDisposed || Disposing || _builtTabs.Contains(_activeTab))
                return;

            Container.SuspendLayout();
            try
            {
                _suppressLayoutQueue = true;
                EnsureTabContentBuilt(_activeTab);
                ShowActiveTabContent();
                UpdateTabBarSelection();
                _layoutController.MarkDirty();
            }
            finally
            {
                _suppressLayoutQueue = false;
                Container.ResumeLayout(false);
            }

            LayoutMainPanel(force: true);
            RefreshPreview();
            QueueDeferredTabGroupsIfActive();
        }

        private void EnsureTabContentBuilt(string key)
        {
            string tab = MainPanelTabKeys.Normalize(key);
            if (_builtTabs.Contains(tab))
                return;

            string? previousBuildingTab = _buildingTab;
            _buildingTab = tab;
            try
            {
                using (UiJankProfiler.Measure("MainPanel.BuildTabContent", tab, thresholdMs: 1))
                {
                    MainPanelInitialTabBuildPlan plan = MainPanelInitialTabGroupPlanner.Build(tab);
                    foreach (MainPanelInitialTabGroupPlan group in plan.Groups)
                    {
                        using (UiJankProfiler.Measure("MainPanel.BuildTabGroup", group.Scope, thresholdMs: 1))
                        {
                            _tabGroupBuildRouter.GetInitialGroupBuild(group.Key)();
                        }
                    }

                    _tabGroupBuildRouter.RunInitialFollowUp(plan.FollowUp);
                }

                _builtTabs.Add(tab);
                _layoutController.MarkDirty();
            }
            finally
            {
                _buildingTab = previousBuildingTab;
            }
        }

        private void RunInTabBuildScope(string tab, Action build)
        {
            string? previousBuildingTab = _buildingTab;
            _buildingTab = MainPanelTabKeys.Normalize(tab);
            Container.SuspendLayout();
            try
            {
                build();
            }
            finally
            {
                _buildingTab = previousBuildingTab;
                Container.ResumeLayout(false);
            }
        }

        private void QueueDeferredTabGroupsIfActive()
        {
            _deferredSpecialGroups.QueueFloatAppearanceBuildIfActive();
            _deferredSpecialGroups.QueueTaskbarAdvancedBuildIfActive();

            foreach (var plan in MainPanelDeferredGroupPlanner.BuildSupplementalGroups(_activeTab, _builtTabs))
                QueueDeferredTabGroupBuild(plan.Key, plan.Tab, plan.Scope, _tabGroupBuildRouter.GetSupplementalGroupBuild(plan.Key));
        }

        private void CreateTabBar()
        {
            _tabBar = new MainPanelTabBar(SelectTab);
            _tabBar.Attach(Container);
            UpdateTabBarSelection();
        }

        private void UpdateTabBarSelection()
        {
            _tabBar?.UpdateSelection(_activeTab);
        }

        private void ShowActiveTabContent()
        {
            _tabPanelHost.ShowActiveTabContent(_activeTab);
            _previewController.Show();
            _tabBar?.Show();
        }

        private void InvalidateActiveTabLayoutCache()
        {
            _layoutController.InvalidateActiveTabCache();
        }

        private new Panel AddGroupToPage(LiteSettingsGroup group)
        {
            TrackSettingsGroup(group);
            var wrapper = new Panel
            {
                Dock = DockStyle.None,
                AutoSize = false,
                Padding = new Padding(0, 0, 0, UIUtils.S(20)),
                BackColor = Color.Transparent
            };
            group.SizeChanged += (_, __) =>
            {
                wrapper.Tag = null;
                _layoutController.MarkDirty();
                if (!_buildingPage && !_suppressLayoutQueue && !_layoutController.IsInLayout)
                    QueueLayoutMainPanel();
            };
            string tab = _buildingTab ?? _activeTab;
            Panel tabPanel = _tabPanelHost.EnsureTabPanel(tab);
            tabPanel.SuspendLayout();
            wrapper.SuspendLayout();
            try
            {
                wrapper.Controls.Add(group);
                tabPanel.Controls.Add(wrapper);
            }
            finally
            {
                wrapper.ResumeLayout(false);
                tabPanel.ResumeLayout(false);
            }
            _layoutController.MarkDirty();
            if (!_buildingPage && !_suppressLayoutQueue)
                QueueLayoutMainPanel(force: true);
            return wrapper;
        }

        private void QueueLayoutMainPanel(bool force = false)
        {
            _layoutController.Queue(force);
        }

        private void LayoutMainPanel(bool force = false)
        {
            _layoutController.Layout(force);
        }

        private void CreateFloatBehaviorGroup() => _floatGroupsBuilder.CreateBehaviorGroup();

        private void CreateFloatAppearanceModeGroupShell() => _floatGroupsBuilder.CreateAppearanceModeGroupShell();

        private void CreateFloatAppearanceModeEditor() => _floatGroupsBuilder.CreateAppearanceModeEditor();

        private void CreateFloatAppearanceWidthGroup() => _styleGroupsBuilder.CreateFloatAppearanceWidthGroup();

        private void CreateFloatAppearanceOpacityGroup() => _styleGroupsBuilder.CreateFloatAppearanceOpacityGroup();

        private void CreateFloatAppearanceAdvancedGroup() => _styleGroupsBuilder.CreateFloatAppearanceAdvancedGroup();

        private void CreateFloatLayoutGroup() => _styleGroupsBuilder.CreateFloatLayoutGroup();

        private void CreateTaskbarGeneralGroup() => _taskbarGroupsBuilder.CreateGeneralGroup();

        private void CreateTaskbarAdvancedBehaviorGroup() => _taskbarGroupsBuilder.CreateAdvancedBehaviorGroup();

        private void CreateTaskbarAlignGroupShell()
        {
            _taskbarGroupsBuilder.CreateAlignGroupShell();
            _deferredSpecialGroups.MarkTaskbarAlignShellBuilt();
        }

        private void CreateTaskbarAlignEditor()
        {
            _taskbarGroupsBuilder.CreateAlignEditor();
        }

        private void CreateTaskbarOffsetGroup() => _taskbarGroupsBuilder.CreateOffsetGroup();

        private void CreateTaskbarFontGroup() => _styleGroupsBuilder.CreateTaskbarFontGroup();

        private void CreateTaskbarFontFamilyGroup() => _styleGroupsBuilder.CreateTaskbarFontFamilyGroup();

        private void CreateTaskbarSpacingGroup() => _styleGroupsBuilder.CreateTaskbarSpacingGroup();

        private void CreateTaskbarColorGroup() => _styleGroupsBuilder.CreateTaskbarColorGroup();

        private void CreateTaskbarPresetGroup() => _styleGroupsBuilder.CreateTaskbarPresetGroup();

        private void CreateItemMonitorDisplayGroup() => _monitorGroupsBuilder.CreateItemMonitorDisplayGroup();

        private void CreateItemMonitorColorGroup() => _monitorGroupsBuilder.CreateItemMonitorColorGroup();

        private void CreateInventoryTrendDisplayGroup() => _monitorGroupsBuilder.CreateInventoryTrendDisplayGroup();

        private void CreateInventoryTrendColorGroup() => _monitorGroupsBuilder.CreateInventoryTrendColorGroup();

        private void InvalidateDeferredGroupLayout(LiteSettingsGroup group)
        {
            if (group.Parent is Control wrapper)
            {
                wrapper.Tag = null;
            }

            _layoutController.MarkDirty();
            QueueLayoutMainPanel(force: true);
        }

        private void ApplyTaskbarStylePreset(bool bold)
        {
            _settingsApplier.ApplyTaskbarStylePreset(bold);
            RefreshFromStore();
            RefreshMonitorDisplay();
        }

        private void ApplyPreset(int type)
        {
            _settingsApplier.ApplyTaskbarPreset(type);
            RefreshFromStore();
            RefreshMonitorDisplay();
        }

        private void EnsureSafeVisibility(LiteCheck? hideMainCheck, LiteCheck? showTaskbarCheck)
        {
            bool hideMain = hideMainCheck?.Checked ?? Get(nameof(Settings.HideMainForm), false);
            bool showTaskbar = showTaskbarCheck?.Checked ?? Get(nameof(Settings.ShowTaskbar), true);
            var visibility = _settingsApplier.ResolveSafeVisibility(hideMain, showTaskbar);
            if (!visibility.RequiresCorrection)
                return;

            GlobalPromptService.Show("为了防止程序无法唤出，不能同时隐藏或穿透所有可交互入口。",
                "安全警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (hideMainCheck != null)
            {
                hideMainCheck.Checked = !visibility.HideMainForm;
                Set(nameof(Settings.HideMainForm), visibility.HideMainForm);
            }
            if (showTaskbarCheck != null)
            {
                showTaskbarCheck.Checked = visibility.ShowTaskbar;
                Set(nameof(Settings.ShowTaskbar), visibility.ShowTaskbar);
            }
        }

        private void RefreshMonitorDisplay()
        {
            RefreshPreview();
            _monitorRefreshScheduler.Queue();
        }

        private void RefreshPreview()
        {
            _previewController.Refresh();
        }
    }
}
