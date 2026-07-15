using CS2TradeMonitor.src.Core.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class SystemSettingsPageModel
    {
        public static SystemSettingsModuleHealthSummaryViewModel BuildModuleHealthSummary(
            IReadOnlyCollection<MonitorModuleHealth> snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            int abnormalCount = snapshot.Count(health => IsAbnormal(health.State));
            string text = abnormalCount == 0
                ? $"全部正常（{snapshot.Count} 个模块）"
                : $"有异常 {abnormalCount} 项（共 {snapshot.Count} 个模块）";

            return new SystemSettingsModuleHealthSummaryViewModel(text, abnormalCount == 0);
        }

        public static SystemSettingsModuleHealthRowViewModel BuildModuleHealthRow(MonitorModuleHealth health)
        {
            ArgumentNullException.ThrowIfNull(health);

            bool restartEnabled = health.State is MonitorModuleState.Faulted
                or MonitorModuleState.Paused
                or MonitorModuleState.Stopped;

            return new SystemSettingsModuleHealthRowViewModel(
                health.DisplayName,
                FormatModuleState(health),
                health.State,
                health.LastChanged.LocalDateTime.ToString("MM-dd HH:mm:ss"),
                health.ProcessIsolationCandidate ? "高风险，后续可进程隔离" : health.Scope,
                health.IsHighRisk,
                restartEnabled,
                restartEnabled ? "重启" : "正常");
        }

        internal static string FormatModuleState(MonitorModuleHealth health)
        {
            ArgumentNullException.ThrowIfNull(health);

            return health.State switch
            {
                MonitorModuleState.NotStarted => "未启动",
                MonitorModuleState.Starting => "启动中",
                MonitorModuleState.Running => "运行中",
                MonitorModuleState.Paused => "已暂停",
                MonitorModuleState.Faulted => "异常",
                MonitorModuleState.Stopping => "停止中",
                MonitorModuleState.Stopped => "已停止",
                _ => health.Message
            };
        }

        private static bool IsAbnormal(MonitorModuleState state)
        {
            return state is not MonitorModuleState.Running and not MonitorModuleState.Starting;
        }
    }

    internal sealed record SystemSettingsModuleHealthSummaryViewModel(
        string Text,
        bool Ok);

    internal sealed record SystemSettingsModuleHealthRowViewModel(
        string Name,
        string StatusText,
        MonitorModuleState State,
        string ChangedText,
        string DetailText,
        bool DetailWarn,
        bool RestartEnabled,
        string RestartText);
}
