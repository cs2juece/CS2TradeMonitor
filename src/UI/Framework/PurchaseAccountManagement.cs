using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class PurchaseAccountManagement : UserControl
{
    private readonly PurchaseAccounts? _accounts;
    private readonly Label _current = PurchaseMonitorUi.Text(role: "sub");
    private readonly Label _independent = PurchaseMonitorUi.Text(role: "sub");

    public PurchaseAccountManagement(PurchaseAccounts? accounts)
    {
        _accounts = accounts;
        Margin = Padding.Empty;
        BackColor = UIColors.CardBg;
        Tag = "surface";
        var currentLogin = PurchaseMonitorUi.Button("管理当前账号", () => OpenLogin(PurchaseAccountSource.CurrentYouPin));
        var independentLogin = PurchaseMonitorUi.Button("登录独立账号", () => OpenLogin(PurchaseAccountSource.Independent));
        currentLogin.Enabled = independentLogin.Enabled = accounts is not null;
        Controls.Add(PurchaseMonitorUi.Stack(
            (PurchaseMonitorUi.Text("读取账号", 14, true), 36),
            (PurchaseMonitorUi.Text("先在这里登录账号，再添加店铺。每家店铺由你选择使用哪个账号。", role: "sub"), 40),
            (PurchaseMonitorUi.Text("悠悠有品当前账号", 11, true), 30),
            (_current, 42), (PurchaseMonitorUi.Actions(currentLogin), 44),
            (PurchaseMonitorUi.Text("独立监控账号", 11, true), 30),
            (_independent, 42), (PurchaseMonitorUi.Actions(independentLogin), 44)));
        RefreshAccounts();
    }

    public void RefreshAccounts()
    {
        _current.Text = Describe(PurchaseAccountSource.CurrentYouPin, "与悠悠有品模块共用登录状态");
        _independent.Text = Describe(PurchaseAccountSource.Independent, "单独登录，仅用于求购读取");
    }

    private string Describe(PurchaseAccountSource source, string usage)
    {
        if (_accounts is null) return "账号服务不可用";
        var state = _accounts.Auth(source).GetState();
        string status = !string.IsNullOrWhiteSpace(state.Error) ? "登录异常，请验证或重新登录"
            : state.HasCredential ? $"{state.NickName} · 凭据已保存" : "未登录";
        return $"{status} · {usage}";
    }

    private void OpenLogin(PurchaseAccountSource source)
    {
        if (_accounts is null) return;
        using var dialog = new PurchaseLoginDialog(_accounts, source);
        dialog.ShowDialog(FindForm());
        if (!IsDisposed) RefreshAccounts();
    }
}
