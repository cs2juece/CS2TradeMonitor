using CS2TradeMonitor.Application.Abstractions;
using System.Security.Cryptography;

namespace CS2TradeMonitor.Infrastructure.Security
{
    public sealed class DpapiSecureDataProtector : ISecureDataProtector
    {
        public byte[] Protect(byte[] plaintext, byte[] entropy)
            => ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);

        public byte[] Unprotect(byte[] protectedData, byte[] entropy)
            => ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser);
    }
}
