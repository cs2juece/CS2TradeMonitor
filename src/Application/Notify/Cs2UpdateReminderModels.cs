using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.Notify
{
    public sealed class Cs2UpdateDetectedEventArgs : EventArgs
    {
        public Cs2UpdateDetectedEventArgs(string title, string message, IReadOnlyList<Cs2UpdateLogItem> items)
        {
            Title = title;
            Message = message;
            Items = items;
        }

        public string Title { get; }
        public string Message { get; }
        public IReadOnlyList<Cs2UpdateLogItem> Items { get; }
    }

    public sealed class Cs2UpdateCheckResult
    {
        public Cs2UpdateCheckResult(bool success, bool hasNewUpdate, string message, Cs2UpdateLogItem? latest, int newCount, DateTime checkedAt)
        {
            Success = success;
            HasNewUpdate = hasNewUpdate;
            Message = message;
            Latest = latest;
            NewCount = newCount;
            CheckedAt = checkedAt;
        }

        public bool Success { get; }
        public bool HasNewUpdate { get; }
        public string Message { get; }
        public Cs2UpdateLogItem? Latest { get; }
        public int NewCount { get; }
        public DateTime CheckedAt { get; }

        public static Cs2UpdateCheckResult NotChecked() => new(false, false, "未检查", null, 0, DateTime.MinValue);
        public static Cs2UpdateCheckResult Fail(string message) => new(false, false, message, null, 0, DateTime.Now);
        public static Cs2UpdateCheckResult Fail(string message, DateTime checkedAt) => new(false, false, message, null, 0, checkedAt);
    }

    public sealed class Cs2UpdateLogItem
    {
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("logId")] public string LogId { get; set; } = "";
        [JsonPropertyName("publishedAt")] public long PublishedAt { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
        [JsonPropertyName("moduleSummaries")] public List<Cs2UpdateModuleSummary>? ModuleSummaries { get; set; }

        [JsonIgnore]
        public string Source
        {
            get => From;
            set => From = value ?? "";
        }

        [JsonIgnore]
        public string Key
        {
            get
            {
                string from = (From ?? "").Trim();
                string logId = (LogId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(logId))
                    return $"{from}:{logId}";

                string title = (Title ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(title))
                    return $"fallback:{PublishedAt}:{title}";

                return $"fallback:{PublishedAt}:{(Summary ?? Content ?? "").Trim()}";
            }
        }

        public string GetNotificationText()
        {
            string title = string.IsNullOrWhiteSpace(Title) ? "CS2 更新" : Title.Trim();
            string summary = string.IsNullOrWhiteSpace(Summary) ? StripMarkdown(Content).Trim() : Summary.Trim();
            if (summary.Length > 180)
                summary = summary[..180] + "...";

            string time = Cs2UpdateReminderService.FormatTime(PublishedAt);
            return string.IsNullOrWhiteSpace(summary)
                ? $"{title}{Environment.NewLine}{time}"
                : $"{title}{Environment.NewLine}{time}{Environment.NewLine}{summary}";
        }

        private static string StripMarkdown(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text
                .Replace("#", "")
                .Replace("`", "")
                .Replace("*", "")
                .Replace("\r", "")
                .Replace("\n\n", "\n")
                .Trim();
        }
    }

    public sealed class Cs2UpdateModuleSummary
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }
}
