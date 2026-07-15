using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleActionResultHelper
    {
        public static YouPinSaleActionResult BuildFailedActionResult(string source, int code, string message, string fallback)
        {
            if (YouPinMobileApiClient.IsLoginExpired(code, message))
                return YouPinSaleActionResult.Failed(source + "：悠悠有品登录状态失效，请重新登录。");

            string normalized = NormalizeBusinessFailure(message);
            if (!string.IsNullOrWhiteSpace(normalized))
                return YouPinSaleActionResult.Failed(source + "：" + normalized);

            string text = string.IsNullOrWhiteSpace(message)
                ? $"{source}：{fallback}，悠悠有品返回代码 {code}"
                : $"{source}：{fallback}，{message}";
            return YouPinSaleActionResult.Failed(YouPinMobileApiClient.Sanitize(text));
        }

        public static YouPinSaleActionResult MergeActionFailures(string actionName, params YouPinSaleActionResult[] results)
        {
            var messages = results
                .Where(x => x != null && !x.Ok && !string.IsNullOrWhiteSpace(x.Message))
                .Select(x => x.Message.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToList();

            string detail = messages.Count == 0 ? "两个接口均未成功。" : string.Join("；", messages);
            return YouPinSaleActionResult.Failed($"{actionName}失败：{detail}");
        }

        public static string BuildFriendlyHttpError(HttpResponseMessage resp)
        {
            int statusCode = (int)resp.StatusCode;
            return statusCode switch
            {
                400 => "请求参数被平台拒绝。",
                401 or 403 => "悠悠有品登录状态失效，请重新登录。",
                404 => "平台接口暂不可用。",
                408 => "请求超时，请稍后重试。",
                429 => "请求过于频繁，请稍后重试。",
                >= 500 => "悠悠有品服务器暂时不可用，请稍后重试。",
                _ => $"请求失败，状态码 {statusCode}。"
            };
        }

        public static string BuildFriendlyExceptionMessage(Exception ex, string actionName)
        {
            var wrapped = YouPinMobileApiClient.WrapException(ex, actionName);
            string text = YouPinMobileApiClient.Sanitize(wrapped.Message);
            if (string.IsNullOrWhiteSpace(text))
                return actionName + "失败。";

            return text
                .Replace("HTTP 429 Too Many Requests", "请求过于频繁", StringComparison.OrdinalIgnoreCase)
                .Replace("Too Many Requests", "请求过于频繁", StringComparison.OrdinalIgnoreCase)
                .Replace("Not Found", "接口不存在", StringComparison.OrdinalIgnoreCase)
                .Replace("Unauthorized", "登录状态失效", StringComparison.OrdinalIgnoreCase)
                .Replace("Forbidden", "请求被平台拒绝", StringComparison.OrdinalIgnoreCase)
                .Replace("Bad Request", "请求参数有误", StringComparison.OrdinalIgnoreCase)
                .Replace("Internal Server Error", "服务器内部错误", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeBusinessFailure(string message)
        {
            string text = message?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return "";

            if (text.Contains("状态不能发送报价", StringComparison.Ordinal)
                || text.Contains("不能发送报价", StringComparison.Ordinal))
            {
                return "订单不是待发送状态，可能已进入确认报价，请刷新或点击“确认报价”。";
            }

            if (text.Contains("signature", StringComparison.OrdinalIgnoreCase)
                || text.Contains("签名", StringComparison.Ordinal)
                || text.Contains("风控", StringComparison.Ordinal))
            {
                return "悠悠风控校验失败，当前接口可能需要手机端确认。";
            }

            if (text.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || text.Contains("429", StringComparison.Ordinal))
            {
                return "接口限流，已停止当前操作，请稍后再试。";
            }

            return "";
        }

        public static string BuildActionSuccessMessage(
            string actionName,
            string source,
            string orderNo,
            string tradeOfferId,
            JsonElement root,
            string nextStep)
        {
            if (actionName.Contains("发送报价", StringComparison.Ordinal))
                return "发送报价成功，待您令牌验证。";

            if (actionName.Contains("确认报价", StringComparison.Ordinal))
                return "确认报价成功，请刷新列表确认状态。";

            return string.IsNullOrWhiteSpace(nextStep)
                ? $"{actionName}成功。"
                : $"{actionName}成功，{nextStep.Trim()}。";
        }

        public static string MaskActionId(string? id)
        {
            string value = (id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return "";
            if (value.Length <= 8)
                return "#" + value;
            return "#" + value[^6..];
        }

        public static string BuildOfferStatusText(int status)
        {
            return status switch
            {
                1 => "报价正在发送中。",
                2 => "报价发送处理中。",
                3 => "待您令牌验证，请在 Steam 手机令牌中确认报价。",
                4 => "报价发送失败，请稍后重试或在手机端处理。",
                _ => status > 0 ? $"报价状态：{status}" : "暂无明确报价状态。"
            };
        }

        public static int ResolveOfferStatus(JsonElement data)
        {
            int status = GetInt(data, "status", "Status");
            if (status > 0)
                return status;

            int failed = GetInt(data, "sendFailedNum", "SendFailedNum") + GetInt(data, "confirmFailedNum", "ConfirmFailedNum");
            int succeeded = GetInt(data, "sendSuccessNum", "SendSuccessNum") + GetInt(data, "confirmSuccessNum", "ConfirmSuccessNum");
            int pending = GetInt(data, "offerIngNum", "OfferIngNum") + GetInt(data, "offerNeedTokenNum", "OfferNeedTokenNum");
            if (failed > 0)
                return 4;
            if (succeeded > 0)
                return 3;
            if (pending > 0)
                return 2;
            return 0;
        }

        public static string BuildOfferStatusSummary(JsonElement data)
        {
            var messages = new List<string>();
            AppendOfferMessages(data, messages, "offerMessagesList", "OfferMessagesList");
            AppendOfferMessages(data, messages, "sendOfferMessagesList", "SendOfferMessagesList");
            if (messages.Count > 0)
                return string.Join("；", messages.Take(3));

            int total = GetInt(data, "totalNum", "TotalNum");
            int sent = GetInt(data, "sendSuccessNum", "SendSuccessNum");
            int failed = GetInt(data, "sendFailedNum", "SendFailedNum");
            int confirmed = GetInt(data, "confirmSuccessNum", "ConfirmSuccessNum");
            int confirming = GetInt(data, "offerIngNum", "OfferIngNum");
            int needToken = GetInt(data, "offerNeedTokenNum", "OfferNeedTokenNum");
            if (total <= 0)
                return "";
            if (failed == 0 && confirmed > 0)
                return "确认报价成功。";
            if (failed == 0 && (needToken > 0 || sent > 0))
                return "待您令牌验证，请在 Steam 手机令牌中确认报价。";

            return $"报价状态：共 {total} 单，发送成功 {sent}，发送失败 {failed}，确认成功 {confirmed}，处理中 {confirming}，需令牌 {needToken}。";
        }

        private static void AppendOfferMessages(JsonElement data, List<string> messages, params string[] names)
        {
            if (!TryGetProperty(data, out var list, names) || list.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in list.EnumerateArray())
            {
                string message = GetString(item, "message", "Message", "msg", "Msg") ?? "";
                string count = GetString(item, "count", "Count") ?? "";
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                messages.Add(string.IsNullOrWhiteSpace(count) ? message.Trim() : $"{message.Trim()}：{count.Trim()}");
            }
        }
    }
}
