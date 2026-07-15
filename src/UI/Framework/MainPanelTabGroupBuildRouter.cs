using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class MainPanelTabGroupBuildRouter
    {
        private readonly IReadOnlyDictionary<string, Action> _initialBuilds;
        private readonly IReadOnlyDictionary<string, Action> _supplementalBuilds;
        private readonly Action _queueFloatAppearanceBuild;
        private readonly Action _queueTaskbarAdvancedBuild;

        public MainPanelTabGroupBuildRouter(
            Action createFloatBehaviorGroup,
            Action createFloatLayoutGroup,
            Action createTaskbarGeneralGroup,
            Action createTaskbarFontGroup,
            Action createItemMonitorDisplayGroup,
            Action createInventoryTrendDisplayGroup,
            Action createTaskbarFontFamilyGroup,
            Action createTaskbarSpacingGroup,
            Action createTaskbarColorGroup,
            Action createTaskbarPresetGroup,
            Action createItemMonitorColorGroup,
            Action createInventoryTrendColorGroup,
            Action queueFloatAppearanceBuild,
            Action queueTaskbarAdvancedBuild)
        {
            _initialBuilds = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Taskbar.General"] = createTaskbarGeneralGroup ?? throw new ArgumentNullException(nameof(createTaskbarGeneralGroup)),
                ["Style.Font"] = createTaskbarFontGroup ?? throw new ArgumentNullException(nameof(createTaskbarFontGroup)),
                ["ItemMonitor.Display"] = createItemMonitorDisplayGroup ?? throw new ArgumentNullException(nameof(createItemMonitorDisplayGroup)),
                ["InventoryTrend.Display"] = createInventoryTrendDisplayGroup ?? throw new ArgumentNullException(nameof(createInventoryTrendDisplayGroup)),
                ["Float.Behavior"] = createFloatBehaviorGroup ?? throw new ArgumentNullException(nameof(createFloatBehaviorGroup)),
                ["Float.Layout"] = createFloatLayoutGroup ?? throw new ArgumentNullException(nameof(createFloatLayoutGroup))
            };
            _supplementalBuilds = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
            {
                ["Style.FontFamily"] = createTaskbarFontFamilyGroup ?? throw new ArgumentNullException(nameof(createTaskbarFontFamilyGroup)),
                ["Style.Spacing"] = createTaskbarSpacingGroup ?? throw new ArgumentNullException(nameof(createTaskbarSpacingGroup)),
                ["Style.Color"] = createTaskbarColorGroup ?? throw new ArgumentNullException(nameof(createTaskbarColorGroup)),
                ["Style.Preset"] = createTaskbarPresetGroup ?? throw new ArgumentNullException(nameof(createTaskbarPresetGroup)),
                ["ItemMonitor.Color"] = createItemMonitorColorGroup ?? throw new ArgumentNullException(nameof(createItemMonitorColorGroup)),
                ["InventoryTrend.Color"] = createInventoryTrendColorGroup ?? throw new ArgumentNullException(nameof(createInventoryTrendColorGroup))
            };
            _queueFloatAppearanceBuild = queueFloatAppearanceBuild ?? throw new ArgumentNullException(nameof(queueFloatAppearanceBuild));
            _queueTaskbarAdvancedBuild = queueTaskbarAdvancedBuild ?? throw new ArgumentNullException(nameof(queueTaskbarAdvancedBuild));
        }

        public Action GetInitialGroupBuild(string key)
        {
            if (_initialBuilds.TryGetValue(key, out Action? build))
                return build;

            throw new InvalidOperationException("未知主面板初始组：" + key);
        }

        public Action GetSupplementalGroupBuild(string key)
        {
            if (_supplementalBuilds.TryGetValue(key, out Action? build))
                return build;

            throw new InvalidOperationException("未知主面板延迟组：" + key);
        }

        public void RunInitialFollowUp(MainPanelInitialTabFollowUp followUp)
        {
            switch (followUp)
            {
                case MainPanelInitialTabFollowUp.FloatAppearance:
                    _queueFloatAppearanceBuild();
                    break;
                case MainPanelInitialTabFollowUp.TaskbarAdvanced:
                    _queueTaskbarAdvancedBuild();
                    break;
            }
        }
    }
}
