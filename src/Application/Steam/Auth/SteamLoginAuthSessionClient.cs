using CS2TradeMonitor.Application.Steam;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CS2TradeMonitor.src.SystemServices;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginCryptoSupport;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginHttpDiagnostics;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal static class SteamLoginAuthSessionClient
    {
        public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36 CS2TradeMonitor/1.0";
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

        public sealed record AccessTokenRefreshResult(string AccessToken, string RefreshToken);

        public static async Task<RsaKeyResponse> GetRsaKeyAsync(HttpClient http, string accountName, CancellationToken cancellationToken)
        {
            var writer = new SteamProtoWriter();
            writer.WriteString(1, accountName);
            byte[] response = await SendSteamApiAsync(http, HttpMethod.Get, "GetPasswordRSAPublicKey", writer.ToArray(), cancellationToken);
            var reader = new SteamProtoReader(response);
            var result = new RsaKeyResponse();
            while (reader.TryReadField(out int field, out int wire))
            {
                switch (field)
                {
                    case 1:
                        result.PublicKeyMod = reader.ReadString(wire);
                        break;
                    case 2:
                        result.PublicKeyExp = reader.ReadString(wire);
                        break;
                    case 3:
                        result.Timestamp = reader.ReadUInt64(wire).ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        reader.Skip(wire);
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(result.PublicKeyMod)
                || string.IsNullOrWhiteSpace(result.PublicKeyExp)
                || string.IsNullOrWhiteSpace(result.Timestamp))
                throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 登录协议返回缺少 RSA 公钥，请改用 Steam 网页登录。");

            return result;
        }

        public static async Task<BeginAuthResponse> BeginAuthSessionAsync(
            HttpClient http,
            string accountName,
            string encryptedPassword,
            string timestamp,
            CancellationToken cancellationToken)
        {
            var writer = new SteamProtoWriter();
            writer.WriteString(1, UserAgent);
            writer.WriteString(2, accountName);
            writer.WriteString(3, encryptedPassword);
            writer.WriteUInt64(4, ulong.Parse(timestamp, CultureInfo.InvariantCulture));
            writer.WriteBool(5, true);
            writer.WriteUInt64(6, 3); // MobileApp, so shared_secret DeviceCode can confirm without waiting for phone approval.
            writer.WriteUInt64(7, 1); // Persistent
            writer.WriteString(8, "Community");

            byte[] response = await SendSteamApiAsync(http, HttpMethod.Post, "BeginAuthSessionViaCredentials", writer.ToArray(), cancellationToken);
            return SteamLoginProtoResponseParser.DecodeBeginAuthResponse(response);
        }

        public static async Task SubmitSteamGuardCodeAsync(
            HttpClient http,
            ulong clientId,
            string steamId,
            string code,
            CancellationToken cancellationToken)
        {
            var writer = new SteamProtoWriter();
            writer.WriteUInt64(1, clientId);
            writer.WriteFixed64(2, ulong.Parse(steamId, CultureInfo.InvariantCulture));
            writer.WriteString(3, code);
            writer.WriteUInt64(4, 3); // DeviceCode
            await SendSteamApiAsync(http, HttpMethod.Post, "UpdateAuthSessionWithSteamGuardCode", writer.ToArray(), cancellationToken);
        }

        public static async Task<PollAuthResponse> PollUntilAuthenticatedAsync(
            HttpClient http,
            BeginAuthResponse begin,
            bool mobileConfirmationWithoutToken,
            CancellationToken cancellationToken)
        {
            var poll = await TryPollUntilAuthenticatedAsync(http, begin, attempts: 12, cancellationToken);
            if (poll != null)
                return poll;

            if (mobileConfirmationWithoutToken)
            {
                throw new SteamLoginException(
                    SteamLoginFailureCategory.InvalidTwoFactor,
                    "Steam 同时弹出手机批准，但本地未保存可自动批准的移动端 access_token；已尝试 Steam Guard 验证码仍未通过。请用包含 access_token/oauth_token 的 maFile 导入，或使用 Steam 网页登录。");
            }

            throw new SteamLoginException(SteamLoginFailureCategory.InvalidTwoFactor, "Steam Guard 验证未完成。请优先检查 shared_secret 是否来自当前 Steam 账号；本机会使用 Steam QueryTime 生成验证码，通常不是本机时间问题。");
        }

        public static async Task<PollAuthResponse?> TryPollUntilAuthenticatedAsync(
            HttpClient http,
            BeginAuthResponse begin,
            int attempts,
            CancellationToken cancellationToken)
        {
            int delayMs = Math.Clamp((int)(begin.IntervalSeconds <= 0 ? 1000 : begin.IntervalSeconds * 1000), 1000, 5000);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                await Task.Delay(delayMs, cancellationToken);
                var writer = new SteamProtoWriter();
                writer.WriteUInt64(1, begin.ClientId);
                writer.WriteBytes(2, begin.RequestId);
                byte[] response = await SendSteamApiAsync(http, HttpMethod.Post, "PollAuthSessionStatus", writer.ToArray(), cancellationToken);
                var poll = SteamLoginProtoResponseParser.DecodePollAuthResponse(response);
                if (!string.IsNullOrWhiteSpace(poll.RefreshToken))
                    return poll;
            }

            return null;
        }

        public static async Task<bool> TrySubmitMobileConfirmationAsync(
            HttpClient http,
            string accessToken,
            ulong clientId,
            string steamId,
            string identitySecret,
            CancellationToken cancellationToken)
        {
            try
            {
                int version = await GetAuthSessionVersionAsync(http, accessToken, clientId, cancellationToken);
                byte[] signature = BuildMobileConfirmationSignature(version, clientId, steamId, identitySecret);

                var writer = new SteamProtoWriter();
                writer.WriteUInt64(1, (ulong)version);
                writer.WriteUInt64(2, clientId);
                writer.WriteFixed64(3, ulong.Parse(steamId, CultureInfo.InvariantCulture));
                writer.WriteBytes(4, signature);
                writer.WriteBool(5, true);
                writer.WriteUInt64(6, 1); // Persistent
                await SendSteamApiAsync(http, HttpMethod.Post, "UpdateAuthSessionWithMobileConfirmation", writer.ToArray(), cancellationToken, accessToken);
                return true;
            }
            catch (SteamLoginException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public static async Task<AccessTokenRefreshResult> GenerateAccessTokenForAppAsync(
            HttpClient http,
            string refreshToken,
            string steamId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new SteamLoginException(SteamLoginFailureCategory.AuthExpired, "RefreshToken 为空，无法续期 AccessToken。");
            string steamIdText = (steamId ?? "").Trim();
            if (!ulong.TryParse(steamIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "SteamID 无法解析，无法续期 AccessToken。");

            using var request = new HttpRequestMessage(HttpMethod.Post, SteamUrls.GenerateAccessTokenForApp)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["refresh_token"] = refreshToken.Trim(),
                    ["steamid"] = steamIdText,
                    ["renewal_type"] = "1"
                })
            };

            using var response = await SendWithDiagnosticsAsync(http, request, "GenerateAccessTokenForApp", cancellationToken);
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string summary = SanitizeResponseSummary(text);
                SteamOfferAuditLog.Error($"Steam API returned HTTP error. Step=GenerateAccessTokenForApp; Host=api.steampowered.com; Status={(int)response.StatusCode}; Body={summary}");
                throw new SteamLoginException(SteamLoginFailureCategory.NetworkError, $"Steam 登录接口 GenerateAccessTokenForApp 返回 HTTP {(int)response.StatusCode}。请稍后重试。");
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("response", out var body))
                {
                    string accessToken = body.TryGetProperty("access_token", out var accessElement) ? accessElement.GetString() ?? "" : "";
                    string nextRefreshToken = body.TryGetProperty("refresh_token", out var refreshElement) ? refreshElement.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(accessToken))
                        return new AccessTokenRefreshResult(accessToken, nextRefreshToken);
                }
            }
            catch (JsonException ex)
            {
                SteamOfferAuditLog.Error("Steam GenerateAccessTokenForApp JSON parse failed.", ex);
                throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 续期接口返回无法解析。");
            }

            throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 续期接口未返回 access_token。");
        }

        public static async Task<int> GetAuthSessionVersionAsync(HttpClient http, string accessToken, ulong clientId, CancellationToken cancellationToken)
        {
            try
            {
                var writer = new SteamProtoWriter();
                writer.WriteUInt64(1, clientId);
                byte[] response = await SendSteamApiAsync(http, HttpMethod.Post, "GetAuthSessionInfo", writer.ToArray(), cancellationToken, accessToken);
                var reader = new SteamProtoReader(response);
                while (reader.TryReadField(out int field, out int wire))
                {
                    if (field == 8)
                    {
                        long version = (long)reader.ReadUInt64(wire);
                        return version > 0 && version <= int.MaxValue ? (int)version : 1;
                    }
                    reader.Skip(wire);
                }
            }
            catch (SteamLoginException)
            {
                // AuthSessionInfo 查询失败时按协议默认版本继续，后续登录流程仍会校验结果。
            }

            return 1;
        }

        public static async Task<long> GetSteamServerTimeAsync(HttpClient http, string steamId, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, SteamUrls.QueryTimeV1)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["steamid"] = string.IsNullOrWhiteSpace(steamId) ? "0" : steamId
                    })
                };
                using var response = await SendWithDiagnosticsAsync(http, request, "QueryTime", cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    SteamOfferAuditLog.InfoThrottled(
                        "steam-login-querytime-http",
                        $"Steam QueryTime returned HTTP {(int)response.StatusCode}; using local system time.",
                        TimeSpan.FromMinutes(10));
                    return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                string text = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("response", out var body)
                    && body.TryGetProperty("server_time", out var timeElement))
                {
                    if (timeElement.ValueKind == JsonValueKind.String
                        && long.TryParse(timeElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long stringTime))
                        return stringTime;
                    if (timeElement.ValueKind == JsonValueKind.Number && timeElement.TryGetInt64(out long numberTime))
                        return numberTime;
                }
            }
            catch (Exception ex)
            {
                // Keep login usable if Steam's time endpoint is unavailable; the caller still gets normal 2FA diagnostics.
                SteamOfferAuditLog.InfoThrottled(
                    "steam-login-querytime-unavailable",
                    "Steam QueryTime unavailable; using local system time. " + SteamOfferAuditLog.RedactSecrets(ex.Message),
                    TimeSpan.FromMinutes(10));
            }

            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        internal static SteamLoginException ClassifyAllowedConfirmations(List<AllowedConfirmation> confirmations, string fallback)
        {
            if (confirmations.Any(x => x.Type == 2 || x.Type == 5))
                return new SteamLoginException(SteamLoginFailureCategory.EmailCodeRequired, "Steam 要求邮箱验证码。请使用 Steam 网页登录手动处理。");
            if (confirmations.Any(x => x.Type == 4))
                return new SteamLoginException(SteamLoginFailureCategory.EmailCodeRequired, "Steam 要求手机端确认登录；本机不能仅靠 identity_secret 直接批准。请使用 Steam 网页登录，或粘贴/导入可用 AccessToken 后再恢复。");
            return new SteamLoginException(SteamLoginFailureCategory.InvalidPassword, fallback);
        }

        internal static SteamLoginException ClassifyEResult(int eresult, string errorMessage)
        {
            string suffix = string.IsNullOrWhiteSpace(errorMessage) ? "" : " " + DiagnosticsLogger.Redact(errorMessage);
            return eresult switch
            {
                5 => new SteamLoginException(SteamLoginFailureCategory.InvalidPassword, "Steam 账号名或密码错误，已停止重试。" + suffix),
                63 or 66 or 74 => new SteamLoginException(SteamLoginFailureCategory.EmailCodeRequired, "Steam 要求邮箱验证。请使用 Steam 网页登录手动处理。" + suffix),
                84 or 95 or 96 or 97 => new SteamLoginException(SteamLoginFailureCategory.RateLimited, "Steam 登录触发限流，已停止本次自动登录。" + suffix),
                85 or 88 or 89 => new SteamLoginException(SteamLoginFailureCategory.InvalidTwoFactor, "Steam Guard 验证失败。请检查 shared_secret 是否来自当前 Steam 账号；本机会优先使用 Steam 时间校准。" + suffix),
                101 => new SteamLoginException(SteamLoginFailureCategory.CaptchaRequired, "Steam 要求 CAPTCHA。请使用 Steam 网页登录手动处理。" + suffix),
                _ => new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, $"Steam 登录接口返回 EResult={eresult}，请改用 Steam 网页登录。" + suffix)
            };
        }

        private static async Task<byte[]> SendSteamApiAsync(
            HttpClient http,
            HttpMethod method,
            string apiMethod,
            byte[] payload,
            CancellationToken cancellationToken,
            string accessToken = "")
        {
            string encoded = Convert.ToBase64String(payload);
            string url = SteamUrls.AuthenticationService(apiMethod);
            HttpRequestMessage request;
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(accessToken))
                query.Add("access_token=" + Uri.EscapeDataString(accessToken.Trim()));
            if (method == HttpMethod.Get)
            {
                query.Insert(0, "input_protobuf_encoded=" + Uri.EscapeDataString(encoded));
                request = new HttpRequestMessage(HttpMethod.Get, url + "?" + string.Join("&", query));
            }
            else
            {
                string requestUrl = query.Count == 0 ? url : url + "?" + string.Join("&", query);
                request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["input_protobuf_encoded"] = encoded
                    })
                };
            }

            using (request)
            using (var response = await SendWithDiagnosticsAsync(http, request, apiMethod, cancellationToken))
            {
                if (response.Headers.TryGetValues("x-eresult", out var resultValues)
                    && int.TryParse(resultValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int eresult)
                    && eresult != 1)
                {
                    string errorMessage = "";
                    if (response.Headers.TryGetValues("x-error_message", out var messages))
                        errorMessage = messages.FirstOrDefault() ?? "";
                    throw ClassifyEResult(eresult, errorMessage);
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    string summary = SanitizeResponseSummary(Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 512)));
                    SteamOfferAuditLog.Error($"Steam API returned HTTP error. Step={apiMethod}; Host=api.steampowered.com; Status={(int)response.StatusCode}; Body={summary}");
                    throw new SteamLoginException(SteamLoginFailureCategory.NetworkError, $"Steam 登录接口 {apiMethod} 返回 HTTP {(int)response.StatusCode}。请稍后重试。");
                }
                if (bytes.Length > 0 && bytes[0] == (byte)'{')
                    throw new SteamLoginException(SteamLoginFailureCategory.ProtocolChanged, "Steam 登录接口返回 JSON 错误，请改用 Steam 网页登录。");
                return bytes;
            }
        }
    }
}
