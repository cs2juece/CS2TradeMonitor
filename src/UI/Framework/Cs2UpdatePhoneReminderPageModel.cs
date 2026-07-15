using CS2TradeMonitor.Application.Notify;
using CS2TradeMonitor.src.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal enum Cs2UpdatePhoneReminderTone
    {
        Subtle,
        Primary,
        Positive,
        Warning
    }

    internal sealed record Cs2UpdatePhoneReminderText(string Text, Cs2UpdatePhoneReminderTone Tone);

    internal sealed record Cs2UpdatePhoneReminderStatusBlock(
        string Caption,
        Cs2UpdatePhoneReminderText Value,
        string Detail);

    internal sealed record Cs2UpdatePhoneReminderViewModel(
        Cs2UpdatePhoneReminderText Cs2Pill,
        Cs2UpdatePhoneReminderText PhonePill,
        Cs2UpdatePhoneReminderStatusBlock CurrentStatus,
        Cs2UpdatePhoneReminderStatusBlock LastCheck,
        Cs2UpdatePhoneReminderStatusBlock PhoneChannel,
        Cs2UpdatePhoneReminderStatusBlock BackupEntry,
        string BaselineTitle,
        string BaselineTime,
        string LatestTitle,
        string LatestTime,
        string ReadStrategy,
        string SendHealth,
        Cs2UpdatePhoneReminderTone SendHealthTone,
        int EnabledBackupCount,
        int TotalBackupCount);

    internal readonly record struct Cs2UpdatePhoneReminderLayout(
        bool TwoColumn,
        Rectangle Header,
        Rectangle Overview,
        Rectangle UpdateCard,
        Rectangle PhoneCard,
        Rectangle FlowCard,
        Rectangle HistoryCard,
        Rectangle BackupCard,
        int TotalHeight);

    internal static class Cs2UpdatePhoneReminderPageModel
    {
        private const int TotalBackupChannels = 6;

        public static Cs2UpdatePhoneReminderViewModel BuildView(
            Settings cfg,
            Cs2UpdateCheckResult result,
            IReadOnlyList<Cs2UpdateLogItem> recentItems,
            PhoneAlertChannelConfig server,
            bool serverConfigured,
            string maskedSecret)
        {
            ArgumentNullException.ThrowIfNull(cfg);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(recentItems);
            ArgumentNullException.ThrowIfNull(server);

            bool updateEnabled = cfg.Cs2UpdateReminderEnabled;
            bool phoneEnabled = cfg.PhoneAlertEnabled && server.Enabled;
            string lastStatus = string.IsNullOrWhiteSpace(cfg.Cs2UpdateLastStatus) ? "未检查" : cfg.Cs2UpdateLastStatus.Trim();
            string failure = BuildFailureReason(lastStatus, result);
            bool failed = updateEnabled && !string.IsNullOrWhiteSpace(failure);
            bool hasNewUpdate = updateEnabled
                && !failed
                && (result.HasNewUpdate || LooksLikeNewUpdateStatus(lastStatus));

            Cs2UpdatePhoneReminderText currentValue = new(
                BuildCurrentStatus(updateEnabled, lastStatus, hasNewUpdate, failed),
                ResolveCurrentStatusTone(updateEnabled, hasNewUpdate, failed, lastStatus));

            Cs2UpdatePhoneReminderText cs2Pill = new(
                updateEnabled ? "CS2 更新提醒已启用" : "CS2 更新提醒已关闭",
                updateEnabled ? Cs2UpdatePhoneReminderTone.Positive : Cs2UpdatePhoneReminderTone.Subtle);

            Cs2UpdatePhoneReminderText phonePill = new(
                serverConfigured && phoneEnabled ? "手机提醒已就绪" : "手机提醒未绑定",
                serverConfigured && phoneEnabled ? Cs2UpdatePhoneReminderTone.Positive : Cs2UpdatePhoneReminderTone.Warning);

            string lastCheckText = Cs2UpdateReminderService.FormatTime(cfg.Cs2UpdateLastCheckTime);
            string phoneMain = serverConfigured
                ? (phoneEnabled ? "Server 酱已绑定" : "Server 酱未启用")
                : "Server 酱未绑定";
            string phoneDetail = serverConfigured
                ? (phoneEnabled ? $"已隐藏显示：{maskedSecret}" : "已填写 SendKey，手机提醒开关未启用")
                : "手机提醒暂不可用";
            int enabledBackup = PhoneAlertChannelCatalog.CountEnabledBackupChannels(cfg.PhoneAlertChannels ?? Enumerable.Empty<PhoneAlertChannelConfig>());

            var latest = result.Latest ?? recentItems.FirstOrDefault();
            string baselineTitle = string.IsNullOrWhiteSpace(cfg.Cs2UpdateBaselineTitle)
                ? "暂无基准"
                : cfg.Cs2UpdateBaselineTitle.Trim();
            string baselineTime = Cs2UpdateReminderService.FormatTime(cfg.Cs2UpdateBaselinePublishedAt);
            string latestTitle;
            string latestTime;
            if (latest != null)
            {
                latestTitle = string.IsNullOrWhiteSpace(latest.Title) ? "CS2 更新记录" : latest.Title.Trim();
                latestTime = Cs2UpdateReminderService.FormatTime(latest.PublishedAt);
            }
            else if (!string.IsNullOrWhiteSpace(cfg.Cs2UpdateBaselineTitle))
            {
                latestTitle = cfg.Cs2UpdateBaselineTitle.Trim();
                latestTime = baselineTime;
            }
            else
            {
                latestTitle = "暂无数据";
                latestTime = "";
            }

            return new Cs2UpdatePhoneReminderViewModel(
                cs2Pill,
                phonePill,
                new Cs2UpdatePhoneReminderStatusBlock(
                    "当前状态",
                    currentValue,
                    failed ? "检查失败，请先重试" : (updateEnabled ? "当前提醒正常" : "更新提醒已关闭")),
                new Cs2UpdatePhoneReminderStatusBlock(
                    "最近检查",
                    new Cs2UpdatePhoneReminderText(lastCheckText, cfg.Cs2UpdateLastCheckTime > 0 ? Cs2UpdatePhoneReminderTone.Primary : Cs2UpdatePhoneReminderTone.Subtle),
                    $"检测间隔 {NormalizeInterval(cfg.Cs2UpdateReminderRefreshSec)} 秒"),
                new Cs2UpdatePhoneReminderStatusBlock(
                    "手机通道",
                    new Cs2UpdatePhoneReminderText(phoneMain, serverConfigured && phoneEnabled ? Cs2UpdatePhoneReminderTone.Positive : Cs2UpdatePhoneReminderTone.Warning),
                    phoneDetail),
                new Cs2UpdatePhoneReminderStatusBlock(
                    "备用入口",
                    new Cs2UpdatePhoneReminderText($"{enabledBackup} / {TotalBackupChannels} 启用", enabledBackup > 0 ? Cs2UpdatePhoneReminderTone.Primary : Cs2UpdatePhoneReminderTone.Subtle),
                    "点击后进入管理"),
                baselineTitle,
                baselineTime,
                latestTitle,
                latestTime,
                "只读取公开更新记录；首次启用或重置基准时不弹出提醒。",
                BuildSendHealth(serverConfigured, phoneEnabled, server.LastTestResult),
                ResolveSendHealthTone(serverConfigured, phoneEnabled, server.LastTestResult),
                enabledBackup,
                TotalBackupChannels);
        }

        public static Cs2UpdatePhoneReminderLayout BuildLayout(int contentWidth)
        {
            int width = Math.Max(1, contentWidth);
            int gap = UIUtils.S(22);
            int y = 0;

            var header = new Rectangle(0, y, width, UIUtils.S(70));
            y = header.Bottom + UIUtils.S(14);
            var overview = new Rectangle(0, y, width, width < UIUtils.S(760) ? UIUtils.S(192) : UIUtils.S(106));
            y = overview.Bottom + gap;

            bool twoColumn = width >= UIUtils.S(900);
            Rectangle updateCard;
            Rectangle phoneCard;
            if (twoColumn)
            {
                int cardW = (width - gap) / 2;
                updateCard = new Rectangle(0, y, cardW, UIUtils.S(278));
                phoneCard = new Rectangle(updateCard.Right + gap, y, width - cardW - gap, UIUtils.S(278));
                y = Math.Max(updateCard.Bottom, phoneCard.Bottom) + gap;
            }
            else
            {
                updateCard = new Rectangle(0, y, width, UIUtils.S(286));
                y = updateCard.Bottom + gap;
                phoneCard = new Rectangle(0, y, width, UIUtils.S(278));
                y = phoneCard.Bottom + gap;
            }

            var flow = new Rectangle(0, y, width, width < UIUtils.S(760) ? UIUtils.S(126) : UIUtils.S(74));
            y = flow.Bottom + gap;

            Rectangle history;
            Rectangle backup;
            if (twoColumn)
            {
                int backupW = Math.Min(UIUtils.S(390), Math.Max(UIUtils.S(320), width / 3));
                history = new Rectangle(0, y, width - backupW - gap, UIUtils.S(236));
                backup = new Rectangle(history.Right + gap, y, backupW, UIUtils.S(236));
                y = Math.Max(history.Bottom, backup.Bottom);
            }
            else
            {
                history = new Rectangle(0, y, width, UIUtils.S(236));
                y = history.Bottom + gap;
                backup = new Rectangle(0, y, width, UIUtils.S(214));
                y = backup.Bottom;
            }

            return new Cs2UpdatePhoneReminderLayout(
                twoColumn,
                header,
                overview,
                updateCard,
                phoneCard,
                flow,
                history,
                backup,
                y + UIUtils.S(24));
        }

        public static Color ResolveToneColor(Cs2UpdatePhoneReminderTone tone)
        {
            return tone switch
            {
                Cs2UpdatePhoneReminderTone.Positive => UIColors.Positive,
                Cs2UpdatePhoneReminderTone.Warning => UIColors.TextWarn,
                Cs2UpdatePhoneReminderTone.Primary => UIColors.Primary,
                _ => UIColors.TextSub
            };
        }

        public static int NormalizeInterval(int seconds)
        {
            return Math.Clamp(seconds <= 0 ? 600 : seconds, 60, 86400);
        }

        private static string BuildCurrentStatus(bool enabled, string lastStatus, bool hasNewUpdate, bool failed)
        {
            if (!enabled)
                return "已关闭";
            if (failed)
                return "检查失败";
            if (hasNewUpdate)
                return string.IsNullOrWhiteSpace(lastStatus) ? "CS2 已有最新更新" : lastStatus;
            if (string.IsNullOrWhiteSpace(lastStatus) || string.Equals(lastStatus, "未检查", StringComparison.OrdinalIgnoreCase))
                return "等待首次检查";

            return lastStatus;
        }

        private static Cs2UpdatePhoneReminderTone ResolveCurrentStatusTone(
            bool enabled,
            bool hasNewUpdate,
            bool failed,
            string lastStatus)
        {
            if (!enabled)
                return Cs2UpdatePhoneReminderTone.Subtle;
            if (failed || hasNewUpdate)
                return Cs2UpdatePhoneReminderTone.Warning;
            if (string.IsNullOrWhiteSpace(lastStatus) || string.Equals(lastStatus, "未检查", StringComparison.OrdinalIgnoreCase))
                return Cs2UpdatePhoneReminderTone.Subtle;

            return Cs2UpdatePhoneReminderTone.Positive;
        }

        private static string BuildFailureReason(string lastStatus, Cs2UpdateCheckResult result)
        {
            if (result.CheckedAt != DateTime.MinValue && !result.Success && !IsNotChecked(result.Message))
                return NormalizeFailure(result.Message);

            if (!IsNotChecked(lastStatus) && LooksLikeFailure(lastStatus))
                return NormalizeFailure(lastStatus);

            return "";
        }

        private static bool LooksLikeFailure(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || text.Contains("不可用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("异常", StringComparison.OrdinalIgnoreCase)
                || text.Contains("HTTP", StringComparison.OrdinalIgnoreCase)
                || text.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || text.Contains("timed out", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeNewUpdateStatus(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("发现", StringComparison.OrdinalIgnoreCase)
                || text.Contains("已有最新更新", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNotChecked(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                || string.Equals(text.Trim(), "未检查", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFailure(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "未知错误";

            text = text.Trim();
            const int maxLen = 120;
            return text.Length > maxLen ? text[..maxLen] + "..." : text;
        }

        private static string BuildSendHealth(bool serverConfigured, bool phoneEnabled, string lastTestResult)
        {
            if (!serverConfigured)
                return "未绑定，手机提醒暂不可用";
            if (!phoneEnabled)
                return "已填写 SendKey，手机提醒未启用";
            if (!string.IsNullOrWhiteSpace(lastTestResult) && !string.Equals(lastTestResult, "测试成功", StringComparison.OrdinalIgnoreCase))
                return lastTestResult;

            return "已就绪，主通道可用于推送";
        }

        private static Cs2UpdatePhoneReminderTone ResolveSendHealthTone(bool serverConfigured, bool phoneEnabled, string lastTestResult)
        {
            if (!serverConfigured || !phoneEnabled)
                return Cs2UpdatePhoneReminderTone.Warning;
            if (!string.IsNullOrWhiteSpace(lastTestResult) && !string.Equals(lastTestResult, "测试成功", StringComparison.OrdinalIgnoreCase))
                return Cs2UpdatePhoneReminderTone.Warning;

            return Cs2UpdatePhoneReminderTone.Positive;
        }
    }
}
