using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum MainPanelDeferredSpecialGroupAction
    {
        None,
        FloatAppearanceShell,
        FloatAppearanceModeEditor,
        FloatAppearanceWidth,
        FloatAppearanceOpacity,
        FloatAppearanceAdvanced,
        TaskbarAdvanced,
        TaskbarAlignShell,
        TaskbarAlignEditor,
        TaskbarOffset
    }

    internal static class MainPanelDeferredSpecialGroupPlanner
    {
        public static MainPanelDeferredSpecialGroupAction PickNextFloatAction(
            bool modeShellBuilt,
            bool modeBuilt,
            bool widthBuilt,
            bool opacityBuilt,
            bool advancedBuilt)
        {
            if (!modeBuilt)
                return modeShellBuilt
                    ? MainPanelDeferredSpecialGroupAction.FloatAppearanceModeEditor
                    : MainPanelDeferredSpecialGroupAction.FloatAppearanceShell;

            if (!widthBuilt)
                return MainPanelDeferredSpecialGroupAction.FloatAppearanceWidth;

            if (!opacityBuilt)
                return MainPanelDeferredSpecialGroupAction.FloatAppearanceOpacity;

            return advancedBuilt
                ? MainPanelDeferredSpecialGroupAction.None
                : MainPanelDeferredSpecialGroupAction.FloatAppearanceAdvanced;
        }

        public static MainPanelDeferredSpecialGroupAction PickNextTaskbarAction(
            bool advancedBuilt,
            bool alignShellBuilt,
            bool alignBuilt)
        {
            if (!advancedBuilt)
                return MainPanelDeferredSpecialGroupAction.TaskbarAdvanced;

            if (!alignBuilt)
                return alignShellBuilt
                    ? MainPanelDeferredSpecialGroupAction.TaskbarAlignEditor
                    : MainPanelDeferredSpecialGroupAction.TaskbarAlignShell;

            return MainPanelDeferredSpecialGroupAction.TaskbarOffset;
        }
    }

    internal sealed class MainPanelDeferredSpecialGroupCoordinator : IDisposable
    {
        private readonly Func<bool> _isUsable;
        private readonly Func<bool> _isHandleCreated;
        private readonly Func<bool> _panelsBuilt;
        private readonly Func<string> _activeTab;
        private readonly Func<IReadOnlyCollection<string>> _builtTabs;
        private readonly Action<string, Action> _runInTabBuildScope;
        private readonly Action _markLayoutDirty;
        private readonly Action _layoutMainPanel;
        private readonly Action _layoutAndRefresh;
        private readonly Action _createFloatAppearanceModeGroupShell;
        private readonly Action _createFloatAppearanceModeEditor;
        private readonly Action _createFloatAppearanceWidthGroup;
        private readonly Action _createFloatAppearanceOpacityGroup;
        private readonly Action _createFloatAppearanceAdvancedGroup;
        private readonly Action _createTaskbarAdvancedBehaviorGroup;
        private readonly Action _createTaskbarAlignGroupShell;
        private readonly Action _createTaskbarAlignEditor;
        private readonly Action _createTaskbarOffsetGroup;
        private readonly Action<DeferredTabGroupBuild, int> _queueTabGroupBuild;
        private readonly UiDeferredActionScheduler _deferredActions;
        private bool _floatAppearanceModeShellBuilt;
        private bool _floatAppearanceModeBuilt;
        private bool _floatAppearanceBuilt;
        private bool _floatAppearanceOpacityBuilt;
        private bool _floatAppearanceAdvancedBuilt;
        private bool _taskbarAlignShellBuilt;
        private bool _taskbarAlignBuilt;
        private bool _taskbarAdvancedBuilt;

        public MainPanelDeferredSpecialGroupCoordinator(
            Func<bool> isUsable,
            Func<bool> isHandleCreated,
            Func<bool> panelsBuilt,
            Func<string> activeTab,
            Func<IReadOnlyCollection<string>> builtTabs,
            Action<string, Action> runInTabBuildScope,
            Action markLayoutDirty,
            Action layoutMainPanel,
            Action layoutAndRefresh,
            Action createFloatAppearanceModeGroupShell,
            Action createFloatAppearanceModeEditor,
            Action createFloatAppearanceWidthGroup,
            Action createFloatAppearanceOpacityGroup,
            Action createFloatAppearanceAdvancedGroup,
            Action createTaskbarAdvancedBehaviorGroup,
            Action createTaskbarAlignGroupShell,
            Action createTaskbarAlignEditor,
            Action createTaskbarOffsetGroup,
            Action<DeferredTabGroupBuild, int> queueTabGroupBuild)
        {
            _isUsable = isUsable ?? throw new ArgumentNullException(nameof(isUsable));
            _isHandleCreated = isHandleCreated ?? throw new ArgumentNullException(nameof(isHandleCreated));
            _panelsBuilt = panelsBuilt ?? throw new ArgumentNullException(nameof(panelsBuilt));
            _activeTab = activeTab ?? throw new ArgumentNullException(nameof(activeTab));
            _builtTabs = builtTabs ?? throw new ArgumentNullException(nameof(builtTabs));
            _runInTabBuildScope = runInTabBuildScope ?? throw new ArgumentNullException(nameof(runInTabBuildScope));
            _markLayoutDirty = markLayoutDirty ?? throw new ArgumentNullException(nameof(markLayoutDirty));
            _layoutMainPanel = layoutMainPanel ?? throw new ArgumentNullException(nameof(layoutMainPanel));
            _layoutAndRefresh = layoutAndRefresh ?? throw new ArgumentNullException(nameof(layoutAndRefresh));
            _createFloatAppearanceModeGroupShell = createFloatAppearanceModeGroupShell ?? throw new ArgumentNullException(nameof(createFloatAppearanceModeGroupShell));
            _createFloatAppearanceModeEditor = createFloatAppearanceModeEditor ?? throw new ArgumentNullException(nameof(createFloatAppearanceModeEditor));
            _createFloatAppearanceWidthGroup = createFloatAppearanceWidthGroup ?? throw new ArgumentNullException(nameof(createFloatAppearanceWidthGroup));
            _createFloatAppearanceOpacityGroup = createFloatAppearanceOpacityGroup ?? throw new ArgumentNullException(nameof(createFloatAppearanceOpacityGroup));
            _createFloatAppearanceAdvancedGroup = createFloatAppearanceAdvancedGroup ?? throw new ArgumentNullException(nameof(createFloatAppearanceAdvancedGroup));
            _createTaskbarAdvancedBehaviorGroup = createTaskbarAdvancedBehaviorGroup ?? throw new ArgumentNullException(nameof(createTaskbarAdvancedBehaviorGroup));
            _createTaskbarAlignGroupShell = createTaskbarAlignGroupShell ?? throw new ArgumentNullException(nameof(createTaskbarAlignGroupShell));
            _createTaskbarAlignEditor = createTaskbarAlignEditor ?? throw new ArgumentNullException(nameof(createTaskbarAlignEditor));
            _createTaskbarOffsetGroup = createTaskbarOffsetGroup ?? throw new ArgumentNullException(nameof(createTaskbarOffsetGroup));
            _queueTabGroupBuild = queueTabGroupBuild ?? throw new ArgumentNullException(nameof(queueTabGroupBuild));
            _deferredActions = new UiDeferredActionScheduler(_isUsable);
        }

        public void Reset()
        {
            _floatAppearanceModeShellBuilt = false;
            _floatAppearanceModeBuilt = false;
            _floatAppearanceBuilt = false;
            _floatAppearanceOpacityBuilt = false;
            _floatAppearanceAdvancedBuilt = false;
            _taskbarAlignShellBuilt = false;
            _taskbarAlignBuilt = false;
            _taskbarAdvancedBuilt = false;
        }

        public void MarkTaskbarAlignShellBuilt()
        {
            _taskbarAlignShellBuilt = true;
        }

        public void QueueDeferredFloatAppearanceBuild()
        {
            if (_floatAppearanceModeShellBuilt || !_isUsable() || !_isHandleCreated())
                return;

            _deferredActions.Schedule("float-appearance", 90, BuildDeferredFloatAppearanceNow);
        }

        public void QueueFloatAppearanceBuildIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Float))
                return;

            switch (MainPanelDeferredSpecialGroupPlanner.PickNextFloatAction(
                _floatAppearanceModeShellBuilt,
                _floatAppearanceModeBuilt,
                _floatAppearanceBuilt,
                _floatAppearanceOpacityBuilt,
                _floatAppearanceAdvancedBuilt))
            {
                case MainPanelDeferredSpecialGroupAction.FloatAppearanceShell:
                    QueueDeferredFloatAppearanceBuild();
                    break;
                case MainPanelDeferredSpecialGroupAction.FloatAppearanceModeEditor:
                    QueueDeferredFloatAppearanceModeEditorIfActive();
                    break;
                case MainPanelDeferredSpecialGroupAction.FloatAppearanceWidth:
                    QueueDeferredFloatWidthGroupIfActive();
                    break;
                case MainPanelDeferredSpecialGroupAction.FloatAppearanceOpacity:
                    QueueDeferredFloatAppearanceOpacityBuild();
                    break;
                case MainPanelDeferredSpecialGroupAction.FloatAppearanceAdvanced:
                    QueueDeferredFloatAppearanceAdvancedBuild();
                    break;
            }
        }

        public void QueueTaskbarAdvancedBuildIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Taskbar))
                return;

            switch (MainPanelDeferredSpecialGroupPlanner.PickNextTaskbarAction(
                _taskbarAdvancedBuilt,
                _taskbarAlignShellBuilt,
                _taskbarAlignBuilt))
            {
                case MainPanelDeferredSpecialGroupAction.TaskbarAdvanced:
                    QueueDeferredTaskbarAdvancedBuild();
                    break;
                case MainPanelDeferredSpecialGroupAction.TaskbarAlignShell:
                    QueueDeferredTaskbarAlignShellIfActive();
                    break;
                case MainPanelDeferredSpecialGroupAction.TaskbarAlignEditor:
                    QueueDeferredTaskbarAlignEditorIfActive();
                    break;
                case MainPanelDeferredSpecialGroupAction.TaskbarOffset:
                    QueueDeferredTaskbarOffsetGroupIfActive();
                    break;
            }
        }

        public void QueueDeferredTaskbarAdvancedBuild()
        {
            if (_taskbarAdvancedBuilt || !_isUsable() || !_isHandleCreated())
                return;

            _deferredActions.Schedule("taskbar-advanced", 120, BuildDeferredTaskbarAdvancedNow);
        }

        public void Dispose()
        {
            _deferredActions.Dispose();
        }

        private bool IsActiveBuiltTab(string tab)
        {
            return string.Equals(_activeTab(), tab, StringComparison.OrdinalIgnoreCase)
                && _builtTabs().Contains(tab, StringComparer.OrdinalIgnoreCase);
        }

        private void QueueDeferredTaskbarAlignShellIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Taskbar) || !_taskbarAdvancedBuilt)
                return;

            _queueTabGroupBuild(
                new DeferredTabGroupBuild(
                    "Taskbar.AlignShell",
                    MainPanelTabKeys.Taskbar,
                    "Taskbar.Align",
                    _createTaskbarAlignGroupShell,
                    QueueDeferredTaskbarAlignEditorIfActive),
                90);
        }

        private void QueueDeferredTaskbarAlignEditorIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Taskbar) || !_taskbarAlignShellBuilt)
                return;

            _queueTabGroupBuild(
                new DeferredTabGroupBuild(
                    "Taskbar.AlignEditor",
                    MainPanelTabKeys.Taskbar,
                    "Taskbar.AlignEditor",
                    _createTaskbarAlignEditor,
                    () =>
                    {
                        _taskbarAlignBuilt = true;
                        QueueDeferredTaskbarOffsetGroupIfActive();
                    }),
                90);
        }

        private void QueueDeferredTaskbarOffsetGroupIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Taskbar)
                || !_taskbarAdvancedBuilt
                || !_taskbarAlignBuilt)
            {
                return;
            }

            _queueTabGroupBuild(
                new DeferredTabGroupBuild(
                    "Taskbar.Offset",
                    MainPanelTabKeys.Taskbar,
                    "Taskbar.Offset",
                    _createTaskbarOffsetGroup,
                    null),
                90);
        }

        private void QueueDeferredFloatWidthGroupIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Float))
                return;

            _queueTabGroupBuild(
                new DeferredTabGroupBuild(
                    "Float.Appearance.Width",
                    MainPanelTabKeys.Float,
                    "Float.AppearanceWidth",
                    _createFloatAppearanceWidthGroup,
                    () =>
                    {
                        _floatAppearanceBuilt = true;
                        QueueDeferredFloatAppearanceOpacityBuild();
                    }),
                90);
        }

        private void QueueDeferredFloatAppearanceModeEditorIfActive()
        {
            if (!IsActiveBuiltTab(MainPanelTabKeys.Float) || !_floatAppearanceModeShellBuilt)
                return;

            _queueTabGroupBuild(
                new DeferredTabGroupBuild(
                    "Float.Appearance.ModeEditor",
                    MainPanelTabKeys.Float,
                    "Float.AppearanceMode",
                    _createFloatAppearanceModeEditor,
                    () =>
                    {
                        _floatAppearanceModeBuilt = true;
                        QueueDeferredFloatWidthGroupIfActive();
                    }),
                90);
        }

        private void BuildDeferredTaskbarAdvancedNow()
        {
            if (_taskbarAdvancedBuilt || !_isUsable() || !_panelsBuilt() || !IsTaskbarTabActive())
                return;

            _runInTabBuildScope(MainPanelTabKeys.Taskbar, () =>
            {
                using (UiJankProfiler.Measure("MainPanel.BuildDeferredTaskbarAdvanced", thresholdMs: 1))
                using (UiJankProfiler.Measure("MainPanel.BuildTabGroup", "Taskbar.Advanced", thresholdMs: 1))
                {
                    _createTaskbarAdvancedBehaviorGroup();
                }

                _taskbarAdvancedBuilt = true;
                _markLayoutDirty();
            });

            _layoutAndRefresh();
            QueueTaskbarAdvancedBuildIfActive();
        }

        private void BuildDeferredFloatAppearanceNow()
        {
            if (_floatAppearanceModeShellBuilt || !_isUsable() || !_panelsBuilt() || !IsFloatTabActive())
                return;

            _runInTabBuildScope(MainPanelTabKeys.Float, () =>
            {
                using (UiJankProfiler.Measure("MainPanel.BuildDeferredFloatAppearance", thresholdMs: 1))
                using (UiJankProfiler.Measure("MainPanel.BuildTabGroup", "Float.Appearance", thresholdMs: 1))
                {
                    _createFloatAppearanceModeGroupShell();
                }

                _floatAppearanceModeShellBuilt = true;
                _markLayoutDirty();
            });

            _layoutAndRefresh();
            QueueDeferredFloatAppearanceModeEditorIfActive();
        }

        private void QueueDeferredFloatAppearanceOpacityBuild()
        {
            if (_floatAppearanceOpacityBuilt || !_isUsable() || !_isHandleCreated())
                return;

            _deferredActions.Schedule("float-appearance-opacity", 140, BuildDeferredFloatAppearanceOpacityNow);
        }

        private void BuildDeferredFloatAppearanceOpacityNow()
        {
            if (_floatAppearanceOpacityBuilt || !_isUsable() || !_panelsBuilt() || !IsFloatTabActive())
                return;

            _runInTabBuildScope(MainPanelTabKeys.Float, () =>
            {
                using (UiJankProfiler.Measure("MainPanel.BuildDeferredFloatAppearanceOpacity", thresholdMs: 1))
                using (UiJankProfiler.Measure("MainPanel.BuildTabGroup", "Float.AppearanceOpacity", thresholdMs: 1))
                {
                    _createFloatAppearanceOpacityGroup();
                }

                _floatAppearanceOpacityBuilt = true;
                _markLayoutDirty();
            });

            _layoutAndRefresh();
            QueueDeferredFloatAppearanceAdvancedBuild();
        }

        private void QueueDeferredFloatAppearanceAdvancedBuild()
        {
            if (_floatAppearanceAdvancedBuilt || !_isUsable() || !_isHandleCreated())
                return;

            _deferredActions.Schedule("float-appearance-advanced", 220, BuildDeferredFloatAppearanceAdvancedNow);
        }

        private void BuildDeferredFloatAppearanceAdvancedNow()
        {
            if (_floatAppearanceAdvancedBuilt || !_isUsable() || !_panelsBuilt() || !IsFloatTabActive())
                return;

            _runInTabBuildScope(MainPanelTabKeys.Float, () =>
            {
                using (UiJankProfiler.Measure("MainPanel.BuildDeferredFloatAppearanceAdvanced", thresholdMs: 1))
                using (UiJankProfiler.Measure("MainPanel.BuildTabGroup", "Float.AppearanceAdvanced", thresholdMs: 1))
                {
                    _createFloatAppearanceAdvancedGroup();
                }

                _floatAppearanceAdvancedBuilt = true;
                _markLayoutDirty();
            });

            _layoutMainPanel();
        }

        private bool IsFloatTabActive()
        {
            return string.Equals(_activeTab(), MainPanelTabKeys.Float, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsTaskbarTabActive()
        {
            return string.Equals(_activeTab(), MainPanelTabKeys.Taskbar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
