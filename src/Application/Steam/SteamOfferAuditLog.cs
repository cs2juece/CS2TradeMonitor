using CS2TradeMonitor.src.SystemServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CS2TradeMonitor.Application.Steam
{
    public static class SteamOfferAuditLog
    {
        public const string LogFileName = "quote_action_log.txt";
        public const string TechnicalLogFileName = "steam_offer_tech_log.txt";
        public const string SystemSteam = "Steam";
        public const string SystemYouPin = "悠悠";
        public const string TriggerBackgroundAuto = "后台自动";
        public const string TriggerUserManual = "用户手动";
        public const string TriggerUserCheckNow = "用户立即检查";
        public const string TriggerSystem = "系统";
        private const string HeaderTitle = "CS2 Trade Monitor 报价动作日志";
        private const string TechnicalHeaderTitle = "CS2 Trade Monitor Steam 报价技术日志";
        private const string HeaderColumns = "时间\t系统\t触发\t动作\t结果\t订单\t报价号\t说明";
        private const string TechnicalHeaderColumns = "时间\t级别\t说明";
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private static readonly Regex SteamCredentialsRegex = new(
            @"(?i)\b(api[-_\s]?key|shared[-_\s]?secret|identity[-_\s]?secret|access[-_\s]?token|refresh[-_\s]?token|steamRefresh_steam|steamLoginSecure|steamLogin|sessionid|cookie|password)\s*[:=]\s*""?([a-zA-Z0-9%_\-\.\+=/\|]+)""?",
            RegexOptions.Compiled);
        private static readonly Regex LogIdReferenceRegex = new(
            @"(?<label>(?:报价号|订单|TradeOfferId|OrderNo)\s*[=:：]\s*)(?<id>[A-Za-z0-9_-]{5,})",
            RegexOptions.Compiled);
        private static readonly object ThrottleLock = new();
        private static readonly object LogLock = new();
        private static readonly Dictionary<string, DateTime> LastInfoByKey = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> LastBusinessByKey = new(StringComparer.Ordinal);
        private static readonly TimeSpan BusinessDuplicateWindow = TimeSpan.FromSeconds(3);
        private static Func<string> _logPathProvider = DefaultLogPathProvider;

        public static string LogFilePath => _logPathProvider();

        public static string TechnicalLogFilePath
        {
            get
            {
                string directory = Path.GetDirectoryName(LogFilePath) ?? DiagnosticsLogger.LogDirectory;
                return Path.Combine(directory, TechnicalLogFileName);
            }
        }

        internal static void SetLogPathProviderForTests(Func<string> provider)
        {
            _logPathProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        internal static void ResetForTests()
        {
            _logPathProvider = DefaultLogPathProvider;
            lock (ThrottleLock)
            {
                LastInfoByKey.Clear();
                LastBusinessByKey.Clear();
            }
        }

        public static string EnsureLogFile()
        {
            string path = LogFilePath;
            try
            {
                lock (LogLock)
                {
                    EnsureLogFileNoLock(path, HeaderTitle, HeaderColumns, archiveUnexpectedHeader: true);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("SteamOfferLog", "EnsureLogFile", ex, retryable: true, category: "Log");
            }

            return path;
        }

        public static void Info(string message)
        {
            if (IsTechnicalMessage(message))
            {
                AppendTechnicalLog("INFO", message, null);
                return;
            }

            WriteBusinessLog(SystemSteam, TriggerSystem, "记录", "信息", "", "", TranslateBusinessMessage(message));
        }

        public static void Diagnostic(string message)
        {
            AppendTechnicalLog("INFO", message, null);
        }

        public static void DiagnosticThrottled(string key, string message, TimeSpan window)
        {
            if (string.IsNullOrWhiteSpace(key) || window <= TimeSpan.Zero)
            {
                Diagnostic(message);
                return;
            }

            var now = DateTime.Now;
            string stateKey = "diagnostic:" + key;
            lock (ThrottleLock)
            {
                if (LastInfoByKey.TryGetValue(stateKey, out var last) && now - last < window)
                    return;

                LastInfoByKey[stateKey] = now;
            }

            Diagnostic(message);
        }

        public static void DiagnosticError(string message, Exception? ex = null)
        {
            AppendTechnicalLog("ERROR", message, ex);
        }

        public static void InfoThrottled(string key, string message, TimeSpan window)
        {
            if (string.IsNullOrWhiteSpace(key) || window <= TimeSpan.Zero)
            {
                Info(message);
                return;
            }

            var now = DateTime.Now;
            lock (ThrottleLock)
            {
                if (LastInfoByKey.TryGetValue(key, out var last) && now - last < window)
                    return;

                LastInfoByKey[key] = now;
            }

            Info(message);
        }

        public static void Error(string message, Exception? ex = null)
        {
            if (IsTechnicalMessage(message))
            {
                AppendTechnicalLog("ERROR", message, ex);
                return;
            }

            string detail = TranslateBusinessMessage(message);
            if (ex != null)
                detail += " 异常：" + RedactSecrets(ex.Message);
            WriteBusinessLog(SystemSteam, TriggerSystem, "错误", "失败", "", ExtractTradeOfferId(message), detail);
        }

        public static void LogImportToken(string steamId, string source)
        {
            string maskedSteamId = MaskSteamId(steamId);
            WriteBusinessLog(SystemSteam, TriggerUserManual, "导入令牌", "成功", "", "", $"已导入 Steam 令牌。SteamId={maskedSteamId}；来源={NormalizeLogCell(source)}");
        }

        public static void LogAcceptOffer(string tradeOfferId, bool safe, bool verifiedByYouPin, string? orderNo = null, string trigger = TriggerUserManual)
        {
            string msg = $"已同意 Steam 报价。安全收货={FormatBool(safe)}；悠悠匹配={FormatBool(verifiedByYouPin)}";
            if (!string.IsNullOrEmpty(orderNo))
            {
                msg += $"；订单={MaskLogId(orderNo)}";
            }
            WriteBusinessLog(SystemSteam, trigger, "同意报价", "成功", orderNo ?? "", tradeOfferId, msg);
        }

        public static void LogMobileConfirmation(string tradeOfferId, string? orderNo, string trigger, string message)
        {
            WriteBusinessLog(SystemSteam, trigger, "手机确认", "成功", orderNo ?? "", tradeOfferId, RedactSecrets(message));
        }

        public static void LogMobileConfirmationMatchEvaluation(int candidates, int sameOfferId, bool matched)
        {
            Info(
                $"Steam mobile confirmation match evaluated. Candidates={Math.Max(0, candidates)}; "
                + $"SameOfferId={Math.Max(0, sameOfferId)}; Matched={matched}");
        }

        public static void LogMobileConfirmationSubmissionStarted()
        {
            Info("Steam mobile confirmation submission started. MatchSource=SteamOfferId");
        }

        public static void LogMobileConfirmationSubmissionCompleted(bool success, Exception? exception = null)
        {
            if (exception == null)
            {
                Info($"Steam mobile confirmation submission completed. Success={success}; Outcome=Returned");
                return;
            }

            Info(
                $"Steam mobile confirmation submission completed. Success=False; Outcome=Exception; ErrorType={exception.GetType().Name}");
        }

        public static void LogDenyOffer(string tradeOfferId, string trigger = TriggerUserManual)
        {
            WriteBusinessLog(SystemSteam, trigger, "拒绝报价", "成功", "", tradeOfferId, "已拒绝 Steam 报价。");
        }

        public static void LogRefreshResult(bool ok, int count, string message, string trigger = TriggerSystem)
        {
            string detail = string.IsNullOrWhiteSpace(message)
                ? ok ? $"刷新报价完成，当前 {count} 条需要处理。" : "刷新报价失败。"
                : message;
            string result = ok
                ? LooksLikeRefreshWarning(detail) ? "警告" : "成功"
                : "失败";
            WriteBusinessLog(SystemSteam, trigger, "刷新报价", result, "", "", detail);
        }

        public static void LogAutoTradeStarted(bool enabled, int intervalSeconds)
        {
            WriteBusinessLog(
                SystemSteam,
                TriggerBackgroundAuto,
                "自动处理",
                enabled ? "已启动" : "仅读取",
                "",
                "",
                enabled
                    ? $"自动处理交易已启动，检查间隔 {intervalSeconds} 秒。"
                    : $"后台读取已启动，自动处理未开启，检查间隔 {intervalSeconds} 秒。");
        }

        public static void LogAutoTradeFailure(string reason)
        {
            WriteBusinessLog(SystemSteam, TriggerBackgroundAuto, "自动处理", "失败", "", "", "自动处理失败：" + RedactSecrets(reason));
        }

        public static void LogTradeAction(
            string system,
            string trigger,
            string action,
            string result,
            string orderNo,
            string tradeOfferId,
            string message)
        {
            WriteBusinessLog(system, trigger, action, result, orderNo, tradeOfferId, message);
        }

        public static string RedactSecrets(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string basicRedacted = DiagnosticsLogger.Redact(text);
            return SteamCredentialsRegex.Replace(basicRedacted, match => match.Groups[1].Value + "=" + MaskSecret(match.Groups[2].Value));
        }

        public static string MaskSecret(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Length <= 8) return "***";
            return text[..3] + "***" + text[^3..];
        }

        private static string MaskSteamId(string? steamId)
        {
            if (string.IsNullOrEmpty(steamId)) return string.Empty;
            if (steamId.Length <= 6) return "****";
            return steamId[..3] + "****" + steamId[^3..];
        }

        private static string DefaultLogPathProvider()
        {
            return DiagnosticsLogger.GetDiagnosticFilePath(LogFileName);
        }

        private static void WriteBusinessLog(string system, string trigger, string action, string result, string orderNo, string tradeOfferId, string message)
        {
            try
            {
                string line = string.Join("\t", new[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    NormalizeLogCell(system),
                    NormalizeLogCell(trigger),
                    NormalizeLogCell(action),
                    NormalizeLogCell(result),
                    NormalizeLogCell(MaskLogId(orderNo)),
                    NormalizeLogCell(MaskLogId(tradeOfferId)),
                    NormalizeLogCell(MaskKnownIdReferences(RedactSecrets(message)))
                }) + Environment.NewLine;

                lock (LogLock)
                {
                    if (ShouldSuppressDuplicateBusinessLog(system, trigger, action, result, orderNo, tradeOfferId, message))
                        return;

                    string path = LogFilePath;
                    EnsureLogFileNoLock(path, HeaderTitle, HeaderColumns, archiveUnexpectedHeader: true);
                    RotateIfNeeded(path);
                    EnsureLogFileNoLock(path, HeaderTitle, HeaderColumns, archiveUnexpectedHeader: true);
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch (Exception logEx)
            {
                DiagnosticsLogger.Ignored("SteamOfferLog", "WriteBusinessLog", logEx, retryable: true, category: "Log");
            }
        }

        private static void AppendTechnicalLog(string level, string message, Exception? ex)
        {
            try
            {
                string detail = RedactSecrets(message);
                if (ex != null)
                    detail += " Exception=" + RedactSecrets(ex.ToString());

                string line = string.Join("\t", new[]
                {
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    NormalizeLogCell(level),
                    NormalizeLogCell(detail)
                }) + Environment.NewLine;

                lock (LogLock)
                {
                    string path = TechnicalLogFilePath;
                    EnsureLogFileNoLock(path, TechnicalHeaderTitle, TechnicalHeaderColumns, archiveUnexpectedHeader: false);
                    RotateIfNeeded(path);
                    EnsureLogFileNoLock(path, TechnicalHeaderTitle, TechnicalHeaderColumns, archiveUnexpectedHeader: false);
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch (Exception logEx)
            {
                DiagnosticsLogger.Ignored("SteamOfferLog", "AppendTechnicalLog", logEx, retryable: true, category: "Log");
            }
        }

        private static void EnsureLogFileNoLock(string path, string title, string columns, bool archiveUnexpectedHeader)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (archiveUnexpectedHeader && File.Exists(path) && new FileInfo(path).Length > 0)
            {
                string firstLine = File.ReadLines(path, Encoding.UTF8).FirstOrDefault() ?? "";
                if (!string.Equals(firstLine, title, StringComparison.Ordinal))
                    ArchiveUnexpectedLogFile(path);
            }

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                File.WriteAllText(
                    path,
                    title + Environment.NewLine + columns + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }

        private static void ArchiveUnexpectedLogFile(string path)
        {
            string archivePath = path + ".legacy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            if (File.Exists(archivePath))
                archivePath += "_" + Guid.NewGuid().ToString("N");
            File.Move(path, archivePath);
        }

        private static bool ShouldSuppressDuplicateBusinessLog(
            string system,
            string trigger,
            string action,
            string result,
            string orderNo,
            string tradeOfferId,
            string message)
        {
            string key = string.Join("\u001f", system, trigger, action, result, orderNo, tradeOfferId, message);
            var now = DateTime.Now;
            if (LastBusinessByKey.TryGetValue(key, out var last) && now - last < BusinessDuplicateWindow)
                return true;

            LastBusinessByKey[key] = now;
            if (LastBusinessByKey.Count > 256)
            {
                foreach (string staleKey in LastBusinessByKey
                    .Where(pair => now - pair.Value > TimeSpan.FromMinutes(5))
                    .Select(pair => pair.Key)
                    .ToList())
                {
                    LastBusinessByKey.Remove(staleKey);
                }
            }

            return false;
        }

        private static bool LooksLikeRefreshWarning(string message)
        {
            string text = message ?? "";
            return text.Contains("Steam 网络不可用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("暂未同步", StringComparison.OrdinalIgnoreCase)
                || text.Contains("部分 Steam 数据暂时不可用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("部分报价数据暂时未同步", StringComparison.OrdinalIgnoreCase)
                || text.Contains("暂时不可用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("暂时无法确认", StringComparison.OrdinalIgnoreCase)
                || text.Contains("请稍后刷新", StringComparison.OrdinalIgnoreCase)
                || text.Contains("请开启加速器", StringComparison.OrdinalIgnoreCase);
        }

        private static void RotateIfNeeded(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= MaxLogBytes)
                return;

            string archive3 = path + ".3";
            string archive2 = path + ".2";
            string archive1 = path + ".1";
            if (File.Exists(archive3)) File.Delete(archive3);
            if (File.Exists(archive2)) File.Move(archive2, archive3, overwrite: true);
            if (File.Exists(archive1)) File.Move(archive1, archive2, overwrite: true);
            File.Move(path, archive1, overwrite: true);
        }

        private static string NormalizeLogCell(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Trim();
        }

        private static string MaskLogId(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0)
                return "";
            if (text.Length <= 4)
                return "***";

            return "***" + text[^4..];
        }

        private static string MaskKnownIdReferences(string? value)
        {
            string text = value ?? string.Empty;
            if (text.Length == 0)
                return text;

            return LogIdReferenceRegex.Replace(text, match =>
                match.Groups["label"].Value + MaskLogId(match.Groups["id"].Value));
        }

        private static bool IsTechnicalMessage(string? message)
        {
            string text = message ?? "";
            string[] prefixes =
            {
                "Steam offer load stage.",
                "Steam web tradeoffers parsed.",
                "Synced Steam time offset.",
                "Failed to sync Steam time offset",
                "Steam mobile confirmations fetched.",
                "Steam mobile confirmations unavailable",
                "Steam mobile confirmation match evaluated.",
                "Steam mobile confirmation submission started.",
                "Steam mobile confirmation submission completed.",
                "Steam offer background enrichment failed.",
                "Steam web asset class info enrichment skipped.",
                "Steam web offer detail enrichment skipped.",
                "Steam web detail page enrichment skipped.",
                "Steam offer detail enrichment skipped.",
                "Steam confirmation detail enrichment skipped.",
                "Steam offer detail batch enrichment skipped.",
                "Steam web trade offers unavailable",
                "Steam Web API trade offers unavailable",
                "Steam web trade offers auth expired",
                "Steam new trade acknowledgement skipped:",
                "Steam trade offer ignored as already handled or inactive.",
                "Steam API returned HTTP error.",
                "Steam GenerateAccessTokenForApp JSON parse failed.",
                "Steam QueryTime unavailable;",
                "Steam HTTP request failed.",
                "Steam HTTP request timed out.",
                "Steam finalizelogin failed.",
                "LoginRedirect",
                "LoginPage",
                "WebTradeOffers"
            };

            return prefixes.Any(prefix => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                || text.Contains("HttpStatus=", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Host=api.steampowered.com", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ElapsedMs=", StringComparison.OrdinalIgnoreCase)
                || text.Contains("HasTradeOfferSignal=", StringComparison.OrdinalIgnoreCase);
        }

        private static string TranslateBusinessMessage(string? message)
        {
            string text = RedactSecrets(message);
            if (text.StartsWith("Steam auto trade record.", StringComparison.OrdinalIgnoreCase))
                return text.Replace("Steam auto trade record.", "自动处理记录。")
                    .Replace("Type=", "类型=")
                    .Replace("Direction=", "方向=")
                    .Replace("Items=", "饰品=")
                    .Replace("Source=", "来源=")
                    .Replace("Result=", "结果=")
                    .Replace("Reason=", "原因=")
                    .Replace("TradeOfferId=", "报价号=")
                    .Replace("OrderNo=", "订单=");
            if (text.StartsWith("Steam auto trade failure. Reason=", StringComparison.OrdinalIgnoreCase))
                return "自动处理失败：" + text["Steam auto trade failure. Reason=".Length..];
            if (text.StartsWith("Matched mobile confirmation accepted.", StringComparison.OrdinalIgnoreCase))
                return text.Replace("Matched mobile confirmation accepted.", "已完成匹配的 Steam 手机确认。")
                    .Replace("TradeOfferId=", "报价号=")
                    .Replace("Source=", "来源=");
            if (text.StartsWith("Accept trade offer failed.", StringComparison.OrdinalIgnoreCase))
                return text.Replace("Accept trade offer failed.", "同意 Steam 报价失败。").Replace("TradeOfferId=", "报价号=");
            if (text.StartsWith("Auto accept trade offer failed.", StringComparison.OrdinalIgnoreCase))
                return text.Replace("Auto accept trade offer failed.", "自动接收 Steam 报价失败。").Replace("TradeOfferId=", "报价号=");
            if (text.StartsWith("Matched mobile confirmation failed.", StringComparison.OrdinalIgnoreCase))
                return text.Replace("Matched mobile confirmation failed.", "Steam 手机确认失败。").Replace("TradeOfferId=", "报价号=");
            if (text.StartsWith("Deny trade offer failed.", StringComparison.OrdinalIgnoreCase))
                return text.Replace("Deny trade offer failed.", "拒绝 Steam 报价失败。").Replace("TradeOfferId=", "报价号=");
            if (text.StartsWith("Load Steam offers", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("Load Steam confirmations", StringComparison.OrdinalIgnoreCase))
                return "刷新 Steam 报价失败：" + text;

            return text;
        }

        private static string ExtractTradeOfferId(string? message)
        {
            string text = message ?? "";
            const string key = "TradeOfferId=";
            int index = text.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return "";

            int start = index + key.Length;
            int end = text.IndexOfAny(new[] { ';', ' ', '\t', '\r', '\n' }, start);
            return end < 0 ? text[start..].Trim() : text[start..end].Trim();
        }

        private static string FormatBool(bool value) => value ? "是" : "否";
    }
}
