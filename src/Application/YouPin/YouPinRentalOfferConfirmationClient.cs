using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleActionResultHelper;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleReminderHttpHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinRentalOfferConfirmationClient
    {
        internal const string ConfirmEndpoint = "/api/youpin/bff/order/offer/confirm";
        internal const string ConfirmResultEndpoint = "/api/youpin/bff/order/offer/confirm/result";
        private static readonly TimeSpan DefaultPollDelay = TimeSpan.FromMilliseconds(1800);
        private readonly HttpClient _http;

        public YouPinRentalOfferConfirmationClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        internal Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; }
            = static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);

        internal int PollAttempts { get; set; } = 4;

        public async Task<YouPinSaleActionResult> ConfirmAsync(
            string orderNo,
            string tradeOfferId,
            YouPinCredential credential,
            CancellationToken cancellationToken = default)
        {
            string normalizedOrderNo = (orderNo ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalizedOrderNo))
                return YouPinSaleActionResult.Failed("确认报价：订单号为空。");

            try
            {
                string requestId = await SubmitAsync(normalizedOrderNo, credential, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestId))
                    return YouPinSaleActionResult.Failed("确认报价：悠悠未返回确认请求号，请刷新订单状态后重试。");

                int attempts = Math.Max(1, PollAttempts);
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    await DelayAsync(DefaultPollDelay, cancellationToken).ConfigureAwait(false);
                    YouPinSaleActionResult? result = await PollAsync(
                        normalizedOrderNo,
                        requestId,
                        tradeOfferId,
                        credential,
                        cancellationToken).ConfigureAwait(false);
                    if (result != null)
                        return result;
                }

                return YouPinSaleActionResult.Failed("确认报价：悠悠已接收请求，但尚未返回确认结果，稍后将重新同步订单状态。");
            }
            catch (Exception ex)
            {
                return YouPinSaleActionResult.Failed("确认报价：" + BuildFriendlyExceptionMessage(ex, "确认悠悠租赁报价"));
            }
        }

        private async Task<string> SubmitAsync(
            string orderNo,
            YouPinCredential credential,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinMobileApiClient.BaseUrl + ConfirmEndpoint);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                orderId = orderNo,
                Sessionid = credential.DeviceToken
            });
            ApplyYouPinHeaders(request, credential.Token, credential.DeviceToken, credential.Uk);

            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "提交悠悠租赁确认报价",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(BuildFriendlyHttpError(response));

            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "提交悠悠租赁确认报价").ConfigureAwait(false);
            JsonElement root = document.RootElement;
            int code = GetInt(root, "code", "Code");
            string message = GetString(root, "msg", "Msg", "message", "Message") ?? "";
            if (code != 0)
                throw new YouPinRentalConfirmationException(BuildFailedActionResult("确认报价", code, message, "提交失败").Message);

            return TryGetProperty(root, out JsonElement data, "data", "Data")
                ? GetString(data, "requestId", "RequestId") ?? ""
                : "";
        }

        private async Task<YouPinSaleActionResult?> PollAsync(
            string orderNo,
            string requestId,
            string tradeOfferId,
            YouPinCredential credential,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinMobileApiClient.BaseUrl + ConfirmResultEndpoint);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                orderId = orderNo,
                requestId,
                Sessionid = credential.DeviceToken
            });
            ApplyYouPinHeaders(request, credential.Token, credential.DeviceToken, credential.Uk);

            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "查询悠悠租赁确认报价结果",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return YouPinSaleActionResult.Failed("确认报价结果：" + BuildFriendlyHttpError(response));

            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "查询悠悠租赁确认报价结果").ConfigureAwait(false);
            JsonElement root = document.RootElement;
            int code = GetInt(root, "code", "Code");
            string message = GetString(root, "msg", "Msg", "message", "Message") ?? "";
            if (code != 0)
                return BuildFailedActionResult("确认报价结果", code, message, "查询失败");

            if (!TryGetProperty(root, out JsonElement data, "data", "Data"))
                return YouPinSaleActionResult.Failed("确认报价结果：悠悠未返回结果数据。");

            int resultType = GetInt(data, "resultType", "ResultType");
            if (resultType == 0)
                return null;

            string title = GetString(data, "resultTitle", "ResultTitle") ?? "";
            string content = GetString(data, "resultContent", "ResultContent") ?? "";
            string resultMessage = FirstText(title, content, message);
            if (resultType == 2)
            {
                return YouPinSaleActionResult.Success(
                    string.IsNullOrWhiteSpace(resultMessage)
                        ? "悠悠已确认租赁报价，下一步处理 Steam 报价。"
                        : "悠悠已确认租赁报价：" + Sanitize(resultMessage),
                    tradeOfferId,
                    resultType);
            }

            return YouPinSaleActionResult.Failed(
                string.IsNullOrWhiteSpace(resultMessage)
                    ? $"确认报价结果：悠悠返回未支持的结果状态 {resultType}。"
                    : "确认报价结果：" + Sanitize(resultMessage));
        }

        private sealed class YouPinRentalConfirmationException : InvalidOperationException
        {
            public YouPinRentalConfirmationException(string message)
                : base(message)
            {
            }
        }
    }
}
