using System.Text.Json;

namespace CS2TradeMonitor.UpdateSecurity
{
    internal sealed class ProgramFileManifest
    {
        public int Version { get; set; } = 1;
        public List<string> Files { get; set; } = new();
        public List<string> Obsolete { get; set; } = new();
    }

    internal static class ProgramFileManifestPolicy
    {
        public const string RelativePath = "app/program-files.json";
        private static readonly HashSet<string> AllowedObsolete = new(StringComparer.OrdinalIgnoreCase)
        {
            "CS2TradeMonitor.App.exe",
            "CS2TradeMonitor.Updater.exe",
            "WebView2Loader.dll",
            "THIRD_PARTY_NOTICES.txt",
            "SteamDT_API_填写说明.txt",
            "docs/SteamDT_API_填写说明.txt",
            "docs/使用说明(必读).txt"
        };

        public static ProgramFileManifest LoadAndValidate(string sourceRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
            string root = Path.GetFullPath(sourceRoot);
            string path = Path.Combine(root, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new InvalidDataException("更新包缺少程序文件清单：" + RelativePath);

            HashSet<string> actualFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(pathValue => NormalizeRelative(Path.GetRelativePath(root, pathValue)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return ParseAndValidate(File.ReadAllText(path), actualFiles);
        }

        public static ProgramFileManifest ParseAndValidate(string json, IEnumerable<string> actualFilePaths)
        {
            ProgramFileManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ProgramFileManifest>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("程序文件清单为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("程序文件清单格式无效。", ex);
            }

            if (manifest.Version != 1)
                throw new InvalidDataException("不支持的程序文件清单版本。");

            HashSet<string> declaredFiles = NormalizeUnique(manifest.Files, "files");
            if (declaredFiles.Count == 0 || !declaredFiles.Contains(RelativePath))
                throw new InvalidDataException("程序文件清单未包含自身或文件列表为空。");

            foreach (string relative in declaredFiles)
            {
                if (UpdatePackageFilePolicy.IsForbidden(relative))
                    throw new InvalidDataException("程序文件清单包含禁止路径：" + relative);
            }

            HashSet<string> actualFiles = actualFilePaths
                .Select(NormalizeRelative)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!actualFiles.SetEquals(declaredFiles))
                throw new InvalidDataException("程序文件清单与更新包实际文件不一致。");

            HashSet<string> obsolete = NormalizeUnique(manifest.Obsolete, "obsolete");
            foreach (string relative in obsolete)
            {
                if (!AllowedObsolete.Contains(relative))
                    throw new InvalidDataException("程序文件清单包含未授权的 obsolete 路径：" + relative);
                if (declaredFiles.Contains(relative))
                    throw new InvalidDataException("程序文件不能同时属于 files 和 obsolete：" + relative);
            }

            manifest.Files = declaredFiles.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            manifest.Obsolete = obsolete.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            return manifest;
        }

        public static string NormalizeRelative(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException("程序文件清单包含空路径。");

            string normalized = relativePath.Replace('\\', '/').Trim().TrimEnd('/');
            if (normalized.Length == 0
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || Path.IsPathRooted(normalized)
                || normalized.Contains(':', StringComparison.Ordinal))
            {
                throw new InvalidDataException("程序文件清单包含绝对路径：" + relativePath);
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
                throw new InvalidDataException("程序文件清单包含非法路径：" + relativePath);
            return string.Join('/', segments);
        }

        private static HashSet<string> NormalizeUnique(IEnumerable<string>? values, string section)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in values ?? Array.Empty<string>())
            {
                string normalized = NormalizeRelative(value);
                if (!result.Add(normalized))
                    throw new InvalidDataException($"程序文件清单 {section} 包含重复路径：{normalized}");
            }
            return result;
        }
    }
}
