using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    internal static class SoftwareUpdateDialogPresentation
    {
        private const string GenericReleaseNote = "本版本包含功能改进与问题修复，详细内容请查看官方发布页。";

        public static string FormatReleaseTime(string? releaseDate, TimeZoneInfo? timeZone = null)
        {
            if (!DateTimeOffset.TryParse(
                    releaseDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                    out var parsed))
            {
                return "未知";
            }

            var local = TimeZoneInfo.ConvertTime(parsed, timeZone ?? TimeZoneInfo.Local);
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public static string FormatSourceName(string? sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                return "官方 Release";

            string source = sourceName.Trim();
            if (source.Contains("GitHub", StringComparison.OrdinalIgnoreCase))
                return "官方 Release";
            if (source.Contains("官方", StringComparison.OrdinalIgnoreCase))
                return "官方发布";

            return source;
        }

        public static string FormatChangelog(string? changelog, string? version)
        {
            string displayVersion = string.IsNullOrWhiteSpace(version) ? "新版" : $"v{version.Trim().TrimStart('v', 'V')}";
            string heading = $"{displayVersion} 更新内容";
            string normalized = NormalizeLines(changelog);
            if (string.IsNullOrWhiteSpace(normalized))
                return $"{heading}\r\n\r\n{GenericReleaseNote}";

            var lines = normalized
                .Split('\n')
                .Select(x => x.TrimEnd())
                .ToList();

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            if (lines.Count == 0)
                return $"{heading}\r\n\r\n{GenericReleaseNote}";

            if (IsVersionOnlyHeading(lines[0], version))
            {
                if (lines.Count == 1)
                    return $"{heading}\r\n\r\n{GenericReleaseNote}";

                lines[0] = heading;
                return string.Join("\r\n", lines);
            }

            return $"{heading}\r\n\r\n{string.Join("\r\n", lines)}";
        }

        public static string BuildTrustMessage(string? sha256)
        {
            return string.IsNullOrWhiteSpace(sha256)
                ? "官方发布 · 安装前检查更新包完整性"
                : "官方发布 · 安装前进行 SHA-256 完整性校验";
        }

        private static string NormalizeLines(string? value)
        {
            return (value ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();
        }

        private static bool IsVersionOnlyHeading(string line, string? version)
        {
            var candidates = new List<string>
            {
                "CS2 Trade Monitor",
                "CS2 交易监控"
            };

            if (!string.IsNullOrWhiteSpace(version))
            {
                string cleanVersion = version.Trim().TrimStart('v', 'V');
                candidates.Add($"CS2 Trade Monitor v{cleanVersion}");
                candidates.Add($"CS2 交易监控 v{cleanVersion}");
                candidates.Add($"v{cleanVersion}");
            }

            return candidates.Any(x => string.Equals(line.Trim(), x, StringComparison.OrdinalIgnoreCase));
        }
    }
}
