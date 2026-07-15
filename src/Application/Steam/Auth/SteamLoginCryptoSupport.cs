using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CS2TradeMonitor.Application.Steam.Auth
{
    internal static class SteamLoginCryptoSupport
    {
        public static byte[] BuildMobileConfirmationSignature(int version, ulong clientId, string steamId, string identitySecret)
        {
            byte[] secret = Convert.FromBase64String((identitySecret ?? "").Trim());
            Span<byte> data = stackalloc byte[18];
            BinaryPrimitives.WriteUInt16LittleEndian(data[..2], (ushort)Math.Clamp(version, 0, ushort.MaxValue));
            BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(2, 8), clientId);
            BinaryPrimitives.WriteUInt64LittleEndian(data.Slice(10, 8), ulong.Parse(steamId, CultureInfo.InvariantCulture));
            return HMACSHA256.HashData(secret, data);
        }

        public static string EncryptPassword(string password, string modulusHex, string exponentHex)
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = HexToBytes(modulusHex),
                Exponent = HexToBytes(exponentHex)
            });
            return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(password), RSAEncryptionPadding.Pkcs1));
        }

        public static string RandomHex(int bytes)
        {
            Span<byte> buffer = stackalloc byte[bytes];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToHexString(buffer).ToLowerInvariant();
        }

        public static byte[] HexToBytes(string hex)
        {
            hex = (hex ?? "").Trim();
            if (hex.Length % 2 == 1)
                hex = "0" + hex;
            byte[] bytes = Convert.FromHexString(hex);
            int firstNonZero = 0;
            while (firstNonZero < bytes.Length - 1 && bytes[firstNonZero] == 0)
                firstNonZero++;
            return firstNonZero == 0 ? bytes : bytes[firstNonZero..];
        }
    }
}
