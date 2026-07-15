using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryTrendSummaryCard
    {
        public YouPinInventoryTrendSummaryCard(
            LiteSettingsGroup group,
            Label lastFetchLabel,
            Label authLabel,
            Label marketValueLabel,
            Label marketSubLabel,
            Label deltaValueLabel,
            Label deltaSubLabel,
            Label purchaseValueLabel,
            Label purchaseSubLabel)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            LastFetchLabel = lastFetchLabel ?? throw new ArgumentNullException(nameof(lastFetchLabel));
            AuthLabel = authLabel ?? throw new ArgumentNullException(nameof(authLabel));
            MarketValueLabel = marketValueLabel ?? throw new ArgumentNullException(nameof(marketValueLabel));
            MarketSubLabel = marketSubLabel ?? throw new ArgumentNullException(nameof(marketSubLabel));
            DeltaValueLabel = deltaValueLabel ?? throw new ArgumentNullException(nameof(deltaValueLabel));
            DeltaSubLabel = deltaSubLabel ?? throw new ArgumentNullException(nameof(deltaSubLabel));
            PurchaseValueLabel = purchaseValueLabel ?? throw new ArgumentNullException(nameof(purchaseValueLabel));
            PurchaseSubLabel = purchaseSubLabel ?? throw new ArgumentNullException(nameof(purchaseSubLabel));
        }

        public LiteSettingsGroup Group { get; }
        public Label LastFetchLabel { get; }
        public Label AuthLabel { get; }
        public Label MarketValueLabel { get; }
        public Label MarketSubLabel { get; }
        public Label DeltaValueLabel { get; }
        public Label DeltaSubLabel { get; }
        public Label PurchaseValueLabel { get; }
        public Label PurchaseSubLabel { get; }
    }

    internal static class YouPinInventoryTrendSummaryCardFactory
    {
        public static YouPinInventoryTrendSummaryCard Create(
            Func<Task> refreshNowAsync,
            Action openRefreshSettings,
            Action openAuthDialog,
            bool showAuthControls = true)
        {
            ArgumentNullException.ThrowIfNull(refreshNowAsync);
            ArgumentNullException.ThrowIfNull(openRefreshSettings);
            ArgumentNullException.ThrowIfNull(openAuthDialog);

            var group = new LiteSettingsGroup("悠悠有品库存涨跌");
            var header = CreateHeaderStatusBar(refreshNowAsync, openRefreshSettings, openAuthDialog, showAuthControls);
            var summary = CreateSummaryPanel(
                out Label marketValueLabel,
                out Label marketSubLabel,
                out Label deltaValueLabel,
                out Label deltaSubLabel,
                out Label purchaseValueLabel,
                out Label purchaseSubLabel);

            group.AddHeaderInlineAction(header.Panel);
            group.AddFullItem(summary);

            return new YouPinInventoryTrendSummaryCard(
                group,
                header.LastFetchLabel,
                header.AuthLabel,
                marketValueLabel,
                marketSubLabel,
                deltaValueLabel,
                deltaSubLabel,
                purchaseValueLabel,
                purchaseSubLabel);
        }

        private static YouPinInventoryTrendHeaderStatusBar CreateHeaderStatusBar(
            Func<Task> refreshNowAsync,
            Action openRefreshSettings,
            Action openAuthDialog,
            bool showAuthControls)
        {
            var panel = new Panel
            {
                Height = UIUtils.S(32),
                BackColor = Color.Transparent
            };

            var lastFetchLabel = YouPinInventoryTrendUiFactory.CreateHeaderLabel("上次刷新时间：暂无", UIColors.TextSub);
            var authLabel = YouPinInventoryTrendUiFactory.CreateHeaderLabel("登录状态：未知", UIColors.TextSub);

            var refreshButton = YouPinInventoryTrendUiFactory.CreateHeaderButton("立即刷新");
            refreshButton.Click += async (_, __) => await refreshNowAsync();

            var settingsButton = YouPinInventoryTrendUiFactory.CreateHeaderButton("刷新设置");
            settingsButton.Click += (_, __) => openRefreshSettings();

            var authButton = new LiteButton("登录/管理登录", true)
            {
                Width = UIUtils.S(118),
                Height = UIUtils.S(30)
            };
            authButton.Click += (_, __) => openAuthDialog();

            panel.Controls.Add(lastFetchLabel);
            panel.Controls.Add(refreshButton);
            panel.Controls.Add(settingsButton);
            if (showAuthControls)
            {
                panel.Controls.Add(authLabel);
                panel.Controls.Add(authButton);
            }

            panel.Layout += (_, __) =>
            {
                var layout = YouPinInventoryTrendSummaryCardModel.BuildHeaderLayout(
                    panel.Width,
                    panel.Height,
                    refreshButton.Width,
                    refreshButton.Height,
                    settingsButton.Width,
                    settingsButton.Height,
                    showAuthControls ? authButton.Width : 0,
                    showAuthControls ? authButton.Height : 0);

                lastFetchLabel.Bounds = layout.LastFetchBounds;
                refreshButton.Bounds = layout.RefreshButtonBounds;
                settingsButton.Bounds = layout.SettingsButtonBounds;
                if (showAuthControls)
                {
                    authLabel.Bounds = layout.AuthLabelBounds;
                    authButton.Bounds = layout.AuthButtonBounds;
                }
            };

            return new YouPinInventoryTrendHeaderStatusBar(panel, lastFetchLabel, authLabel);
        }

        private static Control CreateSummaryPanel(
            out Label marketValueLabel,
            out Label marketSubLabel,
            out Label deltaValueLabel,
            out Label deltaSubLabel,
            out Label purchaseValueLabel,
            out Label purchaseSubLabel)
        {
            var panel = new Panel
            {
                Height = UIUtils.S(118),
                Dock = DockStyle.Top,
                BackColor = Color.Transparent
            };

            var market = YouPinInventoryTrendUiFactory.CreateStatCard("市场价", out marketValueLabel, out marketSubLabel);
            var delta = YouPinInventoryTrendUiFactory.CreateStatCard("涨跌", out deltaValueLabel, out deltaSubLabel);
            var purchase = YouPinInventoryTrendUiFactory.CreateStatCard("购入价", out purchaseValueLabel, out purchaseSubLabel);

            panel.Controls.Add(market);
            panel.Controls.Add(delta);
            panel.Controls.Add(purchase);

            panel.Layout += (_, __) =>
            {
                var layout = YouPinInventoryTrendSummaryCardModel.BuildSummaryLayout(panel.ClientSize.Width, panel.Height);
                market.Bounds = layout.MarketBounds;
                delta.Bounds = layout.DeltaBounds;
                purchase.Bounds = layout.PurchaseBounds;
            };

            return panel;
        }
    }

    internal sealed record YouPinInventoryTrendHeaderStatusBar(
        Panel Panel,
        Label LastFetchLabel,
        Label AuthLabel);

    internal static class YouPinInventoryTrendSummaryCardModel
    {
        public static YouPinInventoryTrendHeaderLayout BuildHeaderLayout(
            int panelWidth,
            int panelHeight,
            int refreshButtonWidth,
            int refreshButtonHeight,
            int settingsButtonWidth,
            int settingsButtonHeight,
            int authButtonWidth,
            int authButtonHeight)
        {
            int gap = UIUtils.S(10);
            int y = (Math.Max(1, panelHeight) - Math.Max(1, refreshButtonHeight)) / 2;
            int x = 0;

            var lastFetchBounds = new Rectangle(x, 0, UIUtils.S(166), Math.Max(1, panelHeight));
            x = lastFetchBounds.Right + gap;
            var refreshButtonBounds = new Rectangle(x, y, Math.Max(1, refreshButtonWidth), Math.Max(1, refreshButtonHeight));
            x = refreshButtonBounds.Right + gap;
            var settingsButtonBounds = new Rectangle(
                x,
                (Math.Max(1, panelHeight) - Math.Max(1, settingsButtonHeight)) / 2,
                Math.Max(1, settingsButtonWidth),
                Math.Max(1, settingsButtonHeight));
            x = settingsButtonBounds.Right + gap;

            int right = Math.Max(1, panelWidth);
            var authButtonBounds = new Rectangle(
                Math.Max(x, right - Math.Max(1, authButtonWidth)),
                (Math.Max(1, panelHeight) - Math.Max(1, authButtonHeight)) / 2,
                Math.Max(1, authButtonWidth),
                Math.Max(1, authButtonHeight));
            int middleRight = authButtonBounds.Left - gap;
            int available = Math.Max(0, middleRight - x);
            int authWidth = Math.Min(UIUtils.S(150), available);
            var authLabelBounds = new Rectangle(x, 0, authWidth, Math.Max(1, panelHeight));

            return new YouPinInventoryTrendHeaderLayout(
                lastFetchBounds,
                refreshButtonBounds,
                settingsButtonBounds,
                authLabelBounds,
                authButtonBounds);
        }

        public static YouPinInventoryTrendSummaryLayout BuildSummaryLayout(int clientWidth, int panelHeight)
        {
            int gap = UIUtils.S(14);
            int width = Math.Max(1, (Math.Max(1, clientWidth) - gap * 2) / 3);
            int height = Math.Max(1, panelHeight);

            var marketBounds = new Rectangle(0, 0, width, height);
            var deltaBounds = new Rectangle(width + gap, 0, width, height);
            var purchaseBounds = new Rectangle(
                (width + gap) * 2,
                0,
                Math.Max(1, Math.Max(1, clientWidth) - (width + gap) * 2),
                height);

            return new YouPinInventoryTrendSummaryLayout(marketBounds, deltaBounds, purchaseBounds);
        }
    }

    internal readonly record struct YouPinInventoryTrendHeaderLayout(
        Rectangle LastFetchBounds,
        Rectangle RefreshButtonBounds,
        Rectangle SettingsButtonBounds,
        Rectangle AuthLabelBounds,
        Rectangle AuthButtonBounds);

    internal readonly record struct YouPinInventoryTrendSummaryLayout(
        Rectangle MarketBounds,
        Rectangle DeltaBounds,
        Rectangle PurchaseBounds);
}
