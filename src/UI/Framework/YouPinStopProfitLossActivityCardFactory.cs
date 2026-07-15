using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class YouPinStopProfitLossActivityCardFactory
    {
        public static LiteSettingsGroup CreateTestHelpCard(Control mockTestRow)
        {
            ArgumentNullException.ThrowIfNull(mockTestRow);

            var group = new LiteSettingsGroup("测试与说明");
            group.AddHeaderInlineAction(CreateHeaderDescription("低频操作放在这里；模拟测试只用于检查提醒样式和规则提示。"));
            group.AddFullItem(mockTestRow);
            group.AddHint("需要至少两次相隔观察时间的悠悠涨跌快照，才会判断止盈/止损。");
            group.AddHint("开启“只看指定单品”后，只检查已添加的指定单品；未添加时不会报警。");
            group.AddHint("提醒冷却用于避免同一单品在短时间内重复弹出。");
            return group;
        }

        public static LiteSettingsGroup CreateRecentAlertCard(IEnumerable<YouPinStopProfitLossAlert> recentAlerts)
        {
            var group = new LiteSettingsGroup("最近止盈/损报警");
            var alerts = YouPinStopProfitLossActivityCardModel.SelectVisibleAlerts(recentAlerts).ToList();
            if (alerts.Count == 0)
            {
                group.AddFullItem(CreateEmptyRow(YouPinStopProfitLossActivityCardModel.EmptyRecentAlertText));
                return group;
            }

            foreach (var alert in alerts)
                group.AddFullItem(CreateAlertRow(alert));

            return group;
        }

        private static Label CreateHeaderDescription(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Height = UIUtils.S(24),
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static Control CreateEmptyRow(string text)
        {
            return new Label
            {
                Text = text,
                Height = UIUtils.S(44),
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UIColors.TextSub,
                Font = new Font("Microsoft YaHei UI", 9F),
                BackColor = Color.Transparent
            };
        }

        private static Control CreateAlertRow(YouPinStopProfitLossAlert alert)
        {
            var row = new Panel { Height = UIUtils.S(58), BackColor = Color.Transparent };
            var title = new Label
            {
                Text = YouPinStopProfitLossActivityCardModel.BuildAlertTitle(alert),
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            var detail = new Label
            {
                Text = YouPinStopProfitLossActivityCardModel.BuildAlertDetail(alert),
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = alert.Percent > 0 ? Color.FromArgb(220, 70, 90) : Color.FromArgb(80, 160, 130),
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };

            row.Controls.Add(title);
            row.Controls.Add(detail);
            row.Layout += (_, __) =>
            {
                title.SetBounds(0, UIUtils.S(6), row.Width, UIUtils.S(22));
                detail.SetBounds(0, title.Bottom + UIUtils.S(2), row.Width, UIUtils.S(22));
            };
            row.Paint += (_, e) =>
            {
                using var pen = new Pen(UIColors.Border);
                e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1);
            };
            return row;
        }
    }

    internal static class YouPinStopProfitLossActivityCardModel
    {
        public const string EmptyRecentAlertText = "暂无报警。开启后需要至少两次相隔观察时间的悠悠涨跌快照，才会开始判断。";

        public static IEnumerable<YouPinStopProfitLossAlert> SelectVisibleAlerts(
            IEnumerable<YouPinStopProfitLossAlert>? alerts,
            int maxCount = 10)
        {
            if (alerts == null || maxCount <= 0)
                return Enumerable.Empty<YouPinStopProfitLossAlert>();

            return alerts.Take(maxCount);
        }

        public static string BuildAlertTitle(YouPinStopProfitLossAlert alert)
        {
            ArgumentNullException.ThrowIfNull(alert);
            return $"{alert.Direction}  {alert.Name}";
        }

        public static string BuildAlertDetail(YouPinStopProfitLossAlert alert)
        {
            ArgumentNullException.ThrowIfNull(alert);
            return $"{YouPinStopProfitLossPageModel.FormatSignedPercent(alert.Percent)}  ¥{alert.OldUnitPrice:F2} -> ¥{alert.NewUnitPrice:F2}  {alert.WindowMinutes / 60.0:0.#} 小时  {alert.Time:MM-dd HH:mm}";
        }
    }
}
