using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    // 悠悠有品新主入口，承载新结构（库存涨跌 / 库存存取 / 悠悠报价 / 包租公 / 设置）。
    // 不改动「库存止盈/损」；旧 YouPinHostPage 入口已移除。
    public sealed class YouPinCcHostPage : SettingsPageBase, ISettingsSubRouteHost
    {
        private readonly PageHost _contentHost;
        private readonly SettingsTransaction _settingsTransaction;
        private readonly YouPinCcAuthBar _authStatusCard;
        private readonly Panel _authWrapper;
        private readonly Panel _topCard;
        private readonly Panel _tabWrapper;
        private readonly FrameworkTopTabHeader<YouPinCcMainTab> _tabHeader;
        private readonly YouPinPageRuntimeServices _runtimeServices;
        private bool _hostAttached;
        private YouPinCcMainTab _activeTab = YouPinCcMainTab.InventoryTrend;
        private YouPinCcMainTab? _pendingSubRouteTab;
        private YouPinInventoryTrendPage? _inventoryPage;
        private YouPinInventoryStoragePage? _inventoryStoragePage;
        private YouPinSaleReminderPage? _quotePage;
        private YouPinAutoQuotePage? _autoQuotePage;
        private YouPinGridPage? _gridPage;
        private YouPinCcSettingsPage? _settingsPage;
        private YouPinCcLandlordPage? _landlordPage;

        public YouPinCcHostPage()
            : this(YouPinPageRuntimeServices.Resolve())
        {
        }

        internal YouPinCcHostPage(YouPinPageRuntimeServices runtimeServices)
        {
            _runtimeServices = runtimeServices ?? throw new ArgumentNullException(nameof(runtimeServices));
            BackColor = UIColors.MainBg;

            _settingsTransaction = new SettingsTransaction(() => Config);
            _settingsTransaction.Draft.DraftChanged += (_, __) => NotifySettingsChanged();

            _contentHost = new PageHost
            {
                Dock = DockStyle.Fill,
                BackColor = UIColors.MainBg
            };

            _tabHeader = new FrameworkTopTabHeader<YouPinCcMainTab>(
                new[]
                {
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.InventoryTrend, "库存涨跌", 98),
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.InventoryStorage, "库存存取", 98),
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.Quote, "悠悠报价", 98),
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.AutoQuote, "悠悠自动报价", 126),
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.Grid, "交易网格", 98),
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.Landlord, "包租公", 82),
                    new FrameworkTopTabItem<YouPinCcMainTab>(YouPinCcMainTab.Settings, "设置", 82)
                },
                _activeTab,
                "悠悠有品");
            _tabHeader.TabSelected += SwitchTab;
            _tabWrapper = new Panel
            {
                Height = UIUtils.S(52),
                BackColor = UIColors.MainBg
            };
            _tabWrapper.Controls.Add(_tabHeader);
            _tabWrapper.Layout += (_, __) => _tabHeader.SetBounds(
                0,
                0,
                Math.Max(1, _tabWrapper.ClientSize.Width),
                UIUtils.S(40));

            _authStatusCard = new YouPinCcAuthBar(() => Config, () =>
            {
                _settingsTransaction.Rebase();
                RefreshCurrentPage();
            }, YouPinAuthRuntimeServices.Resolve());
            _topCard = new YouPinCcRoundedPanel
            {
                Radius = UIUtils.S(4),
                DrawBorder = false,
                FillOverride = UIColors.MainBg,
                Padding = Padding.Empty
            };
            _topCard.Controls.Add(_authStatusCard);
            _topCard.Controls.Add(_tabWrapper);
            _authWrapper = new Panel
            {
                Dock = DockStyle.Top,
                Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding(bottomPadding: 0),
                BackColor = UIColors.MainBg
            };
            _authWrapper.Controls.Add(_topCard);
            _authWrapper.Layout += (_, __) => LayoutAuthCard();

            Controls.Add(_contentHost);
            Controls.Add(_authWrapper);
        }

        public override void OnShow()
        {
            base.OnShow();
            _settingsTransaction.Rebase();
            RestoreRememberedTab();
            ApplyPendingSubRoute();
            if (!_hostAttached)
            {
                _contentHost.AttachSettings(_settingsTransaction.Draft);
                _hostAttached = true;
            }

            _authStatusCard.RefreshState();
            ShowActivePage();
        }

        public override void OnHide()
        {
            StoreActiveTab();
            _contentHost.SaveCurrentPage();
            _settingsTransaction.Commit();
            if (_contentHost.CurrentPage is Control current && current.Visible)
                _contentHost.CurrentPage?.Deactivate();
            base.OnHide();
        }

        public override void Save()
        {
            StoreActiveTab();
            _contentHost.SaveCurrentPage();
            _settingsTransaction.Commit();
        }

        public bool SwitchSubRoute(string subRoute)
        {
            if (!YouPinCcTabMemoryModel.TryParseTabKey(subRoute, out var tab))
                return false;

            if (!_hostAttached)
            {
                _pendingSubRouteTab = tab;
                _activeTab = tab;
                _tabHeader.SetActiveTab(tab);
                return true;
            }

            SwitchTab(tab);
            return true;
        }

        public override void OnThemeChanged()
        {
            base.OnThemeChanged();
            BackColor = UIColors.MainBg;
            _authWrapper.BackColor = UIColors.MainBg;
            if (_topCard is YouPinCcRoundedPanel card)
            {
                card.FillOverride = UIColors.MainBg;
                card.Invalidate();
            }
            _tabWrapper.BackColor = UIColors.MainBg;
            _contentHost.BackColor = UIColors.MainBg;
            _authStatusCard.RefreshState();
            _tabHeader.RefreshTheme();
            if (_contentHost.CurrentPage is FrameworkSettingsPageBase frameworkPage)
                frameworkPage.ApplySystemTheme();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            LayoutAuthCard();
            _contentHost.Bounds = new Rectangle(0, _authWrapper.Bottom, ClientSize.Width, Math.Max(1, ClientSize.Height - _authWrapper.Bottom));
            _contentHost.PerformLayout();
            _contentHost.RequestCurrentPageRelayout();
        }

        private void SwitchTab(YouPinCcMainTab tab)
        {
            if (_activeTab == tab)
            {
                StoreActiveTab();
                return;
            }

            _activeTab = tab;
            _tabHeader.SetActiveTab(tab);
            StoreActiveTab();
            ShowActivePage();
        }

        private void ShowActivePage()
        {
            if (!_hostAttached)
                return;

            IUiPage page = EnsureSubPage(_activeTab);

            if (page is ISettingsContextAwareUiPage contextAware)
                contextAware.SetSettingsContext(Config, MainForm, UI);
            if (ReferenceEquals(_contentHost.CurrentPage, page))
                page.Activate();
            else
                _contentHost.ShowPage(page);
        }

        private IUiPage EnsureSubPage(YouPinCcMainTab tab)
        {
            return tab switch
            {
                YouPinCcMainTab.Quote => _quotePage ??= new YouPinSaleReminderPage(
                    _runtimeServices,
                    YouPinSaleReminderPageLayoutMode.QuoteOnlyCc),
                YouPinCcMainTab.InventoryStorage => _inventoryStoragePage ??= new YouPinInventoryStoragePage(_runtimeServices),
                YouPinCcMainTab.AutoQuote => _autoQuotePage ??= new YouPinAutoQuotePage(_runtimeServices),
                YouPinCcMainTab.Grid => _gridPage ??= new YouPinGridPage(_runtimeServices),
                YouPinCcMainTab.Landlord => _landlordPage ??= new YouPinCcLandlordPage(),
                YouPinCcMainTab.Settings => _settingsPage ??= new YouPinCcSettingsPage(_runtimeServices),
                _ => _inventoryPage ??= new YouPinInventoryTrendPage(_runtimeServices, showAuthControls: false)
            };
        }

        private void RefreshCurrentPage()
        {
            if (_contentHost.CurrentPage is ISettingsContextAwareUiPage contextAware)
                contextAware.SetSettingsContext(Config, MainForm, UI);
            _contentHost.CurrentPage?.Activate();
        }

        private void RestoreRememberedTab()
        {
            YouPinCcMainTab remembered = YouPinCcTabMemoryModel.ParseSavedTab(Config.YouPinCcLastTab);
            if (_activeTab == remembered)
            {
                _tabHeader.SetActiveTab(_activeTab);
                return;
            }

            _activeTab = remembered;
            _tabHeader.SetActiveTab(_activeTab);
        }

        private void ApplyPendingSubRoute()
        {
            if (_pendingSubRouteTab is not { } tab)
                return;

            _pendingSubRouteTab = null;
            _activeTab = tab;
            _tabHeader.SetActiveTab(_activeTab);
            StoreActiveTab();
        }

        private void StoreActiveTab()
        {
            _settingsTransaction.Draft.Set(YouPinCcTabMemoryModel.SettingsKey, YouPinCcTabMemoryModel.ToSavedValue(_activeTab));
        }

        private void LayoutAuthCard()
        {
            int viewportWidth = FrameworkSettingsPageLayoutHelper.CalculateVisibleWidthWithinForm(_authWrapper);
            Rectangle bounds = FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(
                viewportWidth,
                _authWrapper.Padding,
                scrollBarWidth: 0);
            int x = bounds.Left;
            int y = bounds.Top;
            int width = Math.Min(bounds.Width, Math.Max(1, viewportWidth - x - _authWrapper.Padding.Right));
            int cardHeight = UIUtils.S(112);
            _topCard.SetBounds(x, y, width, cardHeight);

            int cardPadX = UIUtils.S(20);
            _authStatusCard.SetBounds(cardPadX, UIUtils.S(14), Math.Max(1, width - cardPadX * 2), UIUtils.S(42));
            _tabWrapper.SetBounds(cardPadX, UIUtils.S(62), Math.Max(1, width - cardPadX * 2), UIUtils.S(46));

            int desiredHeight = cardHeight + _authWrapper.Padding.Vertical;
            if (_authWrapper.Height != desiredHeight)
                _authWrapper.Height = desiredHeight;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeUnhostedSubPage(_inventoryPage);
                DisposeUnhostedSubPage(_inventoryStoragePage);
                DisposeUnhostedSubPage(_quotePage);
                DisposeUnhostedSubPage(_autoQuotePage);
                DisposeUnhostedSubPage(_gridPage);
                DisposeUnhostedSubPage(_landlordPage);
                DisposeUnhostedSubPage(_settingsPage);
            }

            base.Dispose(disposing);
        }

        private static void DisposeUnhostedSubPage(IUiPage? page)
        {
            if (page == null)
                return;

            if (page is Control { Parent: not null })
                return;

            page.Dispose();
        }

    }

    internal enum YouPinCcMainTab
    {
        InventoryTrend,
        InventoryStorage,
        Quote,
        AutoQuote,
        Grid,
        Landlord,
        Settings
    }

    internal static class YouPinCcTabMemoryModel
    {
        public const string SettingsKey = nameof(Settings.YouPinCcLastTab);

        public static YouPinCcMainTab ParseSavedTab(string? value)
        {
            return TryParseTabKey(value, out YouPinCcMainTab tab)
                ? tab
                : YouPinCcMainTab.InventoryTrend;
        }

        public static string ToSavedValue(YouPinCcMainTab tab)
        {
            return Enum.IsDefined(typeof(YouPinCcMainTab), tab)
                ? tab.ToString()
                : YouPinCcMainTab.InventoryTrend.ToString();
        }

        public static bool TryParseTabKey(string? value, out YouPinCcMainTab tab)
        {
            tab = YouPinCcMainTab.InventoryTrend;
            string key = (value ?? string.Empty).Trim();
            if (key.Length == 0)
                return true;

            if (key.Equals("Inventory", StringComparison.OrdinalIgnoreCase)
                || key.Equals("InventoryTrend", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Trend", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.InventoryTrend;
                return true;
            }

            if (key.Equals("Quote", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Sale", StringComparison.OrdinalIgnoreCase)
                || key.Equals("SaleReminder", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.Quote;
                return true;
            }

            if (key.Equals("AutoQuote", StringComparison.OrdinalIgnoreCase)
                || key.Equals("YouPinAutoQuote", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Automation", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.AutoQuote;
                return true;
            }

            if (key.Equals("Grid", StringComparison.OrdinalIgnoreCase)
                || key.Equals("TradingGrid", StringComparison.OrdinalIgnoreCase)
                || key.Equals("YouPinGrid", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.Grid;
                return true;
            }

            if (key.Equals("InventoryStorage", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Storage", StringComparison.OrdinalIgnoreCase)
                || key.Equals("InventoryTransfer", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.InventoryStorage;
                return true;
            }

            if (key.Equals("Cc", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Landlord", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.Landlord;
                return true;
            }

            if (key.Equals("Settings", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Setting", StringComparison.OrdinalIgnoreCase))
            {
                tab = YouPinCcMainTab.Settings;
                return true;
            }

            return false;
        }
    }

}
