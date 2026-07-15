using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Notify
{
    internal interface IPhoneAlertProvider
    {
        PhoneAlertChannelType Type { get; }
        Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default);
    }

    public sealed class PhoneAlertChannelTestResult
    {
        public PhoneAlertChannelConfig Channel { get; init; } = new();
        public PhoneAlertSendResult Result { get; init; } = PhoneAlertSendResult.Skip("未测试");
    }

    public sealed class PhoneAlertDispatchService : IPhoneAlertDispatchService
    {
        private static readonly Lazy<PhoneAlertDispatchService> LazyInstance = new(() => new PhoneAlertDispatchService());
        public static PhoneAlertDispatchService Instance => LazyInstance.Value;

        private readonly Dictionary<PhoneAlertChannelType, IPhoneAlertProvider> _providers;

        private PhoneAlertDispatchService()
            : this(NotifyRuntimeServices.Resolve())
        {
        }

        internal PhoneAlertDispatchService(NotifyRuntimeServices runtimeServices)
            : this(
                runtimeServices.ServerChanPush,
                runtimeServices.WxPusher,
                runtimeServices.DomesticHttpFactory)
        {
        }

        internal PhoneAlertDispatchService(
            IServerChanPushService serverChanPushService,
            IWxPusherService wxPusherService,
            IDomesticHttpClientFactory httpFactory)
        {
            if (serverChanPushService == null) throw new ArgumentNullException(nameof(serverChanPushService));
            if (wxPusherService == null) throw new ArgumentNullException(nameof(wxPusherService));
            if (httpFactory == null) throw new ArgumentNullException(nameof(httpFactory));

            var http = httpFactory.Create(8, useCookies: false);

            var providers = new IPhoneAlertProvider[]
            {
                new ServerChanAlertProvider(serverChanPushService),
                new WxPusherAlertProvider(wxPusherService),
                new PushPlusAlertProvider(http),
                new BarkAlertProvider(http),
                new GotifyAlertProvider(http),
                new TelegramAlertProvider(http),
                new WebhookAlertProvider(http)
            };

            _providers = providers.ToDictionary(p => p.Type, p => p);
        }

        public static bool IsConfigured(Settings? cfg)
        {
            return cfg != null
                && cfg.PhoneAlertEnabled
                && cfg.PhoneAlertChannels != null
                && cfg.PhoneAlertChannels.Any(c => c.Enabled && Instance.IsChannelConfigured(c));
        }

        bool IPhoneAlertDispatchService.IsConfigured(Settings? cfg)
        {
            return IsConfigured(cfg);
        }

        public string GetHelpUrl(PhoneAlertChannelType type)
        {
            return Enum.IsDefined(typeof(PhoneAlertChannelType), type)
                ? PhoneAlertChannelDefinitionCatalog.Get(type).HelpUrl
                : "";
        }

        public string MaskSecret(PhoneAlertChannelConfig channel)
        {
            return PhoneAlertChannelDefinitionCatalog.MaskSecret(channel);
        }

        public bool IsChannelConfigured(PhoneAlertChannelConfig? channel)
        {
            return channel != null
                && _providers.ContainsKey(channel.Type)
                && PhoneAlertChannelDefinitionCatalog.IsConfigured(channel);
        }

        public async Task<PhoneAlertSendResult> SendConfiguredAsync(Settings cfg, string title, string message, CancellationToken cancellationToken = default)
        {
            if (cfg == null || !cfg.PhoneAlertEnabled)
                return PhoneAlertSendResult.Skip("手机提醒已关闭");

            var channels = GetEnabledConfiguredChannels(cfg).ToList();
            if (channels.Count == 0)
                return PhoneAlertSendResult.Skip("没有已启用且配置完整的手机提醒通道");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            return cfg.PhoneAlertDispatchMode switch
            {
                PhoneAlertDispatchMode.SendAll => await SendAllAsync(channels, title, message, timeout.Token).ConfigureAwait(false),
                PhoneAlertDispatchMode.PrimaryOnly => await SendFirstAsync(channels.Take(1), title, message, timeout.Token, stopOnSuccess: true).ConfigureAwait(false),
                _ => await SendFirstAsync(channels, title, message, timeout.Token, stopOnSuccess: true).ConfigureAwait(false)
            };
        }

        public async Task<PhoneAlertSendResult> SendChannelAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
        {
            if (!_providers.TryGetValue(channel.Type, out var provider))
                return PhoneAlertSendResult.Fail("不支持的手机提醒通道");

            if (!PhoneAlertChannelDefinitionCatalog.IsConfigured(channel))
                return PhoneAlertSendResult.Skip("通道配置不完整");

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                return await provider.SendAsync(channel, title, message, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogProviderFailure(channel.Type, "timeout");
                return PhoneAlertSendResult.Fail("发送失败：请求超时");
            }
            catch (Exception ex)
            {
                LogProviderFailure(channel.Type, ex.GetType().Name);
                return PhoneAlertSendResult.Fail("发送失败：网络异常");
            }
        }

        public async Task<List<PhoneAlertChannelTestResult>> TestAllEnabledAsync(Settings cfg, string title, string message, CancellationToken cancellationToken = default)
        {
            var result = new List<PhoneAlertChannelTestResult>();
            if (cfg?.PhoneAlertChannels == null)
                return result;

            foreach (var channel in cfg.PhoneAlertChannels.OrderBy(c => c.Priority))
            {
                if (!channel.Enabled)
                    continue;

                var send = await SendChannelAsync(channel, title, message, cancellationToken).ConfigureAwait(false);
                result.Add(new PhoneAlertChannelTestResult { Channel = channel, Result = send });
            }

            return result;
        }

        private IEnumerable<PhoneAlertChannelConfig> GetEnabledConfiguredChannels(Settings cfg)
        {
            return (cfg.PhoneAlertChannels ?? new List<PhoneAlertChannelConfig>())
                .Where(c => c.Enabled && IsChannelConfigured(c))
                .OrderBy(c => c.Priority);
        }

        private async Task<PhoneAlertSendResult> SendFirstAsync(IEnumerable<PhoneAlertChannelConfig> channels, string title, string message, CancellationToken cancellationToken, bool stopOnSuccess)
        {
            var failures = new List<string>();
            foreach (var channel in channels)
            {
                var result = await SendChannelAsync(channel, title, message, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                    return PhoneAlertSendResult.Ok($"{GetChannelName(channel)} 发送成功");

                if (!result.Skipped && !string.IsNullOrWhiteSpace(result.Message))
                    failures.Add($"{GetChannelName(channel)}：{result.Message}");

                if (!stopOnSuccess)
                    continue;
            }

            return failures.Count == 0
                ? PhoneAlertSendResult.Skip("没有可发送的手机提醒通道")
                : PhoneAlertSendResult.Fail("全部手机提醒通道发送失败：" + string.Join("；", failures.Take(3)));
        }

        private async Task<PhoneAlertSendResult> SendAllAsync(List<PhoneAlertChannelConfig> channels, string title, string message, CancellationToken cancellationToken)
        {
            int success = 0;
            var failures = new List<string>();

            foreach (var channel in channels)
            {
                var result = await SendChannelAsync(channel, title, message, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                    success++;
                else if (!result.Skipped)
                    failures.Add($"{GetChannelName(channel)}：{result.Message}");
            }

            if (success > 0)
                return PhoneAlertSendResult.Ok($"手机提醒已发送：成功 {success} 个通道");

            return failures.Count == 0
                ? PhoneAlertSendResult.Skip("没有可发送的手机提醒通道")
                : PhoneAlertSendResult.Fail("全部手机提醒通道发送失败：" + string.Join("；", failures.Take(3)));
        }

        private static string GetChannelName(PhoneAlertChannelConfig channel)
        {
            return string.IsNullOrWhiteSpace(channel.DisplayName) ? channel.Type.ToString() : channel.DisplayName.Trim();
        }

        private static void LogProviderFailure(PhoneAlertChannelType type, string reason)
        {
            DiagnosticsLogger.Error("PhoneAlert", $"{type} send failed: {DiagnosticsLogger.Redact(reason)}");
        }

        private abstract class HttpPhoneAlertProvider : IPhoneAlertProvider
        {
            protected HttpPhoneAlertProvider(HttpClient http)
            {
                Http = http ?? throw new ArgumentNullException(nameof(http));
            }

            protected HttpClient Http { get; }

            public abstract PhoneAlertChannelType Type { get; }
            public abstract Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default);

            protected static string Title(string? title)
            {
                string value = (title ?? "").Trim();
                return string.IsNullOrWhiteSpace(value) ? "CS2交易监控提醒" : value;
            }

            protected static string Message(string? message)
            {
                string value = (message ?? "").Trim();
                return string.IsNullOrWhiteSpace(value) ? "你收到一条来自 CS2交易监控 的提醒。" : value;
            }

            protected static string Short(string title, string message)
            {
                string text = string.IsNullOrWhiteSpace(message) ? title : message;
                text = text.Replace("\r", " ").Replace("\n", " ").Trim();
                return text.Length <= 64 ? text : text[..64];
            }

            protected static string BaseUrl(string? raw, string fallback = "")
            {
                string url = (raw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(url))
                    url = fallback;
                return url.TrimEnd('/');
            }

            protected static PhoneAlertSendResult ParseSuccessCode(string body, params int[] successCodes)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("code", out var codeProp))
                    {
                        int code = codeProp.ValueKind == JsonValueKind.Number
                            ? codeProp.GetInt32()
                            : int.TryParse(codeProp.GetString(), out var parsed) ? parsed : int.MinValue;
                        if (successCodes.Contains(code))
                            return PhoneAlertSendResult.Ok("测试成功");

                        return PhoneAlertSendResult.Fail("发送失败：服务返回 " + code);
                    }

                    if (root.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True)
                        return PhoneAlertSendResult.Ok("测试成功");
                }
                catch (JsonException)
                {
                    return PhoneAlertSendResult.Fail("发送失败：响应格式异常");
                }

                return PhoneAlertSendResult.Fail("发送失败：服务返回失败");
            }
        }

        private sealed class ServerChanAlertProvider : IPhoneAlertProvider
        {
            private readonly IServerChanPushService _pushService;

            public ServerChanAlertProvider(IServerChanPushService pushService)
            {
                _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
            }

            public PhoneAlertChannelType Type => PhoneAlertChannelType.ServerChan;
            public Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
                => _pushService.SendAsync(channel.Secret, title, message, cancellationToken);
        }

        private sealed class WxPusherAlertProvider : IPhoneAlertProvider
        {
            private readonly IWxPusherService _pushService;

            public WxPusherAlertProvider(IWxPusherService pushService)
            {
                _pushService = pushService ?? throw new ArgumentNullException(nameof(pushService));
            }

            public PhoneAlertChannelType Type => PhoneAlertChannelType.WxPusher;

            public async Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
            {
                var result = await _pushService.SendAsync(channel.Secret, title, message, cancellationToken).ConfigureAwait(false);
                if (result.Success) return PhoneAlertSendResult.Ok(result.Message);
                return result.Skipped ? PhoneAlertSendResult.Skip(result.Message) : PhoneAlertSendResult.Fail(result.Message);
            }
        }

        private sealed class PushPlusAlertProvider : HttpPhoneAlertProvider
        {
            public PushPlusAlertProvider(HttpClient http)
                : base(http)
            {
            }

            public override PhoneAlertChannelType Type => PhoneAlertChannelType.PushPlus;

            public override async Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
            {
                var payload = JsonSerializer.Serialize(new
                {
                    token = channel.Secret.Trim(),
                    title = Title(title),
                    content = Message(message),
                    template = "txt"
                });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(NotificationProviderUrls.PushPlusSend, content, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return PhoneAlertSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
                return ParseSuccessCode(body, 0, 200);
            }
        }

        private sealed class BarkAlertProvider : HttpPhoneAlertProvider
        {
            public BarkAlertProvider(HttpClient http)
                : base(http)
            {
            }

            public override PhoneAlertChannelType Type => PhoneAlertChannelType.Bark;

            public override async Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
            {
                string url = $"{BaseUrl(channel.ServerUrl, NotificationProviderUrls.BarkDefaultServer)}/{Uri.EscapeDataString(channel.Secret.Trim())}";
                var payload = JsonSerializer.Serialize(new { title = Title(title), body = Message(message) });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return PhoneAlertSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
                return string.IsNullOrWhiteSpace(body) ? PhoneAlertSendResult.Ok("测试成功") : ParseSuccessCode(body, 0, 200);
            }
        }

        private sealed class GotifyAlertProvider : HttpPhoneAlertProvider
        {
            public GotifyAlertProvider(HttpClient http)
                : base(http)
            {
            }

            public override PhoneAlertChannelType Type => PhoneAlertChannelType.Gotify;

            public override async Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
            {
                string url = $"{BaseUrl(channel.ServerUrl)}/message?token={Uri.EscapeDataString(channel.Secret.Trim())}";
                var payload = JsonSerializer.Serialize(new { title = Title(title), message = Message(message), priority = 5 });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return PhoneAlertSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
                return PhoneAlertSendResult.Ok("测试成功");
            }
        }

        private sealed class TelegramAlertProvider : HttpPhoneAlertProvider
        {
            public TelegramAlertProvider(HttpClient http)
                : base(http)
            {
            }

            public override PhoneAlertChannelType Type => PhoneAlertChannelType.Telegram;

            public override async Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
            {
                string url = NotificationProviderUrls.TelegramSendMessage(channel.Secret.Trim());
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = channel.Extra.Trim(),
                    ["text"] = Title(title) + "\n\n" + Message(message),
                    ["disable_web_page_preview"] = "true"
                });
                using var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return PhoneAlertSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
                return ParseSuccessCode(body, 0);
            }
        }

        private sealed class WebhookAlertProvider : HttpPhoneAlertProvider
        {
            public WebhookAlertProvider(HttpClient http)
                : base(http)
            {
            }

            public override PhoneAlertChannelType Type => PhoneAlertChannelType.Webhook;

            public override async Task<PhoneAlertSendResult> SendAsync(PhoneAlertChannelConfig channel, string title, string message, CancellationToken cancellationToken = default)
            {
                string payload = string.IsNullOrWhiteSpace(channel.Extra)
                    ? JsonSerializer.Serialize(new { title = Title(title), message = Message(message) })
                    : channel.Extra.Replace("{title}", EscapeJsonFragment(Title(title))).Replace("{message}", EscapeJsonFragment(Message(message)));

                using var request = new HttpRequestMessage(HttpMethod.Post, channel.ServerUrl.Trim());
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(channel.Secret))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channel.Secret.Trim());

                using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return PhoneAlertSendResult.Fail($"发送失败：HTTP {(int)response.StatusCode}");
                return PhoneAlertSendResult.Ok("测试成功");
            }

            private static string EscapeJsonFragment(string value)
            {
                string json = JsonSerializer.Serialize(value);
                return json.Length >= 2 ? json[1..^1] : value;
            }
        }

    }
}
