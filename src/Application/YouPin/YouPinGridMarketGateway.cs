using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System.Net.Http;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinGridMarketGateway : IYouPinGridMarketGateway, IDisposable
    {
        private readonly IYouPinAuthService _authService;
        private readonly IAppDiagnostics _diagnostics;
        private readonly HttpClient _http;

        public YouPinGridMarketGateway(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            IAppDiagnostics diagnostics)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20);
        }

        public async Task<YouPinGridMarketQuote> ReadLowestValidListingAsync(
            Settings settings,
            YouPinGridStrategy strategy,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(strategy);
            if (string.IsNullOrWhiteSpace(strategy.TemplateId)
                || string.IsNullOrWhiteSpace(strategy.ItemName))
            {
                return new YouPinGridMarketQuote
                {
                    TemplateId = strategy.TemplateId,
                    ItemName = strategy.ItemName,
                    CapturedAt = DateTime.Now,
                    Message = "请先填写完整饰品名称和悠悠模板 ID"
                };
            }

            YouPinCredential? credential = _authService.GetCredential(settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                throw new InvalidOperationException("请先登录悠悠有品后再刷新交易网格。");

            string device = string.IsNullOrWhiteSpace(credential.DeviceToken)
                ? _authService.EnsureDeviceToken(settings)
                : credential.DeviceToken.Trim();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                YouPinUrls.ApiBase + YouPinUrls.GridSellMarket);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                autoDelivery = 0,
                conditions = Array.Empty<object>(),
                hasSold = true,
                haveBuZhangType = 0,
                integritySellFilter = 0,
                isDialogMarket = true,
                isMultipleZone = 0,
                listSortType = 1,
                listType = 10,
                mergeFlag = 0,
                pageIndex = 1,
                pageSize = 50,
                pageSourceCode = "",
                presaleMoreZones = 2,
                showRentResource = 1,
                sortType = 1,
                sortTypeKey = "",
                sourceChannel = "",
                status = 20,
                stickerAbrade = 0,
                stickersIsSort = false,
                templateId = strategy.TemplateId.Trim(),
                ultraLongLeaseMoreZones = 0,
                userId = credential.UserId ?? string.Empty,
                Sessionid = device
            });
            YouPinMobileApiClient.ApplyHeaders(
                request,
                credential.Token,
                device,
                credential.Uk);

            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "读取悠悠同款在售",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"读取悠悠同款在售失败：HTTP {(int)response.StatusCode}");
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            YouPinGridMarketQuote quote = YouPinGridMarketParser.ParseLowestValidListing(
                body,
                strategy.TemplateId.Trim(),
                strategy.ItemName.Trim(),
                DateTime.Now);
            _diagnostics.Info(
                "YouPinGrid",
                $"Market quote read. Template={_diagnostics.Redact(strategy.TemplateId)}; "
                + $"Available={quote.Available}; Rows={quote.ValidListingCount}");
            return quote;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
