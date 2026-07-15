using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CS2TradeMonitor.Application.Market
{
    internal static class SteamDtLocalItemNameResolver
    {
        private static readonly Lazy<Dictionary<string, string>> LocalNames = new(LoadLocalNames, isThreadSafe: true);

        public static string ResolveNameByMarketHashName(string marketHashName)
        {
            marketHashName = (marketHashName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(marketHashName))
                return "";

            return LocalNames.Value.TryGetValue(marketHashName, out string? name)
                ? name
                : "";
        }

        private static Dictionary<string, string> LoadLocalNames()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string? path = FindLocalItemsFilePath();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return map;

                string json = ReadText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return map;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    string marketHashName = GetString(item, "market_hash_name", "marketHashName").Trim();
                    string name = GetString(item, "name", "name_cn", "nameCn").Trim();
                    if (string.IsNullOrWhiteSpace(marketHashName) || string.IsNullOrWhiteSpace(name))
                        continue;

                    map[marketHashName] = name;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("SteamDTItem", "加载本地饰品名称映射异常: " + ex.Message);
            }

            return map;
        }

        private static string? FindLocalItemsFilePath()
        {
            foreach (string root in EnumerateCandidateRoots())
            {
                foreach (string relative in new[] { @"resources\steamdt_items.json.gz", "steamdt_items.json.gz", @"resources\steamdt_items.json" })
                {
                    string path = Path.Combine(root, relative);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidateRoots()
        {
            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in new[] { global::CS2TradeMonitor.src.SystemServices.InstallationPaths.InstallDirectory, Environment.CurrentDirectory })
            {
                string? current = root;
                for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
                {
                    if (yielded.Add(current))
                        yield return current;

                    current = Directory.GetParent(current)?.FullName;
                }
            }
        }

        private static string ReadText(string path)
        {
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var file = File.OpenRead(path);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string GetString(JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (!element.TryGetProperty(name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";
                if (value.ValueKind == JsonValueKind.Number)
                    return value.ToString();
            }

            return "";
        }
    }
}
