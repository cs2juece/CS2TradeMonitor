using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    public static class SteamCryptoHelper
    {
        public const string SteamGuardAlphabet = "23456789BCDFGHJKMNPQRTVWXY";
        public const int SteamGuardSharedSecretLength = 20;

        public static bool IsValidBase64Secret(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                return Convert.FromBase64String(value.Trim()).Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static bool TryValidateSteamGuardSharedSecret(string value, out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(value))
            {
                message = "shared_secret 为空。";
                return false;
            }

            byte[] secret;
            try
            {
                secret = Convert.FromBase64String(value.Trim());
            }
            catch (FormatException)
            {
                message = "shared_secret 不是有效的 base64。";
                return false;
            }

            if (secret.Length != SteamGuardSharedSecretLength)
            {
                message = $"shared_secret 解码后长度异常：{secret.Length} 字节，应为 {SteamGuardSharedSecretLength} 字节。";
                return false;
            }

            return true;
        }

        public static string GenerateSteamGuardCode(string sharedSecret, long steamTime)
        {
            byte[] secret = DecodeSteamGuardSharedSecret(sharedSecret);
            Span<byte> timeBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(timeBytes, steamTime / 30);
            Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
            HMACSHA1.HashData(secret, timeBytes, hash);

            int offset = hash[^1] & 0x0F;
            int codePoint = ((hash[offset] & 0x7F) << 24)
                | ((hash[offset + 1] & 0xFF) << 16)
                | ((hash[offset + 2] & 0xFF) << 8)
                | (hash[offset + 3] & 0xFF);

            return string.Create(5, codePoint, (chars, value) =>
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    chars[i] = SteamGuardAlphabet[value % SteamGuardAlphabet.Length];
                    value /= SteamGuardAlphabet.Length;
                }
            });
        }

        private static byte[] DecodeSteamGuardSharedSecret(string sharedSecret)
        {
            byte[] secret;
            try
            {
                secret = Convert.FromBase64String((sharedSecret ?? "").Trim());
            }
            catch (FormatException ex)
            {
                throw new FormatException("shared_secret 不是有效的 base64。", ex);
            }

            if (secret.Length != SteamGuardSharedSecretLength)
                throw new FormatException($"shared_secret 解码后长度异常：{secret.Length} 字节，应为 {SteamGuardSharedSecretLength} 字节。");
            return secret;
        }

        public static string GenerateTotpCode(string base32Secret, long unixTimeSeconds, int digits = 6, int periodSeconds = 30)
        {
            if (periodSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(periodSeconds));
            return GenerateHotpCode(base32Secret, unixTimeSeconds / periodSeconds, digits);
        }

        public static string GenerateHotpCode(string base32Secret, long counter, int digits = 6)
        {
            if (digits < 6 || digits > 8) throw new ArgumentOutOfRangeException(nameof(digits));
            byte[] secret = Base32Decode(base32Secret);
            Span<byte> counterBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
            Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
            HMACSHA1.HashData(secret, counterBytes, hash);

            int offset = hash[^1] & 0x0F;
            int codePoint = ((hash[offset] & 0x7F) << 24)
                | ((hash[offset + 1] & 0xFF) << 16)
                | ((hash[offset + 2] & 0xFF) << 8)
                | (hash[offset + 3] & 0xFF);
            int modulo = (int)Math.Pow(10, digits);
            return (codePoint % modulo).ToString("D" + digits);
        }

        public static string GenerateConfirmationHash(string identitySecret, long time, string tag)
        {
            byte[] secret = Convert.FromBase64String((identitySecret ?? "").Trim());
            byte[] tagBytes = Encoding.UTF8.GetBytes(tag ?? "");
            int length = 8 + Math.Min(32, tagBytes.Length);
            byte[] data = new byte[length];
            long value = time;
            for (int i = 7; i >= 0; i--)
            {
                data[i] = (byte)value;
                value >>= 8;
            }
            if (tagBytes.Length > 0)
                Array.Copy(tagBytes, 0, data, 8, length - 8);

            return Convert.ToBase64String(HMACSHA1.HashData(secret, data));
        }

        public static string GenerateDeviceId(string steamId)
        {
            byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes((steamId ?? "").Trim()));
            string hex = Convert.ToHexString(hash).ToLowerInvariant();
            return $"android:{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
        }

        private static byte[] Base32Decode(string base32)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            string text = (base32 ?? "").Trim().TrimEnd('=').Replace(" ", "").Replace("-", "").ToUpperInvariant();
            if (text.Length == 0) throw new FormatException("Base32 secret is empty.");

            var result = new List<byte>();
            int buffer = 0;
            int bitsRemaining = 0;
            foreach (char c in text)
            {
                int value = alphabet.IndexOf(c);
                if (value < 0)
                    throw new FormatException("Base32 secret contains invalid characters.");

                buffer = (buffer << 5) | value;
                bitsRemaining += 5;
                if (bitsRemaining >= 8)
                {
                    bitsRemaining -= 8;
                    result.Add((byte)(buffer >> bitsRemaining));
                    buffer &= (1 << bitsRemaining) - 1;
                }
            }

            return result.ToArray();
        }
    }
}
