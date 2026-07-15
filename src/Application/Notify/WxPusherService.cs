using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Notify
{
    public sealed class WxPusherSendResult
    {
        public bool Success { get; init; }
        public bool Skipped { get; init; }
        public string Message { get; init; } = "";

        public static WxPusherSendResult Ok(string message = "测试成功") => new() { Success = true, Message = message };
        public static WxPusherSendResult Skip(string message) => new() { Skipped = true, Message = message };
        public static WxPusherSendResult Fail(string message) => new() { Message = message };
    }

    public sealed class WxPusherService : IWxPusherService
    {
        private const string SendEndpointPrefix = NotificationProviderUrls.WxPusherSendMessagePrefix;
        public const string SptHelpUrl = NotificationProviderUrls.WxPusherDocs;

        private static readonly Lazy<WxPusherService> LazyInstance = new(() => new WxPusherService());
        public static WxPusherService Instance => LazyInstance.Value;

        private readonly HttpClient _httpClient;

        private WxPusherService()
            : this(NotifyRuntimeServices.ResolveDomesticHttpFactory())
        {
        }

        internal WxPusherService(IDomesticHttpClientFactory httpFactory)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            _httpClient = httpFactory.Create(10);
        }

        public Task<WxPusherSendResult> SendConfiguredAsync(Settings cfg, string title, string message, CancellationToken cancellationToken = default)
        {
            if (cfg == null || !cfg.PhoneAlertEnabled)
                return Task.FromResult(WxPusherSendResult.Skip("已关闭"));

            return SendAsync(cfg.WxPusherSpt, title, message, cancellationToken);
        }

        public async Task<WxPusherSendResult> SendAsync(string? spt, string title, string message, CancellationToken cancellationToken = default)
        {
            string token = (spt ?? "").Trim();
            if (string.IsNullOrWhiteSpace(token))
                return WxPusherSendResult.Skip("请先填写 WxPusher SPT 提醒码");

            string content = BuildContent(title, message);
            string url = SendEndpointPrefix
                         + Uri.EscapeDataString(token)
                         + "/"
                         + Uri.EscapeDataString(content);

            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    LogFailure($"status {(int)response.StatusCode}");
                    return WxPusherSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
                }

                var apiResult = ParseApiResult(body);
                if (!apiResult.Success)
                {
                    string suffix = string.IsNullOrWhiteSpace(apiResult.Message) ? "" : "：" + apiResult.Message;
                    LogFailure(apiResult.Code.HasValue ? $"api code {apiResult.Code.Value}" : "api returned failure");
                    return WxPusherSendResult.Fail("发送失败" + suffix);
                }

                return WxPusherSendResult.Ok("测试成功");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailure("timeout");
                return WxPusherSendResult.Fail("发送失败：请求超时");
            }
            catch (Exception ex)
            {
                LogFailure(ex.GetType().Name);
                return WxPusherSendResult.Fail("发送失败：网络异常");
            }
        }

        public static string MaskSpt(string? spt)
        {
            string value = (spt ?? "").Trim();
            if (value.Length == 0) return "";
            if (value.Length <= 12) return value[..Math.Min(4, value.Length)] + "****";

            return value[..Math.Min(8, value.Length)] + "****" + value[^Math.Min(4, value.Length)..];
        }

        private static string BuildContent(string title, string message)
        {
            title = (title ?? "").Trim();
            message = (message ?? "").Trim();

            if (string.IsNullOrWhiteSpace(title))
                return message;
            if (string.IsNullOrWhiteSpace(message))
                return title;

            return title + Environment.NewLine + message;
        }

        private static ApiParseResult ParseApiResult(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return new ApiParseResult(true, null, "");

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (TryGetBool(root, "success", out bool success))
                    return new ApiParseResult(success, TryGetCode(root), TryGetMessage(root));

                int? code = TryGetCode(root);
                if (code.HasValue)
                    return new ApiParseResult(code.Value == 1000 || code.Value == 0, code.Value, TryGetMessage(root));
            }
            catch (JsonException)
            {
                // Some simple endpoints can return plain text; HTTP 2xx is enough there.
            }

            return new ApiParseResult(true, null, "");
        }

        private static bool TryGetBool(JsonElement root, string name, out bool value)
        {
            value = false;
            if (!root.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False)
                return false;

            value = prop.GetBoolean();
            return true;
        }

        private static int? TryGetCode(JsonElement root)
        {
            foreach (string name in new[] { "code", "status" })
            {
                if (!root.TryGetProperty(name, out var prop))
                    continue;

                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int code))
                    return code;

                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out code))
                    return code;
            }

            return null;
        }

        private static string TryGetMessage(JsonElement root)
        {
            foreach (string name in new[] { "msg", "message", "error" })
            {
                if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return WebUtility.HtmlDecode(prop.GetString() ?? "");
            }

            return "";
        }

        private static void LogFailure(string reason)
        {
            DiagnosticsLogger.Error("WxPusher", "WxPusher send failed: " + DiagnosticsLogger.Redact(reason));
        }

        private readonly record struct ApiParseResult(bool Success, int? Code, string Message);
    }
}
