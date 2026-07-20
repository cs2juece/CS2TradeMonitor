using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.SystemServices
{
    internal static class SoftwareUpdateStartup
    {
        private const string UpdateCompletedArgument = "--updated";
        private const string UpdateTransactionPrefix = "--update-transaction=";

        public static bool IsUpdateCompleted(IEnumerable<string>? args)
        {
            return args?.Any(arg =>
                string.Equals(arg, UpdateCompletedArgument, StringComparison.OrdinalIgnoreCase)) == true;
        }

        public static string BuildSuccessMessage(string? currentVersion)
        {
            string version = (currentVersion ?? string.Empty).Split('+')[0].Trim().TrimStart('v', 'V');
            return string.IsNullOrWhiteSpace(version)
                ? "更新已成功完成，主窗口已打开。"
                : $"已成功更新到 v{version}，主窗口已打开。";
        }

        public static string? GetTransactionId(IEnumerable<string>? args)
        {
            string? value = args?
                .FirstOrDefault(arg => arg.StartsWith(UpdateTransactionPrefix, StringComparison.OrdinalIgnoreCase))?
                [UpdateTransactionPrefix.Length..]
                .Trim()
                .ToLowerInvariant();
            return value?.Length == 32 && value.All(Uri.IsHexDigit) ? value : null;
        }
    }
}
