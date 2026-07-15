using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.YouPin
{
    public sealed class YouPinAuthService : IYouPinAuthService
    {
        private const string BaseUrl = YouPinMobileApiClient.BaseUrl;
        private const string SendSmsEndpoint = "/api/user/Auth/SendSignInSmsCode";
        private const string SmsSignInEndpoint = "/api/user/Auth/SmsSignIn";
        private const string SmsUpSignInEndpoint = "/api/user/Auth/SmsUpSignIn";
        private const string SmsUpConfigEndpoint = "/api/user/Auth/GetSmsUpSignInConfig";
        private const string UserInfoEndpoint = "/api/youpin/bff/user/Account/getUserInfoForApp";
        private const string LegacyUserInfoEndpoint = "/api/user/Account/getUserInfo";
        private const string DeviceW2Endpoint = "/api/deviceW2";
        private const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAv9BDdhCDahZNFuJeesx3gzoQfD7pE0AeWiNBZlc21ph6kU9zd58X/1warV3C1VIX0vMAmhOcj5u86i+L2Lb2V68dX2Nb70MIDeW6Ibe8d0nF8D30tPsM7kaAyvxkY6ECM6RHGNhV4RrzkHmf5DeR9bybQGE0A9jcjuxszD1wsW/n19eeom7MroHqlRorp5LLNR8bSbmhTw6M/RQ/Fm3lKjKcvs1QNVyBNimrbD+ZVPE/KHSZLQ1jdF6tppvFnGxgJU9NFmxGFU0hx6cZiQHkhOQfGDFkElxgtj8gFJ1narTwYbvfe5nGSiznv/EUJSjTHxzX1TEkex0+5j4vSANt1QIDAQAB\n" +
            "-----END PUBLIC KEY-----";

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CS2DesktopMonitor.YouPinAuth.v1");
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static YouPinAuthService? _instance;
        public static YouPinAuthService Instance => _instance ??= new YouPinAuthService();

        private readonly HttpClient _http;
        private readonly object _fileLock = new();
        private readonly string _credentialPath;
        private string _lastStatus = "未登录";
        private string _lastError = "";
        private bool _credentialCacheInitialized;
        private DateTime _credentialCacheWriteUtc;
        private YouPinCredential? _credentialCache;
        private bool _credentialWriteBlocked;

        private YouPinAuthService()
            : this(YouPinServiceRuntimeServices.ResolveDomesticHttpFactory())
        {
        }

        internal YouPinAuthService(IDomesticHttpClientFactory httpFactory)
            : this(httpFactory, RuntimeDataPaths.GetSecureFilePath("youpin_auth.dat"))
        {
        }

        internal YouPinAuthService(IDomesticHttpClientFactory httpFactory, string credentialPath)
        {
            _http = (httpFactory ?? throw new ArgumentNullException(nameof(httpFactory))).Create(20, new Uri(BaseUrl));
            ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);
            _credentialPath = Path.GetFullPath(credentialPath);
        }

        public YouPinCredential? GetCredential(Settings? settings = null)
        {
            var stored = LoadStoredCredential();
            if (_credentialWriteBlocked)
                return null;

            if (stored != null && !string.IsNullOrWhiteSpace(stored.Token))
            {
                string device = ResolveDeviceTokenForUse(stored.DeviceToken, settings);
                if (!string.Equals(stored.DeviceToken?.Trim(), device, StringComparison.Ordinal))
                    stored.DeviceToken = device;
                string persistedUk = YouPinMobileApiClient.GetPersistedUk();
                if (string.IsNullOrWhiteSpace(stored.Uk) && !string.IsNullOrWhiteSpace(persistedUk))
                    stored.Uk = persistedUk;
                stored.Source = "加密保存";
                return stored;
            }

            string legacyToken = settings?.YouPinInventoryToken ?? "";
            if (string.IsNullOrWhiteSpace(legacyToken))
            {
                try
                {
                    // 兼容旧版本手动填写凭据：调用方未传 settings 时只能从持久化配置兜底读取。
                    legacyToken = Settings.Load().YouPinInventoryToken;
                }
                catch
                {
                    legacyToken = "";
                }
            }

            if (string.IsNullOrWhiteSpace(legacyToken)) return null;

            string legacyDevice = settings?.YouPinInventoryDeviceToken ?? "";
            if (string.IsNullOrWhiteSpace(legacyDevice))
            {
                try
                {
                    // 兼容旧版本设备标识：调用方未传 settings 时只能从持久化配置兜底读取。
                    legacyDevice = Settings.Load().YouPinInventoryDeviceToken;
                }
                catch
                {
                    legacyDevice = "";
                }
            }

            return new YouPinCredential
            {
                Token = legacyToken.Trim(),
                DeviceToken = ResolveDeviceTokenForUse(legacyDevice, settings),
                Uk = YouPinMobileApiClient.GetPersistedUk(),
                Source = "手动填写"
            };
        }

        public YouPinAuthState GetState(Settings? settings = null)
        {
            YouPinCredential? credential;
            try
            {
                credential = GetCredential(settings);
            }
            catch (Exception ex)
            {
                _lastError = "悠悠设备档案不可用：" + Sanitize(ex.Message);
                credential = null;
            }
            if (credential == null)
            {
                return new YouPinAuthState
                {
                    HasCredential = false,
                    Status = string.IsNullOrWhiteSpace(_lastError) ? _lastStatus : "登录异常",
                    Error = _lastError
                };
            }

            string name = string.IsNullOrWhiteSpace(credential.NickName) ? "已保存登录凭据" : credential.NickName;
            return new YouPinAuthState
            {
                HasCredential = true,
                NickName = name,
                SavedAt = credential.SavedAt,
                Source = credential.Source,
                DeviceTokenPreview = MaskTail(credential.DeviceToken),
                Status = string.IsNullOrWhiteSpace(_lastError) ? "已保存" : "登录异常",
                Error = _lastError
            };
        }

        public async Task<YouPinSmsSendResult> SendSmsCodeAsync(string phone, Settings? settings = null)
        {
            string normalizedPhone = NormalizePhone(phone);
            if (normalizedPhone.Length < 6)
                return YouPinSmsSendResult.Failed("请输入正确的手机号。");

            string sessionId = EnsureDeviceToken(settings);
            string uk = await TryGetUkAsync();
            using var req = new HttpRequestMessage(HttpMethod.Post, SendSmsEndpoint);
            req.Content = JsonContent(new
            {
                Area = 86,
                Mobile = normalizedPhone,
                Sessionid = sessionId,
                Code = ""
            });
            ApplyHeaders(req, sessionId, sessionId, token: "", uk: uk);

            try
            {
                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "发送验证码");
                var root = await ReadJsonAsync(resp, "发送验证码失败");
                int code = GetInt(root, "Code", "code");
                string msg = GetString(root, "Msg", "msg", "Message", "message") ?? "";

                if (resp.IsSuccessStatusCode && code == 0 && (msg.Contains("成功", StringComparison.Ordinal) || msg.Contains("发送", StringComparison.Ordinal)))
                {
                    _lastStatus = "验证码已发送";
                    _lastError = "";
                    return YouPinSmsSendResult.Success("验证码已发送，请输入短信验证码。", sessionId);
                }

                var up = await GetSmsUpConfigAsync(sessionId);
                if (up.Ok)
                {
                    _lastStatus = "需要短信验证";
                    _lastError = "";
                    return up with { SessionId = sessionId };
                }

                return YouPinSmsSendResult.Failed(string.IsNullOrWhiteSpace(msg) ? $"发送验证码失败：Code={code}" : Sanitize(msg), sessionId);
            }
            catch (Exception ex)
            {
                _lastError = Sanitize(ex.Message);
                return YouPinSmsSendResult.Failed("发送验证码失败：" + _lastError, sessionId);
            }
        }

        public async Task<YouPinLoginResult> CompleteSmsLoginAsync(string phone, string code, string sessionId, Settings? settings = null)
        {
            string normalizedPhone = NormalizePhone(phone);
            string device = string.IsNullOrWhiteSpace(sessionId) ? EnsureDeviceToken(settings) : sessionId.Trim();
            string uk = YouPinMobileApiClient.GetPersistedUk();
            if (normalizedPhone.Length < 6)
                return YouPinLoginResult.Failed("请输入正确的手机号。");
            if (string.IsNullOrWhiteSpace(device))
                return YouPinLoginResult.Failed("请先发送验证码。");

            bool useSmsUp = string.IsNullOrWhiteSpace(code);
            string endpoint = useSmsUp ? SmsUpSignInEndpoint : SmsSignInEndpoint;

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = JsonContent(new
            {
                Area = 86,
                Code = code?.Trim() ?? "",
                DeviceName = device,
                Sessionid = device,
                Mobile = normalizedPhone
            });
            ApplyHeaders(req, device, device, uk: uk);

            try
            {
                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "悠悠有品登录");
                var root = await ReadJsonAsync(resp, "悠悠有品登录失败");
                int resultCode = GetInt(root, "Code", "code");
                string msg = GetString(root, "Msg", "msg", "Message", "message") ?? "";
                string token = "";
                if (TryGetProperty(root, out var data, "Data", "data"))
                    token = GetString(data, "Token", "token") ?? "";

                if (!resp.IsSuccessStatusCode || resultCode != 0 || string.IsNullOrWhiteSpace(token))
                    return YouPinLoginResult.Failed(string.IsNullOrWhiteSpace(msg) ? $"悠悠有品登录失败：Code={resultCode}" : Sanitize(msg));

                var info = await ValidateCredentialAsync(token, device, uk);
                if (!info.Ok)
                    return YouPinLoginResult.Failed(info.Message);

                var credential = new YouPinCredential
                {
                    Token = token,
                    DeviceToken = device,
                    Uk = FirstText(info.Uk, uk),
                    NickName = info.NickName,
                    UserId = info.UserId,
                    SavedAt = DateTime.Now,
                    Source = "加密保存"
                };
                SaveCredential(credential);

                _lastStatus = "登录成功";
                _lastError = "";
                return YouPinLoginResult.Success(string.IsNullOrWhiteSpace(info.NickName) ? "悠悠有品登录成功，凭据已加密保存。" : $"悠悠有品登录成功：{info.NickName}", credential.SavedAt, info.NickName);
            }
            catch (Exception ex)
            {
                _lastError = Sanitize(ex.Message);
                return YouPinLoginResult.Failed("悠悠有品登录失败：" + _lastError);
            }
        }

        public async Task<YouPinLoginResult> ValidateCurrentAsync(Settings? settings = null)
        {
            var credential = GetCredential(settings);
            if (credential == null)
                return YouPinLoginResult.Failed("未保存悠悠有品登录凭据。");

            var info = await ValidateCredentialAsync(credential.Token, credential.DeviceToken, credential.Uk);
            if (!info.Ok)
            {
                _lastError = info.Message;
                return YouPinLoginResult.Failed(info.Message);
            }

            if (credential.Source == "加密保存")
            {
                credential.NickName = info.NickName;
                credential.UserId = info.UserId;
                credential.Uk = FirstText(info.Uk, credential.Uk);
                credential.SavedAt = credential.SavedAt == default ? DateTime.Now : credential.SavedAt;
                SaveCredential(credential);
            }

            _lastStatus = "登录有效";
            _lastError = "";
            return YouPinLoginResult.Success(string.IsNullOrWhiteSpace(info.NickName) ? "悠悠有品登录有效。" : $"悠悠有品登录有效：{info.NickName}", credential.SavedAt, info.NickName);
        }

        public void ClearCredential(Settings? settings = null, bool clearLegacy = true)
        {
            lock (_fileLock)
            {
                try
                {
                    if (_credentialWriteBlocked && File.Exists(_credentialPath))
                    {
                        _lastError = "加密凭据不可用，已保留原文件，不能自动清除。";
                        return;
                    }

                    if (File.Exists(_credentialPath))
                        File.Delete(_credentialPath);
                    _credentialCacheInitialized = true;
                    _credentialCacheWriteUtc = DateTime.MinValue;
                    _credentialCache = null;
                }
                catch (Exception ex)
                {
                    _lastError = Sanitize(ex.Message);
                }
            }

            if (clearLegacy)
            {
                if (settings != null)
                {
                    settings.YouPinInventoryToken = "";
                    settings.YouPinInventoryDeviceToken = "";
                }

                try
                {
                    // 清理旧明文配置必须强制读最新磁盘配置，避免只清掉内存副本。
                    var persisted = Settings.Load(forceReload: true);
                    persisted.YouPinInventoryToken = "";
                    persisted.YouPinInventoryDeviceToken = "";
                    persisted.Save();
                }
                catch
                {
                    // 即使旧配置清理失败，加密凭据本身也已经清除，不能阻断退出流程。
                }
            }

            _lastStatus = "已清除登录凭据";
            if (clearLegacy) _lastError = "";
        }

        public string EnsureDeviceToken(Settings? settings = null)
        {
            var credential = LoadStoredCredential();
            if (_credentialWriteBlocked)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(_lastError)
                        ? "悠悠加密凭据不可用，已保留原文件。"
                        : _lastError);

            if (!string.IsNullOrWhiteSpace(credential?.DeviceToken)
                && !YouPinMobileApiClient.IsLegacyDeviceIdentifier(credential.DeviceToken))
                return credential.DeviceToken.Trim();

            if (!string.IsNullOrWhiteSpace(settings?.YouPinInventoryDeviceToken)
                && !YouPinMobileApiClient.IsLegacyDeviceIdentifier(settings.YouPinInventoryDeviceToken))
                return settings.YouPinInventoryDeviceToken.Trim();

            string stable = YouPinMobileApiClient.GetDeviceToken();
            PersistLegacyDeviceToken(stable, settings, replaceLegacy: true);
            return stable;
        }

        private string ResolveDeviceTokenForUse(string? deviceToken, Settings? settings)
        {
            string current = (deviceToken ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(current) && !YouPinMobileApiClient.IsLegacyDeviceIdentifier(current))
                return current;

            string stable = YouPinMobileApiClient.GetDeviceToken();
            PersistLegacyDeviceToken(stable, settings, replaceLegacy: true);
            return stable;
        }

        private static void PersistLegacyDeviceToken(string deviceToken, Settings? settings, bool replaceLegacy = false)
        {
            if (string.IsNullOrWhiteSpace(deviceToken))
                return;

            try
            {
                if (settings != null && ShouldPersistDeviceToken(settings.YouPinInventoryDeviceToken, replaceLegacy))
                {
                    settings.YouPinInventoryDeviceToken = deviceToken.Trim();
                    settings.Save();
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPin", "PersistRuntimeDeviceToken", ex, retryable: true, category: "DeviceFingerprint");
            }

            try
            {
                // 设备指纹需要落盘兼容旧配置，强制重载可避免覆盖用户刚保存的其他设置。
                var persisted = Settings.Load(forceReload: true);
                if (ShouldPersistDeviceToken(persisted.YouPinInventoryDeviceToken, replaceLegacy))
                {
                    persisted.YouPinInventoryDeviceToken = deviceToken.Trim();
                    persisted.Save();
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPin", "PersistSettingsDeviceToken", ex, retryable: true, category: "DeviceFingerprint");
            }
        }

        private static bool ShouldPersistDeviceToken(string? current, bool replaceLegacy)
        {
            return string.IsNullOrWhiteSpace(current)
                || (replaceLegacy && YouPinMobileApiClient.IsLegacyDeviceIdentifier(current));
        }

        private async Task<YouPinUserInfoResult> ValidateCredentialAsync(string token, string device, string uk = "")
        {
            if (string.IsNullOrWhiteSpace(token))
                return YouPinUserInfoResult.Failed("未保存悠悠有品 Token。");

            var preferred = await ValidateCredentialByEndpointAsync(UserInfoEndpoint, token, device, uk);
            if (preferred.Ok || preferred.Message.Contains("登录状态失效", StringComparison.Ordinal))
                return preferred;

            var legacy = await ValidateCredentialByEndpointAsync(LegacyUserInfoEndpoint, token, device, uk);
            return legacy.Ok ? legacy : preferred;
        }

        private async Task<YouPinUserInfoResult> ValidateCredentialByEndpointAsync(string endpoint, string token, string device, string uk)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                ApplyHeaders(req, device, device, token.Trim(), uk);
                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "校验登录");
                var root = await ReadJsonAsync(resp, "校验登录失败");
                int code = GetInt(root, "Code", "code");
                if (!resp.IsSuccessStatusCode || code != 0)
                {
                    string msg = GetString(root, "Msg", "msg", "Message", "message") ?? "";
                    return YouPinUserInfoResult.Failed(string.IsNullOrWhiteSpace(msg) ? "悠悠有品登录状态失效，请重新登录。" : Sanitize(msg));
                }

                if (!TryGetProperty(root, out var data, "Data", "data"))
                    return YouPinUserInfoResult.Failed("校验登录失败：返回缺少用户信息。");

                return YouPinUserInfoResult.Success(
                    GetString(data, "NickName", "nickName", "nickname") ?? "",
                    GetString(data, "UserId", "userId", "id", "Id") ?? "",
                    FirstText(GetString(data, "uk", "Uk", "u", "U"), uk));
            }
            catch (Exception ex)
            {
                return YouPinUserInfoResult.Failed(Sanitize(ex.Message));
            }
        }

        private async Task<YouPinSmsSendResult> GetSmsUpConfigAsync(string sessionId)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, SmsUpConfigEndpoint);
            ApplyHeaders(req, sessionId, sessionId);

            try
            {
                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取短信验证信息");
                var root = await ReadJsonAsync(resp, "获取短信验证信息失败");
                int code = GetInt(root, "Code", "code");
                if (!resp.IsSuccessStatusCode || code != 0 || !TryGetProperty(root, out var data, "Data", "data"))
                    return YouPinSmsSendResult.Failed("获取短信验证信息失败。", sessionId);

                string content = GetString(data, "SmsUpContent", "smsUpContent", "Content", "content") ?? "";
                string number = GetString(data, "SmsUpNumber", "smsUpNumber", "Number", "number") ?? "";
                if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(number))
                    return YouPinSmsSendResult.Failed("获取短信验证信息失败：返回内容不完整。", sessionId);

                return YouPinSmsSendResult.RequireSmsUp("需要短信验证。", sessionId, content, number);
            }
            catch
            {
                return YouPinSmsSendResult.Failed("获取短信验证信息失败。", sessionId);
            }
        }

        private YouPinCredential? LoadStoredCredential()
        {
            lock (_fileLock)
            {
                try
                {
                    if (!File.Exists(_credentialPath))
                    {
                        _credentialCacheInitialized = true;
                        _credentialCacheWriteUtc = DateTime.MinValue;
                        _credentialCache = null;
                        _credentialWriteBlocked = false;
                        return null;
                    }

                    var writeUtc = File.GetLastWriteTimeUtc(_credentialPath);
                    if (_credentialCacheInitialized && writeUtc == _credentialCacheWriteUtc)
                        return CloneCredential(_credentialCache);

                    string text = File.ReadAllText(_credentialPath).Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new InvalidDataException("悠悠加密凭据内容为空。");

                    byte[] protectedBytes = Convert.FromBase64String(text);
                    byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    var credential = JsonSerializer.Deserialize<YouPinCredential>(Encoding.UTF8.GetString(plain), JsonOptions);
                    if (credential == null || string.IsNullOrWhiteSpace(credential.Token))
                        throw new InvalidDataException("悠悠加密凭据结构无效。");
                    _credentialCacheInitialized = true;
                    _credentialCacheWriteUtc = writeUtc;
                    _credentialCache = CloneCredential(credential);
                    _credentialWriteBlocked = false;
                    if (_lastError.StartsWith("读取加密凭据失败：", StringComparison.Ordinal))
                        _lastError = "";
                    return credential;
                }
                catch (Exception ex)
                {
                    _lastError = "读取加密凭据失败：" + Sanitize(ex.Message);
                    _credentialWriteBlocked = true;
                    _credentialCacheInitialized = true;
                    _credentialCacheWriteUtc = DateTime.MinValue;
                    _credentialCache = null;
                    return null;
                }
            }
        }

        private void SaveCredential(YouPinCredential credential)
        {
            lock (_fileLock)
            {
                if (!_credentialCacheInitialized && File.Exists(_credentialPath))
                    _ = LoadStoredCredential();
                if (_credentialWriteBlocked && File.Exists(_credentialPath))
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(_lastError)
                            ? "悠悠加密凭据不可用，已保留原文件。"
                            : _lastError + " 原文件已保留，不能覆盖。");

                var json = JsonSerializer.Serialize(credential, JsonOptions);
                byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
                RuntimeDataPaths.WriteTextAtomic(_credentialPath, Convert.ToBase64String(protectedBytes));
                _credentialCacheInitialized = true;
                _credentialCacheWriteUtc = File.GetLastWriteTimeUtc(_credentialPath);
                _credentialCache = CloneCredential(credential);
                _credentialWriteBlocked = false;
            }
        }

        private static YouPinCredential? CloneCredential(YouPinCredential? credential)
        {
            if (credential == null) return null;
            return new YouPinCredential
            {
                Token = credential.Token,
                DeviceToken = credential.DeviceToken,
                Uk = credential.Uk,
                NickName = credential.NickName,
                UserId = credential.UserId,
                SavedAt = credential.SavedAt,
                Source = credential.Source
            };
        }

        private async Task<string> TryGetUkAsync()
        {
            try
            {
                string aesKey = RandomString(16);
                string payload = JsonSerializer.Serialize(new { iud = YouPinMobileApiClient.GetDeviceId() });
                using var req = new HttpRequestMessage(HttpMethod.Post, DeviceW2Endpoint);
                req.Content = JsonContent(new
                {
                    encryptedData = EncryptAesEcb(payload, aesKey),
                    encryptedAesKey = EncryptRsaPkcs1(aesKey)
                });
                string device = YouPinMobileApiClient.GetDeviceToken();
                ApplyHeaders(req, device, device);

                using var resp = await YouPinMobileApiClient.SendAsync(_http, req, "获取悠悠设备标识");
                if (!resp.IsSuccessStatusCode) return "";
                string encrypted = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(encrypted)) return "";
                string plain = DecryptAesEcb(encrypted, aesKey);
                using var doc = JsonDocument.Parse(plain);
                string uk = GetString(doc.RootElement, "u", "uk") ?? "";
                YouPinMobileApiClient.PersistUk(uk);
                return uk;
            }
            catch
            {
                return "";
            }
        }

        private static string EncryptAesEcb(string text, string key)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(key);
            using var encryptor = aes.CreateEncryptor();
            byte[] encrypted = encryptor.TransformFinalBlock(Encoding.UTF8.GetBytes(text), 0, Encoding.UTF8.GetByteCount(text));
            return Convert.ToBase64String(encrypted);
        }

        private static string DecryptAesEcb(string base64, string key)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Encoding.UTF8.GetBytes(key);
            using var decryptor = aes.CreateDecryptor();
            byte[] input = Convert.FromBase64String(base64);
            byte[] plain = decryptor.TransformFinalBlock(input, 0, input.Length);
            return Encoding.UTF8.GetString(plain);
        }

        private static string EncryptRsaPkcs1(string text)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(text), RSAEncryptionPadding.Pkcs1));
        }

        private static StringContent JsonContent(object payload)
        {
            return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string fallback)
        {
            using var doc = await YouPinMobileApiClient.ReadJsonDocumentAsync(response, fallback);
            return doc.RootElement.Clone();
        }

        private static void ApplyHeaders(HttpRequestMessage req, string deviceToken, string deviceId, string token = "", string uk = "")
        {
            YouPinMobileApiClient.ApplyHeaders(req, token, deviceToken, uk, deviceId);
        }

        private static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        private static string NormalizePhone(string phone)
        {
            string digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
            if (digits.StartsWith("86", StringComparison.Ordinal) && digits.Length > 11)
                digits = digits[2..];
            return digits;
        }

        private static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var buffer = new char[length];
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[length];
            rng.GetBytes(bytes);
            for (int i = 0; i < length; i++)
                buffer[i] = chars[bytes[i] % chars.Length];
            return new string(buffer);
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
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
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)) return value;
            if (int.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        public static string Sanitize(string? text)
        {
            return YouPinMobileApiClient.Sanitize(text);
        }

        private static string MaskTail(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string trimmed = value.Trim();
            return trimmed.Length <= 6 ? "***" : "***" + trimmed[^4..];
        }
    }

    public sealed class YouPinAuthState
    {
        public bool HasCredential { get; set; }
        public string NickName { get; set; } = "";
        public DateTime SavedAt { get; set; }
        public string Source { get; set; } = "";
        public string DeviceTokenPreview { get; set; } = "";
        public string Status { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public sealed record YouPinSmsSendResult(bool Ok, string Message, string SessionId, bool NeedSmsUp, string SmsUpContent, string SmsUpNumber)
    {
        public static YouPinSmsSendResult Success(string message, string sessionId) => new(true, message, sessionId, false, "", "");
        public static YouPinSmsSendResult RequireSmsUp(string message, string sessionId, string content, string number) => new(true, message, sessionId, true, content, number);
        public static YouPinSmsSendResult Failed(string message, string sessionId = "") => new(false, message, sessionId, false, "", "");
    }

    public sealed record YouPinLoginResult(bool Ok, string Message, DateTime SavedAt, string NickName)
    {
        public static YouPinLoginResult Success(string message, DateTime savedAt, string nickName = "") => new(true, message, savedAt, nickName);
        public static YouPinLoginResult Failed(string message) => new(false, message, DateTime.MinValue, "");
    }

    internal sealed record YouPinUserInfoResult(bool Ok, string Message, string NickName, string UserId, string Uk)
    {
        public static YouPinUserInfoResult Success(string nickName, string userId, string uk) => new(true, "", nickName, userId, uk);
        public static YouPinUserInfoResult Failed(string message) => new(false, message, "", "", "");
    }
}
