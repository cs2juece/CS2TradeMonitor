using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal sealed class DetailedDiagnosticsHttpHandler : DelegatingHandler
    {
        private readonly string _module;
        private readonly DetailedDiagnosticsService? _fixedService;

        public DetailedDiagnosticsHttpHandler(
            string module,
            HttpMessageHandler innerHandler,
            DetailedDiagnosticsService? fixedService = null)
            : base(innerHandler)
        {
            _module = string.IsNullOrWhiteSpace(module) ? "HTTP" : module.Trim();
            _fixedService = fixedService;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            DetailedDiagnosticsService? service = _fixedService ?? DetailedDiagnosticsRuntime.Current;
            if (service?.IsEnabledFast != true)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            string operationId = Guid.NewGuid().ToString("N");
            string? parentOperationId = DetailedDiagnosticOperationContext.CurrentOperationId;
            var stopwatch = Stopwatch.StartNew();
            DetailedDiagnosticBodyCapture? requestBody = await TryCaptureRequestBodyAsync(service, request, cancellationToken)
                .ConfigureAwait(false);
            var requestData = BuildRequestData(service, request, operationId, parentOperationId);
            if (requestBody is not null)
                requestData["requestBody"] = requestBody;
            service.Record("Information", _module, "HttpRequestStarted", requestData, operationId);

            try
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                DetailedDiagnosticBodyCapture? responseBody = await TryCaptureResponseBodyAsync(service, response, cancellationToken)
                    .ConfigureAwait(false);
                string? routeFingerprint = service.Correlate(
                    "httpRoute",
                    request.RequestUri?.AbsolutePath ?? string.Empty);
                if (response.IsSuccessStatusCode && responseBody is not null)
                {
                    responseBody = service.ApplySuccessfulResponseBodySampling(
                        _module,
                        routeFingerprint,
                        responseBody);
                }
                stopwatch.Stop();
                var responseData = BuildRequestData(service, request, operationId, parentOperationId);
                responseData["statusCode"] = (int)response.StatusCode;
                responseData["succeeded"] = response.IsSuccessStatusCode;
                responseData["elapsedMs"] = stopwatch.Elapsed.TotalMilliseconds;
                if (responseBody is not null)
                    responseData["responseBody"] = responseBody;
                service.Record(
                    response.IsSuccessStatusCode ? "Information" : "Warning",
                    _module,
                    "HttpRequestCompleted",
                    responseData,
                    operationId,
                    priority: response.IsSuccessStatusCode
                        ? DetailedDiagnosticPriority.Normal
                        : DetailedDiagnosticPriority.Critical);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var failureData = BuildRequestData(service, request, operationId, parentOperationId);
                failureData["elapsedMs"] = stopwatch.Elapsed.TotalMilliseconds;
                failureData["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name;
                failureData["message"] = ex.Message;
                failureData["hresult"] = ex.HResult;
                if (ex is OperationCanceledException)
                {
                    failureData["cancellationReason"] = cancellationToken.IsCancellationRequested
                        ? "CallerCancellation"
                        : "TimeoutOrTransportCancellation";
                }
                service.Record(
                    "Error",
                    _module,
                    "HttpRequestFailed",
                    failureData,
                    operationId,
                    priority: DetailedDiagnosticPriority.Critical);
                throw;
            }
        }

        private Dictionary<string, object?> BuildRequestData(
            DetailedDiagnosticsService service,
            HttpRequestMessage request,
            string operationId,
            string? parentOperationId)
        {
            string absolutePath = request.RequestUri?.AbsolutePath ?? string.Empty;
            return new Dictionary<string, object?>
            {
                ["operationId"] = operationId,
                ["parentOperationId"] = parentOperationId,
                ["provider"] = _module,
                ["operationName"] = _module + "." + request.Method.Method,
                ["method"] = request.Method.Method,
                ["scheme"] = request.RequestUri?.Scheme ?? "",
                ["host"] = request.RequestUri?.Host ?? "",
                ["routeFingerprint"] = service.Correlate("httpRoute", absolutePath),
                ["pathSegmentCount"] = absolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length,
                ["contentType"] = request.Content?.Headers.ContentType?.MediaType ?? ""
            };
        }

        private static async Task<DetailedDiagnosticBodyCapture?> TryCaptureRequestBodyAsync(
            DetailedDiagnosticsService service,
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent? content = request.Content;
            if (content is null || !IsJson(content.Headers.ContentType))
                return null;
            if (content is not StringContent && content is not ByteArrayContent)
                return null;

            try
            {
                string body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return service.CaptureBody(body, content.Headers.ContentType?.ToString());
            }
            catch
            {
                // Request capture is optional and must never alter the outgoing request result.
                return null;
            }
        }

        private static async Task<DetailedDiagnosticBodyCapture?> TryCaptureResponseBodyAsync(
            DetailedDiagnosticsService service,
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            HttpContent content = response.Content;
            if (!IsJson(content.Headers.ContentType))
                return null;

            try
            {
                byte[] bytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var replacement = new ByteArrayContent(bytes);
                CopyHeaders(content.Headers, replacement.Headers);
                response.Content = replacement;
                content.Dispose();
                string body = Encoding.UTF8.GetString(bytes);
                return service.CaptureBody(body, replacement.Headers.ContentType?.ToString());
            }
            catch
            {
                // Response capture failure must not replace the platform response or business result.
                return null;
            }
        }

        private static bool IsJson(MediaTypeHeaderValue? contentType)
        {
            string mediaType = contentType?.MediaType ?? "";
            return mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyHeaders(HttpContentHeaders source, HttpContentHeaders destination)
        {
            foreach ((string key, IEnumerable<string> values) in source)
                destination.TryAddWithoutValidation(key, values);
        }
    }
}
