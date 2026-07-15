using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace CS2TradeMonitor.Application.Steam.Auth.Import
{
    public sealed class OtpAuthImporter : ITokenImporter
    {
        public bool CanImport(string text, string sourcePath)
        {
            return (text ?? "").TrimStart().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase);
        }

        public TokenImportResult Import(string text, string sourcePath)
        {
            string raw = (text ?? "").Trim();
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
                return TokenImportResult.Failed("导入失败：不是有效的 otpauth:// 链接。");

            string kind = uri.Host.ToLowerInvariant();
            if (kind != "totp" && kind != "hotp")
                return TokenImportResult.Failed("导入失败：只支持 otpauth://totp 和 otpauth://hotp。");

            var query = ParseQuery(uri.Query);
            query.TryGetValue("secret", out var secretValue);
            string secret = secretValue ?? "";
            if (string.IsNullOrWhiteSpace(secret))
                return TokenImportResult.Failed("导入失败：otpauth 链接缺少 secret。");

            string issuer = query.TryGetValue("issuer", out var issuerValue) ? issuerValue.Trim() : "";
            string label = (WebUtility.UrlDecode(uri.AbsolutePath.TrimStart('/')) ?? "").Trim();
            string accountName = label;
            int separator = label.IndexOf(':');
            if (separator >= 0 && separator + 1 < label.Length)
            {
                if (string.IsNullOrWhiteSpace(issuer))
                    issuer = label[..separator].Trim();
                accountName = label[(separator + 1)..].Trim();
            }
            if (string.IsNullOrWhiteSpace(accountName))
                accountName = string.IsNullOrWhiteSpace(issuer) ? "OTP 令牌" : issuer;

            string platform = kind == "hotp"
                ? "HOTP"
                : issuer.Contains("google", StringComparison.OrdinalIgnoreCase) ? "Google" : "TOTP";
            long counter = 0;
            if (kind == "hotp" && query.TryGetValue("counter", out var counterValue))
            {
                string counterText = counterValue ?? "";
                long.TryParse(counterText, NumberStyles.Integer, CultureInfo.InvariantCulture, out counter);
            }

            var token = new SteamTokenEntry
            {
                Platform = platform,
                AccountName = accountName,
                SharedSecret = secret.Replace(" ", "").Trim(),
                HotpCounter = Math.Max(0, counter),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                SavedAt = DateTime.Now
            };
            return TokenImportResult.Success(token, platform + " 令牌已解析。");
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string text = (query ?? "").TrimStart('?');
            foreach (string part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int index = part.IndexOf('=');
                string key = index >= 0 ? part[..index] : part;
                string value = index >= 0 ? part[(index + 1)..] : "";
                result[WebUtility.UrlDecode(key) ?? ""] = WebUtility.UrlDecode(value) ?? "";
            }
            return result;
        }
    }
}
