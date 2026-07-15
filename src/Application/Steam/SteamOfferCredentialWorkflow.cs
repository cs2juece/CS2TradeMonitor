using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam.Auth;
using CS2TradeMonitor.Application.Steam.Auth.Import;
using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.Steam.SteamOfferLoginRecoveryHelper;

namespace CS2TradeMonitor.Application.Steam
{
    internal sealed class SteamOfferCredentialWorkflow
    {
        private readonly ISteamAuthStore _authStore;
        private readonly ISteamTokenVault _tokenVault;
        private readonly ISteamLoginService _loginService;
        private readonly Action _raiseDataUpdated;
        private readonly Action _queuePersonaNameRefresh;
        private readonly Action _queueSteamApiKeyRefresh;

        public SteamOfferCredentialWorkflow(
            ISteamAuthStore authStore,
            ISteamTokenVault tokenVault,
            ISteamLoginService loginService,
            Action raiseDataUpdated,
            Action queuePersonaNameRefresh,
            Action queueSteamApiKeyRefresh)
        {
            _authStore = authStore ?? throw new ArgumentNullException(nameof(authStore));
            _tokenVault = tokenVault ?? throw new ArgumentNullException(nameof(tokenVault));
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));
            _raiseDataUpdated = raiseDataUpdated ?? throw new ArgumentNullException(nameof(raiseDataUpdated));
            _queuePersonaNameRefresh = queuePersonaNameRefresh ?? throw new ArgumentNullException(nameof(queuePersonaNameRefresh));
            _queueSteamApiKeyRefresh = queueSteamApiKeyRefresh ?? throw new ArgumentNullException(nameof(queueSteamApiKeyRefresh));
        }

        public SteamOfferActionResult ImportMaFileText(string jsonText, string sourcePath = "")
        {
            if (TokenImporterChain.Default.CanImport(jsonText, sourcePath))
            {
                var imported = TokenImporterChain.Default.Import(jsonText, sourcePath);
                if (!imported.Ok || imported.Token == null)
                    return SteamOfferActionResult.Failed(imported.Message);

                string savedId = _tokenVault.SaveToken(imported.Token);
                if (string.Equals(imported.Token.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(savedId))
                    _tokenVault.SetDefaultSteamToken(savedId);
                _raiseDataUpdated();
                return SteamOfferActionResult.Success(imported.Message + " 已加密保存。");
            }

            var prepared = SteamMaFileImportPreparer.PrepareText(jsonText, sourcePath, allowManifestFileResolution: true);
            if (!prepared.Ok)
                return SteamOfferActionResult.Failed(prepared.Message);

            try
            {
                var current = _authStore.Load() ?? new SteamAuthCredential();
                var parsed = SteamMaFileImportPreparer.ParseCredential(prepared.Text, current);
                if (!parsed.Ok || parsed.Credential == null)
                    return SteamOfferActionResult.Failed(parsed.Message);

                var credential = parsed.Credential;
                _authStore.Save(credential);
                SteamOfferAuditLog.LogImportToken(credential.SteamId, SteamMaFileImportPreparer.ClassifySource(prepared.SourcePath));
                _raiseDataUpdated();
                return SteamOfferActionResult.Success(
                    string.IsNullOrWhiteSpace(prepared.Message) || prepared.Message.StartsWith("已读取", StringComparison.Ordinal)
                        ? "Steam 令牌已加密保存。"
                        : "Steam 令牌已加密保存。" + prepared.Message);
            }
            catch (JsonException)
            {
                return SteamOfferActionResult.Failed("该 maFile 已加密或不是明文 JSON。请先在 Steam Desktop Authenticator 中解密/导出明文 maFile 再导入。");
            }
            catch (Exception ex)
            {
                return SteamOfferActionResult.Failed("导入失败：" + SteamOfferAuditLog.RedactSecrets(ex.Message));
            }
        }

        public SteamOfferImportFileResult LoadMaFileImportFile(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return SteamOfferImportFileResult.Failed("请选择 Steam maFile / SDA JSON 文件。");

            try
            {
                string text = File.ReadAllText(sourcePath);
                if (TokenImporterChain.Default.CanImport(text, sourcePath))
                    return SteamOfferImportFileResult.Success(text, sourcePath, "已读取 otpauth 令牌链接。请确认来源可信，然后点击“导入并加密保存”。");

                return SteamMaFileImportPreparer.PrepareText(text, sourcePath, allowManifestFileResolution: false);
            }
            catch (Exception ex)
            {
                return SteamOfferImportFileResult.Failed("读取文件失败：" + SteamOfferAuditLog.RedactSecrets(ex.Message), sourcePath);
            }
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
            sessionId = (sessionId ?? "").Trim();
            steamLoginSecure = (steamLoginSecure ?? "").Trim();
            steamLogin = (steamLogin ?? "").Trim();
            apiKey = (apiKey ?? "").Trim();
            accessToken = (accessToken ?? "").Trim();
            refreshToken = (refreshToken ?? "").Trim();
            steamId = (steamId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(steamLoginSecure))
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(sessionId)) missing.Add("sessionid");
                if (string.IsNullOrWhiteSpace(steamLoginSecure)) missing.Add("steamLoginSecure");
                return SteamOfferActionResult.Failed("Steam 登录状态保存失败：缺少 " + string.Join("、", missing) + "。");
            }

            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先导入 Steam 令牌，再保存 Steam 登录状态。");

            credential.SessionId = sessionId;
            credential.SteamLoginSecure = steamLoginSecure;
            if (!string.IsNullOrWhiteSpace(steamLogin))
                credential.SteamLogin = steamLogin;
            if (!string.IsNullOrWhiteSpace(apiKey))
                credential.ApiKey = apiKey;
            if (!string.IsNullOrWhiteSpace(accessToken))
                credential.AccessToken = accessToken;
            if (!string.IsNullOrWhiteSpace(refreshToken))
                credential.RefreshToken = refreshToken;
            if (!string.IsNullOrWhiteSpace(steamId))
                credential.SteamId = steamId;
            if (string.IsNullOrWhiteSpace(credential.DeviceId) && !string.IsNullOrWhiteSpace(credential.SteamId))
                credential.DeviceId = SteamCryptoHelper.GenerateDeviceId(credential.SteamId);
            credential.SessionSavedAt = DateTime.Now;
            credential.LastAutoReloginAt = DateTime.MinValue;
            credential.LastAutoReloginResult = "";
            credential.AutoReloginCooldownUntil = DateTime.MinValue;
            _authStore.Save(credential);
            var saved = _authStore.Load();
            if (saved == null
                || !string.Equals((saved.SessionId ?? "").Trim(), sessionId, StringComparison.Ordinal)
                || !string.Equals((saved.SteamLoginSecure ?? "").Trim(), steamLoginSecure, StringComparison.Ordinal))
            {
                return SteamOfferActionResult.Failed("Steam 登录状态保存后读取不一致，请重新选择当前令牌后再保存。", "session-save-mismatch");
            }

            _raiseDataUpdated();
            _queuePersonaNameRefresh();
            _queueSteamApiKeyRefresh();
            return SteamOfferActionResult.Success("Steam 登录状态已加密保存。");
        }

        public async Task<SteamOfferActionResult> RestoreLoginStateFromTokenTextAsync(string tokenText)
        {
            var credential = _authStore.Load();
            if (credential == null)
                return SteamOfferActionResult.Failed("请先保存 Steam 令牌密钥，再用 Token 恢复登录状态。", "missing-credential");

            var result = await _loginService.RestoreFromTokenTextAsync(credential, tokenText);
            _raiseDataUpdated();
            return result;
        }

        public async Task<SteamOfferActionResult> LoginAndConfigureAsync(SteamAutoLoginRequest request)
        {
            request ??= new SteamAutoLoginRequest();
            var credential = _authStore.Load();
            if (credential != null)
            {
                if (string.IsNullOrWhiteSpace(request.SharedSecret))
                    request.SharedSecret = credential.SharedSecret;
                if (string.IsNullOrWhiteSpace(request.IdentitySecret))
                    request.IdentitySecret = credential.IdentitySecret;
                if (ClampPersistedRateLimitCooldown(credential, DateTime.Now))
                    _authStore.Save(credential);
                if (credential.AutoReloginCooldownUntil > DateTime.Now)
                {
                    return SteamOfferActionResult.Failed(
                        $"Steam 登录冷却中，{credential.AutoReloginCooldownUntil:HH:mm:ss} 后再试；请不要连续点击。也可以展开“其他方式”使用 Steam 网页登录。",
                        "cooldown");
                }
            }

            var result = await _loginService.LoginAndConfigureAsync(request);
            if (result.Ok)
            {
                _raiseDataUpdated();
                _queueSteamApiKeyRefresh();
            }
            if (!result.Ok && IsRateLimited(result.Code))
            {
                var updated = _authStore.Load() ?? credential ?? new SteamAuthCredential();
                updated.LastAutoReloginAt = DateTime.Now;
                updated.LastAutoReloginResult = result.Message;
                updated.AutoReloginCooldownUntil = DateTime.Now.Add(RateLimitCooldown);
                _authStore.Save(updated);
                _raiseDataUpdated();
                return SteamOfferActionResult.Failed(
                    result.Message + $" 已进入 3 分钟冷却，{updated.AutoReloginCooldownUntil:HH:mm:ss} 后再试；期间建议使用 Steam 网页登录。",
                    result.Code);
            }

            return result;
        }

        public SteamOfferActionResult SaveManualTokenSecrets(string sharedSecret, string identitySecret)
        {
            sharedSecret = (sharedSecret ?? "").Trim();
            identitySecret = (identitySecret ?? "").Trim();

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(sharedSecret)) missing.Add("shared_secret");
            if (string.IsNullOrWhiteSpace(identitySecret)) missing.Add("identity_secret");
            if (missing.Count > 0)
                return SteamOfferActionResult.Failed("保存令牌失败：缺少 " + string.Join("、", missing) + "。", "missing-token-fields");
            if (!SteamCryptoHelper.TryValidateSteamGuardSharedSecret(sharedSecret, out string sharedSecretError))
                return SteamOfferActionResult.Failed("保存令牌失败：" + sharedSecretError, "invalid-shared-secret");
            if (!SteamCryptoHelper.IsValidBase64Secret(identitySecret))
                return SteamOfferActionResult.Failed("保存令牌失败：identity_secret 格式无效。", "invalid-identity-secret");

            var current = _authStore.Load() ?? new SteamAuthCredential();
            current.SharedSecret = sharedSecret;
            current.IdentitySecret = identitySecret;
            current.SavedAt = DateTime.Now;
            _authStore.Save(current);
            _raiseDataUpdated();
            return SteamOfferActionResult.Success("令牌密钥已加密保存。下一步优先使用 Steam 网页登录或 Token 恢复登录状态；账号密码登录仅作为备用。");
        }
    }
}
