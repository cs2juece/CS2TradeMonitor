using CS2TradeMonitor.Application.Monitoring;
using CS2TradeMonitor.src.Core.Modules;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum ConsoleActivityFilter
    {
        All,
        Alerts,
        System
    }

    internal enum ConsoleVisualTone
    {
        Neutral,
        Success,
        Info,
        Warning,
        Critical
    }

    internal sealed record ConsoleActivityItem(
        DateTimeOffset OccurredAt,
        string Source,
        string Title,
        string Detail,
        ConsoleVisualTone Tone);

    internal sealed record ConsoleMainLayout(
        Rectangle ActivityBounds,
        Rectangle ReadinessBounds,
        Rectangle UpdatesBounds,
        int HostHeight,
        bool Stacked);

    internal static class ConsolePageModel
    {
        public static ConsoleMainLayout BuildMainLayout(
            int width,
            int wideHeight,
            int horizontalGap,
            int verticalGap,
            int stackThreshold,
            int stackedActivityHeight,
            int stackedReadinessHeight,
            int stackedUpdatesHeight)
        {
            int safeWidth = Math.Max(1, width);
            int safeHorizontalGap = Math.Max(0, horizontalGap);
            int safeVerticalGap = Math.Max(0, verticalGap);
            if (safeWidth < Math.Max(1, stackThreshold))
            {
                int activityHeight = Math.Max(1, stackedActivityHeight);
                int readinessHeight = Math.Max(1, stackedReadinessHeight);
                int updatesHeight = Math.Max(1, stackedUpdatesHeight);
                return new ConsoleMainLayout(
                    new Rectangle(0, 0, safeWidth, activityHeight),
                    new Rectangle(0, activityHeight + safeVerticalGap, safeWidth, readinessHeight),
                    new Rectangle(0, activityHeight + safeVerticalGap + readinessHeight + safeVerticalGap, safeWidth, updatesHeight),
                    activityHeight + readinessHeight + updatesHeight + safeVerticalGap * 2,
                    true);
            }

            int height = Math.Max(1, wideHeight);
            int availableWidth = Math.Max(1, safeWidth - safeHorizontalGap);
            int activityWidth = Math.Clamp((int)Math.Round(availableWidth * 0.512), 1, availableWidth);
            int readinessWidth = Math.Max(1, availableWidth - activityWidth);
            int availableRightHeight = Math.Max(1, height - safeVerticalGap);
            int readinessHeightWide = Math.Clamp((int)Math.Round(availableRightHeight * 0.632), 1, availableRightHeight);
            int updatesHeightWide = Math.Max(1, availableRightHeight - readinessHeightWide);

            return new ConsoleMainLayout(
                new Rectangle(0, 0, activityWidth, height),
                new Rectangle(activityWidth + safeHorizontalGap, 0, readinessWidth, readinessHeightWide),
                new Rectangle(activityWidth + safeHorizontalGap, readinessHeightWide + safeVerticalGap, readinessWidth, updatesHeightWide),
                height,
                false);
        }

        public static IReadOnlyList<ConsoleActivityItem> BuildActivityItems(
            MonitoringConsoleSnapshot snapshot,
            ConsoleActivityFilter filter,
            int limit)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            int boundedLimit = Math.Max(1, limit);

            List<ConsoleActivityItem> alerts = snapshot.RecentAlerts
                .Select(BuildAlertActivity)
                .OrderByDescending(item => item.OccurredAt)
                .ToList();
            List<ConsoleActivityItem> system = snapshot.Modules
                .Select(BuildModuleActivity)
                .OrderByDescending(item => item.OccurredAt)
                .ToList();

            return filter switch
            {
                ConsoleActivityFilter.Alerts => alerts.Take(boundedLimit).ToArray(),
                ConsoleActivityFilter.System => system.Take(boundedLimit).ToArray(),
                _ => BuildBalancedActivityList(alerts, system, boundedLimit)
            };
        }

        public static string BuildHealthSummary(MonitoringConsoleSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            int healthyModules = snapshot.Modules.Count(module => module.IsHealthy);
            int ready = snapshot.AlertReadiness.Count(item => item.State == AlertReadinessState.Ready);
            int waiting = snapshot.AlertReadiness.Count(item => item.State is AlertReadinessState.Waiting or AlertReadinessState.CoolingDown);
            int faulted = snapshot.AlertReadiness.Count(item => item.State == AlertReadinessState.Faulted);

            var parts = new List<string>
            {
                $"{healthyModules} 个监控模块持续工作",
                $"{ready} 项提醒已就绪"
            };
            if (faulted > 0)
                parts.Add($"{faulted} 项异常");
            else if (waiting > 0)
                parts.Add($"{waiting} 项等待条件");
            else
                parts.Add("提醒链路稳定");

            return string.Join("  ·  ", parts);
        }

        public static ConsoleVisualTone ResolveModuleTone(MonitorModuleState state)
        {
            return state switch
            {
                MonitorModuleState.Running => ConsoleVisualTone.Success,
                MonitorModuleState.Starting => ConsoleVisualTone.Info,
                MonitorModuleState.Paused or MonitorModuleState.Stopping => ConsoleVisualTone.Warning,
                MonitorModuleState.Faulted or MonitorModuleState.Stopped => ConsoleVisualTone.Critical,
                _ => ConsoleVisualTone.Neutral
            };
        }

        public static ConsoleVisualTone ResolveReadinessTone(AlertReadinessState state)
        {
            return state switch
            {
                AlertReadinessState.Ready => ConsoleVisualTone.Success,
                AlertReadinessState.Waiting or AlertReadinessState.CoolingDown => ConsoleVisualTone.Warning,
                AlertReadinessState.Faulted => ConsoleVisualTone.Critical,
                _ => ConsoleVisualTone.Neutral
            };
        }

        private static IReadOnlyList<ConsoleActivityItem> BuildBalancedActivityList(
            IReadOnlyList<ConsoleActivityItem> alerts,
            IReadOnlyList<ConsoleActivityItem> system,
            int limit)
        {
            int alertLimit = Math.Min(Math.Min(3, limit), alerts.Count);
            int systemLimit = Math.Min(Math.Max(0, limit - alertLimit), system.Count);
            if (alertLimit + systemLimit < limit)
                alertLimit = Math.Min(alerts.Count, limit - systemLimit);

            return alerts.Take(alertLimit)
                .Concat(system.Take(systemLimit))
                .OrderByDescending(item => item.OccurredAt)
                .Take(limit)
                .ToArray();
        }

        private static ConsoleActivityItem BuildAlertActivity(AlertHistoryEntry entry)
        {
            string source = string.IsNullOrWhiteSpace(entry.Source) ? "提醒" : entry.Source.Trim();
            string title = string.IsNullOrWhiteSpace(entry.Title)
                ? source + ResolveDeliveryVerb(entry.Status)
                : entry.Title.Trim();
            string detail = BuildAlertDetail(entry);
            ConsoleVisualTone tone = entry.Status switch
            {
                AlertDeliveryStatus.Succeeded => ConsoleVisualTone.Success,
                AlertDeliveryStatus.Failed => ConsoleVisualTone.Critical,
                AlertDeliveryStatus.Skipped => ConsoleVisualTone.Warning,
                _ => ConsoleVisualTone.Info
            };
            return new ConsoleActivityItem(entry.OccurredAt, source, title, detail, tone);
        }

        private static ConsoleActivityItem BuildModuleActivity(MonitorModuleHealth health)
        {
            string state = SystemSettingsPageModel.FormatModuleState(health);
            string title = health.DisplayName + state;
            string detail = string.IsNullOrWhiteSpace(health.Message)
                ? "模块状态已同步到控制台。"
                : health.Message.Trim();
            return new ConsoleActivityItem(
                health.LastChanged,
                "系统",
                title,
                detail,
                ResolveModuleTone(health.State));
        }

        private static string BuildAlertDetail(AlertHistoryEntry entry)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Summary))
                parts.Add(entry.Summary.Trim());
            if (!string.IsNullOrWhiteSpace(entry.Detail))
                parts.Add(entry.Detail.Trim());
            if (!string.IsNullOrWhiteSpace(entry.Channel))
                parts.Add(entry.Channel.Trim() + ResolveDeliveryVerb(entry.Status));
            if (parts.Count == 0)
                parts.Add("提醒事件已记录。投递结果：" + ResolveDeliveryVerb(entry.Status).TrimStart('已'));
            return string.Join("  ·  ", parts);
        }

        private static string ResolveDeliveryVerb(AlertDeliveryStatus status)
        {
            return status switch
            {
                AlertDeliveryStatus.Succeeded => "已投递",
                AlertDeliveryStatus.Skipped => "已跳过",
                AlertDeliveryStatus.Failed => "投递失败",
                _ => "已记录"
            };
        }
    }
}
