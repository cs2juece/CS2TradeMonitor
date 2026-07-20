using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinLandlordGateway : IYouPinLandlordGateway, IDisposable
    {
        private const int ShelfPageSize = 100;
        private const int MaxShelfPages = 50;
        private const int MarketPageSize = 50;
        private const int MaxInventoryPages = 50;
        private const int OneClickPricingLeaseType = 1;
        private const int InventoryWriteSceneConfirmationCode = 7000002;
        private const int InventoryWriteMaxAttempts = 2;

        private readonly IYouPinAuthService _authService;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly IAppDiagnostics _diagnostics;
        private readonly HttpClient _http;

        public YouPinLandlordGateway(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            IAppDiagnostics diagnostics)
            : this(
                authService,
                httpFactory,
                diagnostics,
                static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
        {
        }

        internal YouPinLandlordGateway(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            IAppDiagnostics diagnostics,
            Func<TimeSpan, CancellationToken, Task> delayAsync)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20);
        }

        public async Task<YouPinLandlordRemoteSnapshot> ReadSnapshotAsync(
            Settings settings,
            YouPinRentalScanScope scope,
            string runId,
            CancellationToken cancellationToken)
        {
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            var listings = new List<YouPinLandlordRemoteListing>();

            if (scope.HasFlag(YouPinRentalScanScope.InventoryRental))
            {
                IReadOnlyList<YouPinLandlordRemoteListing> normal = await ReadShelfAsync(
                    YouPinUrls.LandlordNormalShelf,
                    YouPinRentalShelfType.InventoryRental,
                    credential,
                    device,
                    runId,
                    cancellationToken).ConfigureAwait(false);
                listings.AddRange(normal);
            }

            if (scope.HasFlag(YouPinRentalScanScope.ZeroCd))
            {
                IReadOnlyList<YouPinLandlordRemoteListing> zeroCd = await ReadShelfAsync(
                    YouPinUrls.LandlordZeroCdShelf,
                    YouPinRentalShelfType.ZeroCd,
                    credential,
                    device,
                    runId,
                    cancellationToken).ConfigureAwait(false);
                listings.AddRange(zeroCd);
            }

            YouPinLandlordPricingPreference preference = await ReadPricingPreferenceAsync(
                credential,
                device,
                cancellationToken).ConfigureAwait(false);

            return new YouPinLandlordRemoteSnapshot(listings.ToArray(), preference);
        }

        public async Task ValidateLoginAsync(Settings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            YouPinLoginResult result = await _authService.ValidateCurrentAsync(settings).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Ok)
                throw new InvalidOperationException("悠悠有品登录校验失败：" + Redact(result.Message));
        }

        public async Task<IReadOnlyList<YouPinLandlordRemoteInventoryItem>> ReadInventoryAsync(
            Settings settings,
            string runId,
            CancellationToken cancellationToken)
        {
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            var result = new List<YouPinLandlordRemoteInventoryItem>();
            var stopwatch = Stopwatch.StartNew();

            for (int pageIndex = 1; pageIndex <= MaxInventoryPages; pageIndex++)
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    YouPinUrls.ApiBase + YouPinUrls.LandlordInventory);
                request.Content = YouPinMobileApiClient.JsonContent(new
                {
                    IsRefresh = pageIndex == 1,
                    PageIndex = pageIndex,
                    AssetStatus = 0,
                    RefreshType = 2,
                    AppType = "4",
                    IsMerge = 0,
                    GameID = 730,
                    Sessionid = device
                });
                YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
                using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                    _http,
                    request,
                    "读取悠悠库存出租资格",
                    cancellationToken).ConfigureAwait(false);
                EnsureHttpSuccess(response, "读取悠悠库存出租资格");
                using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                    response,
                    "读取悠悠库存出租资格").ConfigureAwait(false);
                EnsureApiSuccess(document.RootElement, "读取悠悠库存出租资格");

                IReadOnlyList<InventoryCandidate> candidates = ParseInventoryCandidates(document.RootElement);
                if (candidates.Count == 0)
                    break;

                IReadOnlyDictionary<string, InventoryListingProfile> profiles = await ReadInventoryExtensionAsync(
                    candidates,
                    credential,
                    device,
                    cancellationToken).ConfigureAwait(false);
                InventoryQualification qualification = await ReadInventoryQualificationAsync(
                    candidates,
                    credential,
                    device,
                    cancellationToken).ConfigureAwait(false);
                result.AddRange(candidates.Select(candidate => EvaluateInventoryEligibility(
                    candidate,
                    qualification,
                    profiles.TryGetValue(candidate.AssetId, out InventoryListingProfile? profile)
                        ? profile
                        : InventoryListingProfile.Default)));

                bool hasNext = TryGetProperty(document.RootElement, out JsonElement data, "Data", "data")
                    && GetBool(data, "hasNext", "HasNext");
                if (!hasNext)
                    break;
            }

            stopwatch.Stop();
            _diagnostics.Info(
                "YouPinLandlord",
                $"Inventory qualification scan completed. Run={NormalizeCorrelationId(runId)}; "
                + $"Items={result.Count}; Eligible={result.Count(item => item.IsEligible)}; "
                + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
            return result;
        }

        private async Task<IReadOnlyDictionary<string, InventoryListingProfile>> ReadInventoryExtensionAsync(
            IReadOnlyList<InventoryCandidate> candidates,
            YouPinCredential credential,
            string device,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                YouPinUrls.ApiBase + YouPinUrls.LandlordInventoryExtend);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                inventorytKeyList = candidates.Select(candidate => new
                {
                    abrade = candidate.Abrade,
                    commodityTemplateId = ParseLong(candidate.TemplateId),
                    marketHashName = candidate.MarketHashName,
                    paintSeed = candidate.PaintSeed,
                    steamAssetId = ParseLong(candidate.AssetId)
                }),
                uploadChannel = 0,
                userId = ParseLong(credential.UserId),
                Sessionid = device
            });
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "读取悠悠库存出租扩展配置",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "读取悠悠库存出租扩展配置");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "读取悠悠库存出租扩展配置").ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠库存出租扩展配置");
            return ParseInventoryListingProfiles(document.RootElement, candidates);
        }

        private async Task<InventoryQualification> ReadInventoryQualificationAsync(
            IReadOnlyList<InventoryCandidate> candidates,
            YouPinCredential credential,
            string device,
            CancellationToken cancellationToken)
        {
            long[] assetIds = candidates
                .Select(candidate => long.TryParse(candidate.AssetId, out long value) ? value : 0L)
                .Where(value => value > 0)
                .ToArray();
            if (assetIds.Length == 0)
                return InventoryQualification.Unavailable("库存未返回有效的 Steam 资产标识");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                YouPinUrls.ApiBase + YouPinUrls.LandlordInventoryQualification);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                inventoryOnShelfKeyCheckList = assetIds.Select(assetId => new { steamAssetId = assetId }),
                queryDepositInsuranceList = candidates
                    .Where(candidate => candidate.BusinessId.Length > 0)
                    .Select(candidate => new
                    {
                        businessId = candidate.BusinessId,
                        currentRiskStatus = candidate.CurrentRiskStatus,
                        discountRate = candidate.VipChargePercent.ToString("0.####", CultureInfo.InvariantCulture),
                        discountedState = candidate.VipSwitchStatus.ToString(CultureInfo.InvariantCulture),
                        normalRate = candidate.NormalChargePercent.ToString("0.####", CultureInfo.InvariantCulture),
                        orderSubType = candidate.OrderSubType,
                        platformCommodity = candidate.PlatformCommodity
                    }),
                Sessionid = device
            });
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "校验悠悠库存上架资格",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "校验悠悠库存上架资格");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "校验悠悠库存上架资格").ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "校验悠悠库存上架资格");
            return ParseInventoryQualification(document.RootElement);
        }

        public async Task<IReadOnlyList<YouPinLandlordMarketListing>> ReadMarketAsync(
            Settings settings,
            string templateId,
            string itemName,
            string runId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(templateId))
                return Array.Empty<YouPinLandlordMarketListing>();

            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            var stopwatch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + YouPinUrls.LandlordMarketLease);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                hasLease = "true",
                haveBuZhangType = 0,
                listSortType = "2",
                listType = 30,
                mergeFlag = 0,
                pageIndex = 1,
                pageSize = MarketPageSize,
                sortType = "1",
                sortTypeKey = "LEASE_DEFAULT",
                status = "20",
                stickerAbrade = 0,
                stickersIsSort = false,
                templateId = templateId.Trim(),
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
                "读取悠悠同款出租市场",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "读取悠悠同款出租市场");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "读取悠悠同款出租市场").ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠同款出租市场");

            IReadOnlyList<YouPinLandlordMarketListing> rows = ParseMarketRows(document.RootElement);
            stopwatch.Stop();
            _diagnostics.Info(
                "YouPinLandlord",
                $"Market read completed. Run={NormalizeCorrelationId(runId)}; "
                + $"Item={SanitizeItemName(itemName)}; Rows={rows.Count}; "
                + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
            return rows;
        }

        public async Task<YouPinLandlordWriteResult> ChangeLeasePriceAsync(
            Settings settings,
            YouPinLandlordRepriceCommand command,
            string runId,
            string actionId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            int? compensationType = await InitializeRepriceAsync(
                command.ListingId,
                credential,
                device,
                runId,
                actionId,
                cancellationToken).ConfigureAwait(false);

            long numericListingId = ParseLong(command.ListingId);
            if (numericListingId <= 0)
            {
                return new YouPinLandlordWriteResult(
                    false,
                    "悠悠货架商品标识不是有效数字，已停止改价");
            }

            var stopwatch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Put, YouPinUrls.ApiBase + YouPinUrls.LandlordChangeLeasePrice);
            var commodity = new Dictionary<string, object>
            {
                ["CommodityId"] = numericListingId,
                ["CompensationType"] = compensationType ?? 0,
                ["IsCanLease"] = command.IsCanLease,
                ["IsCanSold"] = command.IsCanSold,
                ["LeaseDeposit"] = command.Deposit.ToString("0.##", CultureInfo.InvariantCulture),
                ["LeaseMaxDays"] = command.LeaseMaxDays,
                ["LeaseUnitPrice"] = command.ShortRent,
                ["Price"] = command.SellPrice
            };
            if (command.LongRent > 0m)
                commodity["LongLeaseUnitPrice"] = command.LongRent;

            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                Commoditys = new[] { commodity },
                Sessionid = device
            });
            _diagnostics.Info(
                "YouPinLandlord",
                $"Lease price write contract selected. Run={NormalizeCorrelationId(runId)}; "
                + $"Action={NormalizeCorrelationId(actionId)}; CommodityIdType=Int64; "
                + $"CompensationType={compensationType ?? 0}; "
                + $"CompensationProfileFound={compensationType.HasValue}; "
                + $"LongRentIncluded={command.LongRent > 0m}");
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "修改悠悠租赁价格",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "修改悠悠租赁价格");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "修改悠悠租赁价格").ConfigureAwait(false);
            LogLeasePriceWriteResponse(
                response,
                document.RootElement,
                runId,
                actionId);
            EnsureApiSuccess(document.RootElement, "修改悠悠租赁价格");

            bool success = ParseWriteSuccess(document.RootElement, command.ListingId, out string message);
            stopwatch.Stop();
            _diagnostics.Info(
                "YouPinLandlord",
                $"Lease price write completed. Run={NormalizeCorrelationId(runId)}; "
                + $"Action={NormalizeCorrelationId(actionId)}; Success={success}; "
                + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
            return new YouPinLandlordWriteResult(success, Redact(message));
        }

        public async Task<YouPinLandlordInventoryWriteResult> ListInventoryAsync(
            Settings settings,
            YouPinLandlordInventoryListCommand command,
            string runId,
            string actionId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            long assetId = ParseLong(command.AssetId);
            if (assetId <= 0)
                return new YouPinLandlordInventoryWriteResult(false, string.Empty, "库存资产标识无效，已停止上架");

            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            var stopwatch = Stopwatch.StartNew();
            _diagnostics.Info(
                "YouPinLandlord",
                $"Inventory listing contract selected. Run={NormalizeCorrelationId(runId)}; "
                + $"Action={NormalizeCorrelationId(actionId)}; HeaderProfile=legacy-android-5.28.3; "
                + $"BodyProfile={(command.IsCanSold && !command.IsCanLease ? "official-minimal-sale-v1" : "steamauto-minimal-normal-v1")}; "
                + $"Mode={(command.IsCanSold && !command.IsCanLease ? "sale" : "normal")}");
            for (int attempt = 1; attempt <= InventoryWriteMaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using HttpRequestMessage request = CreateInventoryListRequest(
                    command,
                    assetId,
                    credential,
                    device);
                using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                    _http,
                    request,
                    "自动上架悠悠普通出租库存",
                    cancellationToken).ConfigureAwait(false);
                EnsureHttpSuccess(response, "自动上架悠悠普通出租库存");
                using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                    response,
                    "自动上架悠悠普通出租库存").ConfigureAwait(false);
                LogInventoryWriteResponse(
                    response,
                    document.RootElement,
                    runId,
                    actionId,
                    attempt);
                int apiCode = GetInt(document.RootElement, "code", "Code");
                if (apiCode != 0)
                {
                    string apiMessage = FirstText(
                        GetString(document.RootElement, "msg", "Msg", "message", "Message"),
                        $"悠悠有品返回 code={apiCode}");
                    if (YouPinMobileApiClient.IsLoginExpired(apiCode, apiMessage))
                        throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");

                    if (attempt < InventoryWriteMaxAttempts
                        && TryReadInventoryWriteSceneChallenge(
                            document.RootElement,
                            out InventoryWriteSceneChallenge challenge))
                    {
                        if (challenge.CountdownSeconds > 0)
                        {
                            await _delayAsync(
                                TimeSpan.FromSeconds(challenge.CountdownSeconds),
                                cancellationToken).ConfigureAwait(false);
                        }

                        InventoryWriteSceneConfirmationResult confirmation =
                            await ConfirmInventoryWriteSceneAsync(
                                credential,
                                device,
                                challenge,
                                runId,
                                actionId,
                                cancellationToken).ConfigureAwait(false);
                        if (!confirmation.Success)
                        {
                            stopwatch.Stop();
                            string confirmationFailure = YouPinLandlordUserNotice
                                .CreateTradingNoticeFailure(Redact(confirmation.Message));
                            _diagnostics.Error(
                                "YouPinLandlord",
                                $"Inventory listing scene confirmation rejected. "
                                + $"Run={NormalizeCorrelationId(runId)}; "
                                + $"Action={NormalizeCorrelationId(actionId)}; "
                                + $"Scene={SanitizeItemName(challenge.SceneName)}; "
                                + $"Message={confirmationFailure}; "
                                + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
                            return new YouPinLandlordInventoryWriteResult(
                                false,
                                string.Empty,
                                confirmationFailure);
                        }

                        _diagnostics.Info(
                            "YouPinLandlord",
                            $"Inventory listing scene confirmed; original write will be retried. "
                            + $"Run={NormalizeCorrelationId(runId)}; Action={NormalizeCorrelationId(actionId)}; "
                            + $"Scene={SanitizeItemName(challenge.SceneName)}; "
                            + $"CountdownSeconds={challenge.CountdownSeconds}; Attempt={attempt}");
                        continue;
                    }

                    if (apiCode == InventoryWriteSceneConfirmationCode)
                    {
                        stopwatch.Stop();
                        string detail = attempt >= InventoryWriteMaxAttempts
                            ? $"自动确认后平台仍要求再次确认（code={apiCode}）"
                            : $"平台未返回有效确认场景（code={apiCode}）";
                        string sceneFailure = YouPinLandlordUserNotice
                            .CreateTradingNoticeFailure(detail);
                        _diagnostics.Error(
                            "YouPinLandlord",
                            $"Inventory listing scene confirmation required but unavailable. "
                            + $"Run={NormalizeCorrelationId(runId)}; "
                            + $"Action={NormalizeCorrelationId(actionId)}; "
                            + $"Attempt={attempt}; Message={sceneFailure}; "
                            + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
                        return new YouPinLandlordInventoryWriteResult(
                            false,
                            string.Empty,
                            sceneFailure);
                    }

                    stopwatch.Stop();
                    string failureMessage = $"自动上架悠悠普通出租库存失败（code={apiCode}）：{Redact(apiMessage)}";
                    _diagnostics.Error(
                        "YouPinLandlord",
                        $"Inventory listing write rejected. Run={NormalizeCorrelationId(runId)}; "
                        + $"Action={NormalizeCorrelationId(actionId)}; Code={apiCode}; Attempt={attempt}; "
                        + $"Message={failureMessage}; ElapsedMs={stopwatch.ElapsedMilliseconds}");
                    return new YouPinLandlordInventoryWriteResult(false, string.Empty, failureMessage);
                }

                YouPinLandlordInventoryWriteResult result = ParseInventoryWriteResult(
                    document.RootElement,
                    command.AssetId);
                stopwatch.Stop();
                _diagnostics.Info(
                    "YouPinLandlord",
                    $"Inventory listing write completed. Run={NormalizeCorrelationId(runId)}; "
                    + $"Action={NormalizeCorrelationId(actionId)}; Success={result.Success}; "
                    + $"Attempts={attempt}; ElapsedMs={stopwatch.ElapsedMilliseconds}");
                return result with { Message = Redact(result.Message) };
            }

            throw new InvalidOperationException("悠悠库存上架重试流程异常结束。");
        }

        private void LogInventoryWriteResponse(
            HttpResponseMessage response,
            JsonElement root,
            string runId,
            string actionId,
            int attempt)
        {
            LogSafeApiResponse(
                "[DEBUG-YP-LIST-RESP-v1]",
                "Inventory listing raw response",
                response,
                root,
                runId,
                actionId,
                $"Attempt={attempt}");
        }

        private void LogLeasePriceWriteResponse(
            HttpResponseMessage response,
            JsonElement root,
            string runId,
            string actionId)
        {
            LogSafeApiResponse(
                "[DEBUG-YP-REPRICE-RESP-v1]",
                "Lease price write raw response",
                response,
                root,
                runId,
                actionId,
                "Contract=PriceChangeWithLeaseV2");
        }

        private void LogInventorySceneConfirmationResponse(
            HttpResponseMessage response,
            string rawBody,
            string runId,
            string actionId,
            string sceneName)
        {
            LogSafeRawApiResponse(
                "[DEBUG-YP-LIST-CONFIRM-RESP-v1]",
                "Inventory listing scene confirmation raw response",
                response,
                rawBody,
                runId,
                actionId,
                $"Scene={SanitizeItemName(sceneName)}");
        }

        private void LogSafeApiResponse(
            string marker,
            string operation,
            HttpResponseMessage response,
            JsonElement root,
            string runId,
            string actionId,
            string detail)
        {
            string body = ContainsSensitiveResponseProperty(root)
                ? "<响应包含凭据或交易身份字段，正文已阻止写入诊断日志>"
                : JsonSerializer.Serialize(root);
            LogSafeApiResponseCore(
                marker,
                operation,
                response,
                body,
                runId,
                actionId,
                detail);
        }

        private void LogSafeRawApiResponse(
            string marker,
            string operation,
            HttpResponseMessage response,
            string rawBody,
            string runId,
            string actionId,
            string detail)
        {
            LogSafeApiResponseCore(
                marker,
                operation,
                response,
                FormatSafeResponseBody(rawBody),
                runId,
                actionId,
                detail);
        }

        private void LogSafeApiResponseCore(
            string marker,
            string operation,
            HttpResponseMessage response,
            string body,
            string runId,
            string actionId,
            string detail)
        {
            string headers = string.Join(
                ",",
                response.Headers
                    .Concat(response.Content.Headers)
                    .Where(header => !IsSensitiveResponseHeader(header.Key))
                    .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(header => $"{header.Key}={string.Join("|", header.Value)}"));

            _diagnostics.Info(
                "YouPinLandlord",
                $"{marker} {operation}. Run={NormalizeCorrelationId(runId)}; "
                + $"Action={NormalizeCorrelationId(actionId)}; {detail}; "
                + $"HttpStatus={(int)response.StatusCode}; "
                + $"Reason={response.ReasonPhrase ?? string.Empty}; Headers={headers}; Body={body}");
        }

        private static string FormatSafeResponseBody(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody))
                return "<空响应>";

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawBody);
                return ContainsSensitiveResponseProperty(document.RootElement)
                    ? "<响应包含凭据或交易身份字段，正文已阻止写入诊断日志>"
                    : JsonSerializer.Serialize(document.RootElement);
            }
            catch (JsonException)
            {
                string sanitized = YouPinMobileApiClient.Sanitize(rawBody);
                return sanitized.Length <= 2000 ? sanitized : sanitized[..2000] + "...";
            }
        }

        private static bool IsSensitiveResponseHeader(string name)
        {
            return name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
                || name.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                || name.Contains("signature", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsSensitiveResponseProperty(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (IsSensitiveResponsePropertyName(property.Name)
                        || ContainsSensitiveResponseProperty(property.Value))
                    {
                        return true;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (ContainsSensitiveResponseProperty(item))
                        return true;
                }
            }

            return false;
        }

        private static bool IsSensitiveResponsePropertyName(string name)
        {
            return name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
                || name.Contains("cookie", StringComparison.OrdinalIgnoreCase)
                || name.Contains("signature", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sessionid", StringComparison.OrdinalIgnoreCase)
                || name.Equals("deviceid", StringComparison.OrdinalIgnoreCase)
                || name.Equals("devicetoken", StringComparison.OrdinalIgnoreCase)
                || name.Equals("deviceuk", StringComparison.OrdinalIgnoreCase)
                || name.Equals("uk", StringComparison.OrdinalIgnoreCase)
                || name.Equals("orderno", StringComparison.OrdinalIgnoreCase)
                || name.Equals("commodityno", StringComparison.OrdinalIgnoreCase)
                || name.Equals("tradeofferid", StringComparison.OrdinalIgnoreCase)
                || name.Equals("offerid", StringComparison.OrdinalIgnoreCase);
        }

        private static HttpRequestMessage CreateInventoryListRequest(
            YouPinLandlordInventoryListCommand command,
            long assetId,
            YouPinCredential credential,
            string device)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                YouPinUrls.ApiBase + YouPinUrls.LandlordListInventory);
            if (command.IsCanSold && !command.IsCanLease)
            {
                request.Content = YouPinMobileApiClient.JsonContent(new
                {
                    GameId = 730,
                    ItemInfos = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["AssetId"] = assetId,
                            ["IsCanSold"] = true,
                            ["IsCanLease"] = false,
                            ["Remark"] = string.Empty,
                            ["Price"] = command.SellPrice.ToString("0.##", CultureInfo.InvariantCulture)
                        }
                    },
                    Sessionid = device
                });
                YouPinSaleReminderHttpHelper.ApplyYouPinLegacyAndroidHeaders(
                    request,
                    credential.Token,
                    device,
                    credential.Uk);
                return request;
            }

            var itemInfo = new Dictionary<string, object>
            {
                ["AssetId"] = assetId,
                ["IsCanLease"] = command.IsCanLease,
                ["IsCanSold"] = command.IsCanSold,
                ["LeaseDeposit"] = command.Deposit.ToString("0.##", CultureInfo.InvariantCulture),
                ["LeaseMaxDays"] = command.LeaseMaxDays,
                ["LeaseUnitPrice"] = command.ShortRent,
                ["CompensationType"] = command.CompensationType
            };
            if (command.LeaseMaxDays > 8 && command.LongRent > 0m)
                itemInfo["LongLeaseUnitPrice"] = command.LongRent;

            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                GameId = 730,
                ItemInfos = new[] { itemInfo },
                Sessionid = device
            });
            YouPinSaleReminderHttpHelper.ApplyYouPinLegacyAndroidHeaders(
                request,
                credential.Token,
                device,
                credential.Uk);
            return request;
        }

        private async Task<InventoryWriteSceneConfirmationResult> ConfirmInventoryWriteSceneAsync(
            YouPinCredential credential,
            string device,
            InventoryWriteSceneChallenge challenge,
            string runId,
            string actionId,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                YouPinUrls.ApiBase + YouPinUrls.LandlordGlobalReminderConfirm);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                Sessionid = device,
                sceneCode = challenge.SceneCode
            });
            YouPinSaleReminderHttpHelper.ApplyYouPinLegacyAndroidHeaders(
                request,
                credential.Token,
                device,
                credential.Uk);

            try
            {
                using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                    _http,
                    request,
                    "确认悠悠交易须知",
                    cancellationToken).ConfigureAwait(false);
                string rawBody = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
                LogInventorySceneConfirmationResponse(
                    response,
                    rawBody,
                    runId,
                    actionId,
                    challenge.SceneName);
                if (!response.IsSuccessStatusCode)
                {
                    return new InventoryWriteSceneConfirmationResult(
                        false,
                        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }
                if (string.IsNullOrWhiteSpace(rawBody))
                    return new InventoryWriteSceneConfirmationResult(false, "悠悠有品返回空响应");

                JsonDocument document;
                try
                {
                    document = YouPinMobileApiClient.ParseJson(rawBody, "确认悠悠交易须知");
                }
                catch (InvalidOperationException ex)
                {
                    return new InventoryWriteSceneConfirmationResult(false, ex.Message);
                }

                using (document)
                {
                    int apiCode = GetInt(document.RootElement, "code", "Code");
                    string apiMessage = FirstText(
                        GetString(document.RootElement, "msg", "Msg", "message", "Message"),
                        $"悠悠有品返回 code={apiCode}");
                    if (YouPinMobileApiClient.IsLoginExpired(apiCode, apiMessage))
                    {
                        return new InventoryWriteSceneConfirmationResult(
                            false,
                            "悠悠有品登录状态失效，请重新登录");
                    }
                    if (apiCode != 0)
                        return new InventoryWriteSceneConfirmationResult(false, apiMessage);
                    if (!GetBool(document.RootElement, "data", "Data"))
                        return new InventoryWriteSceneConfirmationResult(false, "平台未确认本次交易须知");

                    return new InventoryWriteSceneConfirmationResult(true, apiMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string message = Redact(ex.Message);
                _diagnostics.Error(
                    "YouPinLandlord",
                    $"Inventory listing scene confirmation request failed. "
                    + $"Run={NormalizeCorrelationId(runId)}; Action={NormalizeCorrelationId(actionId)}; "
                    + $"Scene={SanitizeItemName(challenge.SceneName)}; "
                    + $"Exception={ex.GetType().Name}; Message={message}");
                return new InventoryWriteSceneConfirmationResult(false, message);
            }
        }

        private static bool TryReadInventoryWriteSceneChallenge(
            JsonElement root,
            out InventoryWriteSceneChallenge challenge)
        {
            challenge = null!;
            if (GetInt(root, "code", "Code") != InventoryWriteSceneConfirmationCode
                || !TryGetProperty(root, out JsonElement errorData, "errorData", "ErrorData")
                || errorData.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string sceneCode = (GetString(errorData, "sceneCode", "SceneCode") ?? string.Empty).Trim();
            if (sceneCode.Length == 0)
                return false;

            string sceneName = FirstText(
                GetString(errorData, "sceneName", "SceneName"),
                GetString(errorData, "sceneTitle", "SceneTitle"),
                "平台交易须知");
            int countdownSeconds = Math.Clamp(
                GetInt(errorData, "countdown", "Countdown"),
                0,
                60);
            challenge = new InventoryWriteSceneChallenge(
                sceneCode,
                sceneName,
                countdownSeconds);
            return true;
        }

        public async Task<YouPinLandlordPricingQuote> ReadOneClickPricingAsync(
            Settings settings,
            YouPinLandlordRemoteListing listing,
            string runId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(listing);
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            string commodityHashName = FirstText(listing.MarketHashName, listing.ItemName);
            if (!long.TryParse(
                    listing.TemplateId,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long commodityTemplateId)
                || commodityTemplateId <= 0)
            {
                throw new InvalidOperationException("悠悠商品模板 ID 无效，本件已跳过。请刷新库存后重试。");
            }
            var stopwatch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + YouPinUrls.LandlordOneClickPricing);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                commodityInfos = new[]
                {
                    new
                    {
                        commodityHashName,
                        commodityTemplateId,
                        depositMarketPrice = listing.ReferencePrice.ToString("0.##", CultureInfo.InvariantCulture),
                        leaseType = OneClickPricingLeaseType
                    }
                },
                Sessionid = device
            });
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "读取悠悠一键定价",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "读取悠悠一键定价");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "读取悠悠一键定价").ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠一键定价");
            YouPinLandlordPricingQuote quote = ParsePricingQuote(document.RootElement);
            if (quote.ShortRent <= 0m)
            {
                stopwatch.Stop();
                _diagnostics.Error(
                    "YouPinLandlord",
                    $"One-click pricing returned no valid short rent. Run={NormalizeCorrelationId(runId)}; "
                    + $"NameSource={GetPricingNameSource(listing)}; "
                    + $"Shape={DescribePricingQuoteShape(document.RootElement)}; "
                    + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
                throw new InvalidOperationException(
                    "悠悠一键定价未返回有效短租金，本件已跳过。请检查手机端定价偏好；如设置正常，请查看运行日志。");
            }

            stopwatch.Stop();
            _diagnostics.Info(
                "YouPinLandlord",
                $"One-click pricing read completed. Run={NormalizeCorrelationId(runId)}; "
                + $"Item={SanitizeItemName(listing.ItemName)}; NameSource={GetPricingNameSource(listing)}; "
                + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
            return quote;
        }

        private static string GetPricingNameSource(YouPinLandlordRemoteListing listing)
        {
            return string.IsNullOrWhiteSpace(listing.MarketHashName) ? "display-fallback" : "market-hash";
        }

        public async Task<YouPinLandlordRemoteListing?> RevalidateListingAsync(
            Settings settings,
            string listingId,
            YouPinRentalShelfType rentalType,
            string runId,
            string actionId,
            CancellationToken cancellationToken)
        {
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            IReadOnlyList<YouPinLandlordRemoteListing> listings = await ReadShelfAsync(
                rentalType == YouPinRentalShelfType.ZeroCd
                    ? YouPinUrls.LandlordZeroCdShelf
                    : YouPinUrls.LandlordNormalShelf,
                rentalType,
                credential,
                device,
                runId,
                cancellationToken).ConfigureAwait(false);
            YouPinLandlordRemoteListing? listing = listings.FirstOrDefault(item =>
                string.Equals(item.ListingId, listingId, StringComparison.Ordinal));
            _diagnostics.Info(
                "YouPinLandlord",
                $"Lease listing revalidated. Run={NormalizeCorrelationId(runId)}; "
                + $"Action={NormalizeCorrelationId(actionId)}; Exists={listing != null}; Type={rentalType}");
            return listing;
        }

        private async Task<int?> InitializeRepriceAsync(
            string listingId,
            YouPinCredential credential,
            string device,
            string runId,
            string actionId,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + YouPinUrls.LandlordRepriceInit);
            request.Content = YouPinMobileApiClient.JsonContent(new
            {
                changePriceChannel = 0,
                commodityIdList = new[] { listingId },
                gameId = "730",
                Sessionid = device
            });
            YouPinMobileApiClient.ApplyHeaders(request, credential.Token, device, credential.Uk);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "初始化悠悠租赁改价",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "初始化悠悠租赁改价");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "初始化悠悠租赁改价").ConfigureAwait(false);
            LogSafeApiResponse(
                "[DEBUG-YP-REPRICE-INIT-RESP-v1]",
                "Lease price initialization raw response",
                response,
                document.RootElement,
                runId,
                actionId,
                "Contract=change-price-init-v3");
            EnsureApiSuccess(document.RootElement, "初始化悠悠租赁改价");
            return ParseRepriceCompensationType(document.RootElement, listingId);
        }

        private static int? ParseRepriceCompensationType(JsonElement root, string listingId)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || !TryGetProperty(
                    data,
                    out JsonElement compensationMap,
                    "normalLeaseCompensationMap",
                    "NormalLeaseCompensationMap")
                || !TryGetProperty(compensationMap, out JsonElement profile, listingId)
                || !TryGetProperty(
                    profile,
                    out _,
                    "compensationTypeCode",
                    "CompensationTypeCode"))
            {
                return null;
            }

            return GetInt(profile, "compensationTypeCode", "CompensationTypeCode");
        }

        private async Task<IReadOnlyList<YouPinLandlordRemoteListing>> ReadShelfAsync(
            string path,
            YouPinRentalShelfType rentalType,
            YouPinCredential credential,
            string device,
            string runId,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var listings = new List<YouPinLandlordRemoteListing>();
            for (int pageIndex = 1; pageIndex <= MaxShelfPages; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + path);
                request.Content = YouPinMobileApiClient.JsonContent(new
                {
                    pageIndex,
                    pageSize = ShelfPageSize,
                    whetherMerge = 0,
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
                    "读取悠悠租赁货架",
                    cancellationToken).ConfigureAwait(false);
                EnsureHttpSuccess(response, "读取悠悠租赁货架");
                using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                    response,
                    "读取悠悠租赁货架").ConfigureAwait(false);

                int code = GetInt(document.RootElement, "code", "Code");
                if (code == 9004001)
                    break;
                EnsureApiSuccess(document.RootElement, "读取悠悠租赁货架");

                List<YouPinLandlordRemoteListing> page = ParseShelfPage(document.RootElement, rentalType);
                listings.AddRange(page);
                if (!HasNextPage(document.RootElement, pageIndex, page.Count))
                    break;
            }

            stopwatch.Stop();
            _diagnostics.Info(
                "YouPinLandlord",
                $"Shelf read completed. Run={NormalizeCorrelationId(runId)}; "
                + $"Type={rentalType}; Count={listings.Count}; "
                + $"ElapsedMs={stopwatch.ElapsedMilliseconds}");
            return listings;
        }

        private async Task<YouPinLandlordPricingPreference> ReadPricingPreferenceAsync(
            YouPinCredential credential,
            string device,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                YouPinUrls.ApiBase + YouPinUrls.LandlordPricingPreference);
            request.Content = YouPinMobileApiClient.JsonContent(new { Sessionid = device });
            YouPinMobileApiClient.ApplyHeaders(
                request,
                credential.Token,
                device,
                credential.Uk);

            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                "读取悠悠一键定价偏好",
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, "读取悠悠一键定价偏好");
            using JsonDocument document = await YouPinMobileApiClient.ReadJsonDocumentAsync(
                response,
                "读取悠悠一键定价偏好").ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠一键定价偏好");
            return ParsePricingPreference(document.RootElement);
        }

        private YouPinCredential GetRequiredCredential(Settings settings)
        {
            YouPinCredential? credential = _authService.GetCredential(settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                throw new InvalidOperationException("请先登录悠悠有品后再检查包租公。");
            return credential;
        }

        private string ResolveDeviceToken(YouPinCredential credential, Settings settings)
        {
            return string.IsNullOrWhiteSpace(credential.DeviceToken)
                ? _authService.EnsureDeviceToken(settings)
                : credential.DeviceToken.Trim();
        }

        internal static List<YouPinLandlordRemoteListing> ParseShelfPage(
            JsonElement root,
            YouPinRentalShelfType rentalType)
        {
            var result = new List<YouPinLandlordRemoteListing>();
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || !TryGetProperty(data, out JsonElement list, "commodityInfoList", "CommodityInfoList")
                || list.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement item in list.EnumerateArray())
                AppendShelfItem(result, item, rentalType);
            return result;
        }

        private static void AppendShelfItem(
            List<YouPinLandlordRemoteListing> result,
            JsonElement item,
            YouPinRentalShelfType rentalType)
        {
            if (TryGetProperty(item, out JsonElement merged, "mergeCommodityList", "MergeCommodityList")
                && merged.ValueKind == JsonValueKind.Array
                && merged.GetArrayLength() > 0)
            {
                foreach (JsonElement member in merged.EnumerateArray())
                    AppendShelfItem(result, member, rentalType);
                return;
            }

            string listingId = FirstText(
                GetString(item, "id", "Id", "commodityId", "CommodityId"));
            string assetId = FirstText(GetString(item, "steamAssetId", "SteamAssetId", "assetId", "AssetId"));
            string templateId = FirstText(GetString(item, "templateId", "TemplateId"));
            string itemName = FirstText(
                GetString(item, "name", "Name"),
                GetString(item, "commodityHashName", "CommodityHashName"));
            decimal shortRent = GetDecimal(
                item,
                "shortLeaseAmount",
                "ShortLeaseAmount",
                "leaseUnitPrice",
                "LeaseUnitPrice",
                "leaseAmountDesc",
                "LeaseAmountDesc");

            if (string.IsNullOrWhiteSpace(listingId) && string.IsNullOrWhiteSpace(itemName))
                return;

            result.Add(new YouPinLandlordRemoteListing(
                listingId,
                assetId,
                templateId,
                itemName,
                rentalType,
                shortRent,
                GetDecimal(item, "longLeaseAmount", "LongLeaseAmount"),
                GetDecimal(item, "depositAmount", "DepositAmount"),
                GetInt(item, "leaseMaxDays", "LeaseMaxDays"),
                GetBoolOrDefault(
                    item,
                    true,
                    "canLease",
                    "CanLease",
                    "commodityCanLease",
                    "CommodityCanLease"),
                GetBool(item, "commodityCanSell", "CommodityCanSell"),
                GetDecimal(item, "sellAmount", "SellAmount"),
                GetDecimal(item, "referencePrice", "ReferencePrice")));
        }

        internal static YouPinLandlordPricingQuote ParsePricingQuote(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || !TryGetProperty(data, out JsonElement rows, "pricingInfoVos", "PricingInfoVos")
                || rows.ValueKind != JsonValueKind.Array
                || rows.GetArrayLength() == 0)
            {
                return new YouPinLandlordPricingQuote(0m, 0m, 0m, 0);
            }

            JsonElement item = rows[0];
            return new YouPinLandlordPricingQuote(
                GetDecimal(item, "shortLeaseUnitPrice", "ShortLeaseUnitPrice"),
                GetDecimal(item, "longLeaseUnitPrice", "LongLeaseUnitPrice"),
                GetDecimal(item, "leaseDeposit", "LeaseDeposit"),
                GetInt(item, "leaseMaxDays", "LeaseMaxDays"),
                GetDecimal(item, "price", "Price"));
        }

        private static string DescribePricingQuoteShape(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || data.ValueKind != JsonValueKind.Object)
            {
                return "data-missing-or-invalid";
            }

            if (!TryGetProperty(data, out JsonElement rows, "pricingInfoVos", "PricingInfoVos"))
                return "pricing-rows-missing";
            if (rows.ValueKind != JsonValueKind.Array)
                return "pricing-rows-not-array";
            if (rows.GetArrayLength() == 0)
                return "pricing-rows-empty";

            JsonElement item = rows[0];
            if (item.ValueKind != JsonValueKind.Object)
                return "pricing-row-invalid";
            if (!TryGetProperty(
                    item,
                    out _,
                    "shortLeaseUnitPrice",
                    "ShortLeaseUnitPrice"))
            {
                return "short-rent-field-missing";
            }

            return "short-rent-non-positive-or-invalid";
        }

        internal static YouPinLandlordInventoryWriteResult ParseInventoryWriteResult(
            JsonElement root,
            string assetId)
        {
            string message = FirstText(GetString(root, "Msg", "msg"), "悠悠未返回库存上架结果");
            if (!TryGetProperty(root, out JsonElement data, "Data", "data")
                || data.ValueKind != JsonValueKind.Array)
            {
                return new YouPinLandlordInventoryWriteResult(false, string.Empty, message);
            }
            foreach (JsonElement row in data.EnumerateArray())
            {
                string responseAssetId = GetString(row, "AssetId", "assetId") ?? string.Empty;
                if (!string.Equals(responseAssetId, assetId, StringComparison.Ordinal))
                    continue;
                int status = GetInt(row, "Status", "status");
                string listingId = GetString(row, "CommodityId", "commodityId") ?? string.Empty;
                string rowMessage = FirstText(GetString(row, "Remark", "remark"), message);
                return new YouPinLandlordInventoryWriteResult(
                    status == 1 && listingId.Length > 0,
                    listingId,
                    rowMessage);
            }
            return new YouPinLandlordInventoryWriteResult(false, string.Empty, message);
        }

        internal static bool ParseWriteSuccess(JsonElement root, string listingId, out string message)
        {
            string rootMessage = FirstText(GetString(root, "Msg", "msg"), "悠悠有品未返回改价结果");
            message = rootMessage;
            if (!TryGetProperty(root, out JsonElement data, "Data", "data"))
            {
                message = rootMessage + "；响应缺少 Data，无法确认改价结果";
                return false;
            }

            int successCount = GetInt(data, "SuccessCount", "successCount");
            int failCount = GetInt(data, "FailCount", "failCount");
            if (!TryGetProperty(data, out JsonElement items, "Commoditys", "commoditys")
                || items.ValueKind != JsonValueKind.Array)
            {
                if (successCount > 0)
                    return true;

                message = $"悠悠未确认改价成功（成功 {successCount}，失败 {failCount}）：{rootMessage}";
                return false;
            }

            foreach (JsonElement item in items.EnumerateArray())
            {
                string id = GetString(item, "CommodityId", "commodityId") ?? string.Empty;
                if (!string.Equals(id, listingId, StringComparison.Ordinal))
                    continue;
                int itemSuccess = GetInt(item, "IsSuccess", "isSuccess");
                string itemMessage = FirstText(GetString(item, "Message", "message"));
                if (successCount > 0 && itemSuccess == 1)
                {
                    message = FirstText(itemMessage, rootMessage);
                    return true;
                }

                message = FirstText(
                    itemMessage,
                    $"悠悠返回单件改价失败（成功 {successCount}，失败 {failCount}）",
                    rootMessage);
                return false;
            }

            message = successCount > 0
                ? $"悠悠返回成功数量，但响应明细未包含当前商品（成功 {successCount}，失败 {failCount}）"
                : $"悠悠未确认改价成功（成功 {successCount}，失败 {failCount}）：{rootMessage}";
            return false;
        }

        internal static YouPinLandlordPricingPreference ParsePricingPreference(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || !TryGetProperty(data, out JsonElement game, "730")
                || game.ValueKind != JsonValueKind.Object)
            {
                return YouPinLandlordPricingPreference.Empty;
            }

            var leaseDays = new List<int>();
            if (TryGetProperty(game, out JsonElement dayArray, "leaseDays", "defaultLeaseDays")
                && dayArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement day in dayArray.EnumerateArray())
                {
                    if (day.TryGetInt32(out int value))
                        leaseDays.Add(value);
                }
            }

            return new YouPinLandlordPricingPreference(
                GetInt(game, "pricingType"),
                GetBool(game, "zeroCDRentSwitch", "zeroCdRentConfigEnable"),
                GetBool(game, "fillInRentSwitch"),
                GetBool(game, "autoFillDepositSwitch"),
                leaseDays.ToArray())
            {
                TransactionMode = GetInt(game, "transactionMode"),
                DefaultRentalActivityEnabled = GetBool(game, "defaultRentalActivitySwitch"),
                DepositCompensationType = GetInt(game, "depositCompensateType"),
                LongRentCoefficient = GetDecimal(game, "longRentCoefficient")
            };
        }

        internal static IReadOnlyList<YouPinLandlordMarketListing> ParseMarketRows(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "Data", "data")
                || !TryGetProperty(data, out JsonElement list, "CommodityList", "commodityList")
                || list.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<YouPinLandlordMarketListing>();
            }

            var rows = new List<YouPinLandlordMarketListing>();
            foreach (JsonElement item in list.EnumerateArray())
            {
                string listingId = FirstText(GetString(item, "Id", "id", "CommodityId", "commodityId"));
                if (string.IsNullOrWhiteSpace(listingId))
                    continue;

                rows.Add(new YouPinLandlordMarketListing(
                    listingId,
                    GetDecimal(
                        item,
                        "LeaseUnitPrice",
                        "leaseUnitPrice",
                        "ShortLeaseAmount",
                        "shortLeaseAmount"),
                    GetBool(item, "IsMine", "isMine", "Mine", "mine")));
            }

            return rows;
        }

        private static IReadOnlyList<InventoryCandidate> ParseInventoryCandidates(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "Data", "data")
                || !TryGetProperty(data, out JsonElement rows, "ItemsInfos", "itemsInfos")
                || rows.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<InventoryCandidate>();
            }

            var result = new List<InventoryCandidate>();
            foreach (JsonElement item in rows.EnumerateArray())
            {
                string assetId = GetString(item, "SteamAssetId", "steamAssetId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(assetId))
                    continue;

                TryGetProperty(item, out JsonElement template, "TemplateInfo", "templateInfo");
                TryGetProperty(item, out JsonElement assetInfo, "AssetInfo", "assetInfo");
                TryGetProperty(item, out JsonElement vip, "VipPrerogative", "vipPrerogative");
                string marketHashName = FirstText(
                    GetString(item, "MarketHashName", "marketHashName"),
                    GetString(template, "CommodityHashName", "commodityHashName"));
                string itemName = FirstText(
                    GetString(template, "CommodityName", "commodityName"),
                    GetString(item, "ShotName", "shotName"));
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    itemName = FirstText(
                        SteamDtLocalItemNameResolver.ResolveNameByMarketHashName(marketHashName),
                        marketHashName);
                }
                result.Add(new InventoryCandidate(
                    assetId,
                    GetString(template, "Id", "id") ?? string.Empty,
                    itemName,
                    marketHashName,
                    GetDecimal(template, "MarkPrice", "markPrice", "ShowMarkPrice", "showMarkPrice"),
                    GetBool(item, "Tradable", "tradable"),
                    GetBool(item, "SteamMarketable", "steamMarketable"),
                    GetBool(item, "BanLease", "banLease"),
                    GetInt(item, "AssetStatus", "assetStatus"),
                    GetInt(item, "TradeProtect", "tradeProtect"),
                    FirstText(GetString(assetInfo, "AbradeStr", "abradeStr"), "0"),
                    GetString(assetInfo, "PaintSeed", "paintSeed") ?? "0",
                    GetDecimal(vip, "NormalRate", "normalRate"),
                    GetDecimal(vip, "DiscountRate", "discountRate"),
                    GetInt(vip, "DiscountedState", "discountedState"),
                    GetString(vip, "PlatformCommodity", "platformCommodity") ?? string.Empty,
                    GetString(vip, "CurrentRiskStatus", "currentRiskStatus") ?? string.Empty,
                    GetString(vip, "BusinessId", "businessId") ?? string.Empty,
                    GetInt(vip, "OrderSubType", "orderSubType")));
            }

            return result;
        }

        private static InventoryQualification ParseInventoryQualification(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data"))
                return InventoryQualification.Unavailable("悠悠未返回上架资格数据");

            TryGetProperty(data, out JsonElement shelf, "onShelfQualification", "OnShelfQualification");
            TryGetProperty(data, out JsonElement steam, "steamTradeStatusInfo", "SteamTradeStatusInfo");
            var excludedIds = new HashSet<string>(StringComparer.Ordinal);
            if (TryGetProperty(shelf, out JsonElement excluded, "unContainsAssetIds", "UnContainsAssetIds")
                && excluded.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement value in excluded.EnumerateArray())
                {
                    string id = value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : value.GetRawText();
                    if (!string.IsNullOrWhiteSpace(id))
                        excludedIds.Add(id.Trim('"'));
                }
            }

            return new InventoryQualification(
                GetInt(shelf, "storeOnline", "StoreOnline") == 1,
                GetInt(shelf, "prohibitSale", "ProhibitSale") != 0,
                GetInt(steam, "steamTradeStatus", "SteamTradeStatus"),
                excludedIds,
                FirstText(
                    GetString(shelf, "prohibitSaleMsg", "ProhibitSaleMsg"),
                    GetString(shelf, "storeOffLineMsg", "StoreOffLineMsg")));
        }

        private static YouPinLandlordRemoteInventoryItem EvaluateInventoryEligibility(
            InventoryCandidate item,
            InventoryQualification qualification,
            InventoryListingProfile profile)
        {
            (YouPinLandlordInventoryEligibilityCode Code, string Reason) decision = !item.Tradable
                ? (YouPinLandlordInventoryEligibilityCode.SteamNotTradable, "Steam 当前不可交易")
                : !item.SteamMarketable
                    ? (YouPinLandlordInventoryEligibilityCode.SteamNotMarketable, "Steam 当前不可在市场流通")
                    : item.BanLease
                        ? (YouPinLandlordInventoryEligibilityCode.LeaseBanned, "悠悠标记为禁止出租")
                        : item.AssetStatus != 0
                            ? (YouPinLandlordInventoryEligibilityCode.InventoryUnavailable, "库存状态暂不可上架")
                            : item.TradeProtect != 0
                                ? (YouPinLandlordInventoryEligibilityCode.TradeProtected, "仍处于交易保护期")
                                : profile.CompensationType <= 0
                                    ? (YouPinLandlordInventoryEligibilityCode.ListingConfigurationUnavailable,
                                        "未取得悠悠赔付配置，暂不自动上架")
                                : !qualification.StoreOnline
                                    ? (YouPinLandlordInventoryEligibilityCode.StoreOffline, "悠悠店铺当前不可上架")
                                    : qualification.ProhibitSale
                                        ? (YouPinLandlordInventoryEligibilityCode.PlatformProhibited,
                                            FirstText(qualification.Message, "悠悠暂不允许上架"))
                                        : qualification.ExcludedAssetIds.Contains(item.AssetId)
                                            ? (YouPinLandlordInventoryEligibilityCode.PlatformExcluded, "悠悠上架资格校验未包含此饰品")
                                            : qualification.SteamTradeStatus != 0
                                                ? (YouPinLandlordInventoryEligibilityCode.SteamTradeUnavailable,
                                                    "Steam 交易状态暂不可用")
                                                : (YouPinLandlordInventoryEligibilityCode.Eligible, "符合普通出租上架资格");
            string saleReason = !item.Tradable
                ? "Steam 当前不可交易"
                : !item.SteamMarketable
                    ? "Steam 当前不可在市场流通"
                    : item.AssetStatus != 0
                        ? "库存状态暂不可出售"
                        : item.TradeProtect != 0
                            ? "仍处于交易保护期"
                            : !qualification.StoreOnline
                                ? "悠悠店铺当前不可上架"
                                : qualification.ProhibitSale
                                    ? FirstText(qualification.Message, "悠悠暂不允许出售")
                                    : qualification.ExcludedAssetIds.Contains(item.AssetId)
                                        ? "悠悠上架资格校验未包含此饰品"
                                        : qualification.SteamTradeStatus != 0
                                            ? "Steam 交易状态暂不可用"
                                            : string.Empty;

            return new YouPinLandlordRemoteInventoryItem(
                item.AssetId,
                item.TemplateId,
                item.ItemName,
                item.ReferencePrice,
                decision.Code == YouPinLandlordInventoryEligibilityCode.Eligible,
                decision.Code,
                decision.Reason,
                profile.CompensationType,
                item.NormalChargePercent,
                item.VipChargePercent,
                profile.VipSwitchStatus != 0 ? profile.VipSwitchStatus : item.VipSwitchStatus,
                item.MarketHashName,
                saleReason.Length == 0,
                saleReason.Length == 0 ? "符合悠悠出售上架资格" : saleReason);
        }

        private static IReadOnlyDictionary<string, InventoryListingProfile> ParseInventoryListingProfiles(
            JsonElement root,
            IReadOnlyList<InventoryCandidate> candidates)
        {
            var result = new Dictionary<string, InventoryListingProfile>(StringComparer.Ordinal);
            if (!TryGetProperty(root, out JsonElement data, "data", "Data"))
                return result;
            TryGetProperty(data, out JsonElement compensationMap, "normalLeaseCompensationMap");
            TryGetProperty(data, out JsonElement extendMap, "inventoryExtendMap");
            foreach (InventoryCandidate candidate in candidates)
            {
                int compensationType = 0;
                int vipSwitchStatus = 0;
                if (compensationMap.ValueKind == JsonValueKind.Object
                    && compensationMap.TryGetProperty(candidate.AssetId, out JsonElement compensation))
                {
                    compensationType = GetInt(compensation, "compensationTypeCode");
                }
                if (extendMap.ValueKind == JsonValueKind.Object
                    && extendMap.TryGetProperty(candidate.AssetId, out JsonElement extension))
                {
                    vipSwitchStatus = GetInt(extension, "vipFlag");
                }
                result[candidate.AssetId] = new InventoryListingProfile(compensationType, vipSwitchStatus);
            }
            return result;
        }

        private sealed record InventoryCandidate(
            string AssetId,
            string TemplateId,
            string ItemName,
            string MarketHashName,
            decimal ReferencePrice,
            bool Tradable,
            bool SteamMarketable,
            bool BanLease,
            int AssetStatus,
            int TradeProtect,
            string Abrade,
            string PaintSeed,
            decimal NormalChargePercent,
            decimal VipChargePercent,
            int VipSwitchStatus,
            string PlatformCommodity,
            string CurrentRiskStatus,
            string BusinessId,
            int OrderSubType);

        private sealed record InventoryListingProfile(int CompensationType, int VipSwitchStatus)
        {
            public static InventoryListingProfile Default { get; } = new(0, 0);
        }

        private sealed record InventoryQualification(
            bool StoreOnline,
            bool ProhibitSale,
            int SteamTradeStatus,
            IReadOnlySet<string> ExcludedAssetIds,
            string Message)
        {
            public static InventoryQualification Unavailable(string message)
                => new(false, false, -1, new HashSet<string>(StringComparer.Ordinal), message);
        }

        private sealed record InventoryWriteSceneChallenge(
            string SceneCode,
            string SceneName,
            int CountdownSeconds);

        private sealed record InventoryWriteSceneConfirmationResult(
            bool Success,
            string Message);

        private static bool HasNextPage(JsonElement root, int pageIndex, int itemCount)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data"))
                return false;

            if (TryGetProperty(data, out JsonElement hasNext, "hasNext", "HasNext"))
            {
                if (hasNext.ValueKind == JsonValueKind.True)
                    return true;
                if (hasNext.ValueKind == JsonValueKind.False)
                    return false;
            }

            int totalPages = GetInt(data, "totalPages", "TotalPages");
            return totalPages > 0 ? pageIndex < totalPages : itemCount >= ShelfPageSize;
        }

        private void EnsureApiSuccess(JsonElement root, string action)
        {
            int code = GetInt(root, "code", "Code");
            if (code == 0)
                return;

            string message = FirstText(
                GetString(root, "msg", "Msg", "message", "Message"),
                $"悠悠有品返回 code={code}");
            if (YouPinMobileApiClient.IsLoginExpired(code, message))
                throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");

            throw new InvalidOperationException(action + "失败：" + Redact(message));
        }

        private static void EnsureHttpSuccess(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"{action}失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        private string Redact(string value)
        {
            return _diagnostics.Redact(YouPinMobileApiClient.Sanitize(value));
        }

        private static decimal GetDecimal(JsonElement element, params string[] names)
        {
            string text = GetString(element, names) ?? string.Empty;
            text = text
                .Replace("¥", string.Empty, StringComparison.Ordinal)
                .Replace("￥", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Trim();
            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value)
                ? value
                : 0m;
        }

        private static long ParseLong(string? value)
        {
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
                ? result
                : 0L;
        }

        private static bool GetBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out JsonElement value, names))
                return false;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => value.TryGetInt32(out int number) && number != 0,
                JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value.GetString(), "1", StringComparison.Ordinal),
                _ => false
            };
        }

        private static bool GetBoolOrDefault(
            JsonElement element,
            bool defaultValue,
            params string[] names)
        {
            return TryGetProperty(element, out _, names)
                ? GetBool(element, names)
                : defaultValue;
        }

        private static string SanitizeItemName(string itemName)
        {
            string text = (itemName ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 80 ? text : text[..80];
        }

        private static string NormalizeCorrelationId(string runId)
        {
            string normalized = (runId ?? string.Empty).Trim();
            if (normalized.Length == 0)
                return "none";
            return normalized.Length <= 32 ? normalized : normalized[..32];
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
