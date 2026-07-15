using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Helpers;
using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI
{
    internal static class SettingsFormFactoryReset
    {
        public const string WarningTitle = "恢复软件默认出厂设置";
        public const string SecondConfirmTitle = "确认恢复出厂设置";
        public const string FailureTitle = "重置失败";

        public const string WarningMessage = "【警告】确定要将软件设置恢复到默认出厂状态吗？\n\n" +
            "这将会发生以下变化：\n" +
            "1. 软件的所有配置（包括界面外观、数据源、涨跌提醒、显示项等）都将被完全清空并恢复为出厂默认。\n" +
            "2. 存放在设置文件中的 SteamDT API Key、CSQAQ API Token、Server酱 SendKey 等敏感凭据将被完全清空。\n" +
            "3. 此操作不会删除您的本地运行日志（CS2TradeMonitor_Error.log），也不会删除悠悠有品加密凭据（youpin_auth.dat）。\n\n" +
            "此操作不可逆！需要您的确认。";

        public const string SecondConfirmMessage = "【二次确认】您真的确定要恢复出厂设置并自动重启程序吗？\n\n点击“是”将立即开始重置，点击“否”则取消操作。";

        public static void Perform(IWin32Window? owner)
        {
            if (ShowConfirmation(owner, WarningMessage, WarningTitle) != DialogResult.Yes)
                return;

            if (ShowConfirmation(owner, SecondConfirmMessage, SecondConfirmTitle) != DialogResult.Yes)
                return;

            try
            {
                Settings.GlobalBlockSave = true;
                SettingsHelper.DeleteStoredSettings();
                SystemActions.RestartApplication();
            }
            catch (Exception ex)
            {
                Settings.GlobalBlockSave = false;
                DiagnosticsLogger.Error("Settings", "Factory reset failed.", ex);
                ShowFailure(owner, ex.Message);
            }
        }

        private static DialogResult ShowConfirmation(IWin32Window? owner, string message, string title)
        {
            return owner != null
                ? GlobalPromptService.Show(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                : GlobalPromptService.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        }

        private static void ShowFailure(IWin32Window? owner, string message)
        {
            if (owner != null)
                GlobalPromptService.Show(owner, message, FailureTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            else
                GlobalPromptService.Show(message, FailureTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
