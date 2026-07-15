using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Domain.Steam;

namespace CS2TradeMonitor.Application.Steam.Auth.Import
{
    internal static class SteamMaFileImportPreparer
    {
        public static string ClassifySource(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return "manual";
            string fileName = Path.GetFileName(sourcePath);
            return string.IsNullOrWhiteSpace(fileName) ? "file" : fileName;
        }

        public static SteamOfferImportFileResult PrepareText(string jsonText, string sourcePath, bool allowManifestFileResolution)
        {
            if (string.IsNullOrWhiteSpace(jsonText))
                return SteamOfferImportFileResult.Failed("导入内容为空。", sourcePath);

            JsonDocument doc;
            try
            {
                doc = ParseImportJson(jsonText);
            }
            catch (JsonException)
            {
                return SteamOfferImportFileResult.Failed(
                    "该 maFile 已加密或不是明文 JSON。请先在 Steam Desktop Authenticator 中解密/导出明文 maFile 再导入。",
                    sourcePath,
                    jsonText);
            }

            using (doc)
            {
                var root = doc.RootElement;
                bool hasSharedSecret = !string.IsNullOrWhiteSpace(GetString(root, "shared_secret", "SharedSecret"));
                bool hasIdentitySecret = !string.IsNullOrWhiteSpace(GetString(root, "identity_secret", "IdentitySecret"));

                if (IsSdaManifest(root, hasSharedSecret, hasIdentitySecret))
                {
                    string manifestMessage = "这是 SDA 清单文件(manifest.json)，不含密钥。请改用同目录 maFiles 文件夹里的 <steamid>.maFile。";
                    var candidates = FindManifestMaFileCandidates(root, sourcePath);
                    if (!allowManifestFileResolution)
                    {
                        if (candidates.Count > 1)
                        {
                            return SteamOfferImportFileResult.NeedsSelection(
                                jsonText,
                                sourcePath,
                                manifestMessage + " 已找到多个未加密 maFile，请选择要导入的账户。",
                                candidates);
                        }

                        if (candidates.Count == 1)
                            return LoadResolvedMaFile(candidates[0].Path, manifestMessage + " 已自动切换到：" + Path.GetFileName(candidates[0].Path));

                        return SteamOfferImportFileResult.Failed(BuildManifestMissingMessage(manifestMessage, sourcePath), sourcePath, jsonText);
                    }

                    if (candidates.Count > 0)
                        return LoadResolvedMaFile(candidates[0].Path, manifestMessage + " 已自动导入：" + Path.GetFileName(candidates[0].Path));

                    return SteamOfferImportFileResult.Failed(BuildManifestMissingMessage(manifestMessage, sourcePath), sourcePath, jsonText);
                }

                if (IsEncryptedMaFile(root, hasSharedSecret, hasIdentitySecret))
                {
                    return SteamOfferImportFileResult.Failed(
                        "该 maFile 已加密，请先在 Steam Desktop Authenticator 中解密/导出明文 maFile 再导入。",
                        sourcePath,
                        jsonText);
                }

                return SteamOfferImportFileResult.Success(jsonText, sourcePath, "已读取文件内容。请确认来源可信，然后点击“导入并加密保存”。");
            }
        }

        public static SteamMaFileCredentialParseResult ParseCredential(string jsonText, SteamAuthCredential current)
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            TryGetProperty(root, out var sessionElement, "Session", "session");

            string steamId = ResolveSteamId(root, sessionElement);
            string sharedSecret = FirstText(GetString(root, "shared_secret", "SharedSecret"));
            string identitySecret = FirstText(GetString(root, "identity_secret", "IdentitySecret"));
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(sharedSecret)) missing.Add("shared_secret");
            if (string.IsNullOrWhiteSpace(identitySecret)) missing.Add("identity_secret");
            if (string.IsNullOrWhiteSpace(steamId)) missing.Add("SteamID");
            if (missing.Count > 0)
            {
                return SteamMaFileCredentialParseResult.Failed(
                    "导入失败：明文 maFile 缺少 " + string.Join("、", missing) +
                    "。请确认选择的是 SDA maFiles 文件夹里的 <steamid>.maFile；如果文件已加密，请先在 Steam Desktop Authenticator 中解密/导出明文 maFile。");
            }

