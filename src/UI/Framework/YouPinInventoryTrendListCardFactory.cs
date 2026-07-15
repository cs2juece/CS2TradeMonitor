using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinInventoryTrendListCard
    {
        public YouPinInventoryTrendListCard(
            LiteSettingsGroup group,
            Label emptyLabel,
            LiteUnderlineInput searchInput,
            LiteComboBox filterCombo,
            Panel gridHost,
            ThemedVerticalScrollBar gridScrollBar)
        {
            Group = group ?? throw new ArgumentNullException(nameof(group));
            EmptyLabel = emptyLabel ?? throw new ArgumentNullException(nameof(emptyLabel));
            SearchInput = searchInput ?? throw new ArgumentNullException(nameof(searchInput));
            FilterCombo = filterCombo ?? throw new ArgumentNullException(nameof(filterCombo));
            GridHost = gridHost ?? throw new ArgumentNullException(nameof(gridHost));
            GridScrollBar = gridScrollBar ?? throw new ArgumentNullException(nameof(gridScrollBar));
        }

        public LiteSettingsGroup Group { get; }
        public Label EmptyLabel { get; }
        public LiteUnderlineInput SearchInput { get; }
        public LiteComboBox FilterCombo { get; }
        public Panel GridHost { get; }
        public ThemedVerticalScrollBar GridScrollBar { get; }
    }

    internal static class YouPinInventoryTrendListCardFactory
    {
        public static YouPinInventoryTrendListCard Create(
            Action scheduleFilterRefresh,
            Action scrollGridToCustomBarValue,
            Action updateGridScrollBar)
        {
            ArgumentNullException.ThrowIfNull(scheduleFilterRefresh);
            ArgumentNullException.ThrowIfNull(scrollGridToCustomBarValue);
            ArgumentNullException.ThrowIfNull(updateGridScrollBar);

            var group = new LiteSettingsGroup("库存涨跌明细");
            var toolbar = YouPinInventoryTrendToolbarFactory.Create(scheduleFilterRefresh);
            group.AddFullItem(toolbar.Row);

            var emptyLabel = CreateInitialEmptyLabel();
            group.AddFullItem(emptyLabel);

            var gridHost = CreateGridHost(scrollGridToCustomBarValue, updateGridScrollBar, out ThemedVerticalScrollBar gridScrollBar);
            gridHost.Visible = false;
            group.AddFullItem(gridHost);

            return new YouPinInventoryTrendListCard(
                group,
                emptyLabel,
                toolbar.SearchInput,
                toolbar.FilterCombo,
                gridHost,
                gridScrollBar);
        }

        private static Label CreateInitialEmptyLabel()
        {
            return new Label
            {
                Dock = DockStyle.Top,
                Height = YouPinInventoryTrendListCardModel.BuildInitialEmptyHeight(),
                AutoSize = false,
                Text = YouPinInventoryTrendListCardModel.InitialEmptyText,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Panel CreateGridHost(
            Action scrollGridToCustomBarValue,
            Action updateGridScrollBar,
            out ThemedVerticalScrollBar gridScrollBar)
        {
            var host = new Panel
            {
                Height = YouPinInventoryTrendListCardModel.BuildGridHostHeight(),
                Dock = DockStyle.Top,
                BackColor = UIColors.CardBg
            };

            gridScrollBar = new ThemedVerticalScrollBar
            {
                Dock = DockStyle.Right,
                Width = YouPinInventoryTrendListCardModel.BuildGridScrollBarWidth(),
                Visible = false
            };
            gridScrollBar.ValueChanged += (_, __) => scrollGridToCustomBarValue();

            host.Controls.Add(gridScrollBar);
            gridScrollBar.BringToFront();
            host.Resize += (_, __) => updateGridScrollBar();

            return host;
        }
    }

    internal static class YouPinInventoryTrendListCardModel
    {
        public const string InitialEmptyText = "正在加载库存明细...";

        public static int BuildInitialEmptyHeight()
        {
            return UIUtils.S(46);
        }

        public static int BuildGridHostHeight()
        {
            return UIUtils.S(430);
        }

        public static int BuildGridScrollBarWidth()
        {
            return UIUtils.S(14);
        }
    }
}
