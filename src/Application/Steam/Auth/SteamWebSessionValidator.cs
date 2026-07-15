using CS2TradeMonitor.src.SystemServices;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginCookieHelper;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginHttpDiagnostics;
using static CS2TradeMonitor.Application.Steam.Auth.SteamLoginWebPageParser;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal static class SteamWebSessionValidator
    {
        public static async Task<SteamSessionValidationResult> ValidateAsync(
            HttpClient http,
            SteamWebCookies cookies,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(cookies.SessionId) || string.IsNullOrWhiteSpace(cookies.SteamLoginSecure))
                return SteamSessionValidationResult.Expired("缺少 sessionid 或 steamLoginSecure。");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, SteamUrls.CommunityBase + "/my/");
                request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(cookies));
                using var response = await SendWithDiagnosticsAsync(http, request, "ValidateWebSession", cancellationToken);

                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    string location = response.Headers.Location?.ToString() ?? "";
                    return IsLoginRedirect(location)
                        ? SteamSessionValidationResult.Expired("Steam 重定向到登录页。", response.StatusCode)
                        : SteamSessionValidationResult.Valid();
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    return SteamSessionValidationResult.Expired($"Steam 返回 HTTP {(int)response.StatusCode}。", response.StatusCode);

                if (IsTransientHttpStatus(response.StatusCode))
                    return SteamSessionValidationResult.NetworkUnavailable($"Steam 返回 HTTP {(int)response.StatusCode}，暂时无法验证。", response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    string summary = await ReadResponseSummaryAsync(response, cancellationToken);
                    return SteamSessionValidationResult.ProtocolUnexpected(
                        $"Steam 登录验证 HTTP {(int)response.StatusCode}，响应摘要：{summary}",
                        response.StatusCode);
                }

                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                return LooksLikeLoginPage(html)
                    ? SteamSessionValidationResult.Expired("Steam 返回登录页，网页登录状态已失效。")
                    : SteamSessionValidationResult.Valid();
            }
            catch (SteamLoginException ex) when (ex.Category == SteamLoginFailureCategory.NetworkError)
            {
                return SteamSessionValidationResult.NetworkUnavailable(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return SteamSessionValidationResult.NetworkUnavailable(BuildNetworkFailureMessage("ValidateWebSession", ex));
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return SteamSessionValidationResult.NetworkUnavailable(BuildNetworkFailureMessage("ValidateWebSession", ex));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return SteamSessionValidationResult.ProtocolUnexpected(DiagnosticsLogger.Redact(ex.Message));
            }
        }
    }
}
