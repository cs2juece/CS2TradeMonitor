using System;

namespace CS2TradeMonitor.Application.YouPin
{
    internal enum YouPinDeviceHeartbeatErrorKind
    {
        Success,
        HttpError,
        ApiRejected,
        LoginExpired,
        NetworkError,
        ParseError,
        Unknown
    }

    internal sealed class YouPinDeviceHeartbeatResult
    {
        private YouPinDeviceHeartbeatResult(
            bool success,
            YouPinDeviceHeartbeatErrorKind kind,
            int? httpStatusCode,
            int? apiCode,
            string apiMessage,
            string exceptionType,
            string safeMessage)
        {
            Success = success;
            Kind = kind;
            HttpStatusCode = httpStatusCode;
            ApiCode = apiCode;
            ApiMessage = apiMessage ?? "";
            ExceptionType = exceptionType ?? "";
            SafeMessage = safeMessage ?? "";
        }

        public bool Success { get; }
        public YouPinDeviceHeartbeatErrorKind Kind { get; }
        public int? HttpStatusCode { get; }
        public int? ApiCode { get; }
        public string ApiMessage { get; }
        public string ExceptionType { get; }
        public string SafeMessage { get; }

        public static YouPinDeviceHeartbeatResult Ok(int httpStatusCode, int apiCode, string apiMessage)
        {
            return new YouPinDeviceHeartbeatResult(
                true,
                YouPinDeviceHeartbeatErrorKind.Success,
                httpStatusCode,
                apiCode,
                YouPinMobileApiClient.Sanitize(apiMessage),
                "",
                "");
        }

        public static YouPinDeviceHeartbeatResult Fail(
            YouPinDeviceHeartbeatErrorKind kind,
            int? httpStatusCode = null,
            int? apiCode = null,
            string apiMessage = "",
            string exceptionType = "",
            string safeMessage = "")
        {
            return new YouPinDeviceHeartbeatResult(
                false,
                kind,
                httpStatusCode,
                apiCode,
                YouPinMobileApiClient.Sanitize(apiMessage),
                exceptionType,
                YouPinMobileApiClient.Sanitize(safeMessage));
        }

        public static YouPinDeviceHeartbeatResult FromException(YouPinDeviceHeartbeatErrorKind kind, Exception ex)
        {
            return Fail(
                kind,
                exceptionType: ex.GetType().Name,
                safeMessage: ex.Message);
        }

        public string ToDiagnosticText()
        {
            var parts = new System.Collections.Generic.List<string>
            {
                "Kind=" + Kind
            };

            if (HttpStatusCode.HasValue)
                parts.Add("HTTP=" + HttpStatusCode.Value);
            if (ApiCode.HasValue)
                parts.Add("Code=" + ApiCode.Value);
            if (!string.IsNullOrWhiteSpace(ApiMessage))
                parts.Add("Msg=" + ApiMessage);
            if (!string.IsNullOrWhiteSpace(ExceptionType))
                parts.Add("Exception=" + ExceptionType);
            if (!string.IsNullOrWhiteSpace(SafeMessage))
                parts.Add("Message=" + SafeMessage);

            return string.Join("; ", parts);
        }
    }
}
