using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Modules;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class ModuleHealthRowControls
    {
        public ModuleHealthRowControls(string id, Label name, Label status, Label changed, Label detail, LiteButton restartButton)
        {
            Id = id;
            Name = name;
            Status = status;
            Changed = changed;
            Detail = detail;
            RestartButton = restartButton;
        }

        public string Id { get; }
        public Label Name { get; }
        public Label Status { get; }
        public Label Changed { get; }
        public Label Detail { get; }
        public LiteButton RestartButton { get; }
    }

    internal static class SystemSettingsModuleHealthDetailsFactory
    {
        private const string DetailsNote = "模块异常会被隔离为异常/暂停，不直接拖垮主程序；Steam 报价等高风险模块后续优先进程隔离。";

        public static void Populate(
            Panel detailsPanel,
            IReadOnlyList<MonitorModuleHealth> snapshot,
            ICollection<ModuleHealthRowControls> rows,
            EventHandler restartHandler,
            int rowHeight)
        {
            ArgumentNullException.ThrowIfNull(detailsPanel);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(restartHandler);

            Label noteLabel = CreateNoteLabel();
            detailsPanel.Controls.Add(noteLabel);

            Label headerName = CreateHeaderLabel("模块");
            Label headerStatus = CreateHeaderLabel("状态");
            Label headerChanged = CreateHeaderLabel("更新时间");
            Label headerDetail = CreateHeaderLabel("边界");
            Label headerAction = CreateHeaderLabel("操作");
            detailsPanel.Controls.Add(headerName);
            detailsPanel.Controls.Add(headerStatus);
            detailsPanel.Controls.Add(headerChanged);
            detailsPanel.Controls.Add(headerDetail);
            detailsPanel.Controls.Add(headerAction);

            rows.Clear();
            foreach (MonitorModuleHealth health in snapshot)
            {
                Label name = CreateValueLabel(health.DisplayName, FontStyle.Bold);
                Label status = CreateValueLabel(string.Empty, FontStyle.Bold);
                Label changed = CreateValueLabel(string.Empty);
                Label detail = CreateValueLabel(string.Empty);
                var restartButton = new LiteButton("重启", false)
                {
                    Width = UIUtils.S(64),
                    Height = UIUtils.S(24),
                    Enabled = false,
                    Tag = health.Id
                };
                restartButton.Click += restartHandler;

                detailsPanel.Controls.Add(name);
                detailsPanel.Controls.Add(status);
                detailsPanel.Controls.Add(changed);
                detailsPanel.Controls.Add(detail);
                detailsPanel.Controls.Add(restartButton);
                rows.Add(new ModuleHealthRowControls(health.Id, name, status, changed, detail, restartButton));
            }

            detailsPanel.Layout += (_, __) => LayoutDetails(
                detailsPanel,
                rowHeight,
                noteLabel,
                headerName,
                headerStatus,
                headerChanged,
                headerDetail,
                headerAction,
                rows);
        }

        public static int GetDetailsHeight(int rowCount, int rowHeight)
        {
            return UIUtils.S(72) + Math.Max(0, rowCount) * rowHeight;
        }

        public static void ApplyRow(
            ModuleHealthRowControls row,
            SystemSettingsModuleHealthRowViewModel rowView,
            ToolTip toolTip)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(rowView);
            ArgumentNullException.ThrowIfNull(toolTip);

            row.Name.Text = rowView.Name;
            row.Name.ForeColor = UIColors.TextMain;
            row.Status.Text = rowView.StatusText;
            row.Status.ForeColor = GetModuleStateColor(rowView.State);
            row.Changed.Text = rowView.ChangedText;
            row.Changed.ForeColor = UIColors.TextSub;
            row.Detail.Text = rowView.DetailText;
            row.Detail.ForeColor = rowView.DetailWarn ? UIColors.TextWarn : UIColors.TextSub;
            toolTip.SetToolTip(row.Detail, row.Detail.Text);
            row.RestartButton.Enabled = rowView.RestartEnabled;
            row.RestartButton.Text = rowView.RestartText;
            row.RestartButton.RefreshTheme();
        }

        private static void LayoutDetails(
            Control detailsPanel,
            int rowHeight,
            Label noteLabel,
            Label headerName,
            Label headerStatus,
            Label headerChanged,
            Label headerDetail,
            Label headerAction,
            IEnumerable<ModuleHealthRowControls> rows)
        {
            int left = UIUtils.S(4);
            int right = UIUtils.S(4);
            int top = UIUtils.S(12);
            int gap = UIUtils.S(12);
            int nameWidth = UIUtils.S(128);
            int statusWidth = UIUtils.S(84);
            int changedWidth = UIUtils.S(124);
            int actionWidth = UIUtils.S(72);
            int detailLeft = left + nameWidth + statusWidth + changedWidth + gap * 3;
            int actionLeft = Math.Max(detailLeft + UIUtils.S(180) + gap, detailsPanel.ClientSize.Width - right - actionWidth);
            int detailWidth = Math.Max(UIUtils.S(160), actionLeft - detailLeft - gap);

            noteLabel.SetBounds(left, top, detailsPanel.ClientSize.Width - left - right, UIUtils.S(22));
            top = noteLabel.Bottom + UIUtils.S(8);

            headerName.SetBounds(left, top, nameWidth, UIUtils.S(22));
            headerStatus.SetBounds(headerName.Right + gap, top, statusWidth, UIUtils.S(22));
            headerChanged.SetBounds(headerStatus.Right + gap, top, changedWidth, UIUtils.S(22));
            headerDetail.SetBounds(detailLeft, top, detailWidth, UIUtils.S(22));
            headerAction.SetBounds(actionLeft, top, actionWidth, UIUtils.S(22));
            top = headerName.Bottom + UIUtils.S(2);

            foreach (ModuleHealthRowControls row in rows)
            {
                row.Name.SetBounds(left, top, nameWidth, UIUtils.S(28));
                row.Status.SetBounds(row.Name.Right + gap, top, statusWidth, UIUtils.S(28));
                row.Changed.SetBounds(row.Status.Right + gap, top, changedWidth, UIUtils.S(28));
                row.Detail.SetBounds(detailLeft, top, detailWidth, rowHeight);
                row.RestartButton.SetBounds(actionLeft, top + UIUtils.S(9), actionWidth, UIUtils.S(24));
                top += rowHeight;
            }
        }

        private static Label CreateNoteLabel()
        {
            return new Label
            {
                Text = DetailsNote,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
        }

        private static Label CreateHeaderLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = UIColors.TextSub,
                BackColor = Color.Transparent
            };
        }

        private static Label CreateValueLabel(string text, FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F, style),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                UseMnemonic = false
            };
        }

        private static Color GetModuleStateColor(MonitorModuleState state)
        {
            return state switch
            {
                MonitorModuleState.Running => UIColors.Positive,
                MonitorModuleState.Starting => UIColors.Primary,
                MonitorModuleState.Paused => UIColors.TextWarn,
                MonitorModuleState.Faulted => UIColors.TextCrit,
                _ => UIColors.TextSub
            };
        }
    }
}
