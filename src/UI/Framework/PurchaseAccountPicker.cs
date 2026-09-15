using System.Drawing;
using CS2TradeMonitor.Application.YouPin;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework;

internal sealed class PurchaseAccountPicker : UserControl
{
    private readonly PurchaseAccounts? _accounts;
    private readonly LiteComboBox _source = PurchaseMonitorUi.Choice("使用悠悠有品当前账号", "使用独立监控账号");
    private readonly Label _status = PurchaseMonitorUi.Text(role: "sub");
    public event Action? SelectionChanged;
    public PurchaseAccountSource Source => _source.SelectedIndex switch
    { 0 => PurchaseAccountSource.CurrentYouPin, 1 => PurchaseAccountSource.Independent, _ => PurchaseAccountSource.Unselected };

    public PurchaseAccountPicker(PurchaseAccounts? accounts, PurchaseAccountSource selected = PurchaseAccountSource.Unselected)
    {
        _accounts = accounts;
        _source.SelectedIndex = selected switch { PurchaseAccountSource.CurrentYouPin => 0, PurchaseAccountSource.Independent => 1, _ => -1 };
        Controls.Add(PurchaseMonitorUi.Stack((_source, 40), (_status, 44),
            (PurchaseMonitorUi.Text("账号登录与管理：监控设置 → 读取账号", role: "sub"), 40)));
        _source.Inner.SelectedIndexChanged += (_, _) => { RefreshStatus(); SelectionChanged?.Invoke(); };
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _status.Text = _accounts?.Describe(Source) ?? "读取账号服务不可用";
    }
}

internal sealed class PurchaseLoginDialog : Form
{
    private readonly LiteTextBox _phone = PurchaseMonitorUi.Input("手机号");
    private readonly LiteTextBox _code = PurchaseMonitorUi.Input("短信验证码");
    private readonly Label _result = PurchaseMonitorUi.Text(role: "sub");
    private readonly LiteButton _send;
    private readonly LiteButton _login;
    private readonly LiteButton _validate;
    private readonly PurchaseAccounts _accounts;
    private readonly PurchaseAccountSource _source;
    private string _session = "";
    private string _sentPhone = "";
    private DateTimeOffset _nextSms;
    private bool _smsUp;
    private bool _busy;

    public PurchaseLoginDialog(PurchaseAccounts accounts, PurchaseAccountSource source)
    {
        _accounts = accounts;
        _source = source;
        Text = source == PurchaseAccountSource.Independent ? "独立监控账号登录" : "悠悠有品当前账号登录";
        ClientSize = new Size(UIUtils.S(550), UIUtils.S(410));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = UIColors.MainBg;
        Padding = new Padding(UIUtils.S(20));
        _send = PurchaseMonitorUi.Button("发送验证码", async () => await ExecuteAsync(async () =>
        {
            if (DateTimeOffset.UtcNow < _nextSms) { _result.Text = "请等待60秒后再发送验证码。"; return; }
            _nextSms = DateTimeOffset.UtcNow.AddSeconds(60);
            _session = "";
            _sentPhone = _phone.Text.Trim();
            YouPinSmsSendResult response = await accounts.Auth(source).SendSmsCodeAsync(_sentPhone);
            _smsUp = response.Ok && response.NeedSmsUp;
            if (response.Ok) _session = response.SessionId;
            _result.Text = _smsUp ? $"请用该手机号发送 {response.SmsUpContent} 到 {response.SmsUpNumber}，发送后点击登录。" : response.Message;
        }));
        _login = PurchaseMonitorUi.Button("登录", async () => await ExecuteAsync(async () =>
        {
            if (_session.Length == 0 || _sentPhone != _phone.Text.Trim()) { _result.Text = "请先为该手机号发送验证码。"; return; }
            if (!_smsUp && string.IsNullOrWhiteSpace(_code.Text)) { _result.Text = "请输入短信验证码。"; return; }
            YouPinLoginResult response = await accounts.Auth(source).CompleteSmsLoginAsync(_sentPhone, _smsUp ? "" : _code.Text.Trim(), _session);
            _code.Clear();
            _result.Text = response.Ok ? "登录成功，关闭窗口后保存店铺的账号选择。" : response.Message;
        }), true);
        _validate = PurchaseMonitorUi.Button("验证已保存登录", async () => await ExecuteAsync(async () =>
        {
            YouPinLoginResult response = await accounts.Auth(source).ValidateCurrentAsync();
            _result.Text = response.Ok ? "登录验证成功；求购是否可读需以实际扫描为准。" : response.Message;
        }));
        Controls.Add(PurchaseMonitorUi.Stack(
            (PurchaseMonitorUi.Text(Text, 16, true), 40),
            (PurchaseMonitorUi.Text(source == PurchaseAccountSource.Independent ? "加密独立保存，仅用于求购读取。" : "此处使用悠悠有品模块的同一份登录状态。", role: "sub"), 42),
            (_phone, 40), (_code, 40), (PurchaseMonitorUi.Actions(_send, _login), 44),
            (_result, 92), (PurchaseMonitorUi.Actions(_validate, PurchaseMonitorUi.Button("关闭", Close)), 44)));
        FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        _send.Enabled = _login.Enabled = _validate.Enabled = _phone.Enabled = _code.Enabled = false;
        try { await action(); }
        catch (Exception) { _result.Text = "登录操作失败，请稍后手动重试。"; }
        finally
        {
            _busy = false;
            if (!IsDisposed) _send.Enabled = _login.Enabled = _validate.Enabled = _phone.Enabled = _code.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _phone.Clear(); _code.Clear(); _session = _sentPhone = ""; }
        base.Dispose(disposing);
    }
}
