using CS2TradeMonitor.Application.Steam;
using CS2TradeMonitor.Application.Steam.Auth;
using System;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal static class SteamOfferStatusHeaderModel
    {
        public static SteamOfferStatusHeaderViewModel Build(
            SteamAuthStoreStatus authStatus,
            SteamAutoConfirmState autoConfirm)
        {
            ArgumentNullException.ThrowIfNull(authStatus);
            ArgumentNullException.ThrowIfNull(autoConfirm);

            SteamOfferStatusValueViewModel autoConfirmValue = BuildAutoConfirmValue(autoConfirm);
            if (!authStatus.HasCredential)
            {
                return new SteamOfferStatusHeaderViewModel(
                    new SteamOfferStatusValueViewModel("未绑定", SteamOfferStatusTone.Warning),
                    new SteamOfferStatusValueViewModel("未登录", SteamOfferStatusTone.Warning),
                    new SteamOfferStatusValueViewModel("未创建", SteamOfferStatusTone.Warning),
                    autoConfirmValue);
            }

            return new SteamOfferStatusHeaderViewModel(
                new SteamOfferStatusValueViewModel("已绑定", SteamOfferStatusTone.Success),
                new SteamOfferStatusValueViewModel(
                    authStatus.HasSession ? "已保存" : "未登录",
                    authStatus.HasSession ? SteamOfferStatusTone.Success : SteamOfferStatusTone.Warning),
                new SteamOfferStatusValueViewModel("DPAPI", SteamOfferStatusTone.Muted),
                autoConfirmValue);
        }

        public static SteamOfferStatusValueViewModel BuildAutoConfirmValue(SteamAutoConfirmState autoConfirm)
        {
            ArgumentNullException.ThrowIfNull(autoConfirm);

            return autoConfirm.IsRunning
                ? new SteamOfferStatusValueViewModel("运行中", SteamOfferStatusTone.Success)
                : new SteamOfferStatusValueViewModel("已停止", SteamOfferStatusTone.Muted);
        }
    }

    internal sealed record SteamOfferStatusHeaderViewModel(
        SteamOfferStatusValueViewModel Token,
        SteamOfferStatusValueViewModel Session,
        SteamOfferStatusValueViewModel Encryption,
        SteamOfferStatusValueViewModel AutoConfirm);

    internal sealed record SteamOfferStatusValueViewModel(
        string Text,
        SteamOfferStatusTone Tone);

    internal enum SteamOfferStatusTone
    {
        Success,
        Warning,
        Muted
    }
}
