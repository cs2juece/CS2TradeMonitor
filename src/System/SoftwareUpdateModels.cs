using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.src.SystemServices
{
    public enum SoftwareUpdateState
    {
        Disabled,
        Failed,
        Latest,
        Available
    }

    public sealed class SoftwareUpdateCheckResult
    {
        public SoftwareUpdateState State { get; init; }
        public string Message { get; init; } = "";
        public string CurrentVersion { get; init; } = "";
        public string ManifestUrl { get; init; } = "";
        public string ManifestSourceName { get; init; } = "";
        public SoftwareUpdateManifest? Manifest { get; init; }
        public SoftwareUpdateAsset? Asset { get; init; }
        public string DownloadUrl { get; init; } = "";
        public string DownloadSourceName { get; init; } = "";
        public IReadOnlyList<SoftwareUpdateDownloadSource> DownloadSources { get; init; } = Array.Empty<SoftwareUpdateDownloadSource>();

        public bool HasUpdate => State == SoftwareUpdateState.Available && Manifest != null && Asset != null;
    }

    public sealed class SoftwareUpdateProgress
    {
        public long BytesReceived { get; init; }
        public long? TotalBytes { get; init; }
        public double BytesPerSecond { get; init; }
        public string SourceUrl { get; init; } = "";
        public string SourceName { get; init; } = "";

        public int Percent => TotalBytes.GetValueOrDefault() > 0
            ? (int)Math.Clamp(BytesReceived * 100.0 / TotalBytes.GetValueOrDefault(), 0, 100)
            : 0;
    }

    public sealed class SoftwareUpdateDownloadResult
    {
        public string TransactionId { get; init; } = "";
        public string ZipPath { get; init; } = "";
        public string SourceUrl { get; init; } = "";
        public long SizeBytes { get; init; }
        public string Sha256 { get; init; } = "";
    }

    public sealed class SoftwareUpdateDownloadSource
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        public string DisplayName => !string.IsNullOrWhiteSpace(Name)
            ? Name
            : !string.IsNullOrWhiteSpace(Source)
                ? Source
                : "下载源";
    }

    public sealed class SoftwareUpdateManifest
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("releaseDate")]
        public string ReleaseDate { get; set; } = "";

        [JsonPropertyName("changelog")]
        public string Changelog { get; set; } = "";

        [JsonPropertyName("minSupportedVersion")]
        public string MinSupportedVersion { get; set; } = "";

        [JsonPropertyName("assets")]
        public SoftwareUpdateAssets Assets { get; set; } = new();
    }

    public sealed class SoftwareUpdateAssets
    {
        [JsonPropertyName("winX64")]
        public SoftwareUpdateAsset WinX64 { get; set; } = new();
    }

    public sealed class SoftwareUpdateAsset
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("urls")]
        public JsonElement Urls { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        public IReadOnlyList<string> GetUrls()
        {
            return GetDownloadSources()
                .Select(x => x.Url)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<SoftwareUpdateDownloadSource> GetDownloadSources()
        {
            var urls = new List<SoftwareUpdateDownloadSource>();
            if (!string.IsNullOrWhiteSpace(Url))
            {
                urls.Add(new SoftwareUpdateDownloadSource
                {
                    Name = GuessSourceName(Url),
                    Url = Url.Trim(),
                    Priority = 0
                });
            }

            if (Urls.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in Urls.EnumerateArray())
                {
                    index++;
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var url = item.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            urls.Add(new SoftwareUpdateDownloadSource
                            {
                                Name = GuessSourceName(url),
                                Url = url.Trim(),
                                Priority = index
                            });
                        }
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        string url = ReadString(item, "url");
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            urls.Add(new SoftwareUpdateDownloadSource
                            {
                                Name = ReadString(item, "name"),
                                Source = ReadString(item, "source"),
                                Url = url.Trim(),
                                Priority = ReadInt(item, "priority", index)
                            });
                        }
                    }
                }
            }

            return urls
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderBy(y => y.Priority <= 0 ? int.MaxValue : y.Priority).First())
                .OrderBy(x => x.Priority <= 0 ? int.MaxValue : x.Priority)
                .ToArray();
        }

        private static string ReadString(JsonElement obj, string property)
        {
            return obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
        }

        private static int ReadInt(JsonElement obj, string property, int fallback)
        {
            if (!obj.TryGetProperty(property, out var value))
                return fallback;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result))
                return result;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result))
                return result;

            return fallback;
        }

        private static string GuessSourceName(string url)
        {
            if (url.Contains("gitee.com", StringComparison.OrdinalIgnoreCase))
                return "Gitee 国内";
            if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                || url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                return "GitHub 备用";
            if (url.Contains("cloudflare", StringComparison.OrdinalIgnoreCase)
                || url.Contains("r2.dev", StringComparison.OrdinalIgnoreCase)
                || url.Contains("pages.dev", StringComparison.OrdinalIgnoreCase))
                return "Cloudflare 备用";

            return "下载源";
        }
    }
}
