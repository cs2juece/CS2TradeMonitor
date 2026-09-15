using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Ports;
using CS2TradeMonitor.Shared.Trading;
using IClock = CS2TradeMonitor.Shared.Ports.IClock;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.Core;

namespace CS2TradeMonitor.Application.YouPin
{
    public sealed class YouPinProfitLossService : IYouPinProfitLossService
    {
        private const string BaseUrl = YouPinMobileApiClient.BaseUrl;
        private const int CompletedOrderPageSize = 20;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private static readonly YouPinOrderEndpoint[] BuyEndpoints =
        {
            new("购买记录", "/api/youpin/bff/trade/sale/v1/buy/list", YouPinOrderEndpointKind.SaleBuyList)
        };

        private static readonly YouPinOrderEndpoint[] SellEndpoints =
        {
            new("出售记录", "/api/youpin/bff/trade/sale/v1/sell/list", YouPinOrderEndpointKind.SaleSellList)
        };

        private static readonly object InstanceLock = new();
        private static YouPinProfitLossService? _instance;
        private static Func<YouPinProfitLossServiceDependencies>? _platformFactory;
        public static YouPinProfitLossService Instance
        {
            get
            {
                lock (InstanceLock)
                    return _instance ??= new YouPinProfitLossService(ResolvePlatformDependencies());
            }
        }

        private readonly IYouPinAuthService _authService;
        private readonly HttpClient _http;
        private readonly IClock _clock;
        private readonly CS2TradeMonitor.Shared.Ports.IAppDiagnostics _diagnostics;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private readonly object _stateLock = new();
        private readonly string _historyPath;

        private YouPinProfitLossHistory _history = new();
        private DateTime _lastSync = DateTime.MinValue;
        private string _lastStatus = "未同步";
        private string _lastError = "";

        public event Action? DataUpdated;

        internal YouPinProfitLossService(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            string? historyPath = null)
            : this(CreateCompatibilityDependencies(authService, httpFactory, historyPath))
        {
        }

        private YouPinProfitLossService(YouPinProfitLossServiceDependencies dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            _authService = dependencies.AuthService ?? throw new ArgumentNullException(nameof(dependencies.AuthService));
            _http = (dependencies.HttpFactory ?? throw new ArgumentNullException(nameof(dependencies.HttpFactory))).Create(25);
            _clock = dependencies.Clock ?? throw new ArgumentNullException(nameof(dependencies.Clock));
            _diagnostics = dependencies.Diagnostics ?? throw new ArgumentNullException(nameof(dependencies.Diagnostics));
            _historyPath = Path.GetFullPath(
                dependencies.PlatformHost?.HistoryPath
                ?? throw new ArgumentNullException(nameof(dependencies.PlatformHost)));
            _history = LoadHistory();
            _lastSync = _history.LastSync;
            if (_history.Records.Count > 0)
                _lastStatus = $"已读取本地缓存：{_history.Records.Count} 条成交记录";
        }

        public static void ConfigurePlatform(Func<YouPinProfitLossServiceDependencies> platformFactory)
        {
            ArgumentNullException.ThrowIfNull(platformFactory);
            lock (InstanceLock)
            {
                if (_instance == null)
                    _platformFactory = platformFactory;
            }
        }

        public static YouPinProfitLossService Create(YouPinProfitLossServiceDependencies dependencies)
            => new(dependencies);

        private static YouPinProfitLossServiceDependencies ResolvePlatformDependencies()
            => _platformFactory?.Invoke()
                ?? throw new InvalidOperationException("悠悠吃米/亏米服务的平台依赖尚未配置。");

