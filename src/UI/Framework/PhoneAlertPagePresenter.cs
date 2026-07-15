using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System.Drawing;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class PhoneAlertPagePresenter
    {
        public static PhoneAlertSummaryViewModel BuildSummary(
            PhoneAlertChannelConfig server,
            bool configured,
            bool enabled,
            string maskedSecret)
        {
            bool failed = !string.IsNullOrWhiteSpace(server.LastTestResult)
                && !string.Equals(server.LastTestResult, "测试成功", StringComparison.OrdinalIgnoreCase);

            var bind = new PhoneAlertTextViewModel(
                configured ? "已绑定" : "未绑定",
                configured ? UIColors.Positive : UIColors.TextWarn);
            var enabledStatus = new PhoneAlertTextViewModel(
                enabled ? "已启用" : "已关闭",
                enabled ? UIColors.Positive : UIColors.TextSub);
            var lastSend = new PhoneAlertTextViewModel(
                PhoneAlertPageModel.FormatLastSend(server),
                server.LastTestTime > 0 ? UIColors.TextMain : UIColors.TextSub);
            var failure = new PhoneAlertTextViewModel(
                PhoneAlertPageModel.BuildFailureReason(server, configured, enabled),
                failed || (enabled && !configured) ? UIColors.TextWarn : UIColors.TextSub);
            var summary = new PhoneAlertTextViewModel(
                configured
                    ? (enabled ? $"已绑定：{maskedSecret}" : "已绑定但未启用，打开“启用”后才会发送手机提醒。")
                    : "未绑定：先扫码绑定并粘贴 SendKey。",
                !configured || (enabled && !configured) ? UIColors.TextWarn : UIColors.TextSub);

            return new PhoneAlertSummaryViewModel(
                bind,
                enabledStatus,
                lastSend,
                failure,
                summary,
                BuildSecretConfigStatus(configured));
        }

        public static PhoneAlertTextViewModel BuildSecretConfigStatus(bool configured)
        {
            return new PhoneAlertTextViewModel(
                configured ? "SendKey 已填写，已隐藏显示" : "SendKey 未填写",
                configured ? UIColors.TextSub : UIColors.TextWarn);
        }

        public static PhoneAlertTextViewModel BuildChannelStatus(
            PhoneAlertChannelConfig channel,
            bool configured,
            string maskedSecret)
        {
            string text = configured ? $"已配置：{maskedSecret}" : "未配置完整";
            if (!string.IsNullOrWhiteSpace(channel.LastTestResult))
                text += $"；上次测试：{channel.LastTestResult}";

            return new PhoneAlertTextViewModel(text, configured ? UIColors.Positive : UIColors.TextSub);
        }

        public static PhoneAlertTextViewModel BuildTestResultStatus(
            PhoneAlertChannelConfig channel,
            PhoneAlertSendResult result,
            string maskedSecret)
        {
            string text = result.Success
                ? "测试成功：" + maskedSecret
                : (string.IsNullOrWhiteSpace(result.Message) ? "发送失败" : result.Message);
            return new PhoneAlertTextViewModel(text, result.Success ? UIColors.Positive : UIColors.TextWarn);
        }
    }

    internal sealed record PhoneAlertSummaryViewModel(
        PhoneAlertTextViewModel BindStatus,
        PhoneAlertTextViewModel EnabledStatus,
        PhoneAlertTextViewModel LastSend,
        PhoneAlertTextViewModel Failure,
        PhoneAlertTextViewModel Summary,
        PhoneAlertTextViewModel SecretConfig);

    internal sealed record PhoneAlertTextViewModel(string Text, Color Color);
}
