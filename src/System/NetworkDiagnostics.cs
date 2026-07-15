using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.SystemServices
{
    public static class NetworkDiagnostics
    {
        public static string BuildFailureMessage(string service, string step, Exception ex)
        {
            string safeService = string.IsNullOrWhiteSpace(service) ? "HTTP" : service.Trim();
            string safeStep = string.IsNullOrWhiteSpace(step) ? "UnknownStep" : step.Trim();
            return $"{safeService} {safeStep} 请求失败：{Describe(ex)}";
        }

        public static string Describe(Exception ex)
        {
            var parts = new[]
            {
                DescribePrimary(ex),
                DescribeSocket(ex),
                DescribeTls(ex),
                DescribeWebException(ex),
                DescribeTimeout(ex),
                DescribeIo(ex)
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);

            string message = string.Join("; ", parts);
            return string.IsNullOrWhiteSpace(message)
                ? "未知网络错误"
                : DiagnosticsLogger.Redact(message);
        }

        private static string DescribePrimary(Exception ex)
        {
            if (ex is HttpRequestException http)
            {
                string status = http.StatusCode.HasValue ? "; HTTP " + (int)http.StatusCode.Value : "";
                return "HttpRequestError=" + http.HttpRequestError + status;
            }

            if (ex is TaskCanceledException)
                return "Timeout";

            return ex.GetType().Name;
        }

        private static string DescribeSocket(Exception ex)
        {
            if (FindInner<SocketException>(ex) is not { } socket)
                return "";

            return socket.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData => "DNS=" + socket.SocketErrorCode,
                SocketError.TimedOut => "SocketTimeout",
                SocketError.ConnectionRefused or SocketError.ConnectionReset or SocketError.NetworkUnreachable or SocketError.HostUnreachable
                    => "Socket=" + socket.SocketErrorCode,
                _ => "Socket=" + socket.SocketErrorCode
            };
        }

        private static string DescribeTls(Exception ex)
        {
            return FindInner<AuthenticationException>(ex) is null ? "" : "TLS";
        }

        private static string DescribeWebException(Exception ex)
        {
            return FindInner<WebException>(ex) is { } web ? "WebException=" + web.Status : "";
        }

        private static string DescribeTimeout(Exception ex)
        {
            return FindInner<TimeoutException>(ex) is null ? "" : "Timeout";
        }

        private static string DescribeIo(Exception ex)
        {
            return FindInner<IOException>(ex) is null ? "" : "IO";
        }

        private static T? FindInner<T>(Exception ex) where T : Exception
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is T typed)
                    return typed;
            }

            return null;
        }
    }
}
