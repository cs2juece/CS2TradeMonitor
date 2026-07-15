using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class PhoneAlertPageModel
    {
        public static string FormatLastSend(PhoneAlertChannelConfig channel)
        {
            if (channel.LastTestTime <= 0)
                return "暂无";
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(channel.LastTestTime).LocalDateTime.ToString("MM-dd HH:mm");
            }
            catch
            {
                return "暂无";
            }
        }

        public static string BuildFailureReason(PhoneAlertChannelConfig server, bool configured, bool enabled)
        {
            if (enabled && !configured)
                return "未填写 SendKey";
            if (!string.IsNullOrWhiteSpace(server.LastTestResult)
                && !string.Equals(server.LastTestResult, "测试成功", StringComparison.OrdinalIgnoreCase))
                return server.LastTestResult;
            return "无";
        }

        public static IReadOnlyList<string> GetStrategyTexts()
        {
            return new[] { "主通道失败后尝试备用", "所有已启用通道都发送", "只发送主通道" };
        }

        public static int DispatchModeToIndex(PhoneAlertDispatchMode mode)
        {
            return mode switch
            {
                PhoneAlertDispatchMode.SendAll => 1,
                PhoneAlertDispatchMode.PrimaryOnly => 2,
                _ => 0
            };
        }

        public static PhoneAlertDispatchMode IndexToDispatchMode(int index)
        {
            return index switch
            {
                1 => PhoneAlertDispatchMode.SendAll,
                2 => PhoneAlertDispatchMode.PrimaryOnly,
                _ => PhoneAlertDispatchMode.Failover
            };
        }

    }
}
