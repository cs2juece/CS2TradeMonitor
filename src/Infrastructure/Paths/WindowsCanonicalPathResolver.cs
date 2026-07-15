using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

#if UPDATER_BUILD
namespace CS2TradeMonitor.Updater.Paths
#else
namespace CS2TradeMonitor.Infrastructure.Paths
#endif
{
    internal sealed class WindowsCanonicalPathResolver
    {
        private const uint FileReadAttributes = 0x0080;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileNameNormalized = 0x0;
        private const uint VolumeNameDos = 0x0;

        public string ResolveDirectory(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            string fullPath = Path.GetFullPath(directory);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"安装目录不存在：{fullPath}");

            using SafeFileHandle handle = CreateFile(
                fullPath,
                FileReadAttributes,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法解析安装目录的物理路径：{fullPath}");

            int capacity = 512;
            while (true)
            {
                var buffer = new StringBuilder(capacity);
                uint length = GetFinalPathNameByHandle(
                    handle,
                    buffer,
                    (uint)buffer.Capacity,
                    FileNameNormalized | VolumeNameDos);
                if (length == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法读取安装目录的最终物理路径：{fullPath}");

                if (length < buffer.Capacity)
                    return NormalizeForHash(buffer.ToString());

                capacity = checked((int)length + 1);
            }
        }

        internal static string NormalizeForHash(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            string normalized = path.Replace('/', '\\');
            if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                normalized = @"\\" + normalized[8..];
            else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[4..];

            normalized = Path.GetFullPath(normalized).Replace('/', '\\');
            string root = Path.GetPathRoot(normalized) ?? string.Empty;
            while (normalized.Length > root.Length && normalized.EndsWith('\\'))
                normalized = normalized[..^1];

            return normalized.ToLowerInvariant();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathLength,
            uint flags);
    }
}
