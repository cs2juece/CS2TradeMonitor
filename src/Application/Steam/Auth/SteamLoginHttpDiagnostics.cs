using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal static class SteamLoginHttpDiagnostics
    {
        private static ISteamConnectionResolver SteamConnection => SteamServiceRuntimeServices.ResolveConnection();

        public static async Task<HttpResponseMessage> SendWithDiagnosticsAsync(
            HttpClient http,
            HttpRequestMessage request,
            string step,
            CancellationToken cancellationToken)
        {
            string host = request.RequestUri?.Host ?? "unknown";
            try
            {
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.TooManyRequests)
                    SteamConnection.ReportSuccess();
                return response;
            }
            catch (HttpRequestException ex)
            {
                string detail = BuildNetworkFailureMessage(step, ex);
                SteamConnection.ReportFailure(detail);
                SteamOfferAuditLog.Error($"Steam HTTP request failed. Step={SanitizeLogValue(step)}; Host={SanitizeLogValue(host)}; {detail}");
                throw new SteamLoginException(SteamLoginFailureCategory.NetworkError, detail);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                string detail = BuildNetworkFailureMessage(step, ex);
                SteamConnection.ReportFailure(detail);
                SteamOfferAuditLog.Error($"Steam HTTP request timed out. Step={SanitizeLogValue(step)}; Host={SanitizeLogValue(host)}; {detail}");
                throw new SteamLoginException(SteamLoginFailureCategory.NetworkError, detail);
            }
        }

        public static string BuildNetworkFailureMessage(string step, Exception ex)
        {
            return NetworkDiagnostics.BuildFailureMessage("Steam", SanitizeLogValue(step), ex);
        }

        public static async Task<string> ReadResponseSummaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                string text = await response.Content.ReadAsStringAsync(cancellationToken);
                return SanitizeResponseSummary(text);
            }
            catch (Exception ex)
            {
                return "响应体读取失败：" + DiagnosticsLogger.Redact(ex.Message);
            }
        }

        public static string SanitizeResponseSummary(string? text)
        {
            string clean = SteamOfferAuditLog.RedactSecrets(text ?? "");
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            return clean.Length <= 240 ? clean : clean[..240] + "...";
        }

        public static bool IsTransientHttpStatus(HttpStatusCode status)
        {
            int code = (int)status;
            return code == 408 || code == 425 || code == 429 || code >= 500;
        }

        public static bool IsTransientStatusCode(int statusCode)
        {
            return statusCode == 0
                || statusCode == 408
                || statusCode == 425
                || statusCode == 429
                || statusCode >= 500;
        }

        public static bool IsLoginRedirect(string location)
        {
            string text = location ?? "";
            return text.Contains("/login", StringComparison.OrdinalIgnoreCase)
                || text.Contains("steamcommunity.com/login", StringComparison.OrdinalIgnoreCase)
                || text.Contains("login.steampowered.com", StringComparison.OrdinalIgnoreCase);
        }

        public static bool LooksLikeLoginPage(string html)
        {
            string text = html ?? "";
            return text.Contains("steamcommunity.com/login", StringComparison.OrdinalIgnoreCase)
                || text.Contains("login.steampowered.com", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Sign in to Steam", StringComparison.OrdinalIgnoreCase)
                || text.Contains("g_steamID = false", StringComparison.OrdinalIgnoreCase);
        }

        public static string SanitizeLogValue(string value)
        {
            return DiagnosticsLogger.Redact((value ?? "").Replace('\r', ' ').Replace('\n', ' '));
        }
    }
}
