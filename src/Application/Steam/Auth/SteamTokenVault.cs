using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    public sealed class SteamTokenEntry
    {
        public string Id { get; set; } = "";
        public string Platform { get; set; } = "Steam";
        public string AccountName { get; set; } = "";
        public string PersonaName { get; set; } = "";
        public string SteamId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string SharedSecret { get; set; } = "";
        public string IdentitySecret { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string SteamLoginSecure { get; set; } = "";
        public string SteamLogin { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public DateTime AccessTokenExpiresAt { get; set; } = DateTime.MinValue;
        public DateTime RefreshTokenExpiresAt { get; set; } = DateTime.MinValue;
        public string ApiKey { get; set; } = "";
        public long HotpCounter { get; set; }
        public string LoginAccountName { get; set; } = "";
        public string LoginPassword { get; set; } = "";
        public DateTime LastAutoReloginAt { get; set; } = DateTime.MinValue;
        public string LastAutoReloginResult { get; set; } = "";
        public DateTime AutoReloginCooldownUntil { get; set; } = DateTime.MinValue;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public DateTime SessionSavedAt { get; set; } = DateTime.MinValue;
    }

    internal sealed class SteamVaultData
    {
        public int Version { get; set; } = 1;
        public string DefaultSteamTokenId { get; set; } = "";
        public List<SteamTokenEntry> Tokens { get; set; } = new();
    }

    public sealed class SteamTokenVault : ISteamTokenVault
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CS2DesktopMonitor.SteamTokenVault.v1");
        private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("CS2DesktopMonitor.SteamAuth.v1");
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly object _lock = new();
        private readonly string _path;
        private readonly string _legacyPath;
        private SteamVaultData? _cache;
        private DateTime _cacheWriteUtc;
        private bool _cacheInitialized;
        private bool _writeBlocked;
        private string _lastError = "";

        public static SteamTokenVault Instance { get; } = new();

        public string CredentialPath => _path;
        public string LegacyCredentialPath => _legacyPath;
        public string LastError => _lastError;

        private SteamTokenVault()
            : this(
                RuntimeDataPaths.GetSecureFilePath("steam_tokens.dat"),
                RuntimeDataPaths.GetSecureFilePath("steam_auth.dat"))
        {
        }

        internal SteamTokenVault(string path, string legacyPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
            _path = Path.GetFullPath(path);
            _legacyPath = Path.GetFullPath(legacyPath);
        }

        public List<SteamTokenEntry> GetAllTokens()
        {
            lock (_lock)
            {
                return LoadVaultNoLock().Tokens.Select(t => CloneEntry(t)!).ToList();
            }
        }

        public SteamTokenEntry? GetDefaultSteamToken()
        {
            lock (_lock)
            {
                var vault = LoadVaultNoLock();
                if (string.IsNullOrWhiteSpace(vault.DefaultSteamTokenId))
                    return null;
                var token = vault.Tokens.FirstOrDefault(t =>
                    string.Equals(t.Id, vault.DefaultSteamTokenId, StringComparison.Ordinal)
                    && string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase));
                return CloneEntry(token);
            }
        }

        public string GetDefaultSteamTokenId()
        {
            lock (_lock)
            {
                return LoadVaultNoLock().DefaultSteamTokenId;
            }
        }

        public void SetDefaultSteamToken(string id)
        {
            lock (_lock)
            {
                var vault = LoadVaultNoLock();
                if (!vault.Tokens.Any(t =>
                        string.Equals(t.Id, id, StringComparison.Ordinal)
                        && string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase)))
                    return;
                vault.DefaultSteamTokenId = id;
                SaveVaultNoLock(vault);
            }
        }

        public string SaveToken(SteamTokenEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            lock (_lock)
            {
                var vault = LoadVaultNoLock();
                var clone = CloneEntry(entry) ?? new SteamTokenEntry();
                SteamJwtTokenParser.ApplyTokenMetadata(clone);
                if (string.IsNullOrWhiteSpace(clone.Platform))
                    clone.Platform = "Steam";
                clone.Id = string.Equals(clone.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                    ? ResolveStableSteamTokenId(vault, clone)
                    : string.IsNullOrWhiteSpace(clone.Id) ? BuildTokenId(clone) : clone.Id.Trim();
                if (clone.CreatedAt == default)
                    clone.CreatedAt = DateTime.Now;
                if (clone.SavedAt == default)
                    clone.SavedAt = DateTime.Now;
                clone.UpdatedAt = DateTime.Now;

                int index = vault.Tokens.FindIndex(t => string.Equals(t.Id, clone.Id, StringComparison.Ordinal));
                if (index >= 0)
                    vault.Tokens[index] = clone;
                else
                    vault.Tokens.Add(clone);

                bool isSteam = string.Equals(clone.Platform, "Steam", StringComparison.OrdinalIgnoreCase);
                if (isSteam)
                    vault.DefaultSteamTokenId = clone.Id;

                NormalizeSteamTokensNoLock(vault);
                string savedId = isSteam
                    ? FindSavedSteamTokenId(vault, clone, clone.Id)
                    : clone.Id;
                if (isSteam)
                {
                    vault.Tokens.RemoveAll(t =>
                        string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(t.Id, savedId, StringComparison.Ordinal));
                    vault.DefaultSteamTokenId = savedId;
                }
                SaveVaultNoLock(vault);
                return savedId;
            }
        }

        public void DeleteToken(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_lock)
            {
                var vault = LoadVaultNoLock();
                int removed = vault.Tokens.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));
                if (removed <= 0) return;
                if (string.Equals(vault.DefaultSteamTokenId, id, StringComparison.Ordinal))
                    vault.DefaultSteamTokenId = "";
                SaveVaultNoLock(vault);
            }
        }

        public void ClearDefaultSteamToken()
        {
            lock (_lock)
            {
                var vault = LoadVaultNoLock();
                string id = vault.DefaultSteamTokenId;
                if (string.IsNullOrWhiteSpace(id))
                    id = vault.Tokens.FirstOrDefault(t => string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase))?.Id ?? "";
                if (string.IsNullOrWhiteSpace(id)) return;
                vault.Tokens.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));
                vault.DefaultSteamTokenId = "";
                SaveVaultNoLock(vault);
            }
        }

        public void IncrementHotpCounter(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (_lock)
            {
                var vault = LoadVaultNoLock();
                var token = vault.Tokens.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
                if (token == null) return;
                token.HotpCounter++;
                token.UpdatedAt = DateTime.Now;
                SaveVaultNoLock(vault);
            }
        }

        private SteamVaultData LoadVaultNoLock()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    _lastError = "";
                    _writeBlocked = false;
                    var migrated = TryMigrateLegacyNoLock();
                    if (migrated != null)
                    {
                        NormalizeSteamTokensNoLock(migrated);
                        _cache = migrated;
                        _cacheInitialized = true;
                        _cacheWriteUtc = File.GetLastWriteTimeUtc(_legacyPath);
                        return migrated;
                    }
                    if (_writeBlocked)
                    {
                        _cache = new SteamVaultData();
                        _cacheInitialized = true;
                        return _cache;
                    }

                    _cache = new SteamVaultData();
                    _cacheInitialized = true;
                    _cacheWriteUtc = DateTime.MinValue;
                    return _cache;
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(_path);
                if (_cacheInitialized && _cache != null && writeUtc == _cacheWriteUtc)
                    return _cache;

                string text = File.ReadAllText(_path).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("Steam 令牌库内容为空。");

                byte[] protectedBytes = Convert.FromBase64String(text);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                _cache = JsonSerializer.Deserialize<SteamVaultData>(Encoding.UTF8.GetString(plain), JsonOptions)
                    ?? throw new InvalidDataException("Steam 令牌库结构无效。");
                _cache.Tokens ??= new List<SteamTokenEntry>();
                _cacheInitialized = true;
                _cacheWriteUtc = writeUtc;
                NormalizeSteamTokensNoLock(_cache);
                _writeBlocked = false;
                _lastError = "";
                return _cache;
            }
            catch (Exception ex)
            {
                _lastError = "读取 Steam 令牌库失败：" + DiagnosticsLogger.Redact(ex.Message);
                DiagnosticsLogger.Error("SteamAuth", _lastError);
                _cache = new SteamVaultData();
                _cacheInitialized = true;
                _cacheWriteUtc = DateTime.MinValue;
                _writeBlocked = true;
                return _cache;
            }
        }

        private void SaveVaultNoLock(SteamVaultData vault)
        {
            if (_writeBlocked && (File.Exists(_path) || File.Exists(_legacyPath)))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(_lastError)
                        ? "Steam 令牌库不可用，已保留原文件。"
                        : _lastError + " 原文件已保留，不能覆盖。");

            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? RuntimeDataPaths.SecureDirectory);
            vault.Version = Math.Max(1, vault.Version);
            vault.Tokens ??= new List<SteamTokenEntry>();
            string json = JsonSerializer.Serialize(vault, JsonOptions);
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
            RuntimeDataPaths.WriteTextAtomic(_path, Convert.ToBase64String(protectedBytes));
            _cache = vault;
            _cacheInitialized = true;
            _cacheWriteUtc = File.GetLastWriteTimeUtc(_path);
            _writeBlocked = false;
            _lastError = "";
        }

        private SteamVaultData? TryMigrateLegacyNoLock()
        {
            if (!File.Exists(_legacyPath)) return null;
            try
            {
                string text = File.ReadAllText(_legacyPath).Trim();
                if (string.IsNullOrWhiteSpace(text)) return null;
                byte[] protectedBytes = Convert.FromBase64String(text);
                byte[] plain = ProtectedData.Unprotect(protectedBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(plain));
                var root = doc.RootElement;
                string steamId = GetString(root, "SteamId");
                if (string.IsNullOrWhiteSpace(steamId)) return null;
                var entry = new SteamTokenEntry
                {
                    Id = "",
                    Platform = "Steam",
                    SteamId = steamId.Trim(),
                    AccountName = GetString(root, "AccountName"),
                    PersonaName = GetString(root, "PersonaName"),
                    DeviceId = GetString(root, "DeviceId"),
                    SharedSecret = GetString(root, "SharedSecret"),
                    IdentitySecret = GetString(root, "IdentitySecret"),
                    SessionId = GetString(root, "SessionId"),
                    SteamLoginSecure = GetString(root, "SteamLoginSecure"),
                    SteamLogin = GetString(root, "SteamLogin"),
                    ApiKey = GetString(root, "ApiKey"),
                    LoginAccountName = GetString(root, "LoginAccountName"),
                    LoginPassword = GetString(root, "LoginPassword"),
                    LastAutoReloginAt = GetDateTime(root, "LastAutoReloginAt"),
                    LastAutoReloginResult = GetString(root, "LastAutoReloginResult"),
                    AutoReloginCooldownUntil = GetDateTime(root, "AutoReloginCooldownUntil"),
                    SavedAt = GetDateTime(root, "SavedAt", DateTime.Now),
                    SessionSavedAt = GetDateTime(root, "SessionSavedAt"),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                if (string.IsNullOrWhiteSpace(entry.DeviceId) && !string.IsNullOrWhiteSpace(entry.SteamId))
                    entry.DeviceId = SteamCryptoHelper.GenerateDeviceId(entry.SteamId);

                return new SteamVaultData
                {
                    Version = 1,
                    DefaultSteamTokenId = entry.Id,
                    Tokens = new List<SteamTokenEntry> { entry }
                };
            }
            catch (Exception ex)
            {
                _lastError = "迁移旧 Steam 凭据失败：" + DiagnosticsLogger.Redact(ex.Message);
                DiagnosticsLogger.Error("SteamAuth", _lastError);
                _writeBlocked = true;
                return null;
            }
        }

        private static string BuildTokenId(SteamTokenEntry entry)
        {
            return entry.Platform.Trim().ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N");
        }

        private static string ResolveStableSteamTokenId(SteamVaultData vault, SteamTokenEntry entry)
        {
            string secretId = BuildSecretStableId(entry);
            if (!string.IsNullOrWhiteSpace(secretId))
            {
                var match = vault.Tokens.FirstOrDefault(t =>
                    string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(BuildSecretStableId(t), secretId, StringComparison.Ordinal));
                if (match != null && !string.IsNullOrWhiteSpace(match.Id))
                    return match.Id.Trim();
            }

            string accountId = BuildAccountStableId(entry);
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                var match = vault.Tokens.FirstOrDefault(t =>
                    string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(BuildAccountStableId(t), accountId, StringComparison.Ordinal));
                if (match != null && !string.IsNullOrWhiteSpace(match.Id))
                    return match.Id.Trim();

                return accountId;
            }

            if (!string.IsNullOrWhiteSpace(secretId))
                return secretId;
            if (!string.IsNullOrWhiteSpace(entry.Id))
                return entry.Id.Trim();
            return "steam_" + Guid.NewGuid().ToString("N");
        }

        private static string FindSavedSteamTokenId(SteamVaultData vault, SteamTokenEntry entry, string fallbackId)
        {
            string secretId = BuildSecretStableId(entry);
            if (!string.IsNullOrWhiteSpace(secretId))
            {
                var match = vault.Tokens.FirstOrDefault(t =>
                    string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(BuildSecretStableId(t), secretId, StringComparison.Ordinal));
                if (match != null && !string.IsNullOrWhiteSpace(match.Id))
                    return match.Id;
            }

            string accountId = BuildAccountStableId(entry);
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                var match = vault.Tokens.FirstOrDefault(t =>
                    string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(BuildAccountStableId(t), accountId, StringComparison.Ordinal));
                if (match != null && !string.IsNullOrWhiteSpace(match.Id))
                    return match.Id;
            }

            var byId = vault.Tokens.FirstOrDefault(t => string.Equals(t.Id, fallbackId, StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(byId?.Id) ? fallbackId : byId.Id;
        }

        private static bool NormalizeSteamTokensNoLock(SteamVaultData vault)
        {
            vault.Tokens ??= new List<SteamTokenEntry>();
            bool changed = false;
            var result = new List<SteamTokenEntry>();
            var groups = new List<List<SteamTokenEntry>>();

            foreach (var token in vault.Tokens)
            {
                if (!string.Equals(token.Platform, "Steam", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(token);
                    continue;
                }

                string accountId = BuildAccountStableId(token);
                string secretId = BuildSecretStableId(token);
                if (string.IsNullOrWhiteSpace(accountId) && string.IsNullOrWhiteSpace(secretId))
                {
                    if (string.IsNullOrWhiteSpace(token.Id))
                    {
                        token.Id = "steam_" + Guid.NewGuid().ToString("N");
                        changed = true;
                    }
                    result.Add(token);
                    continue;
                }

                var matches = groups
                    .Where(g => g.Any(existing => HasSameSteamIdentity(existing, accountId, secretId)))
                    .ToList();
                if (matches.Count == 0)
                {
                    groups.Add(new List<SteamTokenEntry> { token });
                    continue;
                }

                matches[0].Add(token);
                for (int i = 1; i < matches.Count; i++)
                {
                    matches[0].AddRange(matches[i]);
                    groups.Remove(matches[i]);
                    changed = true;
                }
            }

            foreach (var group in groups)
            {
                string originalDefault = vault.DefaultSteamTokenId;
                string canonicalId = ChooseCanonicalId(group, originalDefault);
                if (group.Count == 1)
                {
                    var token = group[0];
                    SteamJwtTokenParser.ApplyTokenMetadata(token);
                    if (!string.Equals(token.Id, canonicalId, StringComparison.Ordinal))
                    {
                        token.Id = canonicalId;
                        token.UpdatedAt = DateTime.Now;
                        changed = true;
                    }
                    if (string.IsNullOrWhiteSpace(token.DeviceId) && !string.IsNullOrWhiteSpace(token.SteamId))
                    {
                        token.DeviceId = SteamCryptoHelper.GenerateDeviceId(token.SteamId);
                        token.UpdatedAt = DateTime.Now;
                        changed = true;
                    }
                    result.Add(token);
                }
                else
                {
                    var merged = MergeSteamTokenGroup(group, canonicalId);
                    result.Add(merged);
                    changed = true;
                }

                if (group.Any(t => string.Equals(t.Id, originalDefault, StringComparison.Ordinal)))
                {
                    if (!string.Equals(vault.DefaultSteamTokenId, canonicalId, StringComparison.Ordinal))
                    {
                        vault.DefaultSteamTokenId = canonicalId;
                        changed = true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(vault.DefaultSteamTokenId)
                && !result.Any(t => string.Equals(t.Id, vault.DefaultSteamTokenId, StringComparison.Ordinal)))
            {
                vault.DefaultSteamTokenId = result.FirstOrDefault(t =>
                    string.Equals(t.Platform, "Steam", StringComparison.OrdinalIgnoreCase))?.Id ?? "";
                changed = true;
            }

            if (result.Count != vault.Tokens.Count
                || result.Where((t, i) => i >= vault.Tokens.Count || !ReferenceEquals(t, vault.Tokens[i])).Any())
            {
                vault.Tokens = result;
                changed = true;
            }

            return changed;
        }

        private static bool HasSameSteamIdentity(SteamTokenEntry token, string accountId, string secretId)
        {
            return (!string.IsNullOrWhiteSpace(accountId)
                    && string.Equals(BuildAccountStableId(token), accountId, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(secretId)
                    && string.Equals(BuildSecretStableId(token), secretId, StringComparison.Ordinal));
        }

        private static string ChooseCanonicalId(List<SteamTokenEntry> group, string defaultId)
        {
            if (group.Count == 1 && !string.IsNullOrWhiteSpace(group[0].Id))
                return group[0].Id.Trim();

            var defaultToken = group.FirstOrDefault(t => string.Equals(t.Id, defaultId, StringComparison.Ordinal));
            if (defaultToken != null && !string.IsNullOrWhiteSpace(defaultToken.Id))
                return defaultToken.Id.Trim();

            var accountId = group.Select(BuildAccountStableId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(accountId))
                return accountId;

            var secretId = group.Select(BuildSecretStableId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(secretId))
                return secretId;

            return group.Select(t => t.Id).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))?.Trim()
                ?? "steam_" + Guid.NewGuid().ToString("N");
        }

        private static SteamTokenEntry MergeSteamTokenGroup(List<SteamTokenEntry> group, string canonicalId)
        {
            var merged = CloneEntry(group.OrderByDescending(ScoreToken).ThenByDescending(t => t.UpdatedAt).First()) ?? new SteamTokenEntry();
            merged.Id = canonicalId;
            merged.Platform = "Steam";

            var latestSecrets = group
                .Where(t => !string.IsNullOrWhiteSpace(t.SharedSecret) || !string.IsNullOrWhiteSpace(t.IdentitySecret))
                .OrderByDescending(SecretTimestamp)
                .ThenByDescending(t => t.UpdatedAt)
                .FirstOrDefault();
            if (latestSecrets != null)
            {
                merged.SharedSecret = latestSecrets.SharedSecret;
                merged.IdentitySecret = latestSecrets.IdentitySecret;
            }

            foreach (var token in group.OrderByDescending(ScoreToken).ThenByDescending(t => t.UpdatedAt))
            {
                SteamJwtTokenParser.ApplyTokenMetadata(token);
                merged.AccountName = FirstText(merged.AccountName, token.AccountName);
                merged.PersonaName = FirstText(merged.PersonaName, token.PersonaName);
                merged.SteamId = FirstText(merged.SteamId, token.SteamId);
                merged.DeviceId = FirstText(merged.DeviceId, token.DeviceId);
                if (latestSecrets == null)
                {
                    merged.SharedSecret = FirstText(merged.SharedSecret, token.SharedSecret);
                    merged.IdentitySecret = FirstText(merged.IdentitySecret, token.IdentitySecret);
                }
                merged.SteamLogin = FirstText(merged.SteamLogin, token.SteamLogin);
                merged.RefreshToken = FirstText(merged.RefreshToken, token.RefreshToken);
                merged.AccessToken = FirstText(merged.AccessToken, token.AccessToken);
                if (token.AccessTokenExpiresAt > merged.AccessTokenExpiresAt)
                    merged.AccessTokenExpiresAt = token.AccessTokenExpiresAt;
                if (token.RefreshTokenExpiresAt > merged.RefreshTokenExpiresAt)
                    merged.RefreshTokenExpiresAt = token.RefreshTokenExpiresAt;
                merged.ApiKey = FirstText(merged.ApiKey, token.ApiKey);
                merged.LoginAccountName = FirstText(merged.LoginAccountName, token.LoginAccountName);
                merged.LoginPassword = FirstText(merged.LoginPassword, token.LoginPassword);
                merged.LastAutoReloginResult = FirstText(merged.LastAutoReloginResult, token.LastAutoReloginResult);

                bool tokenHasSession = !string.IsNullOrWhiteSpace(token.SessionId)
                    && !string.IsNullOrWhiteSpace(token.SteamLoginSecure);
                bool mergedHasSession = !string.IsNullOrWhiteSpace(merged.SessionId)
                    && !string.IsNullOrWhiteSpace(merged.SteamLoginSecure);
                if (tokenHasSession && (!mergedHasSession || token.SessionSavedAt > merged.SessionSavedAt))
                {
                    merged.SessionId = token.SessionId;
                    merged.SteamLoginSecure = token.SteamLoginSecure;
                    merged.SteamLogin = FirstText(token.SteamLogin, merged.SteamLogin);
                    merged.SessionSavedAt = token.SessionSavedAt;
                }

                if (token.LastAutoReloginAt > merged.LastAutoReloginAt)
                    merged.LastAutoReloginAt = token.LastAutoReloginAt;
                if (token.AutoReloginCooldownUntil > merged.AutoReloginCooldownUntil)
                    merged.AutoReloginCooldownUntil = token.AutoReloginCooldownUntil;
                if (token.HotpCounter > merged.HotpCounter)
                    merged.HotpCounter = token.HotpCounter;
                if (token.CreatedAt != default && (merged.CreatedAt == default || token.CreatedAt < merged.CreatedAt))
                    merged.CreatedAt = token.CreatedAt;
                if (token.SavedAt > merged.SavedAt)
                    merged.SavedAt = token.SavedAt;
            }

            if (string.IsNullOrWhiteSpace(merged.DeviceId) && !string.IsNullOrWhiteSpace(merged.SteamId))
                merged.DeviceId = SteamCryptoHelper.GenerateDeviceId(merged.SteamId);
            merged.UpdatedAt = DateTime.Now;
            return merged;
        }

        private static DateTime SecretTimestamp(SteamTokenEntry token)
        {
            if (token.SavedAt != default)
                return token.SavedAt;
            return token.UpdatedAt;
        }

        private static int ScoreToken(SteamTokenEntry token)
        {
            int score = 0;
            if (!string.IsNullOrWhiteSpace(token.SessionId) && !string.IsNullOrWhiteSpace(token.SteamLoginSecure)) score += 100;
            if (!string.IsNullOrWhiteSpace(token.RefreshToken)) score += 30;
            if (!string.IsNullOrWhiteSpace(token.AccessToken)) score += 20;
            if (!string.IsNullOrWhiteSpace(token.SteamId)) score += 25;
            if (!string.IsNullOrWhiteSpace(token.SharedSecret) && !string.IsNullOrWhiteSpace(token.IdentitySecret)) score += 50;
            if (!string.IsNullOrWhiteSpace(token.LoginAccountName) && !string.IsNullOrWhiteSpace(token.LoginPassword)) score += 20;
            if (!string.IsNullOrWhiteSpace(token.AccountName)) score += 10;
            if (!string.IsNullOrWhiteSpace(token.PersonaName)) score += 8;
            return score;
        }

        private static string BuildAccountStableId(SteamTokenEntry token)
        {
            string account = NormalizeAccountName(FirstText(token.LoginAccountName, token.AccountName));
            return string.IsNullOrWhiteSpace(account) ? "" : "steam_account_" + account;
        }

        private static string BuildSecretStableId(SteamTokenEntry token)
        {
            string secret = (token.SharedSecret ?? "").Trim();
            return string.IsNullOrWhiteSpace(secret) ? "" : "steam_secret_" + ShortSha256(secret);
        }

        private static string NormalizeAccountName(string value)
        {
            var sb = new StringBuilder();
            foreach (char ch in (value ?? "").Trim())
            {
                if (!char.IsWhiteSpace(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private static string ShortSha256(string value)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(value.Trim());
            }
            catch (FormatException)
            {
                bytes = Encoding.UTF8.GetBytes(value.Trim());
            }

            byte[] hash = SHA256.HashData(bytes);
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static string FirstText(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }

        private static string GetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value)) return "";
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";
            if (value.ValueKind == JsonValueKind.Number) return value.ToString();
            return "";
        }

        private static DateTime GetDateTime(JsonElement root, string name, DateTime fallback = default)
        {
            string text = GetString(root, name);
            return DateTime.TryParse(text, out var value) ? value : fallback;
        }

        private static SteamTokenEntry? CloneEntry(SteamTokenEntry? entry)
        {
            if (entry == null) return null;
            return new SteamTokenEntry
            {
                Id = entry.Id,
                Platform = entry.Platform,
                AccountName = entry.AccountName,
                PersonaName = entry.PersonaName,
                SteamId = entry.SteamId,
                DeviceId = entry.DeviceId,
                SharedSecret = entry.SharedSecret,
                IdentitySecret = entry.IdentitySecret,
                SessionId = entry.SessionId,
                SteamLoginSecure = entry.SteamLoginSecure,
                SteamLogin = entry.SteamLogin,
                RefreshToken = entry.RefreshToken,
                AccessToken = entry.AccessToken,
                AccessTokenExpiresAt = entry.AccessTokenExpiresAt,
                RefreshTokenExpiresAt = entry.RefreshTokenExpiresAt,
                ApiKey = entry.ApiKey,
                HotpCounter = entry.HotpCounter,
                LoginAccountName = entry.LoginAccountName,
                LoginPassword = entry.LoginPassword,
                LastAutoReloginAt = entry.LastAutoReloginAt,
                LastAutoReloginResult = entry.LastAutoReloginResult,
                AutoReloginCooldownUntil = entry.AutoReloginCooldownUntil,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt,
                SavedAt = entry.SavedAt,
                SessionSavedAt = entry.SessionSavedAt
            };
        }
    }
}
