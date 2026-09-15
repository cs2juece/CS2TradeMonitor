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
                using Stream? file = typeof(SteamDtLocalItemNameResolver).Assembly.GetManifestResourceStream(
                    "CS2TradeMonitor.Shared.Resources.steamdt_items.json.gz");
                if (file is null)
                    return map;
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string json = reader.ReadToEnd();
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
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Missing local display-name data must not change inventory eligibility.
            }

            return map;
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
