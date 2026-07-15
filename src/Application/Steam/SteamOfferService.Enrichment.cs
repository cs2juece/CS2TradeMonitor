using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.Steam.SteamOfferLoginRecoveryHelper;
using static CS2TradeMonitor.Application.Steam.SteamOfferMappingHelper;

namespace CS2TradeMonitor.Application.Steam
{
    public sealed partial class SteamOfferService
    {
        private const string BackgroundPartialWarning = "部分报价数据暂时未同步，请稍后刷新。";

        private void QueueBackgroundOfferEnrichment(
            SteamAuthCredential credential,
            List<SteamOfferItem> foregroundOffers,
            string foregroundWarning,
            TradeOffersResult? webTradeOffersFromForeground,
            bool foregroundOfferListFetched,
            bool triggerImmediateAutoProcessing)
        {
            if (credential == null)
                return;

            int version = Interlocked.Increment(ref _backgroundEnrichmentVersion);
            var credentialSnapshot = CloneCredential(credential);
            var baseOffers = foregroundOffers.Select(CloneOffer).ToList();
            LastBackgroundEnrichmentTask = Task.Run(async () =>
            {
                await Task.Delay(50).ConfigureAwait(false);
                await RunBackgroundOfferEnrichmentAsync(
                    version,
                    credentialSnapshot,
                    baseOffers,
                    foregroundWarning,
                    webTradeOffersFromForeground,
                    foregroundOfferListFetched,
                    triggerImmediateAutoProcessing).ConfigureAwait(false);
            });
        }

        private async Task RunBackgroundOfferEnrichmentAsync(
            int version,
            SteamAuthCredential credential,
            List<SteamOfferItem> offers,
            string partialWarning,
            TradeOffersResult? webTradeOffersFromForeground,
            bool foregroundOfferListFetched,
            bool triggerImmediateAutoProcessing)
        {
            var totalWatch = Stopwatch.StartNew();
            try
            {
                TradeOffersResult? webTradeOffers = webTradeOffersFromForeground;
                if (webTradeOffers == null
                    && !string.IsNullOrWhiteSpace(credential.SessionId)
                    && !string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                {
                    var webWatch = Stopwatch.StartNew();
                    try
                    {
                        webTradeOffers = await _tradeOfferClient.GetTradeOffersFromWebSessionAsync(credential).ConfigureAwait(false);
                        MergeTradeOfferItems(offers, ConvertTradeOffersToItems(webTradeOffers));
                        LogLoadStage("background-web", webWatch);
                    }
                    catch (SteamAuthExpiredException ex)
                    {
                        LogLoadStage("background-web-auth-failed", webWatch);
                        string webWarning = BuildSteamOfferListWarning("BackgroundTradeOffersWeb", ex);
                        if (HasRecoverableLoginState(credential))
                        {
                            var relogin = await EnsureSteamLoginStateAsync(
                                "后台网页报价补全登录状态失效",
                                preferPasswordFallback: true).ConfigureAwait(false);
                            if (relogin.Ok)
                            {
                                SteamAuthCredential? freshCredential = _authStore.Load();
                                if (freshCredential != null)
                                {
                                    credential = CloneCredential(freshCredential);
                                    webWatch = Stopwatch.StartNew();
                                    try
                                    {
                                        webTradeOffers = await _tradeOfferClient.GetTradeOffersFromWebSessionAsync(credential).ConfigureAwait(false);
                                        MergeTradeOfferItems(offers, ConvertTradeOffersToItems(webTradeOffers));
                                        LogLoadStage("background-web-after-relogin", webWatch);
                                    }
                                    catch (Exception retryEx) when (retryEx is SteamAuthExpiredException || IsNonAuthSteamOfferListFailure(retryEx))
                                    {
                                        LogLoadStage("background-web-after-relogin-failed", webWatch);
                                        partialWarning = AppendPartialWarning(partialWarning, "后台网页报价补全暂不可用：" + BuildSteamOfferListWarning("BackgroundTradeOffersWebRetry", retryEx));
                                    }
                                }
                            }
                            else
                            {
                                partialWarning = AppendPartialWarning(partialWarning, "后台网页报价补全自动重登失败：" + relogin.Message);
                            }
                        }
                        else
                        {
                            partialWarning = AppendPartialWarning(partialWarning, "后台网页报价补全暂不可用：" + webWarning);
                        }
                    }
                    catch (Exception ex) when (IsNonAuthSteamOfferListFailure(ex))
                    {
                        LogLoadStage("background-web-failed", webWatch);
                        partialWarning = AppendPartialWarning(partialWarning, "后台网页报价补全暂不可用：" + BuildSteamOfferListWarning("BackgroundTradeOffersWeb", ex));
                    }
                }
                else if (webTradeOffers != null)
                {
                    MergeTradeOfferItems(offers, ConvertTradeOffersToItems(webTradeOffers));
                }

                bool mobileConfirmationsFetched = false;
                int mobileConfirmationCount = 0;
                var confirmationWatch = Stopwatch.StartNew();
                try
                {
                    List<SteamOfferItem> confirmations = await FetchMobileConfirmationsAsync(credential).ConfigureAwait(false);
                    mobileConfirmationsFetched = true;
                    mobileConfirmationCount = confirmations.Count;
                    MergeMobileConfirmations(offers, confirmations);
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-mobile-confirmations-count",
                        $"Steam mobile confirmations fetched. Count={mobileConfirmationCount}",
                        TimeSpan.FromMinutes(1));
                    LogLoadStage("background-mobile-confirmations", confirmationWatch);
                }
                catch (Exception ex) when (IsRecoverableMobileConfirmationFailure(ex))
                {
                    LogLoadStage("background-mobile-confirmations-failed", confirmationWatch);
                    string confirmWarning = BuildMobileConfirmationWarning(ex);
                    string diagnosticWarning = BuildMobileConfirmationDiagnosticWarning(ex);
                    partialWarning = AppendPartialWarning(partialWarning, confirmWarning);
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-mobile-confirmations-background-failure",
                        "Steam mobile confirmations unavailable in background enrichment. Reason=" + diagnosticWarning,
                        TimeSpan.FromMinutes(5));
                }

                var detailWatch = Stopwatch.StartNew();
                TradeOffersResult? webTradeOffersForDetails = CountTradeOfferDetails(webTradeOffers) > 0 ? webTradeOffers : null;
                await EnrichConfirmationsWithOfferDetailsAsync(offers, credential, webTradeOffersForDetails).ConfigureAwait(false);
                LogLoadStage("background-details", detailWatch);

                var displayOffers = PrepareOffersForDisplay(offers);
                string statusWarning = ShouldSuppressBackgroundWarning(displayOffers.Count, mobileConfirmationsFetched, mobileConfirmationCount)
                    ? ""
                    : partialWarning;
                bool keepSuccessfulForegroundStatus = foregroundOfferListFetched && !string.IsNullOrWhiteSpace(statusWarning);
                string userFacingWarning = keepSuccessfulForegroundStatus
                    ? BackgroundPartialWarning
                    : statusWarning;
                string status = keepSuccessfulForegroundStatus
                    ? BuildLoadOffersStatus(displayOffers.Count, "") + BackgroundPartialWarning
                    : BuildLoadOffersStatus(displayOffers.Count, statusWarning);
                if (version == Volatile.Read(ref _backgroundEnrichmentVersion))
                {
                    bool hasNewPendingOffer = UpdateKnownPendingOffers(displayOffers);
                    SetOffers(displayOffers, status, userFacingWarning);
                    if (triggerImmediateAutoProcessing && hasNewPendingOffer)
                        await ImmediateAutoProcessingAsync(CancellationToken.None).ConfigureAwait(false);
                }
                LogLoadStage("background-total", totalWatch);
            }
            catch (Exception ex)
            {
                SteamOfferAuditLog.InfoThrottled(
                    "steam-offer-background-enrichment-failed",
                    "Steam offer background enrichment failed. Reason=" + SteamOfferAuditLog.RedactSecrets(ex.Message),
                    TimeSpan.FromMinutes(5));
            }
        }

