using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Application;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Application.Steam.Auth.Import;
using static CS2TradeMonitor.Application.Steam.SteamOfferLoginRecoveryHelper;
using static CS2TradeMonitor.Application.Steam.SteamOfferMappingHelper;
using static CS2TradeMonitor.Application.Steam.SteamOfferYouPinVerificationHelper;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed partial class SteamOfferService : ISteamOfferService, IManualYouPinOfferAutoConfirmation
    {
        private readonly ISteamConfirmationClient _confirmationClient;
        private readonly ISteamTradeOfferClient _tradeOfferClient;
        private readonly ISteamAuthStore _authStore;
        private readonly ISteamTokenVault _tokenVault;
        private readonly ISteamLoginService _loginService;
        private readonly IYouPinSaleReminderService _youPinSaleReminders;
        private readonly AutoConfirmationService _autoConfirmationService;
        private readonly SteamOfferCredentialWorkflow _credentialWorkflow;
        private readonly SteamOfferAccountRefreshCoordinator _accountRefresh;
        private readonly SteamOfferLoginRecoveryCoordinator _loginRecovery;
        private readonly SteamOfferStateStore _stateStore;
        private readonly HashSet<string> _ignoredTradeOffers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _pendingOfferTriggerSync = new();
        private readonly HashSet<string> _knownPendingOfferIds = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastLoginExpiredNotificationUtc = DateTime.MinValue;
        private int _backgroundEnrichmentVersion;
        private static readonly TimeSpan SteamOfferDetailHistoricalLookupAge = TimeSpan.FromDays(7);
        private const int SteamOfferDetailLookupLimit = 20;

        public Task HandleManuallySentYouPinOfferAsync(
            YouPinSaleOrder order,
            YouPinSaleActionResult sendResult,
            CancellationToken cancellationToken = default)
        {
            return _autoConfirmationService.HandleManuallySentYouPinOfferAsync(order, sendResult, cancellationToken);
        }

        private SteamOfferService()
            : this(SteamServiceRuntimeServices.Resolve())
        {
        }

        internal SteamOfferService(SteamServiceRuntimeServices services)
            : this(
                services.ConfirmationClient,
                services.TradeOfferClient,
                services.AuthStore,
                services.TokenVault,
                services.LoginService,
                services.YouPinSaleReminders)
        {
        }

        internal SteamOfferService(
            ISteamConfirmationClient confirmationClient,
            ISteamTradeOfferClient tradeOfferClient,
            ISteamAuthStore authStore,
            ISteamTokenVault tokenVault,
            ISteamLoginService loginService,
            IYouPinSaleReminderService youPinSaleReminders)
        {
            _confirmationClient = confirmationClient ?? throw new ArgumentNullException(nameof(confirmationClient));
            _tradeOfferClient = tradeOfferClient ?? throw new ArgumentNullException(nameof(tradeOfferClient));
            _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            _tokenVault = tokenVault ?? throw new ArgumentNullException(nameof(tokenVault));
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
            _youPinSaleReminders = youPinSaleReminders ?? throw new ArgumentNullException(nameof(youPinSaleReminders));
            _stateStore = new SteamOfferStateStore(PrepareManualOffer);
            _autoConfirmationService = new AutoConfirmationService(this, _youPinSaleReminders, RaiseDataUpdated);
            ImmediateAutoProcessingAsync = _autoConfirmationService.ProcessLoadedOffersNowAsync;
            _accountRefresh = new SteamOfferAccountRefreshCoordinator(
                _authStore,
                _loginService,
                RaiseDataUpdated);
            _loginRecovery = new SteamOfferLoginRecoveryCoordinator(
                _authStore,
                _loginService,
                RaiseDataUpdated);
            _credentialWorkflow = new SteamOfferCredentialWorkflow(
                _authStore,
                _tokenVault,
                _loginService,
                RaiseDataUpdated,
                _accountRefresh.QueuePersonaNameRefresh,
                _accountRefresh.QueueSteamApiKeyRefresh);
            _accountRefresh.QueueSteamApiKeyRefresh();
        }

        public static SteamOfferService Instance { get; } = new();

        public event Action? DataUpdated;

        internal Task LastBackgroundEnrichmentTask { get; private set; } = Task.CompletedTask;
        internal Func<CancellationToken, Task> ImmediateAutoProcessingAsync { get; set; }

        public long SteamTimeOffsetSeconds => _confirmationClient.TimeOffset;

        public long GetCorrectedSteamTimeSeconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _confirmationClient.TimeOffset;
        }

        public Task SyncSteamTimeOffsetAsync()
        {
            return _confirmationClient.SyncTimeOffsetAsync();
        }

        public SteamOfferState GetState()
        {
            return _stateStore.BuildState(
                _authStore.GetStatus(),
                SteamOfferStateHelper.BuildAutoConfirmState(_autoConfirmationService),
                SteamOfferStateHelper.BuildAutoTradeState(_autoConfirmationService));
        }

        public void StartAutoConfirm(int intervalSeconds, bool autoAcceptSafe, bool allowYouPinVerifiedAccept = true)
        {
            _autoConfirmationService.Start(intervalSeconds, autoAcceptSafe, allowYouPinVerifiedAccept);
            RaiseDataUpdated();
        }

        public void StartAutoTrade(SteamAutoTradeSettings settings)
        {
            _autoConfirmationService.StartAutoTrade(settings);
            RaiseDataUpdated();
        }

        public void StopAutoConfirm()
        {
            _autoConfirmationService.Stop();
            RaiseDataUpdated();
        }

        public void RecordAutoTradeAction(SteamAutoTradeRecord record)
        {
            _autoConfirmationService.Record(record);
            RaiseDataUpdated();
        }

        public void HighlightTradeOffer(string tradeOfferId)
        {
            _stateStore.HighlightTradeOffer(
                tradeOfferId,
                "悠悠有品待办",
                "从悠悠待办跳转的 Steam 报价");
            RaiseDataUpdated();
        }

        public SteamOfferActionResult AddManualTradeOffer(string tradeOfferId)
        {
            string id = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id))
                return SteamOfferActionResult.Failed("Steam 报价号为空。");

            _stateStore.HighlightTradeOffer(
                id,
                "手动添加",
                "手动添加的 Steam 报价，请刷新或打开 Steam 页面核对。");

            RaiseDataUpdated();
            return SteamOfferActionResult.Success("已加入报价列表，请核对后再处理。");
        }

        public SteamOfferActionResult ImportMaFileText(string jsonText, string sourcePath = "")
        {
            return _credentialWorkflow.ImportMaFileText(jsonText, sourcePath);
        }

        public SteamOfferImportFileResult LoadMaFileImportFile(string sourcePath)
        {
            return _credentialWorkflow.LoadMaFileImportFile(sourcePath);
        }

        public SteamOfferActionResult UpdateSession(
            string sessionId,
            string steamLoginSecure,
            string steamLogin = "",
            string apiKey = "",
            string accessToken = "",
            string refreshToken = "",
            string steamId = "")
        {
            return _credentialWorkflow.UpdateSession(sessionId, steamLoginSecure, steamLogin, apiKey, accessToken, refreshToken, steamId);
        }

        public Task<SteamOfferActionResult> EnsureSessionAsync()
        {
            return EnsureSteamLoginStateAsync("manual-check");
        }

        public async Task<SteamOfferActionResult> RestoreLoginStateFromTokenTextAsync(string tokenText)
        {
            return await _credentialWorkflow.RestoreLoginStateFromTokenTextAsync(tokenText);
        }

        public async Task<SteamOfferActionResult> LoginAndConfigureAsync(SteamAutoLoginRequest request)
        {
            return await _credentialWorkflow.LoginAndConfigureAsync(request);
        }

        public async Task<SteamOfferActionResult> RefreshSteamApiKeyAsync()
        {
            return await _accountRefresh.RefreshSteamApiKeyAsync();
        }

        public SteamOfferActionResult SaveManualTokenSecrets(string sharedSecret, string identitySecret)
        {
            return _credentialWorkflow.SaveManualTokenSecrets(sharedSecret, identitySecret);
        }

        public Task<SteamOfferActionResult> ReloginAsync(string reason)
        {
            return EnsureSteamLoginStateAsync(reason);
        }

        public Task<SteamOfferActionResult> EnsureSteamLoginStateAsync(string reason, bool allowPasswordFallback = true, bool preferPasswordFallback = false)
        {
            return _loginRecovery.EnsureSteamLoginStateAsync(reason, allowPasswordFallback, preferPasswordFallback);
        }

        public void ClearCredentials()
        {
            _authStore.Clear();
            _stateStore.ClearAll("已清除 Steam 凭据");
            RaiseDataUpdated();
        }

        public SteamOfferActionResult ClearTokenSecrets()
        {
            if (!_authStore.ClearTokenSecrets())
                return SteamOfferActionResult.Failed("未保存 Steam 令牌，无需清空。", "missing-credential");

            _stateStore.SetStatus("已清空 Steam 令牌密钥", "");
            RaiseDataUpdated();
            return SteamOfferActionResult.Success("已清空令牌密钥。Steam 登录状态保留；验证码和移动确认需要重新保存 shared_secret / identity_secret。");
        }

        public SteamOfferActionResult ClearLoginState()
        {
            if (!_authStore.ClearLoginState())
                return SteamOfferActionResult.Failed("未保存 Steam 凭据，无需清空登录状态。", "missing-credential");

            _stateStore.ClearAll("已清空 Steam 登录状态");
            RaiseDataUpdated();
            return SteamOfferActionResult.Success("已清空 Steam 登录状态。令牌密钥保留；需要报价时请重新登录或用 Token 恢复。");
        }

        public Task<SteamOfferActionResult> LoadOffersAsync(bool useMock = false, bool allowAutoRelogin = true)
        {
            return LoadOffersCoreAsync(useMock, allowAutoRelogin, triggerImmediateAutoProcessing: true);
        }

        public Task<SteamOfferActionResult> LoadOffersForAutoTradeAsync()
        {
            return LoadOffersCoreAsync(useMock: false, allowAutoRelogin: true, triggerImmediateAutoProcessing: false);
        }

        private async Task<SteamOfferActionResult> LoadOffersCoreAsync(
            bool useMock = false,
            bool allowAutoRelogin = true,
            bool triggerImmediateAutoProcessing = true)
        {
            if (useMock)
            {
#if DEBUG
                var mockOffers = SteamOfferMockOfferFactory.Create(DateTime.Now);
                foreach (var offer in mockOffers)
                {
                    SteamOfferSafetyEvaluator.Evaluate(offer);
                }
                SetOffers(mockOffers, "模拟报价已加载。", "");
                return SteamOfferActionResult.Success("模拟报价已加载。");
#else
                SetOffers(new List<SteamOfferItem>(), "模拟报价仅开发环境可用。", "");
                return SteamOfferActionResult.Failed("模拟报价仅开发环境可用。");
#endif
            }

            var credential = _authStore.Load();
            if (credential == null)
            {
                SetOffers(new List<SteamOfferItem>(), "未绑定 Steam 令牌。", "未绑定 Steam 令牌。");
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌。");
            }

            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
            {
                if (allowAutoRelogin && HasRecoverableLoginState(credential))
                {
                    SetOffers(new List<SteamOfferItem>(), "Steam 登录状态缺失，正在自动重新登录…", "");
                    var relogin = await EnsureSteamLoginStateAsync("Steam 登录状态缺失", preferPasswordFallback: true);
                    if (relogin.Ok)
                        return await LoadOffersCoreAsync(useMock, allowAutoRelogin: false, triggerImmediateAutoProcessing);
                    SetOffers(new List<SteamOfferItem>(), "Steam 未登录。", relogin.Message);
                    return SteamOfferActionResult.Failed("Steam 未登录，自动恢复失败：" + relogin.Message);
                }

                SetOffers(new List<SteamOfferItem>(), "Steam 未登录。", "Steam 未登录。");
                return SteamOfferActionResult.Failed("请先在“绑定/管理令牌”中登录并自动配置，或使用 Steam 网页登录保存 Steam 登录状态。");
            }

            if (string.IsNullOrWhiteSpace(credential.IdentitySecret) || string.IsNullOrWhiteSpace(credential.DeviceId) || string.IsNullOrWhiteSpace(credential.SteamId))
            {
                SetOffers(new List<SteamOfferItem>(), "Steam 令牌字段不完整。", "Steam 令牌字段不完整。");
                return SteamOfferActionResult.Failed("Steam 令牌字段不完整，请重新导入 maFile。");
            }

            if (SteamOfferAccountRefreshCoordinator.NeedsSteamApiKeyRefresh(credential))
            {
                _accountRefresh.QueueSteamApiKeyRefresh();
            }

            try
            {
                var loadWatch = Stopwatch.StartNew();
                var offers = new List<SteamOfferItem>();
                string partialWarning = "";
                bool tradeOffersApiSucceeded = false;
                int apiRawOfferCount = 0;
                TradeOffersResult? webTradeOffersForEnrichment = null;
                if (!string.IsNullOrWhiteSpace(credential.ApiKey))
                {
                    var apiWatch = Stopwatch.StartNew();
                    try
                    {
                        var tradeOffers = await _tradeOfferClient.GetTradeOffersAsync(credential);
                        apiRawOfferCount = CountTradeOfferDetails(tradeOffers);
                        offers = ConvertTradeOffersToItems(tradeOffers);
                        tradeOffersApiSucceeded = true;
                        LogLoadStage("api", apiWatch);
                    }
                    catch (Exception ex) when (IsNonAuthSteamOfferListFailure(ex))
                    {
                        LogLoadStage("api-failed", apiWatch);
                        partialWarning = BuildSteamOfferListWarning("LoadTradeOffersApi", ex);
                        SteamOfferAuditLog.InfoThrottled(
                            "steam-tradeoffers-api-partial-failure",
                            "Steam Web API trade offers unavailable, falling back to web session: " + partialWarning,
                            TimeSpan.FromMinutes(5));
                    }
                }

                bool needsWebFallback = !tradeOffersApiSucceeded;
                if (needsWebFallback
                    && !string.IsNullOrWhiteSpace(credential.SessionId)
                    && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                {
                    var webWatch = Stopwatch.StartNew();
                    try
                    {
                        var webTradeOffers = await _tradeOfferClient.GetTradeOffersFromWebSessionAsync(credential);
                        webTradeOffersForEnrichment = webTradeOffers;
                        MergeTradeOfferItems(offers, ConvertTradeOffersToItems(webTradeOffers));
                        if (string.IsNullOrWhiteSpace(credential.ApiKey))
                            partialWarning = "Steam Web API Key 未获取，已改用网页登录状态读取报价；后台会按冷却策略继续补齐 Key。";
                        LogLoadStage("web-fallback", webWatch);
                    }
                    catch (SteamAuthExpiredException ex)
                    {
                        LogLoadStage("web-fallback-auth-failed", webWatch);
                        string webWarning = BuildSteamOfferListWarning("LoadTradeOffersWeb", ex);
                        if (allowAutoRelogin && HasRecoverableLoginState(credential))
                        {
                            SetOffers(new List<SteamOfferItem>(), "Steam 网页登录状态失效，正在自动重新登录…", webWarning);
                            var relogin = await EnsureSteamLoginStateAsync("Steam 网页报价列表登录状态失效", preferPasswordFallback: true);
                            if (relogin.Ok)
                                return await LoadOffersCoreAsync(useMock, allowAutoRelogin: false, triggerImmediateAutoProcessing);

                            if (!tradeOffersApiSucceeded && string.IsNullOrWhiteSpace(credential.ApiKey))
                            {
                                MarkSavedSessionInvalid(credential, relogin.Message);
                                SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效。", relogin.Message);
                                return SteamOfferActionResult.Failed("Steam 登录状态失效，自动恢复失败：" + relogin.Message, relogin.Code);
                            }

                            partialWarning = AppendPartialWarning(partialWarning, "Steam 网页报价列表登录状态失效，自动重登失败：" + relogin.Message);
                            SteamOfferAuditLog.InfoThrottled(
                                "steam-tradeoffers-web-auth-relogin-failed",
                                "Steam web trade offers auth expired and auto relogin failed, keeping API result. Reason=" + SteamOfferAuditLog.RedactSecrets(relogin.Message),
                                TimeSpan.FromMinutes(5));
                        }
                        else
                        {
                            if (!tradeOffersApiSucceeded && string.IsNullOrWhiteSpace(credential.ApiKey))
                                throw;

                            partialWarning = AppendPartialWarning(partialWarning, "Steam 网页报价列表暂不可用：" + webWarning);
                            SteamOfferAuditLog.InfoThrottled(
                                "steam-tradeoffers-web-auth-partial-failure",
                                "Steam web trade offers unavailable, keeping API result and saved session. Reason=" + webWarning,
                                TimeSpan.FromMinutes(5));
                        }
                    }
                    catch (Exception ex) when (IsNonAuthSteamOfferListFailure(ex))
                    {
                        LogLoadStage("web-fallback-failed", webWatch);
                        string webWarning = BuildSteamOfferListWarning("LoadTradeOffersWeb", ex);
                        partialWarning = AppendPartialWarning(
                            partialWarning,
                            string.IsNullOrWhiteSpace(credential.ApiKey)
                                ? "Steam Web API Key 未获取，网页报价列表暂不可用：" + webWarning
                                : "Steam 网页报价列表暂不可用：" + webWarning);
                        SteamOfferAuditLog.InfoThrottled(
                            "steam-tradeoffers-web-partial-failure",
                            "Steam web trade offers unavailable, falling back to mobile confirmations: " + webWarning,
                            TimeSpan.FromMinutes(5));
                    }
                }

                offers = PrepareOffersForDisplay(offers);
                string status = BuildForegroundLoadOffersStatus(offers.Count, partialWarning);
                bool hasNewPendingOffer = UpdateKnownPendingOffers(offers);
                SetOffers(offers, status, partialWarning);
                if (triggerImmediateAutoProcessing && hasNewPendingOffer)
                    await ImmediateAutoProcessingAsync(CancellationToken.None).ConfigureAwait(false);
                bool foregroundOfferListFetched = tradeOffersApiSucceeded || webTradeOffersForEnrichment != null;
                QueueBackgroundOfferEnrichment(
                    credential,
                    offers,
                    partialWarning,
                    webTradeOffersForEnrichment,
                    foregroundOfferListFetched,
                    triggerImmediateAutoProcessing);
                LogLoadStage("foreground-total", loadWatch);
                SteamOfferAuditLog.LogRefreshResult(true, offers.Count, status);
                return SteamOfferActionResult.Success(status);
            }
            catch (SteamAuthExpiredException ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (allowAutoRelogin && HasRecoverableLoginState(credential))
                {
                    SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效，正在自动重新登录…", error);
                    var relogin = await EnsureSteamLoginStateAsync("Steam 登录状态失效", preferPasswordFallback: true);
                    if (relogin.Ok)
                        return await LoadOffersCoreAsync(useMock, allowAutoRelogin: false, triggerImmediateAutoProcessing);

                    MarkSavedSessionInvalid(credential, relogin.Message);
                    PauseSteamBackgroundAfterLoginExpired("Steam 报价后台处理已暂停，请重新登录或用 Token 恢复。");
                    if (IsNetworkError(relogin.Code))
                    {
                        SetOffers(new List<SteamOfferItem>(), "Steam 登录状态已失效，自动重登暂时无法完成。", relogin.Message);
                        return SteamOfferActionResult.Failed("Steam 登录状态失效，自动重登暂时失败：" + relogin.Message, relogin.Code);
                    }

                    SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效。", relogin.Message);
                    return SteamOfferActionResult.Failed("Steam 登录状态失效，自动恢复失败：" + relogin.Message, ex.Code);
                }

                MarkSavedSessionInvalid(credential, error);
                PauseSteamBackgroundAfterLoginExpired("Steam 报价后台处理已暂停，请重新登录或用 Token 恢复。");
                SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效。", error);
                return SteamOfferActionResult.Failed("Steam 登录状态失效，请重新登录或用 Token 恢复。", ex.Code);
            }
            catch (SteamTransientSteamException ex)
            {
                string error = BuildSteamOfferListWarning("LoadOffers", ex);
                SetOffers(new List<SteamOfferItem>(), "Steam 登录状态已保存，但当前无法刷新。", error);
                SteamOfferAuditLog.LogRefreshResult(false, 0, "暂时无法刷新 Steam 报价：" + error);
                SteamOfferAuditLog.DiagnosticError("Load Steam offers temporarily failed", ex);
                return SteamOfferActionResult.Failed("暂时无法刷新 Steam 报价：" + error, ex.Code);
            }
            catch (HttpRequestException ex)
            {
                string error = BuildSteamOfferListWarning("LoadOffers", ex);
                SetOffers(new List<SteamOfferItem>(), "Steam 登录状态已保存，但当前无法刷新。", error);
                SteamOfferAuditLog.LogRefreshResult(false, 0, "暂时无法刷新 Steam 报价：" + error);
                SteamOfferAuditLog.DiagnosticError("Load Steam offers network failed: " + error);
                return SteamOfferActionResult.Failed("暂时无法刷新 Steam 报价：" + error, SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (TaskCanceledException ex)
            {
                string error = BuildSteamOfferListWarning("LoadOffers", ex);
                SetOffers(new List<SteamOfferItem>(), "Steam 登录状态已保存，但当前无法刷新。", error);
                SteamOfferAuditLog.LogRefreshResult(false, 0, "暂时无法刷新 Steam 报价：" + error);
                SteamOfferAuditLog.DiagnosticError("Load Steam offers timed out: " + error);
                return SteamOfferActionResult.Failed("暂时无法刷新 Steam 报价：" + error, SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (Exception ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (allowAutoRelogin && HasRecoverableLoginState(credential) && LooksLikeExplicitAuthExpired(error))
                {
                    SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效，正在自动重新登录…", error);
                    var relogin = await EnsureSteamLoginStateAsync("Steam 登录状态失效", preferPasswordFallback: true);
                    if (relogin.Ok)
                        return await LoadOffersCoreAsync(useMock, allowAutoRelogin: false, triggerImmediateAutoProcessing);

                    MarkSavedSessionInvalid(credential, relogin.Message);
                    PauseSteamBackgroundAfterLoginExpired("Steam 报价后台处理已暂停，请重新登录或用 Token 恢复。");
                    if (IsNetworkError(relogin.Code))
                    {
                        SetOffers(new List<SteamOfferItem>(), "Steam 登录状态已失效，自动重登暂时无法完成。", relogin.Message);
                        return SteamOfferActionResult.Failed("Steam 登录状态失效，自动重登暂时失败：" + relogin.Message, relogin.Code);
                    }

                    SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效。", relogin.Message);
                    return SteamOfferActionResult.Failed("Steam 登录状态失效，自动恢复失败：" + relogin.Message);
                }

                if (LooksLikeExplicitAuthExpired(error))
                {
                    MarkSavedSessionInvalid(credential, error);
                    PauseSteamBackgroundAfterLoginExpired("Steam 报价后台处理已暂停，请重新登录或用 Token 恢复。");
                    SetOffers(new List<SteamOfferItem>(), "Steam 登录状态失效。", error);
                    return SteamOfferActionResult.Failed("Steam 登录状态失效，请重新登录或用 Token 恢复。");
                }

                SetOffers(new List<SteamOfferItem>(), "Steam 登录状态已保存，但当前无法刷新。", error);
                SteamOfferAuditLog.LogRefreshResult(false, 0, "暂时无法刷新 Steam 报价：" + error);
                SteamOfferAuditLog.DiagnosticError("Load Steam confirmations failed", ex);
                return SteamOfferActionResult.Failed("暂时无法刷新 Steam 报价：" + error);
            }
        }

        private static List<SteamOfferItem> FilterOffersNeedingUserAction(IEnumerable<SteamOfferItem> offers)
        {
            return offers
                .Where(NeedsUserAction)
                .ToList();
        }

        private static int CountTradeOfferDetails(TradeOffersResult? result)
        {
            return (result?.SentOffers?.Count ?? 0) + (result?.ReceivedOffers?.Count ?? 0);
        }

        private List<SteamOfferItem> PrepareOffersForDisplay(IEnumerable<SteamOfferItem> offers)
        {
            var list = offers
                .Select(CloneOffer)
                .ToList();
            EnrichWithYouPinVerification(list);
            list = FilterOffersNeedingUserAction(list)
                .Where(x => !IsIgnoredTradeOffer(x.TradeOfferId))
                .ToList();
            foreach (var offer in list)
            {
                SteamOfferSafetyEvaluator.Evaluate(offer);
            }
            return list;
        }

        private static bool NeedsUserAction(SteamOfferItem offer)
        {
            if (offer == null || offer.Status != SteamOfferStatus.Pending)
                return false;
            SteamAutoTradeDirection direction = SteamAutoTradePlanner.GetDirection(offer);
            return SteamOfferSafetyEvaluator.IsPureIncomingGift(offer)
                || (offer.VerifiedByYouPin
                    && direction is SteamAutoTradeDirection.Incoming or SteamAutoTradeDirection.TwoWay)
                || RequiresSteamMobileConfirmation(offer);
        }

        private static bool RequiresSteamMobileConfirmation(SteamOfferItem offer)
        {
            if (offer == null)
                return false;
            if (!string.IsNullOrWhiteSpace(offer.ConfirmationId) && !string.IsNullOrWhiteSpace(offer.ConfirmationKey))
                return true;
            return offer.Status == SteamOfferStatus.Pending
                && LooksLikeSteamMobileConfirmationOffer(offer);
        }

        private static bool LooksLikeSteamMobileConfirmationOffer(SteamOfferItem offer)
        {
            return string.Equals(offer.ConfirmationType, "Steam 移动确认", StringComparison.OrdinalIgnoreCase)
                || string.Equals(offer.ConfirmationType, "Steam 手机确认", StringComparison.OrdinalIgnoreCase)
                || string.Equals(offer.Source, "Steam移动确认", StringComparison.OrdinalIgnoreCase)
                || offer.ItemSummary.Contains("待确认", StringComparison.OrdinalIgnoreCase)
                || offer.ItemSummary.Contains("手机确认", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLoadOffersStatus(int count, string partialWarning)
        {
            string warning = BuildUserFacingPartialWarning(partialWarning);
            return string.IsNullOrWhiteSpace(warning)
                ? count == 0 ? "暂无需你处理的报价。" : $"已刷新 {count} 条报价。"
                : count == 0 ? "未读取到可处理报价。" + warning : $"已刷新 {count} 条报价。" + warning;
        }

        private static string BuildForegroundLoadOffersStatus(int count, string partialWarning)
        {
            string status = BuildLoadOffersStatus(count, partialWarning);
            if (!string.IsNullOrWhiteSpace(partialWarning))
                return status;

            return count == 0
                ? "暂无需你处理的报价。"
                : status;
        }

        private static bool ShouldSuppressBackgroundWarning(int displayOfferCount, bool mobileConfirmationsFetched, int mobileConfirmationCount)
        {
            return displayOfferCount == 0
                && mobileConfirmationsFetched
                && mobileConfirmationCount == 0;
        }

        private static string BuildUserFacingPartialWarning(string partialWarning)
        {
            string text = (partialWarning ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "";

            if (text.Contains("限流", StringComparison.OrdinalIgnoreCase)
                || text.Contains("rate", StringComparison.OrdinalIgnoreCase))
                return "Steam 暂时限流，请稍后再刷新。";

            if (text.Contains("登录状态", StringComparison.OrdinalIgnoreCase)
                || text.Contains("needauth", StringComparison.OrdinalIgnoreCase)
                || text.Contains("login", StringComparison.OrdinalIgnoreCase))
                return "Steam 登录状态暂时无法确认，请重新检测或登录。";

            if (LooksLikeSteamNetworkUnavailableWarning(text))
                return "Steam 网络不可用，报价暂未同步。请开启加速器/代理后刷新。";

            if (text.Contains("部分报价数据暂时未同步", StringComparison.OrdinalIgnoreCase))
                return text;

            if (text.Contains("手机确认", StringComparison.OrdinalIgnoreCase)
                || text.Contains("mobile", StringComparison.OrdinalIgnoreCase)
                || text.Contains("FetchMobileConfirmations", StringComparison.OrdinalIgnoreCase))
                return "部分报价数据暂时未同步，请稍后刷新或在 Steam 中处理。";

            return "部分 Steam 数据暂时不可用，请稍后刷新。";
        }

        private static bool LooksLikeSteamNetworkUnavailableWarning(string text)
        {
            return text.Contains("网络", StringComparison.OrdinalIgnoreCase)
                || text.Contains("代理", StringComparison.OrdinalIgnoreCase)
                || text.Contains("超时", StringComparison.OrdinalIgnoreCase)
                || text.Contains("请求失败", StringComparison.OrdinalIgnoreCase)
                || text.Contains("HttpRequestError", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SecureConnectionError", StringComparison.OrdinalIgnoreCase)
                || text.Contains("BackgroundTradeOffersWeb", StringComparison.OrdinalIgnoreCase)
                || text.Contains("LoadTradeOffersWeb", StringComparison.OrdinalIgnoreCase)
                || text.Contains("网页报价", StringComparison.OrdinalIgnoreCase);
        }

        private static void MergeTradeOfferItems(List<SteamOfferItem> target, List<SteamOfferItem> source)
        {
            var byId = target
                .Where(x => !string.IsNullOrWhiteSpace(x.TradeOfferId))
                .GroupBy(x => x.TradeOfferId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in source)
            {
                string tradeOfferId = (item.TradeOfferId ?? "").Trim();
                if (string.IsNullOrWhiteSpace(tradeOfferId))
                    continue;

                if (!byId.TryGetValue(tradeOfferId, out var existing))
                {
                    target.Add(item);
                    byId[tradeOfferId] = item;
                    continue;
                }

                ApplySupplementalTradeOfferDetails(existing, item);
            }
        }

        private static void ApplySupplementalTradeOfferDetails(SteamOfferItem target, SteamOfferItem details)
        {
            bool targetHasItemDirection = target.ItemsToGive.Count > 0 || target.ItemsToReceive.Count > 0;
            if (!targetHasItemDirection || NeedsTradeOfferDetail(target))
            {
                ApplyTradeOfferDetails(target, details);
                return;
            }

            if (!string.IsNullOrWhiteSpace(details.PartnerSteamId))
                target.PartnerSteamId = details.PartnerSteamId;
            if (!string.IsNullOrWhiteSpace(details.PartnerName))
                target.PartnerName = details.PartnerName;
            if (target.CreatedAt == DateTime.MinValue && details.CreatedAt != DateTime.MinValue)
                target.CreatedAt = details.CreatedAt;
            if (target.ExpirationTime == DateTime.MinValue && details.ExpirationTime != DateTime.MinValue)
                target.ExpirationTime = details.ExpirationTime;
            if (string.IsNullOrWhiteSpace(target.ConfirmationType) && !string.IsNullOrWhiteSpace(details.ConfirmationType))
                target.ConfirmationType = details.ConfirmationType;
            if (target.Type == SteamOfferType.Unknown && details.Type != SteamOfferType.Unknown)
                target.Type = details.Type;
            if (string.IsNullOrWhiteSpace(target.Title) && !string.IsNullOrWhiteSpace(details.Title))
                target.Title = details.Title;
            if (string.IsNullOrWhiteSpace(target.ItemSummary) && !string.IsNullOrWhiteSpace(details.ItemSummary))
                target.ItemSummary = details.ItemSummary;
        }

        private static bool IsRecoverableMobileConfirmationFailure(Exception ex)
        {
            return ex is SteamAuthExpiredException
                || ex is SteamTransientSteamException
                || ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is JsonException
                || ex is InvalidOperationException;
        }

        private static string BuildMobileConfirmationWarning(Exception ex)
        {
            if (ex is SteamAuthExpiredException)
                return "部分报价数据暂时未同步，请稍后刷新或在 Steam 中处理。";

            return "部分报价数据暂时未同步，请检查 Steam 网络/代理后刷新。";
        }

        private static string BuildMobileConfirmationDiagnosticWarning(Exception ex)
        {
            if (ex is SteamAuthExpiredException)
                return "Steam 手机确认暂不可用，请稍后刷新或在手机 Steam 中确认；已保留网页登录状态。";

            return "Steam 手机确认暂不可用：" + BuildSteamOfferListWarning("FetchMobileConfirmations", ex);
        }

        private static string AppendPartialWarning(string current, string warning)
        {
            warning = (warning ?? "").Trim();
            if (string.IsNullOrWhiteSpace(warning))
                return current ?? "";
            if (string.IsNullOrWhiteSpace(current))
                return warning;
            return current.TrimEnd('。') + "；" + warning;
        }

        private void MarkSavedSessionInvalid(SteamAuthCredential credential, string reason)
        {
            credential.SessionId = "";
            credential.SteamLoginSecure = "";
            credential.SteamLogin = "";
            credential.SessionSavedAt = DateTime.MinValue;
            credential.LastAutoReloginResult = string.IsNullOrWhiteSpace(reason)
                ? "Steam 报价接口拒绝已保存登录状态"
                : reason.Trim();
            _authStore.Save(credential);
        }

        public async Task<SteamOfferActionResult> AcceptSafeOffersAsync(bool allowYouPinVerified = true)
        {
            List<SteamOfferItem> safeOffers = _stateStore.GetEligibleOffers(
                x => IsEligibleForSafeBatch(x, allowYouPinVerified) && !IsIgnoredTradeOffer(x.TradeOfferId));

            if (safeOffers.Count == 0)
            {
                return SteamOfferActionResult.Failed("没有可一键同意的纯收货报价。涉及转出库存的报价不会自动处理。");
            }

            int total = safeOffers.Count;
            safeOffers = safeOffers.Take(10).ToList();

            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌。");

            int ok = 0;
            int skipped = 0;
            var messages = new List<string>();
            foreach (var offer in safeOffers)
            {
                var result = await AcceptOfferAsync(offer.TradeOfferId, requireSafe: true);
                if (result.Ok)
                {
                    ok++;
                    if (result.Message.Contains("已跳过", StringComparison.OrdinalIgnoreCase))
                        skipped++;
                }
                else
                {
                    messages.Add(result.Message);
                }
            }

            string skippedText = skipped > 0 ? $"，其中 {skipped} 条已处理/失效并跳过" : "";
            return ok > 0
                ? SteamOfferActionResult.Success(total > 10
                    ? $"已处理本次批量中的 {ok} 条纯收货报价{skippedText}，剩余 {Math.Max(0, total - safeOffers.Count)} 条需再次确认。"
                    : $"已处理纯收货报价 {ok} 条{skippedText}。")
                : SteamOfferActionResult.Failed(messages.Count == 0 ? "纯收货报价处理失败。" : string.Join("；", messages.Take(2)));
        }

        public async Task<SteamOfferActionResult> AcceptOfferAsync(string tradeOfferId, bool requireSafe)
        {
            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                return SteamOfferActionResult.Failed("Steam 报价号为空。");

            SteamOfferItem? offer = _stateStore.FindOffer(tradeOfferId);

            if (offer == null)
                return SteamOfferActionResult.Failed("当前列表中没有找到该 Steam 报价。请先刷新。");
            if (requireSafe && !offer.CanAcceptSafely)
                return SteamOfferActionResult.Failed("该报价不是纯收货报价，不能一键同意。");
            if (requireSafe && !SteamOfferSafetyEvaluator.IsPureIncomingGift(offer))
                return SteamOfferActionResult.Failed("该报价不是纯收货报价，不能一键同意。");

            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌。");

            try
            {
                bool directMobileConfirmation = false;
                if (!string.IsNullOrWhiteSpace(offer.ConfirmationId) && !string.IsNullOrWhiteSpace(offer.ConfirmationKey))
                {
                    await AcknowledgeNewTradeAsync(credential, offer);
                    bool confirmed = await RunSteamWriteWithRetryAsync(
                        credential,
                        "Steam 移动确认",
                        () => _confirmationClient.SendConfirmationAjaxAsync(credential, offer.ConfirmationId, offer.ConfirmationKey, "allow"));
                    if (!confirmed)
                        return SteamOfferActionResult.Failed("Steam 返回确认失败，请稍后重试或打开 Steam 页面手动处理。");
                    directMobileConfirmation = true;
                }
                else
                {
                    await AcknowledgeNewTradeAsync(credential, offer);
                    SteamTradeOfferAcceptResult accept = await RunSteamWriteWithRetryAsync(
                        credential,
                        "Steam 同意报价",
                        () => _tradeOfferClient.AcceptTradeOfferAsync(credential, offer.TradeOfferId));
                    if (accept.AlreadyHandled)
                    {
                        IgnoreTradeOffer(offer.TradeOfferId, accept.Message);
                        MarkOfferStatus(tradeOfferId, SteamOfferStatus.Accepted);
                        return SteamOfferActionResult.Success($"Steam 报价已处理或失效，已跳过：{tradeOfferId}");
                    }
                    if (accept.NeedsMobileConfirmation)
                    {
                        SteamOfferItem? confirmation = await FindMobileConfirmationAsync(credential, offer.TradeOfferId);
                        if (confirmation == null || string.IsNullOrWhiteSpace(confirmation.ConfirmationId) || string.IsNullOrWhiteSpace(confirmation.ConfirmationKey))
                            return SteamOfferActionResult.Failed("Steam 已要求手机确认，但未找到对应移动确认参数，请稍后刷新或在手机 Steam 中确认。");

                        bool confirmed = await RunSteamWriteWithRetryAsync(
                            credential,
                            "Steam 移动确认",
                            () => _confirmationClient.SendConfirmationAjaxAsync(credential, confirmation.ConfirmationId, confirmation.ConfirmationKey, "allow"));
                        if (!confirmed)
                            return SteamOfferActionResult.Failed("Steam 返回确认失败，请稍后重试或打开 Steam 页面手动处理。");
                        SteamOfferAuditLog.LogMobileConfirmation(tradeOfferId, offer.PlatformOrderNo, SteamOfferAuditLog.TriggerUserManual, "用户手动完成Steam手机确认。");
                    }
                    else if (!accept.Ok)
                    {
                        return SteamOfferActionResult.Failed("同意 Steam 报价失败：" + SteamOfferAuditLog.RedactSecrets(accept.Message));
                    }
                }

                MarkOfferStatus(tradeOfferId, SteamOfferStatus.Accepted);
                if (directMobileConfirmation)
                    SteamOfferAuditLog.LogMobileConfirmation(tradeOfferId, offer.PlatformOrderNo, SteamOfferAuditLog.TriggerUserManual, "用户手动完成Steam手机确认。");
                else
                    SteamOfferAuditLog.LogAcceptOffer(tradeOfferId, offer.CanAcceptSafely, offer.VerifiedByYouPin, offer.PlatformOrderNo);
                return SteamOfferActionResult.Success($"已同意 Steam 报价：{tradeOfferId}");
            }
            catch (Exception ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (LooksLikeExplicitAuthExpired(error) || ex is SteamAuthExpiredException)
                    NotifySteamLoginExpiredOnce("Steam 登录失效", "Steam 报价后台处理已暂停，请重新登录或用 Token 恢复。");
                SteamOfferAuditLog.Error($"Accept trade offer failed. TradeOfferId={tradeOfferId}", ex);
                return SteamOfferActionResult.Failed("同意 Steam 报价失败：" + error);
            }
        }

        public async Task<SteamOfferActionResult> AcceptAutoTradeOfferAsync(SteamAutoTradePlanItem plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.TradeOfferId))
                return SteamOfferActionResult.Failed("自动处理计划缺少 Steam 报价号。");

            string tradeOfferId = plan.TradeOfferId.Trim();
            SteamOfferItem? offer = _stateStore.FindOffer(tradeOfferId);
            if (offer == null)
                return SteamOfferActionResult.Failed("当前列表中没有找到该 Steam 报价。请先刷新。");

            if (!IsAutoAcceptPlanStillValid(plan, offer))
                return SteamOfferActionResult.Failed("自动处理校验失败：报价方向、来源或饰品与计划不匹配。");

            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌。", "need_login");

            try
            {
                bool directMobileConfirmation = false;
                if (!string.IsNullOrWhiteSpace(offer.ConfirmationId) && !string.IsNullOrWhiteSpace(offer.ConfirmationKey))
                {
                    if (!SteamAutoTradePlanner.IsMobileConfirmationMatch(plan, offer))
                        return SteamOfferActionResult.Failed("手机确认校验失败：确认项和本轮交易动作不匹配。");

                    await AcknowledgeNewTradeAsync(credential, offer);
                    bool confirmed = await RunSteamWriteWithRetryAsync(
                        credential,
                        "Steam 移动确认",
                        () => _confirmationClient.SendConfirmationAjaxAsync(credential, offer.ConfirmationId, offer.ConfirmationKey, "allow"));
                    if (!confirmed)
                        return SteamOfferActionResult.Failed("Steam 返回确认失败，请稍后重试或打开 Steam 页面手动处理。");
                    directMobileConfirmation = true;
                }
                else
                {
                    await AcknowledgeNewTradeAsync(credential, offer);
                    SteamTradeOfferAcceptResult accept = await RunSteamWriteWithRetryAsync(
                        credential,
                        "Steam 同意报价",
                        () => _tradeOfferClient.AcceptTradeOfferAsync(credential, offer.TradeOfferId));
                    if (accept.AlreadyHandled)
                    {
                        IgnoreTradeOffer(offer.TradeOfferId, accept.Message);
                        MarkOfferStatus(tradeOfferId, SteamOfferStatus.Accepted);
                        return SteamOfferActionResult.Success($"Steam 报价已处理或失效，已跳过：{tradeOfferId}");
                    }
                    if (accept.NeedsMobileConfirmation)
                    {
                        SteamOfferActionResult confirm = await ConfirmMatchedMobileTradeAsync(plan);
                        if (!confirm.Ok)
                            return confirm.Code == "not_found"
                                ? SteamOfferActionResult.Failed("Steam 已要求手机确认，但未找到严格匹配的移动确认项，请稍后刷新或手动确认。")
                                : confirm;
                    }
                    else if (!accept.Ok)
                    {
                        return SteamOfferActionResult.Failed("同意 Steam 报价失败：" + SteamOfferAuditLog.RedactSecrets(accept.Message));
                    }
                }

                MarkOfferStatus(tradeOfferId, SteamOfferStatus.Accepted);
                if (directMobileConfirmation)
                    SteamOfferAuditLog.LogMobileConfirmation(tradeOfferId, plan.MatchedOrderNo, SteamOfferAuditLog.TriggerBackgroundAuto, "本软件完成Steam手机确认。");
                else
                    SteamOfferAuditLog.LogAcceptOffer(tradeOfferId, offer.CanAcceptSafely, offer.VerifiedByYouPin, offer.PlatformOrderNo, SteamOfferAuditLog.TriggerBackgroundAuto);
                return SteamOfferActionResult.Success($"已自动接收 Steam 报价：{tradeOfferId}");
            }
            catch (Exception ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (LooksLikeExplicitAuthExpired(error) || ex is SteamAuthExpiredException)
                    NotifySteamLoginExpiredOnce("Steam 登录失效", "Steam 报价自动处理需要重新登录。");
                SteamOfferAuditLog.Error($"Auto accept trade offer failed. TradeOfferId={tradeOfferId}", ex);
                return SteamOfferActionResult.Failed("自动接收 Steam 报价失败：" + error);
            }
        }

        public async Task<SteamOfferActionResult> ConfirmMatchedMobileTradeAsync(SteamAutoTradePlanItem plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.TradeOfferId))
                return SteamOfferActionResult.Failed("自动处理计划缺少 Steam 报价号。");

            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌。", "need_login");

            try
            {
                SteamOfferItem? confirmation = await FindMatchedMobileConfirmationAsync(credential, plan);
                if (confirmation == null)
                    return SteamOfferActionResult.Failed("未找到相同 Steam 报价号的手机交易确认。", "not_found");

                SteamOfferAuditLog.LogMobileConfirmationSubmissionStarted();
                bool success;
                try
                {
                    success = await RunSteamWriteWithRetryAsync(
                        credential,
                        "Steam 移动确认",
                        () => _confirmationClient.SendConfirmationAjaxAsync(credential, confirmation.ConfirmationId, confirmation.ConfirmationKey, "allow"));
                    SteamOfferAuditLog.LogMobileConfirmationSubmissionCompleted(success);
                }
                catch (Exception ex)
                {
                    SteamOfferAuditLog.LogMobileConfirmationSubmissionCompleted(success: false, exception: ex);
                    throw;
                }
                if (!success)
                    return SteamOfferActionResult.Failed("Steam 返回确认失败，请稍后重试或打开 Steam 页面手动处理。");

                SteamOfferAuditLog.LogMobileConfirmation(plan.TradeOfferId, plan.MatchedOrderNo, SteamOfferAuditLog.TriggerBackgroundAuto, "本软件完成Steam手机确认。");
                return SteamOfferActionResult.Success("已完成匹配的 Steam 手机确认。");
            }
            catch (Exception ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (LooksLikeExplicitAuthExpired(error) || ex is SteamAuthExpiredException)
                    NotifySteamLoginExpiredOnce("Steam 登录失效", "Steam 手机确认自动处理需要重新登录。");
                SteamOfferAuditLog.Error($"Matched mobile confirmation failed. TradeOfferId={plan.TradeOfferId}", ex);
                return SteamOfferActionResult.Failed("Steam 手机确认失败：" + error);
            }
        }

        public async Task<SteamTradeOfferStatusResult> QueryTradeOfferStatusAsync(string tradeOfferId)
        {
            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                return SteamTradeOfferStatusResult.NotFound("Steam 报价号为空。");

            SteamAuthCredential? credential = _authStore.Load();
            if (credential == null)
                return SteamTradeOfferStatusResult.QueryFailed("未保存 Steam 登录凭据，无法查询报价状态。");

            var errors = new List<string>();
            if (!string.IsNullOrWhiteSpace(credential.ApiKey))
            {
                try
                {
                    TradeOfferDetail detail = await _tradeOfferClient.GetTradeOfferAsync(credential, tradeOfferId).ConfigureAwait(false);
                    SteamTradeOfferStatusResult apiStatus = MapAuthoritativeTradeOfferStatus(detail);
                    if (apiStatus.Kind != SteamTradeOfferStatusKind.QueryFailed)
                        return apiStatus;
                    errors.Add(apiStatus.Message);
                }
                catch (Exception ex)
                {
                    errors.Add(SteamOfferAuditLog.RedactSecrets(ex.Message));
                }
            }

            if (!string.IsNullOrWhiteSpace(credential.SessionId)
                && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
            {
                try
                {
                    TradeOfferDetail detail = await _tradeOfferClient.GetTradeOfferFromWebSessionAsync(credential, tradeOfferId).ConfigureAwait(false);
                    return MapAuthoritativeTradeOfferStatus(detail);
                }
                catch (Exception ex)
                {
                    errors.Add(SteamOfferAuditLog.RedactSecrets(ex.Message));
                }
            }

            string error = errors.FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? "没有可用的 Steam API Key 或网页登录状态。";
            return LooksLikeMissingTradeOffer(error)
                ? SteamTradeOfferStatusResult.NotFound("Steam 未返回该报价，报价可能已失效或不存在。")
                : SteamTradeOfferStatusResult.QueryFailed("Steam 报价状态查询失败：" + error);
        }

        private static SteamTradeOfferStatusResult MapAuthoritativeTradeOfferStatus(TradeOfferDetail detail)
        {
            ArgumentNullException.ThrowIfNull(detail);
            return detail.TradeOfferState switch
            {
                2 => SteamTradeOfferStatusResult.Active("Steam 报价仍处于活动状态。", 2),
                3 => SteamTradeOfferStatusResult.Accepted("Steam 报价已接受。", 3),
                9 => SteamTradeOfferStatusResult.NeedsMobileConfirmation("Steam 报价正在等待手机确认。", 9),
                11 => SteamTradeOfferStatusResult.Accepted("Steam 报价已接受，当前处于交易暂挂。", 11),
                4 => SteamTradeOfferStatusResult.Failed("Steam 报价已被还价替代。", 4),
                5 => SteamTradeOfferStatusResult.Failed("Steam 报价已过期。", 5),
                6 => SteamTradeOfferStatusResult.Failed("Steam 报价已取消。", 6),
                7 => SteamTradeOfferStatusResult.Failed("Steam 报价已被拒绝。", 7),
                8 => SteamTradeOfferStatusResult.Failed("Steam 报价包含无效物品。", 8),
                10 => SteamTradeOfferStatusResult.Failed("Steam 报价已被二次验证取消。", 10),
                _ => SteamTradeOfferStatusResult.QueryFailed($"Steam 返回未知报价状态：{detail.TradeOfferState}。")
            };
        }

        private static bool LooksLikeMissingTradeOffer(string message)
        {
            return message.Contains("不存在", StringComparison.OrdinalIgnoreCase)
                || message.Contains("为空", StringComparison.OrdinalIgnoreCase)
                || message.Contains("404", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not found", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutoAcceptPlanStillValid(SteamAutoTradePlanItem plan, SteamOfferItem offer)
        {
            SteamAutoTradeDirection direction = SteamAutoTradePlanner.GetDirection(offer);
            if (plan.Direction != SteamAutoTradeDirection.Unknown && direction != plan.Direction)
                return false;

            SteamAutoTradeCategory category = SteamAutoTradePlanner.ClassifyOffer(offer);
            if (category != plan.Category)
                return false;

            if (plan.Category == SteamAutoTradeCategory.PureIncoming)
                return SteamOfferSafetyEvaluator.IsPureIncomingGift(offer)
                    && !offer.VerifiedByYouPin
                    && string.IsNullOrWhiteSpace(offer.PlatformOrderNo)
                    && string.IsNullOrWhiteSpace(offer.YouPinOrderNo);

            if (plan.Category == SteamAutoTradeCategory.YouPinPurchase)
                return direction == SteamAutoTradeDirection.Incoming
                    && (offer.VerifiedByYouPin
                        || !string.IsNullOrWhiteSpace(offer.PlatformOrderNo)
                        || !string.IsNullOrWhiteSpace(offer.YouPinOrderNo));

            if (plan.Category == SteamAutoTradeCategory.YouPinRental)
            {
                string offerOrderNo = string.IsNullOrWhiteSpace(offer.PlatformOrderNo)
                    ? offer.YouPinOrderNo
                    : offer.PlatformOrderNo;
                return direction is SteamAutoTradeDirection.Incoming or SteamAutoTradeDirection.TwoWay
                    && offer.VerifiedByYouPin
                    && !string.IsNullOrWhiteSpace(plan.MatchedOrderNo)
                    && string.Equals(plan.MatchedOrderNo.Trim(), offerOrderNo?.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private async Task AcknowledgeNewTradeAsync(SteamAuthCredential credential, SteamOfferItem offer)
        {
            try
            {
                bool ok = await _tradeOfferClient.AcknowledgeNewTradeAsync(credential, offer.TradeOfferId);
                if (!ok)
                {
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-trade-ack-returned-false",
                        $"Steam new trade acknowledgement returned false. TradeOfferId={offer.TradeOfferId}",
                        TimeSpan.FromMinutes(10));
                }
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-trade-ack-failed",
                    "Steam new trade acknowledgement skipped: " + SteamOfferAuditLog.RedactSecrets(ex.Message),
                    TimeSpan.FromMinutes(10));
            }
        }

        private async Task<SteamOfferItem?> FindMobileConfirmationAsync(SteamAuthCredential credential, string tradeOfferId)
        {
            var confirmations = await FetchMobileConfirmationsAsync(credential);
            return confirmations.FirstOrDefault(x => string.Equals(x.TradeOfferId, tradeOfferId, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<SteamOfferItem?> FindMatchedMobileConfirmationAsync(SteamAuthCredential credential, SteamAutoTradePlanItem plan)
        {
            var confirmations = await FetchMobileConfirmationsAsync(credential);
            SteamOfferItem? matched = confirmations.FirstOrDefault(x => SteamAutoTradePlanner.IsMobileConfirmationMatch(plan, x));
            int sameOfferId = confirmations.Count(x =>
                !string.IsNullOrWhiteSpace(plan.TradeOfferId)
                && !string.IsNullOrWhiteSpace(x.TradeOfferId)
                && string.Equals(plan.TradeOfferId.Trim(), x.TradeOfferId.Trim(), StringComparison.OrdinalIgnoreCase));
            SteamOfferAuditLog.LogMobileConfirmationMatchEvaluation(confirmations.Count, sameOfferId, matched != null);
            return matched;
        }

        private bool IsIgnoredTradeOffer(string tradeOfferId)
        {
            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                return false;

            lock (_ignoredTradeOffers)
            {
                return _ignoredTradeOffers.Contains(tradeOfferId);
            }
        }

        private void IgnoreTradeOffer(string tradeOfferId, string reason)
        {
            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                return;

            lock (_ignoredTradeOffers)
            {
                _ignoredTradeOffers.Add(tradeOfferId);
            }

            SteamOfferAuditLog.InfoThrottled(
                "steam-trade-offer-ignored:" + tradeOfferId,
                "Steam trade offer ignored as already handled or inactive. TradeOfferId=" + tradeOfferId + "; Reason=" + SteamOfferAuditLog.RedactSecrets(reason),
                TimeSpan.FromMinutes(10));
        }

        public async Task<SteamOfferActionResult> DenyOfferAsync(string tradeOfferId)
        {
            tradeOfferId = (tradeOfferId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(tradeOfferId))
                return SteamOfferActionResult.Failed("Steam 报价号为空。");

            SteamOfferItem? offer = _stateStore.FindOffer(tradeOfferId);

            if (offer == null)
                return SteamOfferActionResult.Failed("当前列表中没有找到该 Steam 报价。请先刷新。");
            if (string.IsNullOrWhiteSpace(offer.ConfirmationId) || string.IsNullOrWhiteSpace(offer.ConfirmationKey))
                return SteamOfferActionResult.Failed("该报价没有移动确认信息，暂不支持网页拒绝接口。");

            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌。");

            try
            {
                bool success = await RunSteamWriteWithRetryAsync(
                    credential,
                    "Steam 移动确认",
                    () => _confirmationClient.SendConfirmationAjaxAsync(credential, offer.ConfirmationId, offer.ConfirmationKey, "cancel"));
                if (!success)
                    return SteamOfferActionResult.Failed("Steam 返回拒绝失败，请稍后重试或打开 Steam 页面手动处理。");

                MarkOfferStatus(tradeOfferId, SteamOfferStatus.Denied);
                SteamOfferAuditLog.LogDenyOffer(tradeOfferId);
                return SteamOfferActionResult.Success($"已拒绝 Steam 报价：{tradeOfferId}");
            }
            catch (Exception ex)
            {
                string error = SteamOfferAuditLog.RedactSecrets(ex.Message);
                if (LooksLikeExplicitAuthExpired(error) || ex is SteamAuthExpiredException)
                    NotifySteamLoginExpiredOnce("Steam 登录失效", "Steam 报价后台处理已暂停，请重新登录或用 Token 恢复。");
                SteamOfferAuditLog.Error($"Deny trade offer failed. TradeOfferId={tradeOfferId}", ex);
                return SteamOfferActionResult.Failed("拒绝 Steam 报价失败：" + error);
            }
        }

        private Task<T> RunSteamWriteWithRetryAsync<T>(
            SteamAuthCredential credential,
            string operationName,
            Func<Task<T>> operation)
        {
            return TradeWriteOperationGate.RunWithRetryAsync(
                BuildSteamWriteGateKey(credential),
                operation,
                TradeWriteOperationGate.IsRetryableTransient,
                operationName);
        }

        private static string BuildSteamWriteGateKey(SteamAuthCredential credential)
        {
            string id = FirstText(credential.SteamId, credential.AccountName, credential.LoginAccountName, "unknown");
            return "Steam:" + id;
        }

        private void PauseSteamBackgroundAfterLoginExpired(string message)
        {
            _autoConfirmationService.Stop();
            NotifySteamLoginExpiredOnce("Steam 登录失效", message);
            RaiseDataUpdated();
        }

        private void NotifySteamLoginExpiredOnce(string title, string message)
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastLoginExpiredNotificationUtc < TimeSpan.FromMinutes(30))
                return;

            _lastLoginExpiredNotificationUtc = now;
            try
            {
                // 登录失效通知需要读取勿扰和手机通道配置，属于低频告警路径而非 UI 热刷新路径。
                var settings = Settings.Load();
                if (!settings.DoNotDisturbEnabled)
                {
                    AppNotificationHub.Instance.Request(
                        title,
                        message,
                        AppNotificationSeverity.Warning,
                        AppNotificationPlacement.Desktop);
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (PhoneAlertDispatchService.IsConfigured(settings))
                            await PhoneAlertDispatchService.Instance.SendConfiguredAsync(settings, title, message).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        SteamOfferAuditLog.InfoThrottled(
                            "steam-login-expired-phone-alert-failed",
                            "Steam login expired phone alert failed: " + SteamOfferAuditLog.RedactSecrets(ex.Message),
                            TimeSpan.FromMinutes(10));
                    }
                });
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-login-expired-notify-failed",
                    "Steam login expired notification failed: " + SteamOfferAuditLog.RedactSecrets(ex.Message),
                    TimeSpan.FromMinutes(10));
            }
        }

        private async Task<List<SteamOfferItem>> FetchMobileConfirmationsAsync(SteamAuthCredential credential)
        {
            string text = await _confirmationClient.FetchConfirmationsRawAsync(credential);

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (GetBool(root, "needauth", "needAuthentication"))
                throw new SteamAuthExpiredException("Steam 移动确认返回 needauth，登录状态已失效。");
            if (!GetBool(root, "success") && !TryGetProperty(root, out _, "conf", "confirmations"))
            {
                string msg = GetString(root, "message", "msg") ?? "Steam 返回失败";
                throw new InvalidOperationException(msg);
            }

            var list = new List<SteamOfferItem>();
            if (TryGetProperty(root, out var confirmations, "conf", "confirmations") && confirmations.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in confirmations.EnumerateArray())
                {
                    var offer = ParseConfirmation(item);
                    if (!string.IsNullOrWhiteSpace(offer.TradeOfferId))
                        list.Add(offer);
                }
            }

            return list;
        }

        private async Task EnrichConfirmationsWithOfferDetailsAsync(
            List<SteamOfferItem> offers,
            SteamAuthCredential credential,
            TradeOffersResult? webTradeOffers = null)
        {
            if (offers.Count == 0)
                return;

            var missingDetails = offers
                .Where(NeedsTradeOfferDetail)
                .Take(SteamOfferDetailLookupLimit)
                .ToList();
            if (missingDetails.Count == 0)
                return;

            await EnrichMissingDetailsFromConfirmationDetailsAsync(missingDetails, credential);
            await EnrichMissingDetailsFromWebSessionAsync(missingDetails, credential, webTradeOffers);

            if (string.IsNullOrWhiteSpace(credential.ApiKey))
                return;

            await EnrichMissingDetailsFromTradeOfferListAsync(
                missingDetails,
                () => _tradeOfferClient.GetTradeOffersForDetailLookupAsync(credential),
                "steam-offer-detail-lookup-all");

            if (missingDetails.Any(NeedsTradeOfferDetail))
            {
                await EnrichMissingDetailsFromTradeOfferListAsync(
                    missingDetails,
                    () => _tradeOfferClient.GetHistoricalTradeOffersForDetailLookupAsync(credential, SteamOfferDetailHistoricalLookupAge),
                    "steam-offer-detail-lookup-historical");
            }

            foreach (var offer in missingDetails.Where(NeedsTradeOfferDetail).ToList())
            {
                try
                {
                    var detail = await _tradeOfferClient.GetTradeOfferAsync(credential, offer.TradeOfferId);
                    var enriched = CreateOfferFromTradeDetail(detail, detail.IsOurOffer);
                    ApplyTradeOfferDetails(offer, enriched);
                    await Task.Delay(150);
                }
                catch (Exception ex)
                {
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-offer-detail-enrich-failed:" + offer.TradeOfferId,
                        "Steam offer detail enrichment skipped. TradeOfferId=" + offer.TradeOfferId + "; Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                        TimeSpan.FromMinutes(10));
                }
            }
        }

        private async Task EnrichMissingDetailsFromConfirmationDetailsAsync(List<SteamOfferItem> missingDetails, SteamAuthCredential credential)
        {
            foreach (var offer in missingDetails
                         .Where(NeedsTradeOfferDetail)
                         .Where(x => !string.IsNullOrWhiteSpace(x.ConfirmationId))
                         .ToList())
            {
                try
                {
                    string expectedId = (offer.TradeOfferId ?? "").Trim();
                    string html = await _confirmationClient.FetchConfirmationDetailsHtmlAsync(credential, offer.ConfirmationId);
                    string htmlTradeOfferId = SteamTradeOfferWebHtmlParser.ExtractTradeOfferIdFromHtml(html);
                    if (!string.IsNullOrWhiteSpace(htmlTradeOfferId)
                        && !htmlTradeOfferId.Equals(expectedId, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Steam 移动确认详情报价号不匹配。");
                    }

                    var detail = SteamTradeOfferWebHtmlParser.ParseTradeOfferDetailPage(html, expectedId, forceSentOffer: true);
                    if (detail == null)
                        throw new InvalidOperationException("Steam 移动确认详情未返回饰品明细。");

                    detail.TradeOfferId = expectedId;
                    await _tradeOfferClient.EnrichTradeOfferDetailAssetsAsync(credential, detail);
                    var enriched = CreateOfferFromTradeDetail(detail, isSentOffer: true);
                    ApplyTradeOfferDetails(offer, enriched);
                    await Task.Delay(120);
                }
                catch (Exception ex)
                {
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-confirmation-detail-enrich-failed:" + offer.TradeOfferId,
                        "Steam confirmation detail enrichment skipped. TradeOfferId=" + offer.TradeOfferId + "; Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                        TimeSpan.FromMinutes(5));
                }
            }
        }

        private async Task EnrichMissingDetailsFromWebSessionAsync(
            List<SteamOfferItem> missingDetails,
            SteamAuthCredential credential,
            TradeOffersResult? webTradeOffers = null)
        {
            if (!missingDetails.Any(NeedsTradeOfferDetail))
                return;
            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                return;

            try
            {
                var result = webTradeOffers ?? await _tradeOfferClient.GetTradeOffersFromWebSessionAsync(credential);
                var byId = BuildTradeOfferDetailLookup(result);
                LogWebDetailLookupDiagnostic(missingDetails, byId);
                foreach (var offer in missingDetails.Where(NeedsTradeOfferDetail))
                {
                    if (!byId.TryGetValue(offer.TradeOfferId.Trim(), out var detail))
                        continue;

                    var enriched = CreateOfferFromTradeDetail(detail, detail.IsOurOffer);
                    ApplyTradeOfferDetails(offer, enriched);
                }

                await EnrichMissingDetailsFromWebDetailPagesAsync(missingDetails, credential);
                ApplyAnonymousWebDetailsByOrder(missingDetails, result);
            }
            catch (SteamAuthExpiredException)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-offer-detail-web-auth-enrich-skipped",
                    "Steam web offer detail enrichment skipped because web session is unavailable; saved session is kept.",
                    TimeSpan.FromMinutes(10));
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-offer-detail-web-enrich-failed",
                    "Steam web offer detail enrichment skipped. Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                TimeSpan.FromMinutes(10));
            }
        }

        private async Task EnrichMissingDetailsFromWebDetailPagesAsync(List<SteamOfferItem> missingDetails, SteamAuthCredential credential)
        {
            foreach (var offer in missingDetails.Where(NeedsTradeOfferDetail).ToList())
            {
                try
                {
                    var detail = await _tradeOfferClient.GetTradeOfferFromWebSessionAsync(credential, offer.TradeOfferId);
                    var enriched = CreateOfferFromTradeDetail(detail, isSentOffer: true);
                    ApplyTradeOfferDetails(offer, enriched);
                    await Task.Delay(120);
                }
                catch (Exception ex)
                {
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-offer-web-detail-page-enrich-failed:" + offer.TradeOfferId,
                        "Steam web detail page enrichment skipped. TradeOfferId=" + offer.TradeOfferId + "; Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                        TimeSpan.FromMinutes(5));
                }
            }
        }

        private static void LogWebDetailLookupDiagnostic(
            IReadOnlyList<SteamOfferItem> missingDetails,
            IReadOnlyDictionary<string, TradeOfferDetail> byId)
        {
            string missingIds = string.Join(
                ",",
                missingDetails
                    .Select(x => (x.TradeOfferId ?? "").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(8));
            string foundIds = string.Join(",", byId.Keys.Take(8));
            SteamOfferAuditLog.InfoThrottled(
                "steam-web-detail-lookup-diagnostic",
                $"Steam web detail lookup. Missing={missingDetails.Count}; DetailIds={byId.Count}; MissingIds={missingIds}; FoundIds={foundIds}",
                TimeSpan.FromMinutes(2));
        }

        private static void ApplyAnonymousWebDetailsByOrder(List<SteamOfferItem> missingDetails, TradeOffersResult result)
        {
            var stillMissing = missingDetails
                .Where(NeedsTradeOfferDetail)
                .ToList();
            if (stillMissing.Count == 0)
                return;

            var anonymousSent = result.SentOffers
                .Where(x => string.IsNullOrWhiteSpace(x.TradeOfferId)
                    && (x.ItemsToGive.Count > 0 || x.ItemsToReceive.Count > 0))
                .ToList();
            int rawAnonymousCount = anonymousSent.Count;
            if (stillMissing.Count != 1 || anonymousSent.Count != 1)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-web-detail-anonymous-skip",
                    $"Steam web anonymous detail skipped. Missing={stillMissing.Count}; AnonymousSent={rawAnonymousCount}",
                    TimeSpan.FromMinutes(2));
                return;
            }

            for (int i = 0; i < stillMissing.Count; i++)
            {
                var detail = anonymousSent[i];
                detail.TradeOfferId = stillMissing[i].TradeOfferId;
                var enriched = CreateOfferFromTradeDetail(detail, isSentOffer: true);
                ApplyTradeOfferDetails(stillMissing[i], enriched);
            }

            SteamOfferAuditLog.InfoThrottled(
                "steam-web-detail-anonymous-applied",
                $"Steam web anonymous detail applied. Count={anonymousSent.Count}; Raw={rawAnonymousCount}",
                TimeSpan.FromMinutes(2));
        }

        private async Task EnrichMissingDetailsFromTradeOfferListAsync(
            List<SteamOfferItem> missingDetails,
            Func<Task<TradeOffersResult>> loadDetails,
            string logKey)
        {
            if (!missingDetails.Any(NeedsTradeOfferDetail))
                return;

            try
            {
                var result = await loadDetails();
                var byId = BuildTradeOfferDetailLookup(result);
                foreach (var offer in missingDetails.Where(NeedsTradeOfferDetail))
                {
                    if (!byId.TryGetValue(offer.TradeOfferId.Trim(), out var detail))
                        continue;

                    var enriched = CreateOfferFromTradeDetail(detail, detail.IsOurOffer);
                    ApplyTradeOfferDetails(offer, enriched);
                }
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.InfoThrottled(
                    logKey,
                    "Steam offer detail batch enrichment skipped. Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                    TimeSpan.FromMinutes(10));
            }
        }

        private void EnrichWithYouPinVerification(List<SteamOfferItem> offers)
        {
            SteamOfferYouPinVerificationHelper.EnrichWithYouPinVerification(offers, _youPinSaleReminders.GetState());
        }

        private void SetOffers(List<SteamOfferItem> offers, string status, string error)
        {
            _stateStore.SetOffers(offers, status, error);
            RaiseDataUpdated();
        }

        private void PrepareManualOffer(SteamOfferItem manualOffer)
        {
            var probe = new List<SteamOfferItem> { manualOffer };
            EnrichWithYouPinVerification(probe);
            SteamOfferSafetyEvaluator.Evaluate(manualOffer);
        }

        private void MarkOfferStatus(string tradeOfferId, SteamOfferStatus status)
        {
            _stateStore.MarkOfferStatus(tradeOfferId, status);
            RaiseDataUpdated();
        }

        private void MarkOfferStatuses(IEnumerable<string> tradeOfferIds, SteamOfferStatus status)
        {
            bool shouldRaise = _stateStore.MarkOfferStatuses(tradeOfferIds, status);
            if (!shouldRaise) return;
            RaiseDataUpdated();
        }

        private void RaiseDataUpdated()
        {
            try
            {
                DataUpdated?.Invoke();
            }
            catch
            {
                // DataUpdated 是 UI/页面刷新通知，订阅方异常不能影响报价服务状态。
            }
        }

    }

}
