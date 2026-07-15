using System.Security.Cryptography;
using System.Text;

#if UPDATER_BUILD
namespace CS2TradeMonitor.Updater.Paths
#else
namespace CS2TradeMonitor.Infrastructure.Paths
#endif
{
    public static class InstanceIdentity
    {
        public static (string CanonicalInstallRoot, string InstanceHash) Resolve(string installRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
            string canonical = new WindowsCanonicalPathResolver().ResolveDirectory(installRoot);
            return (canonical, ComputeHash(canonical));
        }

        public static string ComputeHash(string canonicalInstallRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(canonicalInstallRoot);
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalInstallRoot);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }
}
