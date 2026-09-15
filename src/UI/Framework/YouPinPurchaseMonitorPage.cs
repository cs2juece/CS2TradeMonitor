using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring;
using CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.Links;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using YouPinPurchaseMonitor.Models;

namespace CS2TradeMonitor.src.UI.Framework;

public sealed class YouPinPurchaseMonitorHostPage : SettingsPageBase, ISettingsSubRouteHost
{
    private readonly IYouPinPurchaseMonitoringModule _module;
    private readonly PageHost _contentHost;
    private readonly Panel _tabWrapper;
    private readonly FrameworkTopTabHeader<YouPinPurchaseMonitorTab> _tabHeader;
    private SettingsTransaction? _settingsTransaction;
    private bool _hostAttached;
    private YouPinPurchaseMonitorTab _activeTab = YouPinPurchaseMonitorTab.Stores;
    private readonly Dictionary<YouPinPurchaseMonitorTab, IUiPage> _pages = [];
    private (Guid StoreId, string View)? _pendingStoreRoute;

    public YouPinPurchaseMonitorHostPage()
        : this(YouPinPurchaseMonitorPageRuntimeServices.Resolve())
    {
    }

    internal YouPinPurchaseMonitorHostPage(YouPinPurchaseMonitorPageRuntimeServices services)
    {
        _module = (services ?? throw new ArgumentNullException(nameof(services))).Module;
        BackColor = UIColors.MainBg;
        _contentHost = new PageHost { Dock = DockStyle.Fill, BackColor = UIColors.MainBg };
        _tabHeader = new FrameworkTopTabHeader<YouPinPurchaseMonitorTab>(
            [
                new(YouPinPurchaseMonitorTab.Stores, "店铺动态", 110),
                new(YouPinPurchaseMonitorTab.History, "变化记录", 110),
                new(YouPinPurchaseMonitorTab.Status, "扫描状态", 110),
                new(YouPinPurchaseMonitorTab.Settings, "监控设置", 110)
            ],
            _activeTab,
            "求购监控");
        _tabHeader.TabSelected += SwitchTab;
        _tabWrapper = new Panel
        {
            Dock = DockStyle.Top,
            Height = UIUtils.S(52),
            Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding(bottomPadding: 0),
            BackColor = UIColors.MainBg
        };
        _tabWrapper.Controls.Add(_tabHeader);
        _tabWrapper.Layout += (_, _) => _tabHeader.SetBounds(
            _tabWrapper.Padding.Left,
            0,
            Math.Max(1, _tabWrapper.ClientSize.Width - _tabWrapper.Padding.Horizontal),
            UIUtils.S(40));
        Controls.Add(_contentHost);
        Controls.Add(_tabWrapper);
    }

    public override void OnShow()
    {
        base.OnShow();
        if (!_hostAttached)
        {
            _settingsTransaction = new SettingsTransaction(() => Config);
            _contentHost.AttachSettings(_settingsTransaction.Draft);
            _hostAttached = true;
        }
        ShowActivePage();
    }

    public override void OnHide()
    {
        _contentHost.SaveCurrentPage();
        _settingsTransaction?.Commit();
        _contentHost.CurrentPage?.Deactivate();
        base.OnHide();
    }

    public override void Save()
    {
        _contentHost.SaveCurrentPage();
        _settingsTransaction?.Commit();
    }

    public bool SwitchSubRoute(string subRoute)
    {
        string[] parts = subRoute.Split('/');
        if (parts.Length == 3 && parts[0].Equals("store", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[1], out Guid storeId) && storeId != Guid.Empty
            && parts[2] is "activity" or "purchases" or "rules")
        {
            _pendingStoreRoute = (storeId, parts[2]);
            SwitchTab(YouPinPurchaseMonitorTab.Stores);
            return true;
        }
        if (!TryParseTab(subRoute, out YouPinPurchaseMonitorTab tab))
            return false;
        SwitchTab(tab);
        return true;
    }

    public override void OnThemeChanged()
    {
        base.OnThemeChanged();
        BackColor = UIColors.MainBg;
        _tabWrapper.BackColor = UIColors.MainBg;
        _contentHost.BackColor = UIColors.MainBg;
        _tabHeader.RefreshTheme();
        if (_contentHost.CurrentPage is FrameworkSettingsPageBase page)
            page.ApplySystemTheme();
    }

    public override void RequestViewportRelayout()
    {
        base.RequestViewportRelayout();
        _contentHost.Bounds = new Rectangle(
            0,
            _tabWrapper.Bottom,
            ClientSize.Width,
            Math.Max(1, ClientSize.Height - _tabWrapper.Bottom));
        _contentHost.RequestCurrentPageRelayout();
    }

    private void SwitchTab(YouPinPurchaseMonitorTab tab)
    {
        _activeTab = tab;
        _tabHeader.SetActiveTab(tab);
        ShowActivePage();
    }

