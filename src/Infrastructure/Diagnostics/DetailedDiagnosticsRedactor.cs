using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal sealed class DetailedDiagnosticBodyCapture
    {
        public string ContentType { get; init; } = "";
        public long OriginalLengthBytes { get; init; }
        public string Sha256 { get; init; } = "";
        public bool ParseSucceeded { get; init; }
        public bool Truncated { get; init; }
        public JsonNode? RedactedBody { get; init; }
        public string? RedactedFragment { get; init; }
        public string? FailureReason { get; init; }
    }

    internal sealed partial class DetailedDiagnosticsRedactor
    {
        public const string RemovedUnverified = "[REMOVED_UNVERIFIED]";
        public const string RemovedSensitive = "[REMOVED_SENSITIVE]";
        public const string RedactedSecret = "[REDACTED]";
        private static readonly HashSet<string> VerifiedSafeBodyFields = new(StringComparer.Ordinal)
        {
            "code", "msg", "message", "success", "status", "error", "errors", "reason", "result", "results",
            "data", "list", "items", "item", "rows", "values", "value", "name", "title", "type", "category",
            "enabled", "available", "valid", "retryable", "skipped", "completed", "state", "action", "scene",
            "count", "total", "page", "pagesize", "size", "length", "index", "rank", "position", "quantity",
            "price", "unitprice", "rentprice", "longrentprice", "deposit", "currency", "rate", "ratio", "factor",
            "interval", "seconds", "minutes", "hours", "days", "leasedays", "defaultleasedays", "duration",
            "createdat", "updatedat", "timestamp", "time", "date", "version", "platform", "method", "operation",
            "compensationtype", "tradetype", "renttype", "longrentincluded", "hasmore", "next", "source"
        };
        private readonly int _maximumBodyBytes;
        private readonly DiagnosticCorrelationService _correlation;
        private readonly string[] _privatePaths;

        public DetailedDiagnosticsRedactor(
            int maximumBodyBytes,
            DiagnosticCorrelationService correlation,
            params string?[] privatePaths)
        {
            if (maximumBodyBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBodyBytes));
            ArgumentNullException.ThrowIfNull(correlation);
            _maximumBodyBytes = maximumBodyBytes;
            _correlation = correlation;
            _privatePaths = privatePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .OrderByDescending(path => path.Length)
                .ToArray();
        }

        public DetailedDiagnosticBodyCapture CaptureBody(string? body, string? contentType)
        {
            string safeContentType = SanitizeText(contentType ?? "");
            byte[] originalBytes = Encoding.UTF8.GetBytes(body ?? "");
            string sha256 = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant();
            if (string.IsNullOrEmpty(body))
            {
                return new DetailedDiagnosticBodyCapture
                {
                    ContentType = safeContentType,
                    OriginalLengthBytes = originalBytes.LongLength,
                    Sha256 = sha256,
                    ParseSucceeded = true,
                    RedactedBody = JsonValue.Create(string.Empty)
                };
            }

            if (!IsJsonContentType(contentType))
            {
                return FailedCapture(safeContentType, originalBytes.LongLength, sha256, "UnsupportedContentType");
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(body, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
                JsonNode? redacted = RedactBodyElement(document.RootElement, propertyName: null, parentVerified: true);
                byte[] safeBytes = JsonSerializer.SerializeToUtf8Bytes(redacted);
                if (safeBytes.Length <= _maximumBodyBytes)
                {
                    return new DetailedDiagnosticBodyCapture
                    {
                        ContentType = safeContentType,
                        OriginalLengthBytes = originalBytes.LongLength,
                        Sha256 = sha256,
                        ParseSucceeded = true,
                        RedactedBody = redacted
                    };
                }

                return new DetailedDiagnosticBodyCapture
                {
                    ContentType = safeContentType,
                    OriginalLengthBytes = originalBytes.LongLength,
                    Sha256 = sha256,
                    ParseSucceeded = true,
                    Truncated = true,
                    RedactedFragment = Encoding.UTF8.GetString(safeBytes.AsSpan(0, _maximumBodyBytes))
                };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                return FailedCapture(safeContentType, originalBytes.LongLength, sha256, ex.GetType().Name);
            }
        }

        public JsonNode? SanitizeEventData(IReadOnlyDictionary<string, object?>? data)
        {
            if (data is null || data.Count == 0)
                return null;

            var result = new JsonObject();
            foreach ((string key, object? value) in data)
            {
                string safeKey = SanitizeKey(key);
                result[safeKey] = SanitizeEventValue(key, value);
            }

            return result;
        }

        public string SanitizeText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string safe = BearerPattern().Replace(text, "$1[REDACTED]");
            safe = SensitivePairPattern().Replace(safe, "$1$2[REDACTED]");
            safe = PhonePattern().Replace(safe, "[REDACTED_PHONE]");
            safe = SteamIdPattern().Replace(safe, "[REDACTED_IDENTIFIER]");
            safe = LongNumericIdentifierPattern().Replace(safe, "[REDACTED_IDENTIFIER]");
            foreach (string path in _privatePaths)
                safe = safe.Replace(path, "[INSTALL_ROOT]", StringComparison.OrdinalIgnoreCase);
            return safe;
        }

        private JsonNode? RedactBodyElement(JsonElement element, string? propertyName, bool parentVerified)
        {
            string normalizedName = NormalizeName(propertyName);
            if (IsSecretName(normalizedName))
                return JsonValue.Create(RedactedSecret);
            if (IsPhoneName(normalizedName))
                return JsonValue.Create(RemovedSensitive);
            if (IsCorrelatedIdentifierName(normalizedName))
            {
                string raw = ElementToStableText(element);
                return JsonValue.Create(_correlation.Correlate(normalizedName, raw) ?? RemovedSensitive);
            }

            bool verified = parentVerified && (propertyName is null || VerifiedSafeBodyFields.Contains(normalizedName));
            if (!verified)
                return JsonValue.Create(RemovedUnverified);

            return element.ValueKind switch
            {
                JsonValueKind.Object => RedactBodyObject(element),
                JsonValueKind.Array => RedactBodyArray(element, normalizedName),
                JsonValueKind.String => JsonValue.Create(SanitizeText(element.GetString())),
                JsonValueKind.Number => JsonNode.Parse(element.GetRawText()),
                JsonValueKind.True => JsonValue.Create(true),
                JsonValueKind.False => JsonValue.Create(false),
                JsonValueKind.Null => null,
                _ => JsonValue.Create(RemovedUnverified)
            };
        }

        private JsonObject RedactBodyObject(JsonElement element)
        {
            var result = new JsonObject();
            foreach (JsonProperty property in element.EnumerateObject())
                result[property.Name] = RedactBodyElement(property.Value, property.Name, parentVerified: true);
            return result;
        }

        private JsonArray RedactBodyArray(JsonElement element, string propertyName)
        {
            var result = new JsonArray();
            foreach (JsonElement item in element.EnumerateArray())
                result.Add(RedactBodyElement(item, propertyName, parentVerified: true));
            return result;
        }

        private JsonNode? SanitizeEventValue(string key, object? value)
        {
            string normalizedName = NormalizeName(key);
            if (IsSecretName(normalizedName))
                return JsonValue.Create(RedactedSecret);
            if (IsPhoneName(normalizedName))
                return JsonValue.Create(RemovedSensitive);
            if (IsCorrelatedIdentifierName(normalizedName))
                return JsonValue.Create(_correlation.Correlate(normalizedName, value?.ToString()) ?? RemovedSensitive);
            if (value is null)
                return null;
            if (value is string text)
                return JsonValue.Create(SanitizeText(text));
            if (value is bool boolean)
                return JsonValue.Create(boolean);
            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
                return JsonSerializer.SerializeToNode(value);
            if (value is DateTime dateTime)
                return JsonValue.Create(dateTime.ToUniversalTime());
            if (value is DateTimeOffset dateTimeOffset)
                return JsonValue.Create(dateTimeOffset.ToUniversalTime());

            try
            {
                JsonNode? node = JsonSerializer.SerializeToNode(value);
                return SanitizeEventNode(node);
            }
            catch
            {
                return JsonValue.Create(RemovedUnverified);
            }
        }

        private JsonNode? SanitizeEventNode(JsonNode? node)
        {
            if (node is null)
                return null;
            if (node is JsonObject sourceObject)
            {
                var result = new JsonObject();
                foreach ((string key, JsonNode? value) in sourceObject)
                {
                    string normalized = NormalizeName(key);
                    result[SanitizeKey(key)] = IsSecretName(normalized)
                        ? JsonValue.Create(RedactedSecret)
                        : IsPhoneName(normalized)
                            ? JsonValue.Create(RemovedSensitive)
                            : IsCorrelatedIdentifierName(normalized)
                                ? JsonValue.Create(_correlation.Correlate(normalized, value?.ToJsonString()) ?? RemovedSensitive)
                                : SanitizeEventNode(value);
                }
                return result;
            }
            if (node is JsonArray sourceArray)
            {
                var result = new JsonArray();
                foreach (JsonNode? value in sourceArray)
                    result.Add(SanitizeEventNode(value));
                return result;
            }
            if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? text))
                return JsonValue.Create(SanitizeText(text));
            return node.DeepClone();
        }

        private static DetailedDiagnosticBodyCapture FailedCapture(
            string contentType,
            long length,
            string sha256,
            string reason)
        {
            return new DetailedDiagnosticBodyCapture
            {
                ContentType = contentType,
                OriginalLengthBytes = length,
                Sha256 = sha256,
                ParseSucceeded = false,
                FailureReason = reason
            };
        }

        private static bool IsJsonContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return false;
            return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("text/json", StringComparison.OrdinalIgnoreCase);
        }

        private static string ElementToStableText(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? string.Empty
                : element.GetRawText();
        }

        private static string SanitizeKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "field";
            string safe = Regex.Replace(key.Trim(), "[^A-Za-z0-9_.-]", "_");
            return string.IsNullOrWhiteSpace(safe) ? "field" : safe;
        }

        private static string NormalizeName(string? name)
            => string.Concat((name ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();

        private static bool IsSecretName(string name)
            => name.Contains("token", StringComparison.Ordinal)
                || name.Contains("cookie", StringComparison.Ordinal)
                || name.Contains("authorization", StringComparison.Ordinal)
                || name.Contains("password", StringComparison.Ordinal)
                || name.Contains("secret", StringComparison.Ordinal)
                || name.Contains("sessionid", StringComparison.Ordinal)
                || name.Contains("loginstate", StringComparison.Ordinal)
                || name.Contains("apikey", StringComparison.Ordinal)
                || name.Contains("sendkey", StringComparison.Ordinal);

        private static bool IsPhoneName(string name)
            => name.Contains("phone", StringComparison.Ordinal) || name.Contains("mobile", StringComparison.Ordinal);

        private static bool IsCorrelatedIdentifierName(string name)
            => name.Contains("orderno", StringComparison.Ordinal)
                || name.Contains("orderid", StringComparison.Ordinal)
                || name.Contains("deviceid", StringComparison.Ordinal)
                || name.Contains("deviceuk", StringComparison.Ordinal)
                || name.Contains("userid", StringComparison.Ordinal)
                || name.Contains("accountid", StringComparison.Ordinal)
                || name.Contains("steamid", StringComparison.Ordinal);

        [GeneratedRegex(@"(?i)\b(bearer\s+)([A-Za-z0-9._\-+/=]+)")]
        private static partial Regex BearerPattern();

        [GeneratedRegex(@"(?i)\b(api[-_\s]?key|api[-_\s]?token|token|cookie|authorization|password|secret|sessionid)\b([""']?\s*[:=]\s*[""']?)([^\s,;&""']+)")]
        private static partial Regex SensitivePairPattern();

        [GeneratedRegex(@"\b1[3-9]\d{9}\b")]
        private static partial Regex PhonePattern();

        [GeneratedRegex(@"\b7656\d{13}\b")]
        private static partial Regex SteamIdPattern();

        [GeneratedRegex(@"\b\d{8,}\b")]
        private static partial Regex LongNumericIdentifierPattern();
    }
}