            var credential = new SteamAuthCredential
            {
                SteamId = steamId,
                AccountName = FirstText(GetString(root, "account_name", "AccountName", "accountName"), current.AccountName),
                PersonaName = FirstText(GetString(root, "persona_name", "PersonaName", "personaName", "steam_persona_name"), current.PersonaName),
                DeviceId = FirstText(GetString(root, "device_id", "DeviceID", "deviceId"), current.DeviceId),
                SharedSecret = sharedSecret,
                IdentitySecret = identitySecret,
                SessionId = current.SessionId,
                SteamLoginSecure = current.SteamLoginSecure,
                SteamLogin = FirstText(GetString(root, "steamLogin", "SteamLogin", "steam_login"), current.SteamLogin),
                RefreshToken = FirstText(GetString(root, "refresh_token", "RefreshToken", "refreshToken"), current.RefreshToken),
                AccessToken = FirstText(GetString(root, "access_token", "AccessToken", "accessToken", "oauth_token", "OAuthToken", "oauthToken"), current.AccessToken),
                ApiKey = FirstText(GetString(root, "api_key", "ApiKey", "apikey", "web_api_key", "SteamWebApiKey"), current.ApiKey),
                SessionSavedAt = current.SessionSavedAt,
                SavedAt = DateTime.Now
            };

            if (string.IsNullOrWhiteSpace(credential.DeviceId) && !string.IsNullOrWhiteSpace(credential.SteamId))
                credential.DeviceId = SteamCryptoHelper.GenerateDeviceId(credential.SteamId);

