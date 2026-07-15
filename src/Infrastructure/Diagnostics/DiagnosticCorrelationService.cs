using System.Security.Cryptography;
using System.Text;
using CS2TradeMonitor.Application.Abstractions;

namespace CS2TradeMonitor.Infrastructure.Diagnostics
{
    internal sealed class DiagnosticCorrelationService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CS2TradeMonitor.DetailedDiagnostics.Correlation.v1");
        private readonly object _sync = new();
        private readonly string _path;
        private readonly string _instanceHash;
        private readonly ISecureDataProtector _protector;
        private byte[]? _effectiveKey;
        private bool _initialized;

        public DiagnosticCorrelationService(
            string path,
            string instanceHash,
            ISecureDataProtector protector)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(instanceHash);
            ArgumentNullException.ThrowIfNull(protector);
            _path = Path.GetFullPath(path);
            _instanceHash = instanceHash;
            _protector = protector;
        }

        public string? LastError { get; private set; }

        public bool EnsureAvailable() => GetEffectiveKey() is not null;

        public string? Correlate(string category, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            byte[]? key = GetEffectiveKey();
            if (key is null)
                return null;

            string normalizedCategory = string.IsNullOrWhiteSpace(category) ? "identifier" : category.Trim().ToLowerInvariant();
            byte[] payload = Encoding.UTF8.GetBytes(normalizedCategory + "\u001f" + value.Trim());
            using var hmac = new HMACSHA256(key);
            return "hmac:" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
        }

        private byte[]? GetEffectiveKey()
        {
            lock (_sync)
            {
                if (_initialized)
                    return _effectiveKey;

                _initialized = true;
                try
                {
                    byte[]? rawKey = null;
                    try
                    {
                        if (File.Exists(_path))
                        {
                            byte[] protectedBytes = File.ReadAllBytes(_path);
                            rawKey = _protector.Unprotect(protectedBytes, Entropy);
                            if (rawKey.Length != 32)
                                throw new CryptographicException("诊断关联密钥长度无效。");
                        }
                        else
                        {
                            rawKey = RandomNumberGenerator.GetBytes(32);
                            byte[] protectedBytes = _protector.Protect(rawKey, Entropy);
                            string directory = Path.GetDirectoryName(_path)
                                ?? throw new InvalidOperationException("诊断关联密钥路径没有父目录。");
                            Directory.CreateDirectory(directory);
                            using var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                            stream.Write(protectedBytes);
                            stream.Flush(flushToDisk: true);
                        }

                        using var derivation = new HMACSHA256(rawKey);
                        _effectiveKey = derivation.ComputeHash(Encoding.UTF8.GetBytes(_instanceHash));
                        return _effectiveKey;
                    }
                    finally
                    {
                        if (rawKey is not null)
                            CryptographicOperations.ZeroMemory(rawKey);
                    }
                }
                catch (Exception ex)
                {
                    LastError = "诊断关联身份不可用：" + ex.GetType().Name;
                    _effectiveKey = null;
                    return null;
                }
            }
        }
    }
}
