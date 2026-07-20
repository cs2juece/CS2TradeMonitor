using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal readonly record struct QuantResearchEntryHeroLayout(
        Rectangle Eyebrow,
        Rectangle Title,
        Rectangle Description,
        Rectangle Status,
        Rectangle StatusDetail,
        Rectangle Address,
        Rectangle StartButton,
        Rectangle OpenButton,
        Rectangle RefreshButton,
        Rectangle CopyButton,
        int RequiredHeight);

    internal static class QuantResearchEntryHeroLayoutModel
    {
        internal static QuantResearchEntryHeroLayout Build(
            Size clientSize,
            int visibleWidth,
            Size startButtonSize,
            Size openButtonSize,
            Size refreshButtonSize,
            Size copyButtonSize,
            float scaleFactor)
        {
            float scale = scaleFactor > 0F ? scaleFactor : 1F;
            int S(int value) => (int)(value * scale);

            int left = S(4);
            int right = S(4);
            int effectiveWidth = Math.Max(1, Math.Min(clientSize.Width, visibleWidth) - left - right);
            int gap = S(8);
            int actionsTop = S(190);
            int totalActionWidth = startButtonSize.Width
                + openButtonSize.Width
                + refreshButtonSize.Width
                + copyButtonSize.Width
                + (gap * 3);
            bool wrapActions = totalActionWidth > effectiveWidth;
            var startButton = new Rectangle(
                left,
                actionsTop,
                startButtonSize.Width,
                startButtonSize.Height);
            var openButton = new Rectangle(
                startButton.Right + gap,
                actionsTop,
                openButtonSize.Width,
                openButtonSize.Height);
            var refreshButton = new Rectangle(
                wrapActions ? left : openButton.Right + gap,
                wrapActions ? startButton.Bottom + gap : actionsTop,
                refreshButtonSize.Width,
                refreshButtonSize.Height);
            var copyButton = new Rectangle(
                refreshButton.Right + gap,
                refreshButton.Top,
                copyButtonSize.Width,
                copyButtonSize.Height);
            var address = new Rectangle(
                left,
                S(154),
                effectiveWidth,
                S(36));
            int statusWidth = Math.Min(S(116), effectiveWidth);

            return new QuantResearchEntryHeroLayout(
                new Rectangle(left, S(10), effectiveWidth, S(20)),
                new Rectangle(left, S(31), effectiveWidth, S(38)),
                new Rectangle(left, S(72), effectiveWidth, S(42)),
                new Rectangle(left, S(122), statusWidth, S(28)),
                new Rectangle(
                    left + statusWidth + S(10),
                    S(122),
                    Math.Max(1, effectiveWidth - statusWidth - S(10)),
                    S(28)),
                address,
                startButton,
                openButton,
                refreshButton,
                copyButton,
                wrapActions ? S(280) : S(236));
        }
    }

    public enum QuantResearchServiceState
    {
        Online,
        Offline,
        InvalidAddress
    }

    public sealed record QuantResearchServiceStatus(
        QuantResearchServiceState State,
        string Text,
        string Detail);

    public static class QuantResearchEntryPageModel
    {
        public const string DefaultUrl = "http://127.0.0.1:5078/";
        private const int MaxHealthPayloadBytes = 16 * 1024;
        private static readonly TimeSpan HealthPayloadTimeout = TimeSpan.FromSeconds(2);

        public static Uri ResolveUrl()
        {
            string? configured = Environment.GetEnvironmentVariable("CS2_QUANT_URL");
            if (TryNormalizeAllowedUrl(configured, out Uri? configuredUri))
                return configuredUri!;

            return new Uri(DefaultUrl, UriKind.Absolute);
        }

        internal static bool CanStartLocalService(Uri serviceUrl)
        {
            ArgumentNullException.ThrowIfNull(serviceUrl);
            return serviceUrl.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && serviceUrl.IsLoopback;
        }

        public static bool TryNormalizeAllowedUrl(string? value, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value)
                || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? candidate)
                || !string.IsNullOrEmpty(candidate.UserInfo))
            {
                return false;
            }

            bool isHttps = candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool isLoopbackHttp = candidate.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && candidate.IsLoopback;
            if (!isHttps && !isLoopbackHttp)
                return false;

            uri = new UriBuilder(candidate)
            {
                Fragment = string.Empty
            }.Uri;
            return true;
        }

        public static async Task<QuantResearchServiceStatus> CheckAvailabilityAsync(
            Uri serviceUrl,
            HttpClient httpClient,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(serviceUrl);
            ArgumentNullException.ThrowIfNull(httpClient);
            if (!TryNormalizeAllowedUrl(serviceUrl.AbsoluteUri, out Uri? normalized))
            {
                return new QuantResearchServiceStatus(
                    QuantResearchServiceState.InvalidAddress,
                    "地址不可用",
                    "仅允许 HTTPS，或本机回环地址的 HTTP。 ");
            }

            try
            {
                Uri healthUrl = new(normalized!, "/health");
                using var request = new HttpRequestMessage(HttpMethod.Get, healthUrl);
                using HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new QuantResearchServiceStatus(
                        QuantResearchServiceState.Offline,
                        "服务未就绪",
                        $"健康检查返回 HTTP {(int)response.StatusCode}。");
                }

                try
                {
                    using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    bodyTimeout.CancelAfter(HealthPayloadTimeout);
                    await response.Content.LoadIntoBufferAsync(
                        MaxHealthPayloadBytes,
                        bodyTimeout.Token).ConfigureAwait(false);
                    await using Stream content = await response.Content.ReadAsStreamAsync(
                        bodyTimeout.Token).ConfigureAwait(false);
                    using JsonDocument document = await JsonDocument.ParseAsync(
                        content,
                        cancellationToken: bodyTimeout.Token).ConfigureAwait(false);
                    JsonElement root = document.RootElement;
                    bool isExpectedService = root.TryGetProperty("service", out JsonElement service)
                        && service.ValueKind == JsonValueKind.String
                        && string.Equals(service.GetString(), "CS2QuantWeb", StringComparison.Ordinal);
                    bool isHealthy = root.TryGetProperty("status", out JsonElement status)
                        && status.ValueKind == JsonValueKind.String
                        && string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
                    if (isExpectedService && isHealthy)
                    {
                        return new QuantResearchServiceStatus(
                            QuantResearchServiceState.Online,
                            "本地服务已就绪",
                            "可以打开网页并使用全部研究功能。");
                    }
                }
                catch (JsonException)
                {
                    // A different process may own the configured port and return non-CS2QuantWeb content.
                }

                return new QuantResearchServiceStatus(
                    QuantResearchServiceState.Offline,
                    "端口被占用",
                    "该地址没有运行 CS2QuantWeb，请关闭占用端口的其他程序后重试。");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new QuantResearchServiceStatus(
                    QuantResearchServiceState.Offline,
                    "服务未响应",
                    "连接超时，请先启动独立网页服务。 ");
            }
            catch (HttpRequestException)
            {
                return new QuantResearchServiceStatus(
                    QuantResearchServiceState.Offline,
                    "服务未启动",
                    "请先运行 CS2QuantWeb，再打开网页。 ");
            }
        }
    }
}