        public YouPinProfitLossState GetState(Settings? settings)
        {
            bool hasCredential = HasCredential(settings);
            lock (_stateLock)
            {
                var records = _history.Records
                    .Where(x => x != null)
                    .GroupBy(YouPinProfitLossRecordProjection.BuildRecordDedupeKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();

                return new YouPinProfitLossState
                {
                    HasCredential = hasCredential,
                    LastSync = _lastSync,
                    LastStatus = hasCredential ? _lastStatus : "未登录",
                    LastError = _lastError,
                    Records = records,
                    Rows = YouPinProfitLossRecordProjection.BuildRows(records)
                };
            }
        }

        public async Task<YouPinProfitLossRefreshResult> RefreshAsync(Settings? settings, CancellationToken cancellationToken = default)
        {
            if (!await _syncLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return YouPinProfitLossRefreshResult.Skip("已有吃米/亏米统计同步正在执行，本次请求已合并。");

            try
            {
                var credential = _authService.GetCredential(settings);
                if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                    return YouPinProfitLossRefreshResult.Failed("请先在悠悠有品登录区完成登录。");

                string token = credential.Token.Trim();
                string device = string.IsNullOrWhiteSpace(credential.DeviceToken)
                    ? YouPinMobileApiClient.GetDeviceToken()
                    : credential.DeviceToken.Trim();
                string uk = credential.Uk;

                var buyRecords = await FetchOrderRecordsAsync(YouPinProfitLossDirection.Buy, BuyEndpoints, token, device, uk, cancellationToken).ConfigureAwait(false);
                var sellRecords = await FetchOrderRecordsAsync(YouPinProfitLossDirection.Sell, SellEndpoints, token, device, uk, cancellationToken).ConfigureAwait(false);
                var freshRecords = buyRecords.Concat(sellRecords).ToList();

                int added = MergeRecords(freshRecords);
                _lastSync = _clock.UtcNow.LocalDateTime;
                _lastStatus = $"同步成功：新增 {added} 条成交记录";
                _lastError = "";
                SaveHistory();
                RaiseDataUpdated();

                return YouPinProfitLossRefreshResult.Success(_lastStatus, added);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string message = YouPinMobileApiClient.WrapException(ex, "同步悠悠有品成交统计").Message;
                lock (_stateLock)
                {
                    _lastError = message;
                    _lastStatus = "同步失败，已保留本地缓存";
                }
                RaiseDataUpdated();
                return YouPinProfitLossRefreshResult.Failed(message);
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private async Task<List<YouPinProfitLossRecord>> FetchOrderRecordsAsync(
            YouPinProfitLossDirection direction,
            IReadOnlyList<YouPinOrderEndpoint> endpoints,
            string token,
            string device,
            string uk,
            CancellationToken cancellationToken)
        {
            string firstError = "";
            foreach (var endpoint in endpoints)
            {
                try
                {
                    var result = await FetchOrderRecordsFromEndpointAsync(direction, endpoint, token, device, uk, cancellationToken).ConfigureAwait(false);
                    if (result.Success)
                        return result.Records;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (string.IsNullOrWhiteSpace(firstError))
                        firstError = $"{endpoint.Name}读取失败：{YouPinMobileApiClient.Sanitize(ex.Message)}";
                }
            }

            if (!string.IsNullOrWhiteSpace(firstError))
                throw new InvalidOperationException(firstError);

            return new List<YouPinProfitLossRecord>();
        }

        private async Task<YouPinOrderFetchResult> FetchOrderRecordsFromEndpointAsync(
            YouPinProfitLossDirection direction,
            YouPinOrderEndpoint endpoint,
            string token,
            string device,
            string uk,
            CancellationToken cancellationToken)
        {
            var result = new List<YouPinProfitLossRecord>();
            int pageIndex = 1;
            const int pageSize = CompletedOrderPageSize;

            while (pageIndex <= 20)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + endpoint.Path);
                var payload = BuildPayload(endpoint.Kind, pageIndex, pageSize, device);
                req.Content = YouPinMobileApiClient.JsonContent(payload);
                YouPinMobileApiClient.ApplyHeaders(req, token, device, uk);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, endpoint.Name, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

                using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(resp, endpoint.Name).ConfigureAwait(false);
                var root = doc.RootElement;
                int code = GetInt(root, "code", "Code");
                if (code != 0)
                {
                    string msg = GetString(root, "msg", "Msg", "message", "Message") ?? "";
                    if (YouPinMobileApiClient.IsLoginExpired(code, msg))
                        throw new InvalidOperationException("悠悠有品登录状态失效，请重新登录。");

                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品返回 code={code}" : msg);
                }

                var elements = YouPinProfitLossRecordProjection.ExtractOrderArray(root);
                if (pageIndex == 1 && result.Count == 0 && elements.Count == 0 && HasNullOrderListPayload(root))
                    throw new InvalidOperationException($"{endpoint.Name}返回空列表结构，请稍后重试。");

                foreach (var item in elements)
                {
                    var records = YouPinProfitLossRecordProjection.ParseRecords(item, direction, endpoint.Kind);
                    if (records.Count == 0)
                        continue;

                    foreach (var record in records)
                        record.SourceEndpoint = endpoint.Path;

                    result.AddRange(records);
                }

                if (elements.Count == 0 || elements.Count < pageSize)
                    break;

                pageIndex++;
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
            }

            return new YouPinOrderFetchResult(true, result);
        }

        private static bool HasNullOrderListPayload(JsonElement root)
        {
            if (!TryGetProperty(root, out var data, "data", "Data") || data.ValueKind != JsonValueKind.Object)
                return false;

            foreach (string name in new[] { "orderList", "OrderList", "list", "List", "items", "Items" })
            {
                if (data.TryGetProperty(name, out var value))
                    return value.ValueKind == JsonValueKind.Null;
            }

            return false;
        }

        private static Dictionary<string, object> BuildPayload(
            YouPinOrderEndpointKind kind,
            int pageIndex,
            int pageSize,
            string device)
        {
            if (kind == YouPinOrderEndpointKind.SaleBuyList)
            {
                // 抓包确认该接口会混入取消历史，因此请求和响应都按“已完成”口径收敛。
                return new Dictionary<string, object>
                {
                    ["keys"] = "",
                    ["orderStatus"] = 340,
                    ["pageIndex"] = pageIndex,
                    ["pageSize"] = pageSize,
                    ["presenterId"] = 0,
                    ["sceneType"] = 0,
                    ["Sessionid"] = device
                };
            }

            if (kind == YouPinOrderEndpointKind.SaleSellList)
            {
                // 出售记录接口抓包使用字符串状态值；响应仍会二次校验 orderStatus/orderStatusName。
                return new Dictionary<string, object>
                {
                    ["keys"] = "",
                    ["orderStatus"] = "340",
                    ["pageIndex"] = pageIndex,
                    ["pageSize"] = pageSize,
                    ["Sessionid"] = device
                };
            }

            return new Dictionary<string, object>
            {
                ["gameId"] = 730,
                ["GameId"] = 730,
                ["GameID"] = 730,
                ["pageIndex"] = pageIndex,
                ["pageSize"] = pageSize,
                ["PageIndex"] = pageIndex,
                ["PageSize"] = pageSize,
                ["orderStatus"] = 4,
                ["status"] = 4,
                ["Sessionid"] = device
            };
        }

        private int MergeRecords(List<YouPinProfitLossRecord> records)
        {
            lock (_stateLock)
            {
                _history.Records ??= new List<YouPinProfitLossRecord>();
                var known = new HashSet<string>(_history.Records.Select(YouPinProfitLossRecordProjection.BuildRecordDedupeKey), StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (var record in records)
                {
                    string key = YouPinProfitLossRecordProjection.BuildRecordDedupeKey(record);
                    if (!known.Add(key))
                        continue;

                    _history.Records.Add(record);
                    added++;
                }

                _history.Records = _history.Records
                    .Where(x => x != null)
                    .OrderBy(x => x.Time == DateTime.MinValue ? DateTime.MaxValue : x.Time)
                    .TakeLast(5000)
                    .ToList();
                _history.LastSync = _clock.UtcNow.LocalDateTime;
                return added;
            }
        }

        private YouPinProfitLossHistory LoadHistory()
        {
            try
            {
                if (!File.Exists(_historyPath)) return new YouPinProfitLossHistory();
                string json = File.ReadAllText(_historyPath);
                var history = JsonSerializer.Deserialize<YouPinProfitLossHistory>(json, JsonOptions) ?? new YouPinProfitLossHistory();
                history.Records ??= new List<YouPinProfitLossRecord>();
                return history;
            }
            catch
            {
                return new YouPinProfitLossHistory();
            }
        }

        private void SaveHistory()
        {
            try
            {
                lock (_stateLock)
                {
                    _history.LastSync = _lastSync;
                    WriteTextAtomic(_historyPath, JsonSerializer.Serialize(_history, JsonOptions));
                }
            }
            catch (Exception ex)
            {
                ReportDiagnostic(
                    "youpin.profit-loss.history-save-failed",
                    $"保存吃米/亏米统计缓存失败: {YouPinMobileApiClient.Sanitize(ex.Message)}",
                    DiagnosticSeverity.Warning);
            }
        }

        private bool HasCredential(Settings? settings)
        {
            var credential = _authService.GetCredential(settings);
            return credential != null && !string.IsNullOrWhiteSpace(credential.Token);
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (string name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                    return true;
            }

            value = default;
            return false;
        }

        private static string? GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return null;
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.ToString()
            };
        }

        private static int GetInt(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value)) return value;
            if (int.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        private void RaiseDataUpdated()
        {
            try
            {
                DataUpdated?.Invoke();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportDiagnostic(
                    "youpin.profit-loss.data-updated-handler-failed",
                    "吃米/亏米统计更新事件处理失败。",
                    DiagnosticSeverity.Warning);
            }
        }

        private void ReportDiagnostic(string code, string message, DiagnosticSeverity severity)
        {
            try
            {
                _ = _diagnostics.ReportAsync(new DiagnosticEvent(
                    code,
                    message,
                    severity,
                    _clock.UtcNow)).AsTask();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Diagnostics must never change the desktop-authoritative service result.
            }
        }

        private static void WriteTextAtomic(string path, string content)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                    // Preserve the original write result; stale temp cleanup is best-effort.
                }
            }
        }

        private static YouPinProfitLossServiceDependencies CreateCompatibilityDependencies(
            IYouPinAuthService authService,
            IDomesticHttpClientFactory httpFactory,
            string? historyPath)
        {
            string path = string.IsNullOrWhiteSpace(historyPath)
                ? Path.Combine(Path.GetTempPath(), "cs2trade-profitloss-tests", Guid.NewGuid().ToString("N"), "history.json")
                : Path.GetFullPath(historyPath);
            return new YouPinProfitLossServiceDependencies(
                authService,
                httpFactory,
                CompatibilityClock.Instance,
                new CompatibilityPlatformHost(path),
                CompatibilityDiagnostics.Instance);
        }

        private sealed record CompatibilityPlatformHost(string HistoryPath) : IYouPinProfitLossPlatformHost;

        private sealed class CompatibilityClock : IClock
        {
            public static CompatibilityClock Instance { get; } = new();
            public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
                => Task.Delay(delay, cancellationToken);
        }

        private sealed class CompatibilityDiagnostics : CS2TradeMonitor.Shared.Ports.IAppDiagnostics
        {
            public static CompatibilityDiagnostics Instance { get; } = new();
            public ValueTask ReportAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _http.Dispose();
            _syncLock.Dispose();
        }

        private readonly record struct YouPinOrderEndpoint(
            string Name,
            string Path,
            YouPinOrderEndpointKind Kind = YouPinOrderEndpointKind.Generic);

        private readonly record struct YouPinOrderFetchResult(bool Success, List<YouPinProfitLossRecord> Records);
    }

    public class YouPinProfitLossState
    {
        public bool HasCredential { get; set; }
        public DateTime LastSync { get; set; } = DateTime.MinValue;
        public string LastStatus { get; set; } = "";
        public string LastError { get; set; } = "";
        public List<YouPinProfitLossRecord> Records { get; set; } = new();
        public List<YouPinProfitLossRow> Rows { get; set; } = new();

        [JsonIgnore] public double BuyTotal => Rows.Sum(x => x.BuyAmount);
        [JsonIgnore] public double SellTotal => Rows.Sum(x => x.SellAmount);
        [JsonIgnore] public double NetTotal => SellTotal - BuyTotal;
    }

    public class YouPinProfitLossRefreshResult
    {
        public bool Ok { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; } = "";
        public int AddedCount { get; set; }

        public static YouPinProfitLossRefreshResult Success(string message, int addedCount)
            => new() { Ok = true, Message = message, AddedCount = addedCount };

        public static YouPinProfitLossRefreshResult Failed(string message)
            => new() { Ok = false, Message = message };

        public static YouPinProfitLossRefreshResult Skip(string message)
            => new() { Ok = false, Skipped = true, Message = message };
    }
}
