using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;

namespace CS2TradeMonitor.Infrastructure.YouPin
{
    internal sealed class YouPinInventoryStorageHttpAdapter : IYouPinInventoryStorageAdapter, IDisposable
    {
        internal const string CaptureAlignedAppVersion = "5.46.1";
        internal const string CaptureAlignedWebViewVersion = "149.0.7827.159";

        private readonly IYouPinAuthService _authService;
        private readonly HttpClient _http;

        public YouPinInventoryStorageHttpAdapter(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20);
        }

        public async Task<YouPinInventoryStorageViewState> ReadAsync(
            Settings settings,
            YouPinInventoryStorageQuery query,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(query);
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);

            YouPinInventoryStorageAccess access = await ReadAccessAsync(
                credential,
                device,
                cancellationToken).ConfigureAwait(false);

            IReadOnlyList<YouPinInventoryStorageItem> items = Array.Empty<YouPinInventoryStorageItem>();
            IReadOnlyList<YouPinInventoryStorageUnit> units = Array.Empty<YouPinInventoryStorageUnit>();
            string message;

            switch (query.View)
            {
                case YouPinInventoryStorageView.Storable:
                    items = await ReadStorableItemsAsync(credential, device, cancellationToken).ConfigureAwait(false);
                    units = await ReadStorageUnitsAsync(
                        credential,
                        device,
                        Math.Max(0, query.RequestedCount),
                        queryType: 1,
                        cancellationToken).ConfigureAwait(false);
                    message = access.IsBusy
                        ? "悠悠库存正在同步，列表仅供查看。"
                        : $"可存入 {items.Count} 件，目标存储单元 {units.Count} 个。";
                    break;

                case YouPinInventoryStorageView.StoredUnits:
                    units = await ReadStorageUnitsAsync(
                        credential,
                        device,
                        addCount: 0,
                        queryType: 2,
                        cancellationToken).ConfigureAwait(false);
                    message = access.IsBusy
                        ? "悠悠库存正在同步，稍后会自动恢复操作。"
                        : $"已读取 {units.Count} 个存储单元。";
                    break;

                case YouPinInventoryStorageView.StoredItems:
                    if (string.IsNullOrWhiteSpace(query.StorageAssetId))
                        throw new ArgumentException("读取已存饰品时必须指定存储单元。", nameof(query));
                    items = await ReadStoredItemsAsync(
                        credential,
                        device,
                        query.StorageAssetId.Trim(),
                        cancellationToken).ConfigureAwait(false);
                    message = access.IsBusy
                        ? "悠悠库存正在同步，当前明细可能稍后更新。"
                        : $"当前存储单元共 {items.Count} 件饰品。";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(query), query.View, "未知的悠悠库存视图。");
            }

            return new YouPinInventoryStorageViewState(
                query,
                access,
                items,
                units,
                message,
                DateTime.Now);
        }

        public async Task<YouPinInventoryStorageWriteResult> WriteAsync(
            Settings settings,
            YouPinInventoryStorageTransferCommand command,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(command);
            YouPinCredential credential = GetRequiredCredential(settings);
            string device = ResolveDeviceToken(credential, settings);
            string path = command.Direction == YouPinInventoryStorageDirection.Store
                ? YouPinUrls.InventoryStorageAdd
                : YouPinUrls.InventoryStorageTakeOut;
            string action = command.Direction == YouPinInventoryStorageDirection.Store
                ? "存入悠悠库存"
                : "取出悠悠库存";

            using var request = CreateH5Request(
                path,
                new
                {
                    assetIds = command.AssetIds,
                    storageAssetId = command.StorageAssetId
                },
                credential,
                device);
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                action,
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, action);
            using JsonDocument document = await YouPinMobileApiClient
                .ReadJsonDocumentAsync(response, action)
                .ConfigureAwait(false);
            int code = GetRequiredApiCode(document.RootElement, action);
            string message = FirstText(
                GetString(document.RootElement, "msg", "Msg", "message", "Message"),
                code == 0 ? "悠悠已接受操作。" : $"悠悠有品返回 code={code}");
            if (YouPinMobileApiClient.IsLoginExpired(code, message))
                throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");
            return new YouPinInventoryStorageWriteResult(code == 0, YouPinMobileApiClient.Sanitize(message));
        }

        public void Dispose() => _http.Dispose();

        private async Task<YouPinInventoryStorageAccess> ReadAccessAsync(
            YouPinCredential credential,
            string device,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + YouPinUrls.InventoryStorageAccessInfo)
            {
                Content = YouPinMobileApiClient.JsonContent(new
                {
                    refreshType = 0,
                    Sessionid = device
                })
            };
            YouPinMobileApiClient.ApplyHeaders(
                request,
                credential.Token,
                device,
                credential.Uk,
                appVersion: CaptureAlignedAppVersion);
            YouPinMobileApiClient.ApplyH5WebViewHeaders(
                request,
                credential.Uk,
                credential.UserId,
                device,
                CaptureAlignedAppVersion,
                CaptureAlignedWebViewVersion);
            using JsonDocument document = await SendForJsonAsync(
                request,
                "读取悠悠库存存取状态",
                cancellationToken).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            EnsureApiSuccess(root, "读取悠悠库存存取状态");
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || data.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("悠悠库存存取状态响应缺少 data。");
            }

            TryGetProperty(data, out JsonElement storable, "inventoryStorable", "InventoryStorable");
            TryGetProperty(data, out JsonElement storage, "inventoryStorage", "InventoryStorage");
            int handleStatus = GetRequiredInt(
                data,
                "悠悠库存存取状态响应",
                "handleStatus",
                "HandleStatus");
            int storableStatus = GetInt(storable, "status", "Status");
            int storageStatus = GetInt(storage, "status", "Status");
            int storableCount = GetInt(storable, "storableCount", "StorableCount");
            int storedCount = Math.Max(
                GetInt(storage, "hasStorageItemCount", "HasStorageItemCount"),
                GetInt(storage, "hasItemCount", "HasItemCount"));
            int takeOutCount = GetInt(storage, "canTakeOutCount", "CanTakeOutCount");
            bool busy = handleStatus != 0;
            return new YouPinInventoryStorageAccess(
                busy,
                !busy && storableStatus == 1 && storableCount > 0,
                !busy && storageStatus == 1 && takeOutCount > 0,
                storableCount,
                storedCount,
                takeOutCount,
                FirstText(
                    GetString(storable, "storableShowText", "StorableShowText"),
                    GetString(storable, "storageTipText", "StorageTipText"),
                    GetString(storable, "blackStorageTipText", "BlackStorageTipText")),
                FirstText(
                    GetString(storage, "storageShowText", "StorageShowText"),
                    GetString(storage, "storageTipText", "StorageTipText"),
                    GetString(storage, "blackStorageTipText", "BlackStorageTipText")));
        }

        private async Task<IReadOnlyList<YouPinInventoryStorageItem>> ReadStorableItemsAsync(
            YouPinCredential credential,
            string device,
            CancellationToken cancellationToken)
        {
            using var request = CreateH5Request(
                YouPinUrls.InventoryStorageStorableList,
                new { isMerge = 0 },
                credential,
                device);
            using JsonDocument document = await SendForJsonAsync(
                request,
                "读取悠悠可存入库存",
                cancellationToken).ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠可存入库存");
            return ParseItems(document.RootElement, storageAssetId: string.Empty);
        }

        private async Task<IReadOnlyList<YouPinInventoryStorageUnit>> ReadStorageUnitsAsync(
            YouPinCredential credential,
            string device,
            int addCount,
            int queryType,
            CancellationToken cancellationToken)
        {
            using var request = CreateH5Request(
                YouPinUrls.InventoryStorageList,
                new { addCount, queryType },
                credential,
                device);
            using JsonDocument document = await SendForJsonAsync(
                request,
                "读取悠悠库存存储单元",
                cancellationToken).ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠库存存储单元");
            return ParseUnits(document.RootElement);
        }

        private async Task<IReadOnlyList<YouPinInventoryStorageItem>> ReadStoredItemsAsync(
            YouPinCredential credential,
            string device,
            string storageAssetId,
            CancellationToken cancellationToken)
        {
            using var request = CreateH5Request(
                YouPinUrls.InventoryStorageUnitItemList,
                new { isMerge = 0, storageAssetId },
                credential,
                device);
            using JsonDocument document = await SendForJsonAsync(
                request,
                "读取悠悠已存入饰品",
                cancellationToken).ConfigureAwait(false);
            EnsureApiSuccess(document.RootElement, "读取悠悠已存入饰品");
            return ParseItems(document.RootElement, storageAssetId);
        }

        private HttpRequestMessage CreateH5Request(
            string path,
            object payload,
            YouPinCredential credential,
            string device)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, YouPinUrls.ApiBase + path)
            {
                Content = YouPinMobileApiClient.JsonContent(payload)
            };
            YouPinMobileApiClient.ApplyHeaders(
                request,
                credential.Token,
                device,
                credential.Uk,
                appVersion: CaptureAlignedAppVersion);
            YouPinMobileApiClient.ApplyH5WebViewHeaders(
                request,
                credential.Uk,
                credential.UserId,
                device,
                CaptureAlignedAppVersion,
                CaptureAlignedWebViewVersion);
            return request;
        }

        private async Task<JsonDocument> SendForJsonAsync(
            HttpRequestMessage request,
            string action,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await YouPinMobileApiClient.SendAsync(
                _http,
                request,
                action,
                cancellationToken).ConfigureAwait(false);
            EnsureHttpSuccess(response, action);
            return await YouPinMobileApiClient
                .ReadJsonDocumentAsync(response, action)
                .ConfigureAwait(false);
        }

        internal static IReadOnlyList<YouPinInventoryStorageItem> ParseItems(
            JsonElement root,
            string storageAssetId)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || data.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("悠悠库存饰品列表响应缺少 data.itemInfos。");
            }

            bool hasRows = TryGetProperty(data, out JsonElement rows, "itemInfos", "ItemInfos");
            if ((!hasRows || rows.ValueKind == JsonValueKind.Null)
                && (HasExplicitZeroItemCount(data) || IsEmptyStorageUnitStatusResult(data)))
                return Array.Empty<YouPinInventoryStorageItem>();
            if (!hasRows || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("悠悠库存饰品列表响应缺少 data.itemInfos。");

            var result = new List<YouPinInventoryStorageItem>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string assetId = FirstText(GetString(row, "steamAssetId", "SteamAssetId", "assetId", "AssetId"));
                if (string.IsNullOrWhiteSpace(assetId))
                    throw new InvalidOperationException("悠悠库存饰品列表存在缺少资产 ID 的条目。");
                result.Add(new YouPinInventoryStorageItem(
                    assetId,
                    storageAssetId,
                    FirstText(
                        GetString(row, "commodityName", "CommodityName"),
                        GetString(row, "commodityHashName", "CommodityHashName"),
                        "未知饰品"),
                    FirstText(GetString(row, "commodityHashName", "CommodityHashName")),
                    FirstText(GetString(row, "templateId", "TemplateId")),
                    FirstText(GetString(row, "exteriorName", "ExteriorName")),
                    GetImageUrl(row),
                    Convert.ToDecimal(GetDouble(row, "markPrice", "MarkPrice")),
                    GetInt(row, "isMerge", "IsMerge") != 0,
                    FirstText(GetString(data, "itemShowText", "ItemShowText"))));
            }
            return result;
        }

        private static bool IsEmptyStorageUnitStatusResult(JsonElement data)
        {
            // Empty storage units are returned without itemInfos and with only this zero status.
            return data.GetPropertyCount() == 1
                && data.TryGetProperty("queryStorageUnitStatus", out JsonElement status)
                && status.ValueKind == JsonValueKind.Number
                && status.TryGetInt32(out int number)
                && number == 0;
        }

        private static bool HasExplicitZeroItemCount(JsonElement data)
        {
            bool foundPrimaryCount = false;
            foreach (string name in new[] { "itemCount", "ItemCount", "totalCount", "TotalCount", "canTakeOutCount", "CanTakeOutCount" })
            {
                if (!data.TryGetProperty(name, out JsonElement count))
                    continue;

                if (name is "itemCount" or "ItemCount" or "totalCount" or "TotalCount")
                    foundPrimaryCount = true;
                if (!IsZeroInteger(count))
                    return false;
            }
            return foundPrimaryCount;
        }

        private static bool IsZeroInteger(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number == 0;
            return value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int textNumber)
                && textNumber == 0;
        }

        internal static IReadOnlyList<YouPinInventoryStorageUnit> ParseUnits(JsonElement root)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || data.ValueKind != JsonValueKind.Object
                || !TryGetProperty(data, out JsonElement rows, "inventoryStorables", "InventoryStorables")
                || rows.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("悠悠库存单元列表响应缺少 data.inventoryStorables。");
            }

            var result = new List<YouPinInventoryStorageUnit>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string storageAssetId = FirstText(GetString(row, "assetId", "AssetId", "storageAssetId", "StorageAssetId"));
                if (string.IsNullOrWhiteSpace(storageAssetId))
                    continue;
                int itemCount = GetInt(row, "itemCount", "ItemCount");
                result.Add(new YouPinInventoryStorageUnit(
                    storageAssetId,
                    FirstText(GetString(row, "name", "Name"), "未命名存储单元"),
                    FirstText(GetString(row, "imgUlr", "ImgUlr", "imgUrl", "ImgUrl")),
                    itemCount,
                    FirstText(GetString(row, "itemCountShowText", "ItemCountShowText"), $"{itemCount} 件"),
                    GetInt(row, "status", "Status")));
            }
            return result;
        }

        private YouPinCredential GetRequiredCredential(Settings settings)
        {
            YouPinCredential? credential = _authService.GetCredential(settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                throw new InvalidOperationException("请先登录悠悠有品后再使用库存存取。");
            return credential;
        }

        private string ResolveDeviceToken(YouPinCredential credential, Settings settings)
        {
            return string.IsNullOrWhiteSpace(credential.DeviceToken)
                ? _authService.EnsureDeviceToken(settings)
                : credential.DeviceToken.Trim();
        }

        private static void EnsureHttpSuccess(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"{action}失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        private static void EnsureApiSuccess(JsonElement root, string action)
        {
            int code = GetRequiredApiCode(root, action);
            if (code == 0)
                return;

            string message = FirstText(
                GetString(root, "msg", "Msg", "message", "Message"),
                $"悠悠有品返回 code={code}");
            if (YouPinMobileApiClient.IsLoginExpired(code, message))
                throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");

            string sanitized = YouPinMobileApiClient.Sanitize(message);
            if (YouPinInventoryStorageQueryPendingException.IsMatch(sanitized))
            {
                throw new YouPinInventoryStorageQueryPendingException(
                    $"{action}：{sanitized}");
            }

            throw new InvalidOperationException($"{action}失败：{sanitized}");
        }

        private static int GetRequiredApiCode(JsonElement root, string action)
        {
            return GetRequiredInt(root, $"{action}响应", "code", "Code");
        }

        private static int GetRequiredInt(
            JsonElement root,
            string context,
            params string[] propertyNames)
        {
            if (!TryGetProperty(root, out JsonElement valueElement, propertyNames))
                throw new InvalidOperationException($"{context}缺少 {propertyNames[0]}，无法确认操作结果。");

            if (valueElement.ValueKind == JsonValueKind.Number
                && valueElement.TryGetInt32(out int numericValue))
            {
                return numericValue;
            }
            if (valueElement.ValueKind == JsonValueKind.String
                && int.TryParse(
                    valueElement.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int textValue))
            {
                return textValue;
            }

            throw new InvalidOperationException($"{context}中的 {propertyNames[0]} 无效，无法确认操作结果。");
        }
    }
}