            return SteamMaFileCredentialParseResult.Success(credential);
        }

        private static JsonDocument ParseImportJson(string jsonText)
        {
            try
            {
                var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    doc.Dispose();
                    throw new JsonException("Steam auth import root is not object.");
                }

                return doc;
            }
            catch (JsonException)
            {
                throw new JsonException("Encrypted or invalid Steam auth JSON.");
            }
        }

        private static bool IsSdaManifest(JsonElement root, bool hasSharedSecret, bool hasIdentitySecret)
        {
            if (hasSharedSecret || hasIdentitySecret)
                return false;

            bool hasEntries = TryGetProperty(root, out var entries, "entries", "Entries") && entries.ValueKind == JsonValueKind.Array;
            bool hasManifestSwitch = TryGetProperty(
                root,
                out _,
                "periodic_checking_checkall",
                "auto_confirm_market_transactions",
                "auto_confirm_trades",
                "periodic_checking_interval",
                "accept_confirmations_period");
            return hasEntries || hasManifestSwitch;
        }

        private static bool IsEncryptedMaFile(JsonElement root, bool hasSharedSecret, bool hasIdentitySecret)
        {
            if (hasSharedSecret || hasIdentitySecret)
                return false;
            if (GetBool(root, "encrypted", "Encrypted"))
                return true;
            return TryGetProperty(root, out _, "ciphertext", "cipher_text", "encrypted_data", "encryptedData", "iv", "salt");
        }

        private static SteamOfferImportFileResult LoadResolvedMaFile(string path, string message)
        {
            try
            {
                string text = File.ReadAllText(path);
                return SteamOfferImportFileResult.Success(text, path, message);
            }
            catch (Exception ex)
            {
                return SteamOfferImportFileResult.Failed("已定位 maFile，但读取失败：" + SteamOfferAuditLog.RedactSecrets(ex.Message), path);
            }
        }

        private static string BuildManifestMissingMessage(string manifestMessage, string sourcePath)
        {
            string suffix = string.IsNullOrWhiteSpace(sourcePath)
                ? ""
                : " 未找到 entries 中对应的 .maFile；请确认 manifest.json 旁边存在 maFiles 文件夹，且里面有对应账户的 .maFile。";
            return manifestMessage + suffix;
        }

        private static List<SteamOfferImportCandidate> FindManifestMaFileCandidates(JsonElement root, string sourcePath)
        {
            var candidates = new List<SteamOfferImportCandidate>();
            if (string.IsNullOrWhiteSpace(sourcePath))
                return candidates;

            string? baseDir = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            if (string.IsNullOrWhiteSpace(baseDir))
                return candidates;

            if (!TryGetProperty(root, out var entries, "entries", "Entries") || entries.ValueKind != JsonValueKind.Array)
                return candidates;

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;
                if (GetBool(entry, "encrypted", "Encrypted"))
                    continue;

                TryGetProperty(entry, out var sessionElement, "Session", "session");
                string fileName = FirstText(GetString(entry, "filename", "Filename", "file", "File", "maFile", "mafile", "path", "Path"));
                string steamId = ResolveSteamId(entry, sessionElement);
                string account = FirstText(GetString(entry, "account_name", "AccountName", "accountName", "username", "Username", "name", "Name"), steamId);

                foreach (var path in BuildManifestCandidatePaths(baseDir, fileName, steamId))
                {
                    if (!File.Exists(path))
                        continue;
                    if (candidates.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    candidates.Add(new SteamOfferImportCandidate
                    {
                        DisplayName = FirstText(account, Path.GetFileNameWithoutExtension(path), "Steam 账户") + " - " + Path.GetFileName(path),
                        Path = path
                    });
                    break;
                }
            }

            return candidates;
        }

        private static IEnumerable<string> BuildManifestCandidatePaths(string baseDir, string fileName, string steamId)
        {
            var paths = new List<string>();
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                string trimmed = fileName.Trim();
                AddCandidatePath(paths, Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(baseDir, trimmed));
                AddCandidatePath(paths, Path.Combine(baseDir, "maFiles", trimmed));
                if (string.IsNullOrWhiteSpace(Path.GetExtension(trimmed)))
                {
                    AddCandidatePath(paths, Path.IsPathRooted(trimmed) ? trimmed + ".maFile" : Path.Combine(baseDir, trimmed + ".maFile"));
                    AddCandidatePath(paths, Path.Combine(baseDir, "maFiles", trimmed + ".maFile"));
                }
            }

            if (!string.IsNullOrWhiteSpace(steamId))
            {
                AddCandidatePath(paths, Path.Combine(baseDir, "maFiles", steamId + ".maFile"));
                AddCandidatePath(paths, Path.Combine(baseDir, steamId + ".maFile"));
            }

            return paths;
        }

        private static void AddCandidatePath(List<string> paths, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            if (!paths.Any(x => x.Equals(full, StringComparison.OrdinalIgnoreCase)))
                paths.Add(full);
        }

        private static string ResolveSteamId(JsonElement root, JsonElement sessionElement)
        {
            string steamId = FirstText(
                GetString(root, "SteamID", "steamid", "steam_id", "account_steamid", "steamid64", "steam_id64", "steam_id_64"),
                GetString(sessionElement, "SteamID", "steamid", "steam_id", "account_steamid", "steamid64", "steam_id64", "steam_id_64"),
                GetStringFromJsonObjectString(root, new[] { "Session", "session" }, "SteamID", "steamid", "steam_id", "account_steamid", "steamid64", "steam_id64", "steam_id_64"));
            if (!string.IsNullOrWhiteSpace(steamId))
                return steamId;

            string accountId = FirstText(
                GetString(root, "account_id", "AccountID", "AccountId", "accountId", "accountid", "steam_account_id"),
                GetString(sessionElement, "account_id", "AccountID", "AccountId", "accountId", "accountid", "steam_account_id"),
                GetStringFromJsonObjectString(root, new[] { "Session", "session" }, "account_id", "AccountID", "AccountId", "accountId", "accountid", "steam_account_id"));
            return AccountIdToSteamId64(accountId);
        }

        private static string AccountIdToSteamId64(string accountIdText)
        {
            if (!ulong.TryParse((accountIdText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong accountId))
                return "";
            const ulong steamIdBase = 76561197960265728UL;
            return (steamIdBase + accountId).ToString(CultureInfo.InvariantCulture);
        }

        private static string? GetStringFromJsonObjectString(JsonElement element, string[] objectNames, params string[] names)
        {
            if (!TryGetProperty(element, out var value, objectNames) || value.ValueKind != JsonValueKind.String)
                return null;

            string? text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                using var doc = JsonDocument.Parse(text);
                return GetString(doc.RootElement, names);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object) return false;
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out value))
                    return true;
            }

            foreach (var prop in element.EnumerateObject())
            {
                if (names.Any(name => prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = prop.Value;
                    return true;
                }
            }

            return false;
        }

        private static string? GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return null;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind == JsonValueKind.Number) return value.ToString();
            return null;
        }

        private static bool GetBool(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var value, names)) return false;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return bool.TryParse(value.ToString(), out bool result) && result;
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
    }

    internal sealed class SteamMaFileCredentialParseResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public SteamAuthCredential? Credential { get; init; }

        public static SteamMaFileCredentialParseResult Success(SteamAuthCredential credential) => new()
        {
            Ok = true,
            Credential = credential
        };

        public static SteamMaFileCredentialParseResult Failed(string message) => new()
        {
            Ok = false,
            Message = message
        };
    }
}
