using CS2TradeMonitor.Application.Steam;
using System;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal sealed class SteamOfferCredentialCoordinator
    {
        private readonly SteamOfferPageRuntimeServices _runtimeServices;
        private readonly Func<Form?> _findOwner;
        private readonly Action _queueSteamTimeSync;
        private readonly Action _refreshStatus;
        private readonly Action<bool, int> _queueOfferListRender;
        private readonly Func<SteamOfferCredentialPrompt, bool> _confirm;
        private readonly Action<string, SteamOfferOperationStatusTone> _setStatus;
        private readonly Func<SteamOfferActionResult> _clearTokenSecrets;
        private readonly Func<SteamOfferActionResult> _clearLoginState;
        private readonly Func<string, string, string, string, string, string, SteamOfferActionResult> _updateSession;
        private readonly SteamOfferPagePresenter? _presenter;
        private SteamAuthBindingDialog? _authDialog;
        private SteamWebLoginDialog? _webLoginDialog;

        public SteamOfferCredentialCoordinator(
            SteamOfferPageRuntimeServices runtimeServices,
            Func<Form?> findOwner,
            Action queueSteamTimeSync,
            Action refreshStatus,
            Action<bool, int> queueOfferListRender,
            Func<SteamOfferCredentialPrompt, bool> confirm,
            Action<string, SteamOfferOperationStatusTone> setStatus,
            Func<SteamOfferActionResult> clearTokenSecrets,
            Func<SteamOfferActionResult> clearLoginState,
            Func<string, string, string, string, string, string, SteamOfferActionResult> updateSession,
            SteamOfferPagePresenter? presenter = null)
        {
            _runtimeServices = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            _findOwner = findOwner ?? throw new ArgumentNullException(nameof(findOwner));
            _queueSteamTimeSync = queueSteamTimeSync ?? throw new ArgumentNullException(nameof(queueSteamTimeSync));
            _refreshStatus = refreshStatus ?? throw new ArgumentNullException(nameof(refreshStatus));
            _queueOfferListRender = queueOfferListRender ?? throw new ArgumentNullException(nameof(queueOfferListRender));
            _confirm = confirm ?? throw new ArgumentNullException(nameof(confirm));
            _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
            _clearTokenSecrets = clearTokenSecrets ?? throw new ArgumentNullException(nameof(clearTokenSecrets));
            _clearLoginState = clearLoginState ?? throw new ArgumentNullException(nameof(clearLoginState));
            _updateSession = updateSession ?? throw new ArgumentNullException(nameof(updateSession));
            _presenter = presenter;
        }

        public void ShowAuthDialog()
        {
            if (_authDialog != null && !_authDialog.IsDisposed)
            {
                _authDialog.Activate();
                return;
            }

            _queueSteamTimeSync();
            var dialog = new SteamAuthBindingDialog(_runtimeServices, _presenter);
            _authDialog = dialog;
            dialog.OpenWebLoginRequested += (_, __) => OpenSteamLogin();
            dialog.StatusChanged += (_, __) => _refreshStatus();
            dialog.AuthChanged += (_, __) =>
            {
                _refreshStatus();
                _queueOfferListRender(true, 1);
            };
            dialog.FormClosed += (_, __) =>
            {
                if (ReferenceEquals(_authDialog, dialog))
                    _authDialog = null;
                dialog.Dispose();
            };
            dialog.Show(_findOwner());
        }

        public void ClearTokenSecrets()
        {
            if (!_confirm(SteamOfferCredentialPrompt.ClearTokenSecrets))
                return;

            SteamOfferActionResult result = _clearTokenSecrets();
            SetResultStatus(result);
            _refreshStatus();
            _queueOfferListRender(true, 1);
        }

        public void ClearLoginState()
        {
            if (!_confirm(SteamOfferCredentialPrompt.ClearLoginState))
                return;

            SteamOfferActionResult result = _clearLoginState();
            SetResultStatus(result);
            _refreshStatus();
            _queueOfferListRender(true, 1);
        }

        public void OpenSteamLogin()
        {
            try
            {
                if (_webLoginDialog != null && !_webLoginDialog.IsDisposed)
                {
                    _webLoginDialog.Activate();
                    return;
                }

                var dialog = new SteamWebLoginDialog();
                _webLoginDialog = dialog;
                dialog.LoginStateCaptured += (_, args) => ApplyCapturedLoginState(args);
                dialog.FormClosed += (_, __) =>
                {
                    if (ReferenceEquals(_webLoginDialog, dialog))
                        _webLoginDialog = null;
                    dialog.Dispose();
                };
                dialog.Show(_findOwner());
            }
            catch (Exception ex)
            {
                _setStatus("打开 Steam 登录页失败：" + ex.Message, SteamOfferOperationStatusTone.Warning);
            }
        }

        private void ApplyCapturedLoginState(SteamLoginStateCapturedEventArgs args)
        {
            SteamOfferActionResult result = _updateSession(
                args.SessionId,
                args.SteamLoginSecure,
                args.SteamLogin,
                args.AccessToken,
                args.RefreshToken,
                args.SteamId);
            SetResultStatus(result);
            _authDialog?.RefreshStatus(result.Message, result.Ok);
            if (result.Ok)
                _refreshStatus();
        }

        private void SetResultStatus(SteamOfferActionResult result)
        {
            _setStatus(
                result.Message,
                result.Ok ? SteamOfferOperationStatusTone.Success : SteamOfferOperationStatusTone.Warning);
        }
    }

    internal sealed record SteamOfferCredentialPrompt(string Title, string Message)
    {
        public static SteamOfferCredentialPrompt ClearTokenSecrets { get; } = new(
            "清空 Steam 令牌",
            "确认只清空 shared_secret / identity_secret 吗？\n\nSteam 登录状态会保留，但验证码和移动确认需要重新保存令牌密钥。");

        public static SteamOfferCredentialPrompt ClearLoginState { get; } = new(
            "清空 Steam 登录状态",
            "确认只清空 Steam 登录状态吗？\n\n令牌密钥会保留；报价列表会清空，需要时可重新网页登录或用 Token 恢复。");
    }
}