        private bool UpdateKnownPendingOffers(IReadOnlyList<SteamOfferItem> refreshedOffers)
        {
            var refreshedIds = refreshedOffers
                .Where(offer => offer.Status == SteamOfferStatus.Pending && !string.IsNullOrWhiteSpace(offer.TradeOfferId))
                .Select(offer => offer.TradeOfferId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (_pendingOfferTriggerSync)
            {
                bool hasNew = refreshedIds.Any(id => !_knownPendingOfferIds.Contains(id));
                _knownPendingOfferIds.UnionWith(refreshedIds);
                return hasNew;
            }
        }

        private static SteamAuthCredential CloneCredential(SteamAuthCredential credential)
        {
            return new SteamAuthCredential
            {
                SteamId = credential.SteamId,
                AccountName = credential.AccountName,
                PersonaName = credential.PersonaName,
                DeviceId = credential.DeviceId,
                SharedSecret = credential.SharedSecret,
                IdentitySecret = credential.IdentitySecret,
                SessionId = credential.SessionId,
                SteamLoginSecure = credential.SteamLoginSecure,
                SteamLogin = credential.SteamLogin,
                RefreshToken = credential.RefreshToken,
                AccessToken = credential.AccessToken,
                AccessTokenExpiresAt = credential.AccessTokenExpiresAt,
                RefreshTokenExpiresAt = credential.RefreshTokenExpiresAt,
                ApiKey = credential.ApiKey,
                LoginAccountName = credential.LoginAccountName,
                LoginPassword = credential.LoginPassword,
                SavedAt = credential.SavedAt,
                SessionSavedAt = credential.SessionSavedAt,
                LastAutoReloginAt = credential.LastAutoReloginAt,
                LastAutoReloginResult = credential.LastAutoReloginResult,
                AutoReloginCooldownUntil = credential.AutoReloginCooldownUntil
            };
        }

        private static void LogLoadStage(string stage, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            SteamOfferAuditLog.InfoThrottled(
                "steam-offer-load-stage:" + stage,
                $"Steam offer load stage. Stage={stage}; ElapsedMs={stopwatch.Elapsed.TotalMilliseconds:F0}",
                TimeSpan.FromMinutes(1));
        }
    }
}
