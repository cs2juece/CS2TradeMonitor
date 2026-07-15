using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum SoftwareUpdateStatusTone
    {
        Neutral,
        Positive,
        Warning,
        Critical
    }

    internal readonly record struct SoftwareUpdateStatusText(string Text, SoftwareUpdateStatusTone Tone);

    internal static class SoftwareUpdateStatusTextFormatter
    {
        public static SoftwareUpdateStatusText Checking() => new("正在检查更新...", SoftwareUpdateStatusTone.Neutral);

        public static SoftwareUpdateStatusText FromResult(SoftwareUpdateCheckResult result)
        {
            ArgumentNullException.ThrowIfNull(result);

            string message = string.IsNullOrWhiteSpace(result.Message) ? "未返回更新状态。" : result.Message.Trim();
            return result.State switch
            {
                SoftwareUpdateState.Available => new($"发现新版本，已打开更新窗口。{message}", SoftwareUpdateStatusTone.Positive),
                SoftwareUpdateState.Latest => new(message, SoftwareUpdateStatusTone.Positive),
                SoftwareUpdateState.Disabled => new(message, SoftwareUpdateStatusTone.Warning),
                _ => new(message, SoftwareUpdateStatusTone.Critical)
            };
        }

        public static SoftwareUpdateStatusText Failed(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return new("检查更新失败：" + SoftwareUpdateService.GetFriendlyError(exception), SoftwareUpdateStatusTone.Critical);
        }
    }
}
