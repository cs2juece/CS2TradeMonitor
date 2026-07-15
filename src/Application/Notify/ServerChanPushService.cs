using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Application.Notify
{
    public sealed class PhoneAlertSendResult
    {
        public bool Success { get; init; }
        public bool Skipped { get; init; }
        public string Message { get; init; } = "";

        public static PhoneAlertSendResult Ok(string message = "测试成功") => new() { Success = true, Message = message };
        public static PhoneAlertSendResult Skip(string message) => new() { Skipped = true, Message = message };
        public static PhoneAlertSendResult Fail(string message) => new() { Message = message };
    }

    public sealed class ServerChanPushService : IServerChanPushService
    {
        public const string ProviderName = "ServerChan";
        public const string HelpUrl = NotificationProviderUrls.ServerChanLogin;

        private static readonly Regex ServerChan3KeyRegex = new(@"^sctp(\d+)t", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Lazy<ServerChanPushService> LazyInstance = new(() => new ServerChanPushService());
        public static ServerChanPushService Instance => LazyInstance.Value;

        private readonly HttpClient _httpClient;

        private ServerChanPushService()
            : this(NotifyRuntimeServices.ResolveDomesticHttpFactory())
        {
        }

        internal ServerChanPushService(IDomesticHttpClientFactory httpFactory)
        {
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            _httpClient = httpFactory.Create(10);
        }

        public static bool IsConfigured(Settings? cfg)
        {
            return cfg != null
                && cfg.PhoneAlertEnabled
                && IsServerChanProvider(cfg.PhoneAlertProvider)
                && !string.IsNullOrWhiteSpace(cfg.ServerChanSendKey);
        }

        public Task<PhoneAlertSendResult> SendConfiguredAsync(Settings cfg, string title, string message, CancellationToken cancellationToken = default)
        {
            if (cfg == null || !cfg.PhoneAlertEnabled)
                return Task.FromResult(PhoneAlertSendResult.Skip("已关闭"));

            if (!IsServerChanProvider(cfg.PhoneAlertProvider))
                return Task.FromResult(PhoneAlertSendResult.Skip("当前推送方式未启用"));

            return SendAsync(cfg.ServerChanSendKey, title, message, cancellationToken);
        }

        public async Task<PhoneAlertSendResult> SendAsync(string? sendKey, string title, string message, CancellationToken cancellationToken = default)
        {
            string key = (sendKey ?? "").Trim();
            if (string.IsNullOrWhiteSpace(key))
                return PhoneAlertSendResult.Skip("请先填写 Server酱 SendKey");

            title = NormalizeTitle(title);
            message = NormalizeMessage(message);
            string shortText = BuildShortText(title, message);

            try
            {
                var result = IsServerChan3Key(key)
                    ? await SendServerChan3Async(key, title, message, shortText, cancellationToken).ConfigureAwait(false)
                    : await SendTurboAsync(key, title, message, shortText, cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailure("timeout");
                return PhoneAlertSendResult.Fail("发送失败：请求超时");
            }
            catch (Exception ex)
            {
                LogFailure(ex.GetType().Name);
                return PhoneAlertSendResult.Fail("发送失败：网络异常");
            }
        }

        public static string MaskSendKey(string? sendKey)
        {
            string value = (sendKey ?? "").Trim();
            if (value.Length == 0) return "";
            if (value.Length <= 12) return value[..Math.Min(4, value.Length)] + "****";

            return value[..Math.Min(7, value.Length)] + "****" + value[^Math.Min(4, value.Length)..];
        }

        private async Task<PhoneAlertSendResult> SendServerChan3Async(string sendKey, string title, string message, string shortText, CancellationToken cancellationToken)
        {
            string url = NotificationProviderUrls.ServerChanFt07Send(sendKey);
            var result = await PostAsync(url, title, message, shortText, includeNoIp: false, cancellationToken).ConfigureAwait(false);
            if (result.Success)
                return result;

            var match = ServerChan3KeyRegex.Match(sendKey);
            if (!match.Success)
                return result;

            string fallbackUrl = NotificationProviderUrls.ServerChanFt07FallbackSend(match.Groups[1].Value, sendKey);
            var fallback = await PostAsync(fallbackUrl, title, message, shortText, includeNoIp: false, cancellationToken).ConfigureAwait(false);
            return fallback.Success ? fallback : result;
        }

        private Task<PhoneAlertSendResult> SendTurboAsync(string sendKey, string title, string message, string shortText, CancellationToken cancellationToken)
        {
            string url = NotificationProviderUrls.ServerChanSctSend(sendKey);
            return PostAsync(url, title, message, shortText, includeNoIp: true, cancellationToken);
        }

        private async Task<PhoneAlertSendResult> PostAsync(string url, string title, string message, string shortText, bool includeNoIp, CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>
            {
                ["title"] = title,
                ["desp"] = message,
                ["short"] = shortText
            };

            if (includeNoIp)
                fields["noip"] = "1";

            using var content = new FormUrlEncodedContent(fields);
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogFailure($"status {(int)response.StatusCode}");
                return PhoneAlertSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
            }

            var parsed = ParseApiResult(body);
            if (!parsed.Success)
            {
                LogFailure(parsed.Code.HasValue ? $"api code {parsed.Code.Value}" : "api returned failure");
                return PhoneAlertSendResult.Fail(string.IsNullOrWhiteSpace(parsed.Message) ? "发送失败：Server酱返回失败" : "发送失败：" + parsed.Message);
            }

            return PhoneAlertSendResult.Ok("测试成功");
        }

        private static bool IsServerChanProvider(string? provider)
        {
            return string.IsNullOrWhiteSpace(provider)
                || string.Equals(provider.Trim(), ProviderName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider.Trim(), "Server酱 SendKey", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsServerChan3Key(string sendKey)
        {
            return sendKey.StartsWith("sctp", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTitle(string? title)
        {
            string value = (title ?? "").Trim();
            return string.IsNullOrWhiteSpace(value) ? "CS2交易监控提醒" : value;
        }

        private static string NormalizeMessage(string? message)
        {
            string value = (message ?? "").Trim();
            return string.IsNullOrWhiteSpace(value) ? "你收到一条来自 CS2交易监控 的提醒。" : value;
        }

        private static string BuildShortText(string title, string message)
        {
            string text = string.IsNullOrWhiteSpace(message) ? title : message;
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 64 ? text : text[..64];
        }

        private static ApiParseResult ParseApiResult(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return new ApiParseResult(false, null, "响应为空");

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                int? code = TryGetCode(root);
                string message = TryGetMessage(root);
                if (code.HasValue)
                    return new ApiParseResult(code.Value == 0, code.Value, message);

                return new ApiParseResult(false, null, string.IsNullOrWhiteSpace(message) ? "无法识别响应" : message);
            }
            catch (JsonException)
            {
                return new ApiParseResult(false, null, "响应格式异常");
            }
        }

        private static int? TryGetCode(JsonElement root)
        {
            foreach (string name in new[] { "code", "errno", "status" })
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
            foreach (string name in new[] { "message", "msg", "errmsg", "error" })
            {
                if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "";
            }

            return "";
        }

        private static void LogFailure(string reason)
        {
            DiagnosticsLogger.Error("ServerChan", "ServerChan send failed: " + DiagnosticsLogger.Redact(reason));
        }

        private readonly record struct ApiParseResult(bool Success, int? Code, string Message);
    }
}
