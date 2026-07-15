using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.SystemServices;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginCookieHelper;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginCryptoSupport;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginHttpDiagnostics;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginTokenTextParser;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginWebPageParser;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    public sealed class SteamLoginService : ISteamLoginService
    {
        private readonly ISteamAuthStore _authStore;
        private readonly ISteamTokenVault _tokenVault;
        private readonly ISteamRoutedHttpClientFactory _httpFactory;
        public static SteamLoginService Instance { get; } = new();

        private SteamLoginService()
            : this(SteamLoginRuntimeServices.Resolve())
        {
        }

        internal SteamLoginService(SteamLoginRuntimeServices services)
            : this(services.AuthStore, services.TokenVault, services.RoutedHttpFactory)
        {
        }

        internal SteamLoginService(
            ISteamAuthStore authStore,
            ISteamTokenVault tokenVault,
            ISteamRoutedHttpClientFactory httpFactory)
        {
            _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            _tokenVault = tokenVault ?? throw new ArgumentNullException(nameof(tokenVault));
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        }

        public async Task<SteamOfferActionResult> RestoreFromRefreshTokenAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null)
                return SteamOfferActionResult.Failed("未绑定 Steam 令牌，无法恢复 Steam 登录状态。", "missing-credential");

            string refreshToken = (credential.RefreshToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(refreshToken))
                return SteamOfferActionResult.Failed("未保存 RefreshToken。", "missing-refresh-token");

            try
            {
                using var http = await CreateHttpClientAsync(cancellationToken);
                string steamId = FirstText(credential.SteamId, TryGetSteamIdFromJwt(refreshToken));
                if (string.IsNullOrWhiteSpace(steamId))
                    return SteamOfferActionResult.Failed("RefreshToken 无法解析 SteamID，请改用 Steam 网页登录。", "missing-steamid");

                var cookies = await FinalizeWebLoginAsync(http, steamId, refreshToken, cancellationToken);
                var validation = await SteamWebSessionValidator.ValidateAsync(http, cookies, cancellationToken);
                if (validation.State == SteamSessionValidationState.Expired)
                    return SteamOfferActionResult.Failed("RefreshToken 已换取登录状态，但 Steam 未确认登录；请改用 Steam 网页登录。", "invalid-web-session");
                if (validation.State == SteamSessionValidationState.NetworkUnavailable)
                    return SteamOfferActionResult.Failed("RefreshToken 已换取登录状态，但暂时无法验证 Steam 登录状态：" + validation.Message, SteamLoginFailureCategory.NetworkError.ToString());
                if (validation.State != SteamSessionValidationState.Valid)
                    return SteamOfferActionResult.Failed("RefreshToken 已换取登录状态，但 Steam 登录验证异常：" + validation.Message, SteamLoginFailureCategory.ProtocolChanged.ToString());

                string apiKey = credential.ApiKey;
                string personaName = await TryFetchPersonaNameAsync(http, cookies, steamId, cancellationToken);
                SaveRestoredCredential(credential, steamId, cookies, refreshToken: refreshToken, accessToken: credential.AccessToken, apiKey: apiKey, personaName: personaName, result: "已用 RefreshToken 恢复 Steam 登录状态");
                return SteamOfferActionResult.Success("已用 RefreshToken 恢复 Steam 登录状态。");
            }
            catch (SteamLoginException ex)
            {
                return SteamOfferActionResult.Failed(ex.Message, ex.Category.ToString());
            }
            catch (HttpRequestException ex)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("RestoreFromRefreshToken", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("RestoreFromRefreshToken", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (Exception ex)
            {
                return SteamOfferActionResult.Failed("恢复 Steam 登录状态失败：" + DiagnosticsLogger.Redact(ex.Message), SteamLoginFailureCategory.Unknown.ToString());
            }
        }

        public async Task<SteamOfferActionResult> RestoreFromAccessTokenAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null)
                return SteamOfferActionResult.Failed("未绑定 Steam 令牌，无法恢复 Steam 登录状态。", "missing-credential");

            string accessToken = (credential.AccessToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(accessToken))
                return SteamOfferActionResult.Failed("未保存 AccessToken。", "missing-access-token");

            try
            {
                using var http = await CreateHttpClientAsync(cancellationToken);
                string steamId = FirstText(credential.SteamId, TryGetSteamIdFromJwt(accessToken));
                if (string.IsNullOrWhiteSpace(steamId))
                    return SteamOfferActionResult.Failed("AccessToken 无法解析 SteamID，请改用 Steam 网页登录。", "missing-steamid");

                var cookies = new SteamWebCookies
                {
                    SessionId = string.IsNullOrWhiteSpace(credential.SessionId) ? RandomHex(12) : credential.SessionId.Trim(),
                    SteamLoginSecure = steamId + "||" + accessToken,
                    SteamLogin = credential.SteamLogin
                };
                var validation = await SteamWebSessionValidator.ValidateAsync(http, cookies, cancellationToken);
                if (validation.State == SteamSessionValidationState.Expired)
                    return SteamOfferActionResult.Failed("AccessToken 已失效或不能建立网页登录状态，请改用 Steam 网页登录。", "invalid-access-token");
                if (validation.State == SteamSessionValidationState.NetworkUnavailable)
                    return SteamOfferActionResult.Failed("AccessToken 已构造网页登录状态，但暂时无法验证 Steam 登录状态：" + validation.Message, SteamLoginFailureCategory.NetworkError.ToString());
                if (validation.State != SteamSessionValidationState.Valid)
                    return SteamOfferActionResult.Failed("AccessToken 登录验证异常：" + validation.Message, SteamLoginFailureCategory.ProtocolChanged.ToString());

                string apiKey = credential.ApiKey;
                string personaName = await TryFetchPersonaNameAsync(http, cookies, steamId, cancellationToken);
                SaveRestoredCredential(credential, steamId, cookies, refreshToken: credential.RefreshToken, accessToken: accessToken, apiKey: apiKey, personaName: personaName, result: "已用 AccessToken 恢复 Steam 登录状态");
                return SteamOfferActionResult.Success("已用 AccessToken 恢复 Steam 登录状态。");
            }
            catch (SteamLoginException ex)
            {
                return SteamOfferActionResult.Failed(ex.Message, ex.Category.ToString());
            }
            catch (HttpRequestException ex)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("RestoreFromAccessToken", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("RestoreFromAccessToken", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (Exception ex)
            {
                return SteamOfferActionResult.Failed("恢复 Steam 登录状态失败：" + DiagnosticsLogger.Redact(ex.Message), SteamLoginFailureCategory.Unknown.ToString());
            }
        }

        public async Task<SteamOfferActionResult> RefreshAccessTokenForAppAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null)
                return SteamOfferActionResult.Failed("未绑定 Steam 令牌，无法续期 Steam 会话。", "missing-credential");

            string refreshToken = (credential.RefreshToken ?? "").Trim();
            if (string.IsNullOrWhiteSpace(refreshToken))
                return SteamOfferActionResult.Failed("未保存 RefreshToken，无法续期 Steam 会话。", "missing-refresh-token");

            try
            {
                using var http = await CreateHttpClientAsync(cancellationToken);
                string steamId = FirstText(credential.SteamId, SteamJwtTokenParser.TryGetSteamId(refreshToken));
                if (string.IsNullOrWhiteSpace(steamId))
                    return SteamOfferActionResult.Failed("RefreshToken 无法解析 SteamID，无法续期 Steam 会话。", "missing-steamid");

                var refreshResult = await SteamLoginAuthSessionClient.GenerateAccessTokenForAppAsync(http, refreshToken, steamId, cancellationToken);
                string accessToken = refreshResult.AccessToken;
                string nextRefreshToken = string.IsNullOrWhiteSpace(refreshResult.RefreshToken) ? refreshToken : refreshResult.RefreshToken;
                var cookies = new SteamWebCookies
                {
                    SessionId = string.IsNullOrWhiteSpace(credential.SessionId) ? RandomHex(12) : credential.SessionId.Trim(),
                    SteamLoginSecure = steamId + "||" + accessToken,
                    SteamLogin = credential.SteamLogin
                };
                var validation = await SteamWebSessionValidator.ValidateAsync(http, cookies, cancellationToken);
                if (validation.State == SteamSessionValidationState.Expired)
                    return SteamOfferActionResult.Failed("AccessToken 已续期，但 Steam 未确认网页登录状态；请改用 Steam 网页登录。", "invalid-web-session");
                if (validation.State == SteamSessionValidationState.NetworkUnavailable)
                    return SteamOfferActionResult.Failed("AccessToken 已续期，但暂时无法验证 Steam 登录状态：" + validation.Message, SteamLoginFailureCategory.NetworkError.ToString());
                if (validation.State != SteamSessionValidationState.Valid)
                    return SteamOfferActionResult.Failed("AccessToken 续期后登录验证异常：" + validation.Message, SteamLoginFailureCategory.ProtocolChanged.ToString());

                string apiKey = credential.ApiKey;
                string personaName = await TryFetchPersonaNameAsync(http, cookies, steamId, cancellationToken);
                SaveRestoredCredential(credential, steamId, cookies, refreshToken: nextRefreshToken, accessToken: accessToken, apiKey: apiKey, personaName: personaName, result: "已自动续期 Steam AccessToken");
                return SteamOfferActionResult.Success("已自动续期 Steam 会话。");
            }
            catch (SteamLoginException ex)
            {
                return SteamOfferActionResult.Failed(ex.Message, ex.Category.ToString());
            }
            catch (HttpRequestException ex)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("RefreshAccessTokenForApp", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("RefreshAccessTokenForApp", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (Exception ex)
            {
                return SteamOfferActionResult.Failed("续期 Steam 会话失败：" + DiagnosticsLogger.Redact(ex.Message), SteamLoginFailureCategory.Unknown.ToString());
            }
        }

        public async Task<SteamOfferActionResult> ValidateSavedSessionAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null)
                return SteamOfferActionResult.Failed("未绑定 Steam 令牌。", "missing-credential");
            if (string.IsNullOrWhiteSpace(credential.SessionId) || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
                return SteamOfferActionResult.Failed("Steam 登录状态未保存。", "missing-session");

            try
            {
                using var http = await CreateHttpClientAsync(cancellationToken);
                var cookies = new SteamWebCookies
                {
                    SessionId = credential.SessionId.Trim(),
                    SteamLoginSecure = credential.SteamLoginSecure.Trim(),
                    SteamLogin = credential.SteamLogin
                };
                var validation = await SteamWebSessionValidator.ValidateAsync(http, cookies, cancellationToken);
                if (validation.State == SteamSessionValidationState.Expired)
                    return SteamOfferActionResult.Failed("已保存的 Steam 登录状态已失效。", "invalid-session");
                if (validation.State == SteamSessionValidationState.NetworkUnavailable)
                    return SteamOfferActionResult.Failed("暂时无法验证 Steam 登录状态，已保留网页登录状态：" + validation.Message, SteamLoginFailureCategory.NetworkError.ToString());
                if (validation.State != SteamSessionValidationState.Valid)
                    return SteamOfferActionResult.Failed("暂时无法确认 Steam 登录状态，已保留网页登录状态：" + validation.Message, SteamLoginFailureCategory.ProtocolChanged.ToString());

                string personaName = await TryFetchPersonaNameAsync(http, cookies, credential.SteamId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(personaName))
                    credential.PersonaName = personaName;
                SteamJwtTokenParser.ApplyTokenMetadata(credential);
                credential.SessionSavedAt = credential.SessionSavedAt == DateTime.MinValue ? DateTime.Now : credential.SessionSavedAt;
                _authStore.Save(credential);
                return SteamOfferActionResult.Success("已保存的 Steam 登录状态仍然有效。");
            }
            catch (HttpRequestException ex)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("ValidateSavedSession", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("ValidateSavedSession", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (Exception ex)
            {
                return SteamOfferActionResult.Failed("验证 Steam 登录状态失败：" + DiagnosticsLogger.Redact(ex.Message), SteamLoginFailureCategory.Unknown.ToString());
            }
        }

        public async Task<SteamOfferActionResult> RestoreFromTokenTextAsync(SteamAuthCredential credential, string tokenText, CancellationToken cancellationToken = default)
        {
            if (credential == null)
                return SteamOfferActionResult.Failed("请先保存 Steam 令牌密钥，再用 Token 恢复登录状态。", "missing-credential");

            var parsed = ParseTokenText(tokenText);
            if (!string.IsNullOrWhiteSpace(parsed.SteamLoginSecure))
            {
                credential.SteamLoginSecure = parsed.SteamLoginSecure;
                credential.SessionId = FirstText(parsed.SessionId, credential.SessionId, RandomHex(12));
                credential.SteamLogin = FirstText(parsed.SteamLogin, credential.SteamLogin);
                credential.AccessToken = FirstText(parsed.AccessToken, credential.AccessToken);
                credential.RefreshToken = FirstText(parsed.RefreshToken, credential.RefreshToken);
                credential.SteamId = FirstText(parsed.SteamId, credential.SteamId, TryGetSteamIdFromSteamLoginSecure(parsed.SteamLoginSecure));
                if (string.IsNullOrWhiteSpace(credential.DeviceId) && !string.IsNullOrWhiteSpace(credential.SteamId))
                    credential.DeviceId = SteamCryptoHelper.GenerateDeviceId(credential.SteamId);

                var validation = await ValidateSavedSessionAsync(credential, cancellationToken);
                if (validation.Ok)
                    return SteamOfferActionResult.Success("已从 Cookie/Token 文本恢复 Steam 登录状态。");
            }

            if (!string.IsNullOrWhiteSpace(parsed.AccessToken))
            {
                credential.AccessToken = parsed.AccessToken;
                credential.SteamId = FirstText(parsed.SteamId, credential.SteamId, TryGetSteamIdFromJwt(parsed.AccessToken));
                var access = await RestoreFromAccessTokenAsync(credential, cancellationToken);
                if (access.Ok)
                    return access;
            }

            if (!string.IsNullOrWhiteSpace(parsed.RefreshToken))
            {
                credential.RefreshToken = parsed.RefreshToken;
                credential.SteamId = FirstText(parsed.SteamId, credential.SteamId, TryGetSteamIdFromJwt(parsed.RefreshToken));
                var refresh = await RestoreFromRefreshTokenAsync(credential, cancellationToken);
                if (refresh.Ok)
                    return refresh;
            }

            string raw = TrimToken(tokenText);
            if (LooksLikeJwt(raw))
            {
                credential.RefreshToken = raw;
                credential.SteamId = FirstText(credential.SteamId, TryGetSteamIdFromJwt(raw));
                var refresh = await RestoreFromRefreshTokenAsync(credential, cancellationToken);
                if (refresh.Ok)
                    return refresh;

                credential.AccessToken = raw;
                var access = await RestoreFromAccessTokenAsync(credential, cancellationToken);
                if (access.Ok)
                    return access;

                return SteamOfferActionResult.Failed("Token 无法恢复 Steam 登录状态；请确认粘贴的是有效 AccessToken/RefreshToken，或改用 Steam 网页登录。", "invalid-token");
            }

            return SteamOfferActionResult.Failed("未识别到 AccessToken、RefreshToken 或 steamLoginSecure Cookie。", "unrecognized-token");
        }

        public async Task<SteamOfferActionResult> LoginAndConfigureAsync(SteamAutoLoginRequest request, CancellationToken cancellationToken = default)
        {
            request ??= new SteamAutoLoginRequest();
            request.SharedSecret = (request.SharedSecret ?? "").Trim();
            request.IdentitySecret = (request.IdentitySecret ?? "").Trim();
            request.AccountName = (request.AccountName ?? "").Trim();
            request.Password ??= "";

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(request.SharedSecret)) missing.Add("shared_secret");
            if (string.IsNullOrWhiteSpace(request.IdentitySecret)) missing.Add("identity_secret");
            if (string.IsNullOrWhiteSpace(request.AccountName)) missing.Add("Steam 账号名");
            if (string.IsNullOrWhiteSpace(request.Password)) missing.Add("Steam 密码");
            if (missing.Count > 0)
                return SteamOfferActionResult.Failed("登录并自动配置失败：缺少 " + string.Join("、", missing) + "。", "missing-fields");
            if (!SteamCryptoHelper.TryValidateSteamGuardSharedSecret(request.SharedSecret, out string sharedSecretError))
                return SteamOfferActionResult.Failed("登录并自动配置失败：" + sharedSecretError, "invalid-shared-secret");
            if (!SteamCryptoHelper.IsValidBase64Secret(request.IdentitySecret))
                return SteamOfferActionResult.Failed("登录并自动配置失败：identity_secret 格式无效。", "invalid-identity-secret");

            try
            {
                using var http = await CreateHttpClientAsync(cancellationToken);
                var rsa = await SteamLoginAuthSessionClient.GetRsaKeyAsync(http, request.AccountName, cancellationToken);
                string encryptedPassword = EncryptPassword(request.Password, rsa.PublicKeyMod, rsa.PublicKeyExp);
                var begin = await SteamLoginAuthSessionClient.BeginAuthSessionAsync(http, request.AccountName, encryptedPassword, rsa.Timestamp, cancellationToken);
                if (string.IsNullOrWhiteSpace(begin.SteamId))
                    throw SteamLoginAuthSessionClient.ClassifyAllowedConfirmations(begin.AllowedConfirmations, "Steam 未返回 SteamID。");

                bool hasDeviceConfirmation = begin.AllowedConfirmations.Any(x => x.Type == 4);
                string mobileAccessToken = "";
                PollAuthResponse? poll = null;
                if (hasDeviceConfirmation)
                {
                    mobileAccessToken = SteamLoginMobileAccessTokenResolver.GetStoredMobileAccessToken(_authStore, begin.SteamId);
                    if (!string.IsNullOrWhiteSpace(mobileAccessToken)
                        && await SteamLoginAuthSessionClient.TrySubmitMobileConfirmationAsync(http, mobileAccessToken, begin.ClientId, begin.SteamId, request.IdentitySecret, cancellationToken))
                    {
                        poll = await SteamLoginAuthSessionClient.TryPollUntilAuthenticatedAsync(http, begin, attempts: 12, cancellationToken);
                    }
                }

                if (poll == null)
                {
                    if (!begin.AllowedConfirmations.Any(x => x.Type == 3))
                        throw SteamLoginAuthSessionClient.ClassifyAllowedConfirmations(begin.AllowedConfirmations, "Steam 登录需要手机端批准；当前没有可用于自动批准的移动端 access_token。请使用 Steam 网页登录，或粘贴/导入可用 AccessToken 后再恢复。");

                    long steamTime = await SteamLoginAuthSessionClient.GetSteamServerTimeAsync(http, begin.SteamId, cancellationToken);
                    string code = SteamCryptoHelper.GenerateSteamGuardCode(request.SharedSecret, steamTime);
                    await SteamLoginAuthSessionClient.SubmitSteamGuardCodeAsync(http, begin.ClientId, begin.SteamId, code, cancellationToken);
                    poll = await SteamLoginAuthSessionClient.PollUntilAuthenticatedAsync(
                        http,
                        begin,
                        hasDeviceConfirmation && string.IsNullOrWhiteSpace(mobileAccessToken),
                        cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(poll.RefreshToken))
                    throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 登录协议返回缺少 refresh_token，请改用 Steam 网页登录。");

                string steamId = FirstText(begin.SteamId, TryGetSteamIdFromJwt(poll.RefreshToken));
                if (string.IsNullOrWhiteSpace(steamId))
                    throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 登录协议返回缺少 SteamID，请改用 Steam 网页登录。");

                var cookies = await FinalizeWebLoginAsync(http, steamId, poll.RefreshToken, cancellationToken);
                string apiKey = PreserveExistingApiKey(_authStore.Load());
                string personaName = await TryFetchPersonaNameAsync(http, cookies, steamId, cancellationToken);

                var credential = new SteamAuthCredential
                {
                    SteamId = steamId,
                    AccountName = FirstText(poll.AccountName, request.AccountName),
                    PersonaName = personaName,
                    DeviceId = SteamCryptoHelper.GenerateDeviceId(steamId),
                    SharedSecret = request.SharedSecret,
                    IdentitySecret = request.IdentitySecret,
                    SessionId = cookies.SessionId,
                    SteamLoginSecure = cookies.SteamLoginSecure,
                    SteamLogin = cookies.SteamLogin,
                    RefreshToken = poll.RefreshToken,
                    AccessToken = poll.AccessToken,
                    ApiKey = apiKey,
                    LoginAccountName = request.AccountName,
                    LoginPassword = request.Password,
                    SavedAt = DateTime.Now,
                    SessionSavedAt = DateTime.Now,
                    LastAutoReloginResult = "登录并自动配置成功",
                    AutoReloginCooldownUntil = DateTime.MinValue
                };

                SteamJwtTokenParser.ApplyTokenMetadata(credential);
                _authStore.Save(credential);

                string apiStatus = string.IsNullOrWhiteSpace(apiKey)
                    ? "API Key 未获取，不影响报价登录状态。"
                    : "API Key 已获取。";
                return SteamOfferActionResult.Success($"Steam 登录状态已保存。SteamID：{SteamAuthSecureStore.MaskSteamId(steamId)}；{apiStatus}");
            }
            catch (SteamLoginException ex)
            {
                return SteamOfferActionResult.Failed(ex.Message, ex.Category.ToString());
            }
            catch (HttpRequestException ex)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("LoginAndConfigure", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return SteamOfferActionResult.Failed(BuildNetworkFailureMessage("LoginAndConfigure", ex), SteamLoginFailureCategory.NetworkError.ToString());
            }
            catch (OperationCanceledException)
            {
                return SteamOfferActionResult.Failed("登录已取消。", "canceled");
            }
            catch (Exception ex)
            {
                return SteamOfferActionResult.Failed("Steam 登录失败：" + DiagnosticsLogger.Redact(ex.Message), SteamLoginFailureCategory.Unknown.ToString());
            }
        }

        internal static string PreserveExistingApiKey(SteamAuthCredential? credential)
        {
            return (credential?.ApiKey ?? "").Trim();
        }

        private async Task<HttpClient> CreateHttpClientAsync(CancellationToken cancellationToken)
        {
            var http = await _httpFactory.CreateResolvedAsync(
                (int)SteamLoginAuthSessionClient.RequestTimeout.TotalSeconds,
                decompression: DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                useCookies: false,
                allowAutoRedirect: false,
                cancellationToken: cancellationToken);
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", SteamLoginAuthSessionClient.UserAgent);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", SteamUrls.CommunityBase);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", SteamUrls.CommunityBase + "/");
            return http;
        }

        private static async Task<SteamWebCookies> FinalizeWebLoginAsync(HttpClient http, string steamId, string refreshToken, CancellationToken cancellationToken)
        {
            string sessionId = RandomHex(12);
            using var finalizeContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["nonce"] = refreshToken,
                ["sessionid"] = sessionId,
                ["redir"] = SteamUrls.WebLoginHome + "?goto="
            });
            using var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, SteamUrls.FinalizeLogin)
            {
                Content = finalizeContent
            };
            finalizeRequest.Headers.TryAddWithoutValidation("Origin", SteamUrls.CommunityBase);
            finalizeRequest.Headers.TryAddWithoutValidation("Referer", SteamUrls.CommunityBase + "/");

            using var finalizeResponse = await SendWithDiagnosticsAsync(http, finalizeRequest, "FinalizeWebLogin", cancellationToken);
            string finalizeText = await finalizeResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!finalizeResponse.IsSuccessStatusCode)
            {
                string summary = SanitizeResponseSummary(finalizeText);
                SteamOfferAuditLog.Error($"Steam finalizelogin failed. Host=login.steampowered.com; Status={(int)finalizeResponse.StatusCode}; Body={summary}");
                throw new SteamLoginException(SteamLoginFailureCategory.NetworkError, $"Steam finalizelogin HTTP {(int)finalizeResponse.StatusCode}。请稍后重试，或改用 Steam 网页登录。");
            }

            using var doc = JsonDocument.Parse(finalizeText);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw SteamLoginAuthSessionClient.ClassifyEResult(error.GetInt32(), "");
            if (!root.TryGetProperty("transfer_info", out var transfers) || transfers.ValueKind != JsonValueKind.Array)
                throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam finalizelogin 返回缺少 transfer_info，请改用 Steam 网页登录。");

            var cookies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var cookiePriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string cookie in GetSetCookieHeaders(finalizeResponse))
                StoreCookie(cookies, cookie, priority: 0, cookiePriorities);

            foreach (var transfer in transfers.EnumerateArray())
            {
                if (!transfer.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
                    continue;
                string? url = urlElement.GetString();
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                int cookiePriority = GetCookieHostPriority(url);

                var fields = new Dictionary<string, string> { ["steamID"] = steamId };
                if (transfer.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in parameters.EnumerateObject())
                        fields[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.ToString();
                }

                bool ok = false;
                for (int attempt = 0; attempt < 3 && !ok; attempt++)
                {
                    using var content = new FormUrlEncodedContent(fields);
                    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                    using var response = await SendWithDiagnosticsAsync(http, request, "TransferWebLogin", cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        SteamOfferAuditLog.InfoThrottled(
                            "steam-transfer-login-http",
                            $"Steam transfer login returned HTTP {(int)response.StatusCode}; retrying.",
                            TimeSpan.FromMinutes(10));
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }

                    foreach (string cookie in GetSetCookieHeaders(response))
                        StoreCookie(cookies, cookie, cookiePriority, cookiePriorities);
                    ok = cookies.ContainsKey("steamLoginSecure");
                    if (!ok)
                        await Task.Delay(500, cancellationToken);
                }
            }

            if (!cookies.TryGetValue("steamLoginSecure", out var secure) || string.IsNullOrWhiteSpace(secure))
                throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 登录成功但未返回 steamLoginSecure，请改用 Steam 网页登录。");

            cookies.TryGetValue("steamLogin", out var steamLogin);
            return new SteamWebCookies
            {
                SessionId = sessionId,
                SteamLoginSecure = secure,
                SteamLogin = steamLogin ?? ""
            };
        }

        private static async Task<string> TryFetchApiKeyAsync(HttpClient http, SteamWebCookies cookies, CancellationToken cancellationToken)
        {
            try
            {
                var page = await FetchApiKeyPageAsync(http, cookies, cancellationToken);
                if (!page.Success)
                {
                    if (page.StatusCode == 429)
                    {
                        throw new SteamLoginException(
                            SteamLoginFailureCategory.RateLimited,
                            $"Steam Web API Key 页面被 Steam 限流：HTTP 429，Page={page.PageKind}。请稍后再试。");
                    }

                    if (string.Equals(page.PageKind, "login-page", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SteamLoginException(
                            SteamLoginFailureCategory.AuthExpired,
                            "Steam Web API Key 页面跳转到登录页；请先重新登录 Steam。");
                    }

                    if (IsTransientStatusCode(page.StatusCode))
                    {
                        throw new SteamLoginException(
                            SteamLoginFailureCategory.NetworkError,
                            $"Steam Web API Key 页面暂时不可用：HTTP {page.StatusCode}，Page={page.PageKind}。");
                    }

                    SteamOfferAuditLog.InfoThrottled(
                        "steam-api-key-page-unavailable:" + page.PageKind,
                        $"Steam Web API key page unavailable. Status={page.StatusCode}; Page={page.PageKind}",
                        TimeSpan.FromMinutes(10));
                    return "";
                }

                string key = ExtractSteamApiKey(page.Html);
                if (string.IsNullOrWhiteSpace(key))
                {
                    if (string.Equals(page.PageKind, "login-page", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new SteamLoginException(
                            SteamLoginFailureCategory.AuthExpired,
                            "Steam Web API Key 页面是登录页；请先重新登录 Steam。");
                    }

                    SteamOfferAuditLog.InfoThrottled(
                        "steam-api-key-not-found:" + page.PageKind,
                        $"Steam Web API key page loaded but key was not found. Page={page.PageKind}",
                        TimeSpan.FromMinutes(10));
                    return "";
                }

                SteamOfferAuditLog.InfoThrottled(
                    "steam-api-key-fetch-success",
                    "Steam Web API key fetched from saved session.",
                    TimeSpan.FromMinutes(10));
                return key;
            }
            catch (SteamLoginException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new SteamLoginException(
                    SteamLoginFailureCategory.NetworkError,
                    BuildNetworkFailureMessage("FetchSteamApiKey", ex));
            }
        }

        public async Task<string> FetchApiKeyFromSavedSessionAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null
                || string.IsNullOrWhiteSpace(credential.SessionId)
                || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
            {
                return "";
            }

            using var http = await CreateHttpClientAsync(cancellationToken);
            var cookies = new SteamWebCookies
            {
                SessionId = credential.SessionId.Trim(),
                SteamLoginSecure = credential.SteamLoginSecure.Trim(),
                SteamLogin = credential.SteamLogin
            };
            var validation = await SteamWebSessionValidator.ValidateAsync(http, cookies, cancellationToken);
            if (validation.State == SteamSessionValidationState.Expired)
                throw new SteamLoginException(SteamLoginFailureCategory.AuthExpired, "请先登录 Steam：" + validation.Message);
            if (validation.State == SteamSessionValidationState.NetworkUnavailable)
                throw new SteamLoginException(SteamLoginFailureCategory.NetworkError, "暂时无法验证 Steam 登录状态，已保留网页登录状态：" + validation.Message);
            if (validation.State != SteamSessionValidationState.Valid)
                throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "暂时无法确认 Steam 登录状态，已保留网页登录状态：" + validation.Message);

            return await TryFetchApiKeyAsync(http, cookies, cancellationToken);
        }

        private static async Task<ApiKeyPageResult> FetchApiKeyPageAsync(HttpClient http, SteamWebCookies cookies, CancellationToken cancellationToken)
        {
            Uri url = new(SteamUrls.DevApiKey);
            for (int redirect = 0; redirect < 5; redirect++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(cookies));
                request.Headers.TryAddWithoutValidation("Referer", SteamUrls.CommunityBase + "/");
                using var response = await SendWithDiagnosticsAsync(http, request, "FetchSteamApiKey", cancellationToken);

                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    Uri? next = ResolveSteamRedirect(url, response.Headers.Location);
                    if (next == null)
                        return ApiKeyPageResult.Failed((int)response.StatusCode, "unsafe-redirect");

                    url = next;
                    continue;
                }

                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                string pageKind = ClassifyApiKeyPage(html);
                return response.IsSuccessStatusCode
                    ? ApiKeyPageResult.Succeeded(html, pageKind)
                    : ApiKeyPageResult.Failed((int)response.StatusCode, pageKind);
            }

            return ApiKeyPageResult.Failed(0, "too-many-redirects");
        }

        public async Task<string> RefreshPersonaNameAsync(SteamAuthCredential credential, CancellationToken cancellationToken = default)
        {
            if (credential == null
                || string.IsNullOrWhiteSpace(credential.SteamId)
                || string.IsNullOrWhiteSpace(credential.SessionId)
                || string.IsNullOrWhiteSpace(credential.SteamLoginSecure))
            {
                return "";
            }

            using var http = await CreateHttpClientAsync(cancellationToken);
            var cookies = new SteamWebCookies
            {
                SessionId = credential.SessionId.Trim(),
                SteamLoginSecure = credential.SteamLoginSecure.Trim(),
                SteamLogin = credential.SteamLogin
            };
            string personaName = await TryFetchPersonaNameAsync(http, cookies, credential.SteamId, cancellationToken);
            if (string.IsNullOrWhiteSpace(personaName))
                return "";

            if (!string.Equals((credential.PersonaName ?? "").Trim(), personaName, StringComparison.Ordinal))
            {
                credential.PersonaName = personaName;
                _authStore.Save(credential);
            }

            return personaName;
        }

        public async Task<string> RefreshPersonaNameAsync(SteamTokenEntry token, CancellationToken cancellationToken = default)
        {
            if (token == null
                || string.IsNullOrWhiteSpace(token.SteamId)
                || string.IsNullOrWhiteSpace(token.SessionId)
                || string.IsNullOrWhiteSpace(token.SteamLoginSecure))
            {
                return "";
            }

            using var http = await CreateHttpClientAsync(cancellationToken);
            var cookies = new SteamWebCookies
            {
                SessionId = token.SessionId.Trim(),
                SteamLoginSecure = token.SteamLoginSecure.Trim(),
                SteamLogin = token.SteamLogin
            };
            string personaName = await TryFetchPersonaNameAsync(http, cookies, token.SteamId, cancellationToken);
            if (string.IsNullOrWhiteSpace(personaName))
                return "";

            if (!string.Equals((token.PersonaName ?? "").Trim(), personaName, StringComparison.Ordinal))
            {
                token.PersonaName = personaName;
                _tokenVault.SaveToken(token);
            }

            return personaName;
        }

        private static async Task<string> TryFetchPersonaNameAsync(HttpClient http, SteamWebCookies cookies, string steamId, CancellationToken cancellationToken)
        {
            string id = (steamId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsDigit))
                return "";

            try
            {
                string[] urls =
                {
                    SteamUrls.ProfileXml(id),
                    SteamUrls.Profile(id)
                };

                foreach (string url in urls)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(cookies));
                    using var response = await http.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    string body = await response.Content.ReadAsStringAsync(cancellationToken);
                    string persona = ExtractPersonaName(body);
                    if (!string.IsNullOrWhiteSpace(persona))
                        return persona;
                }
            }
            catch
            {
                // Nickname is display-only. Failure must not block login or token import paths.
            }

            return "";
        }

        private void SaveRestoredCredential(
            SteamAuthCredential credential,
            string steamId,
            SteamWebCookies cookies,
            string refreshToken,
            string accessToken,
            string apiKey,
            string personaName,
            string result)
        {
            credential.SteamId = FirstText(steamId, credential.SteamId);
            credential.PersonaName = FirstText(personaName, credential.PersonaName);
            credential.SessionId = cookies.SessionId;
            credential.SteamLoginSecure = cookies.SteamLoginSecure;
            credential.SteamLogin = FirstText(cookies.SteamLogin, credential.SteamLogin);
            credential.RefreshToken = FirstText(refreshToken, credential.RefreshToken);
            credential.AccessToken = FirstText(accessToken, credential.AccessToken, TryGetAccessTokenFromSteamLoginSecure(cookies.SteamLoginSecure));
            SteamJwtTokenParser.ApplyTokenMetadata(credential);
            if (!string.IsNullOrWhiteSpace(apiKey))
                credential.ApiKey = apiKey;
            if (string.IsNullOrWhiteSpace(credential.DeviceId) && !string.IsNullOrWhiteSpace(credential.SteamId))
                credential.DeviceId = SteamCryptoHelper.GenerateDeviceId(credential.SteamId);
            credential.SessionSavedAt = DateTime.Now;
            credential.LastAutoReloginAt = DateTime.Now;
            credential.LastAutoReloginResult = result;
            credential.AutoReloginCooldownUntil = DateTime.MinValue;
            _authStore.Save(credential);
        }

    }

}
