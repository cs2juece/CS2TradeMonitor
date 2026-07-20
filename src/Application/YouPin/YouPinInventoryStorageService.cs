using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinInventoryStorageService : IYouPinInventoryStorageService, IDisposable
    {
        private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan ConfirmationDelay = TimeSpan.FromSeconds(2);
        private const int ConfirmationAttempts = 3;

        private readonly IYouPinInventoryStorageAdapter _adapter;
        private readonly IYouPinAuthService _authService;
        private readonly IClock _clock;
        private readonly IAppDiagnostics _diagnostics;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly ConcurrentDictionary<string, DateTime> _recentOperations = new(StringComparer.Ordinal);

        public YouPinInventoryStorageService(
            IYouPinInventoryStorageAdapter adapter,
            IYouPinAuthService authService,
            IClock clock,
            IAppDiagnostics diagnostics)
            : this(adapter, authService, clock, diagnostics, static (delay, token) => Task.Delay(delay, token))
        {
        }

        internal YouPinInventoryStorageService(
            IYouPinInventoryStorageAdapter adapter,
            IYouPinAuthService authService,
            IClock clock,
            IAppDiagnostics diagnostics,
            Func<TimeSpan, CancellationToken, Task> delayAsync)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
        }

        public Task<YouPinInventoryStorageViewState> LoadAsync(
            Settings settings,
            YouPinInventoryStorageQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(query);
            EnsureCredential(settings);
            ValidateQuery(query);
            return _adapter.ReadAsync(settings, query, cancellationToken);
        }

        public async Task<YouPinInventoryStorageTransferResult> ExecuteAsync(
            Settings settings,
            YouPinInventoryStorageTransferCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(command);
            YouPinCredential credential = EnsureCredential(settings);

            string[] assetIds = NormalizeAssetIds(command.AssetIds);
            if (assetIds.Length == 0)
                return Rejected("请至少选择一件饰品。", command);
            if (string.IsNullOrWhiteSpace(command.StorageAssetId))
                return Rejected("请选择悠悠库存存储单元。", command);

            var normalizedCommand = command with
            {
                StorageAssetId = command.StorageAssetId.Trim(),
                AssetIds = assetIds
            };

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RemoveExpiredOperations();
                string operationKey = BuildOperationKey(credential, normalizedCommand);
                if (!_recentOperations.TryAdd(operationKey, _clock.UtcNow))
                {
                    return Rejected(
                        "相同存取操作刚刚已经提交，请等待悠悠同步后刷新确认。",
                        normalizedCommand);
                }

                bool writeStarted = false;
                try
                {
                    YouPinInventoryStorageViewState preflight = await ReadSourceAsync(
                        settings,
                        normalizedCommand,
                        cancellationToken).ConfigureAwait(false);
                    string? validationError = ValidatePreflight(preflight, normalizedCommand);
                    if (!string.IsNullOrWhiteSpace(validationError))
                    {
                        _recentOperations.TryRemove(operationKey, out _);
                        return Rejected(validationError, normalizedCommand);
                    }

                    await TradeWriteOperationGate.WaitAsync(
                        BuildAccountGateKey(credential),
                        cancellationToken).ConfigureAwait(false);
                    writeStarted = true;
                    YouPinInventoryStorageWriteResult write = await _adapter.WriteAsync(
                        settings,
                        normalizedCommand,
                        cancellationToken).ConfigureAwait(false);
                    if (!write.Accepted)
                    {
                        _recentOperations.TryRemove(operationKey, out _);
                        return Rejected(FirstText(write.Message, "悠悠有品未接受本次操作。"), normalizedCommand);
                    }

                    _diagnostics.Info(
                        "YouPinInventoryStorage",
                        $"Transfer accepted. Direction={normalizedCommand.Direction}; Count={assetIds.Length}; Operation={operationKey}");

                    YouPinInventoryStorageViewState? confirmed = await TryConfirmAsync(
                        settings,
                        normalizedCommand,
                        cancellationToken).ConfigureAwait(false);
                    if (confirmed != null)
                    {
                        return new YouPinInventoryStorageTransferResult(
                            YouPinInventoryStorageTransferStatus.Confirmed,
                            normalizedCommand.Direction == YouPinInventoryStorageDirection.Store
                                ? $"已确认存入 {assetIds.Length} 件饰品。"
                                : $"已确认取出 {assetIds.Length} 件饰品。",
                            confirmed);
                    }

                    return new YouPinInventoryStorageTransferResult(
                        YouPinInventoryStorageTransferStatus.AcceptedPending,
                        "悠悠已接受操作，库存仍在同步。请稍后刷新确认，不要重复提交。");
                }
                catch (OperationCanceledException) when (!writeStarted)
                {
                    _recentOperations.TryRemove(operationKey, out _);
                    throw;
                }
                catch (Exception ex) when (!writeStarted)
                {
                    _recentOperations.TryRemove(operationKey, out _);
                    _diagnostics.Error("YouPinInventoryStorage", "Transfer failed before write started.", ex);
                    return Rejected(BuildFriendlyError(ex), normalizedCommand);
                }
                catch (OperationCanceledException ex)
                {
                    _diagnostics.Error("YouPinInventoryStorage", "Transfer result is uncertain after write started.", ex);
                    return new YouPinInventoryStorageTransferResult(
                        YouPinInventoryStorageTransferStatus.AcceptedPending,
                        "请求可能已经提交，但等待结果时被取消。请刷新悠悠库存确认，不要重复提交。");
                }
                catch (Exception ex)
                {
                    _diagnostics.Error("YouPinInventoryStorage", "Transfer result is uncertain after write started.", ex);
                    return new YouPinInventoryStorageTransferResult(
                        YouPinInventoryStorageTransferStatus.AcceptedPending,
                        "请求可能已经提交，但响应或回读失败。请刷新悠悠库存确认，不要重复提交。");
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public void Dispose() => _operationGate.Dispose();

        private async Task<YouPinInventoryStorageViewState> ReadSourceAsync(
            Settings settings,
            YouPinInventoryStorageTransferCommand command,
            CancellationToken cancellationToken)
        {
            YouPinInventoryStorageQuery query = command.Direction == YouPinInventoryStorageDirection.Store
                ? new YouPinInventoryStorageQuery(
                    YouPinInventoryStorageView.Storable,
                    RequestedCount: command.AssetIds.Count)
                : new YouPinInventoryStorageQuery(YouPinInventoryStorageView.StoredItems, command.StorageAssetId);
            return await _adapter.ReadAsync(settings, query, cancellationToken).ConfigureAwait(false);
        }

        private async Task<YouPinInventoryStorageViewState?> TryConfirmAsync(
            Settings settings,
            YouPinInventoryStorageTransferCommand command,
            CancellationToken cancellationToken)
        {
            var query = new YouPinInventoryStorageQuery(
                YouPinInventoryStorageView.StoredItems,
                command.StorageAssetId);

            for (int attempt = 1; attempt <= ConfirmationAttempts; attempt++)
            {
                await _delayAsync(ConfirmationDelay, cancellationToken).ConfigureAwait(false);
                YouPinInventoryStorageViewState state = await _adapter
                    .ReadAsync(settings, query, cancellationToken)
                    .ConfigureAwait(false);
                var current = state.Items.Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
                bool confirmed = command.Direction == YouPinInventoryStorageDirection.Store
                    ? command.AssetIds.All(current.Contains)
                    : command.AssetIds.All(assetId => !current.Contains(assetId));
                if (confirmed && !state.Access.IsBusy)
                    return state;
            }

            return null;
        }

        private static string? ValidatePreflight(
            YouPinInventoryStorageViewState state,
            YouPinInventoryStorageTransferCommand command)
        {
            if (state.Access.IsBusy)
                return "悠悠库存正在同步，请稍后刷新后再操作。";
            if (command.Direction == YouPinInventoryStorageDirection.Store && !state.Access.CanStore)
                return FirstText(state.Access.StoreMessage, "当前没有可存入的饰品。");
            if (command.Direction == YouPinInventoryStorageDirection.TakeOut && !state.Access.CanTakeOut)
                return FirstText(state.Access.TakeOutMessage, "当前没有可取出的饰品。");
            if (command.Direction == YouPinInventoryStorageDirection.Store
                && !state.Units.Any(unit => string.Equals(
                    unit.StorageAssetId,
                    command.StorageAssetId,
                    StringComparison.Ordinal)))
            {
                return "目标存储单元当前不可用，请刷新后重新选择。";
            }

            var available = state.Items.Select(item => item.AssetId).ToHashSet(StringComparer.Ordinal);
            return command.AssetIds.All(available.Contains)
                ? null
                : "所选饰品状态已经变化，请刷新列表后重新选择。";
        }

        private YouPinCredential EnsureCredential(Settings settings)
        {
            YouPinCredential? credential = _authService.GetCredential(settings);
            if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                throw new InvalidOperationException("请先登录悠悠有品后再使用库存存取。");
            return credential;
        }

        private static void ValidateQuery(YouPinInventoryStorageQuery query)
        {
            if (query.View == YouPinInventoryStorageView.StoredItems
                && string.IsNullOrWhiteSpace(query.StorageAssetId))
            {
                throw new ArgumentException("读取已存饰品时必须指定存储单元。", nameof(query));
            }
        }

        private static string[] NormalizeAssetIds(IReadOnlyList<string>? assetIds)
        {
            return (assetIds ?? Array.Empty<string>())
                .Select(value => (value ?? string.Empty).Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private void RemoveExpiredOperations()
        {
            DateTime cutoff = _clock.UtcNow - DuplicateWindow;
            foreach ((string key, DateTime time) in _recentOperations)
            {
                if (time < cutoff)
                    _recentOperations.TryRemove(key, out _);
            }
        }

        private static string BuildOperationKey(
            YouPinCredential credential,
            YouPinInventoryStorageTransferCommand command)
        {
            string account = FirstText(credential.UserId, credential.Uk, credential.DeviceToken, "unknown");
            string assets = string.Join(",", command.AssetIds.OrderBy(value => value, StringComparer.Ordinal));
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{account}|{command.Direction}|{command.StorageAssetId}|{assets}"));
            return Convert.ToHexString(hash.AsSpan(0, 8));
        }

        private static string BuildAccountGateKey(YouPinCredential credential)
        {
            return "YouPin:" + FirstText(credential.UserId, credential.Uk, credential.DeviceToken, "unknown");
        }

        private static YouPinInventoryStorageTransferResult Rejected(
            string message,
            YouPinInventoryStorageTransferCommand command)
        {
            return new YouPinInventoryStorageTransferResult(
                YouPinInventoryStorageTransferStatus.Rejected,
                string.IsNullOrWhiteSpace(message)
                    ? command.Direction == YouPinInventoryStorageDirection.Store
                        ? "存入失败。"
                        : "取出失败。"
                    : message.Trim());
        }

        private static string BuildFriendlyError(Exception ex)
        {
            string message = YouPinMobileApiClient.Sanitize(ex.Message);
            if (YouPinMobileApiClient.LooksLikeSignatureFailure(message))
                return "悠悠库存存取接口要求官方签名，当前请求无法通过验证。";
            if (YouPinMobileApiClient.LooksLikeRateLimitOrRiskControl(message))
                return "悠悠有品提示操作频繁或触发风控，请稍后再试。";
            return string.IsNullOrWhiteSpace(message) ? "库存存取请求失败。" : message;
        }

        private static string FirstText(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return string.Empty;
        }
    }
}
