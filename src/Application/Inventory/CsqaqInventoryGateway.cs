using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.Application.Inventory
{
    public sealed class CsqaqInventoryGateway : ICsqaqInventoryGateway
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _http;
        private readonly CsqaqRequestRateLimiter _requestRateLimiter;

        public CsqaqInventoryGateway(
            IDomesticHttpClientFactory httpFactory,
            CsqaqRequestRateLimiter requestRateLimiter)
        {
            ArgumentNullException.ThrowIfNull(httpFactory);
            _requestRateLimiter = requestRateLimiter ?? throw new ArgumentNullException(nameof(requestRateLimiter));
            _http = httpFactory.Create(20, new Uri(CsqaqUrls.ApiBase), useCookies: false);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("CS2TradeMonitor/1.37 (Windows; local inventory monitor)");
        }

        internal CsqaqInventoryGateway(
            IDomesticHttpClientFactory httpFactory,
            TimeSpan minimumRequestInterval,
            Func<TimeSpan, CancellationToken, Task> delay)
            : this(httpFactory, new CsqaqRequestRateLimiter(minimumRequestInterval, delay))
        {
        }

        public async Task<CsqaqInventoryTargetRecord?> ResolveTargetAsync(
            string apiToken,
            string steamId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                return null;

            using JsonDocument document = await PostAsync(
                "/api/v1/monitor/get_task_list",
                apiToken,
                new
                {
                    page_index = 1,
                    page_size = 50,
                    order = "TREND_TIME",
                    search = steamId.Trim()
                },
                cancellationToken).ConfigureAwait(false);

            JsonElement root = document.RootElement;
            EnsureSuccess(root);
            if (!TryGetProperty(root, "data", out JsonElement data)
                || !TryGetProperty(data, "res", out JsonElement rows)
                || rows.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement row in rows.EnumerateArray())
            {
                string candidateSteamId = GetString(row, "steam_id");
                if (!string.Equals(candidateSteamId, steamId.Trim(), StringComparison.Ordinal))
                    continue;

                int taskId = GetInt32(row, "id");
                if (taskId <= 0)
                    continue;

                return new CsqaqInventoryTargetRecord(
                    taskId,
                    candidateSteamId,
                    GetString(row, "steam_name"),
                    Math.Max(0, GetInt32(row, "amount")),
                    GetDateTimeOffset(row, "updated_at"),
                    GetDateTimeOffset(row, "traded_at"));
            }

            return null;
        }

        public async Task<IReadOnlyList<LocalInventoryChangeEvent>> GetRecentEventsAsync(
            string apiToken,
            CsqaqInventoryTargetRecord target,
            CancellationToken cancellationToken)
        {
            const int pageSize = 100;
            const int maximumPages = 5;
            var result = new List<LocalInventoryChangeEvent>();
            for (int pageIndex = 1; pageIndex <= maximumPages; pageIndex++)
            {
                using JsonDocument document = await PostAsync(
                    "/api/v1/task/get_task_business",
                    apiToken,
                    new
                    {
                        page_index = pageIndex,
                        page_size = pageSize,
                        task_id = target.TaskId,
                        search = string.Empty,
                        type = "ALL"
                    },
                    cancellationToken).ConfigureAwait(false);

                JsonElement root = document.RootElement;
                EnsureSuccess(root);
                if (!TryGetProperty(root, "data", out JsonElement data)
                    || !TryGetProperty(data, "trades", out JsonElement rows)
                    || rows.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                int rowCount = rows.GetArrayLength();
                foreach (JsonElement row in rows.EnumerateArray())
                    result.Add(CreateEvent(target, row));

                if (rowCount < pageSize)
                    break;
                if (pageIndex == maximumPages)
                    throw new InvalidOperationException("CSQAQ 单次返回的库存异动超过 500 条，为避免漏报，本轮未更新基线。请缩短检查间隔后重试。");
            }

            return result
                .GroupBy(item => item.EventKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(item => item.OccurredAt)
                .ThenBy(item => item.EventKey, StringComparer.Ordinal)
                .ToArray();
        }

        private static LocalInventoryChangeEvent CreateEvent(
            CsqaqInventoryTargetRecord target,
            JsonElement row)
        {
            int goodId = GetInt32(row, "good_id");
            string marketName = GetString(row, "market_name");
            int count = Math.Max(1, GetInt32(row, "count"));
            int rawType = GetInt32(row, "type");
            DateTimeOffset occurredAt = GetDateTimeOffset(row, "created_at") ?? DateTimeOffset.MinValue;
            LocalInventoryChangeKind kind = MapKind(rawType);
            string eventKey = string.Join(
                ':',
                target.TaskId.ToString(CultureInfo.InvariantCulture),
                occurredAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                goodId.ToString(CultureInfo.InvariantCulture),
                rawType.ToString(CultureInfo.InvariantCulture),
                count.ToString(CultureInfo.InvariantCulture));

            return new LocalInventoryChangeEvent(
                eventKey,
                target.TaskId,
                target.SteamId,
                target.SteamName,
                goodId,
                marketName,
                count,
                GetInt32(row, "tradable") == 1,
                kind,
                GetKindText(kind),
                occurredAt,
                "CSQAQ");
        }

        internal static LocalInventoryChangeKind MapKind(int rawType)
        {
            return rawType switch
            {
                0 => LocalInventoryChangeKind.Baseline,
                1 => LocalInventoryChangeKind.Purchase,
                2 => LocalInventoryChangeKind.Sale,
                3 => LocalInventoryChangeKind.Deposit,
                4 => LocalInventoryChangeKind.Withdraw,
                5 => LocalInventoryChangeKind.CooldownRecovered,
                6 => LocalInventoryChangeKind.WithdrawOrRecovered,
                7 => LocalInventoryChangeKind.SaleOrDeposit,
                _ => LocalInventoryChangeKind.Unknown
            };
        }

        internal static string GetKindText(LocalInventoryChangeKind kind)
        {
            return kind switch
            {
                LocalInventoryChangeKind.Baseline => "默认库存",
                LocalInventoryChangeKind.Purchase => "买入",
                LocalInventoryChangeKind.Sale => "卖出",
                LocalInventoryChangeKind.Deposit => "存入",
                LocalInventoryChangeKind.Withdraw => "取出",
                LocalInventoryChangeKind.CooldownRecovered => "CD恢复",
                LocalInventoryChangeKind.WithdrawOrRecovered => "取出/恢复",
                LocalInventoryChangeKind.SaleOrDeposit => "卖出/存入",
                _ => "未知"
            };
        }

        private async Task<JsonDocument> PostAsync(
            string endpoint,
            string apiToken,
            object body,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiToken))
                throw new InvalidOperationException("未配置 CSQAQ API Token。");

            await _requestRateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("ApiToken", apiToken.Trim());
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"CSQAQ 库存接口返回 HTTP {(int)response.StatusCode}。", null, response.StatusCode);

            try
            {
                return JsonDocument.Parse(responseBody);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("CSQAQ 库存接口返回了无法识别的数据。", ex);
            }
        }

        private static void EnsureSuccess(JsonElement root)
        {
            int code = GetInt32(root, "code");
            if (code == 200)
                return;

            string message = GetString(root, "msg");
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                ? $"CSQAQ 库存接口返回业务错误 {code}。"
                : $"CSQAQ 库存接口返回业务错误 {code}：{message}");
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                return true;

            value = default;
            return false;
        }

        private static string GetString(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value))
                return string.Empty;

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.ToString();
        }

        private static int GetInt32(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value))
                return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;
            return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
        }

        private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string name)
        {
            string text = GetString(element, name);
            if (DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                    out DateTimeOffset value))
            {
                return value;
            }

            return null;
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