    private void ShowActivePage()
    {
        if (!_hostAttached)
            return;
        if (!_pages.TryGetValue(_activeTab, out IUiPage? page))
        {
            page = _activeTab switch
            {
                YouPinPurchaseMonitorTab.History => new YouPinPurchaseHistoryPage(_module),
                YouPinPurchaseMonitorTab.Status => new YouPinPurchaseStatusPage(_module),
                YouPinPurchaseMonitorTab.Settings => new YouPinPurchaseSettingsPage(_module),
                _ => new YouPinPurchaseStoresPage(_module, () => SwitchTab(YouPinPurchaseMonitorTab.Status),
                    OpenAccountSettings)
            };
            _pages.Add(_activeTab, page);
        }
        if (ReferenceEquals(_contentHost.CurrentPage, page))
            page.Activate();
        else
            _contentHost.ShowPage(page);
        if (page is YouPinPurchaseStoresPage stores && _pendingStoreRoute is { } route)
        {
            _pendingStoreRoute = null;
            stores.OpenStore(route.StoreId, route.View);
        }
    }

    private void OpenAccountSettings()
    {
        SwitchTab(YouPinPurchaseMonitorTab.Settings);
        if (_contentHost.CurrentPage is YouPinPurchaseSettingsPage settings)
            settings.ShowAccounts();
    }

    private static bool TryParseTab(string value, out YouPinPurchaseMonitorTab tab)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (Enum.TryParse(normalized, ignoreCase: true, out tab))
            return true;
        tab = normalized.ToLowerInvariant() switch
        {
            "开始扫描" or "scan" or "扫描状态" => YouPinPurchaseMonitorTab.Status,
            "求购结果" or "results" or "店铺观察" or "watches" or "店铺动态" => YouPinPurchaseMonitorTab.Stores,
            "求购变化" or "changes" or "变化记录" => YouPinPurchaseMonitorTab.History,
            "数据与安全" or "security" or "监控设置" => YouPinPurchaseMonitorTab.Settings,
            _ => (YouPinPurchaseMonitorTab)(-1)
        };
        return Enum.IsDefined(tab);
    }
}

internal enum YouPinPurchaseMonitorTab
{
    Stores,
    History,
    Status,
    Settings
}

internal abstract class YouPinPurchaseTabPage : FrameworkSettingsPageBase
{
    protected YouPinPurchaseTabPage(IYouPinPurchaseMonitoringModule module)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        Module.StateChanged += OnModuleStateChanged;
        Container.AutoScroll = false;
        Container.Padding = new Padding(UIUtils.S(24), UIUtils.S(12), UIUtils.S(24), UIUtils.S(16));
    }

    private bool _active;
    private int _queued;
    protected bool CommandBusy { get; private set; }

    protected IYouPinPurchaseMonitoringModule Module { get; }

    public override void Activate()
    {
        base.Activate();
        _active = true;
        RefreshSnapshot(Module.GetSnapshot());
    }

    protected abstract void RefreshSnapshot(YouPinPurchaseMonitoringSnapshot snapshot);

    protected static TextBox CreateTextBox(bool multiline = false, bool readOnly = false)
    {
        var textBox = new TextBox
        {
            Multiline = multiline,
            ReadOnly = readOnly,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            BackColor = UIColors.InputBg,
            ForeColor = UIColors.TextMain,
            Font = new Font("Microsoft YaHei UI", 9F),
            WordWrap = true
        };
        return textBox;
    }

    protected static LiteComboBox CreateScopeCombo()
    {
        var combo = new LiteComboBox();
        combo.Items.AddRange(["热门 Top 100", "热门 Top 300", "热门 Top 1000"]);
        combo.SelectedIndex = 0;
        return combo;
    }

    protected static YouPinPurchaseScope SelectedScope(LiteComboBox combo)
        => combo.SelectedIndex switch
        {
            1 => YouPinPurchaseScope.Top300,
            2 => YouPinPurchaseScope.Top1000,
            _ => YouPinPurchaseScope.Top100
        };

    protected void ShowError(string title, Exception error)
    {
        if (IsDisposed)
            return;
        GlobalPromptService.Show(
            FindForm(),
            error.Message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void OnModuleStateChanged(object? sender, EventArgs e)
    {
        if (!_active || IsDisposed || !IsHandleCreated || Interlocked.Exchange(ref _queued, 1) != 0)
            return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (_active && !IsDisposed) RefreshSnapshot(Module.GetSnapshot());
            }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _queued, 0);
        }
    }

    public override void Deactivate() { _active = false; base.Deactivate(); }

    public override void ApplySystemTheme()
    {
        base.ApplySystemTheme();
        PurchaseMonitorUi.Theme(Container);
    }

    protected async Task ExecuteAsync(Func<Task> command)
    {
        if (CommandBusy) return;
        CommandBusy = true;
        try { await command(); }
        catch (OperationCanceledException)
        {
            // Page shutdown or the explicit stop action is an expected cancellation.
        }
        catch (Exception error) { ShowError("求购监控", error); }
        finally
        {
            CommandBusy = false;
            if (!IsDisposed) RefreshSnapshot(Module.GetSnapshot());
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Module.StateChanged -= OnModuleStateChanged;
        base.Dispose(disposing);
    }
}
