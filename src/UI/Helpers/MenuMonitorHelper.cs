using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Helpers;

namespace CS2TradeMonitor.src.UI.Helpers
{
    /// <summary>
    /// 菜单监控项生成助手
    /// 职责：生成监控项列表、处理分组、排序、动态标签及首次校准提示
    /// </summary>
    public static class MenuMonitorHelper
    {
        public static ToolStripMenuItem Build(MainForm form, Settings cfg, UIController? ui, bool isTaskbarMode)
        {
            var monitorRoot = new ToolStripMenuItem(LanguageManager.T("Menu.MonitorItemDisplay"));


            // --- 内部辅助函数：首次开启时的最大值设定引导 ---
            void CheckAndRemind(string name)
            {
                if (cfg.MaxLimitTipShown) return;

                string msg = cfg.Language == "zh"
                    ? $"您是首次开启 {name}。\n\n建议设置一下“电脑{name}”实际最大值，让进度条显示更准确。\n\n是否现在去设置？\n\n点“否”将不再提示，程序将在高负载时（如大型游戏时）进行动态学习最大值"
                    : $"First launch of {name}.\n\nSet the actual maximum value for accurate progress bar display.\n\nGo to settings now?\n\nSelect \"No\" to skip permanently. App will auto-learn max value in high-load scenarios (e.g., gaming).";

                cfg.MaxLimitTipShown = true;
                cfg.Save();

                if (GlobalPromptService.Show(msg, "CS2交易监控 设置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        if (ui == null)
                        {
                            GlobalPromptService.Show("设置面板暂不可用，请稍后从右键菜单打开。", "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }

                        AppActions.ShowSettingsPage(cfg, ui, form, "System", modal: true);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Error("SettingsPanel", "设置面板启动失败：" + DiagnosticsLogger.Redact(ex.Message), ex);
                        GlobalPromptService.Show(
                            form,
                            "设置面板启动失败，请稍后重试或查看诊断日志。\n\n错误：" + DiagnosticsLogger.Redact(ex.Message),
                            "CS2交易监控",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }

            // --- 内部辅助函数：判断是否为需要校准的硬件项 ---
            bool IsHardwareItem(string key)
            {
                return (key.Contains("Clock") || key.Contains("Power") ||
                       key.Contains("Fan") || key.Contains("Pump")) && !key.Contains("BAT");
            }

            string ResolveMenuLabel(MonitorItemConfig config, bool shortLabel = false)
            {
                if (config.Key.Equals("STEAMDT.Display", StringComparison.OrdinalIgnoreCase))
                    return "SteamDT";
                if (config.Key.Equals("CSQAQ.Display", StringComparison.OrdinalIgnoreCase))
                    return "QAQ";

                string resolved = shortLabel
                    ? MetricLabelResolver.ResolveShortLabel(config)
                    : MetricLabelResolver.ResolveLabel(config);

                if (!string.IsNullOrWhiteSpace(resolved)) return resolved.Trim();

                string prefix = shortLabel ? "Short." : "Items.";
                string fallback = LanguageManager.T(UIUtils.Intern(prefix + config.Key));
                if (fallback.StartsWith(prefix))
                {
                    fallback = config.Key;
                    if (fallback.StartsWith("DASH.") && fallback.Contains("."))
                    {
                        int lastDot = fallback.LastIndexOf('.');
                        if (lastDot >= 0) fallback = fallback.Substring(lastDot + 1);
                    }
                }

                return string.IsNullOrWhiteSpace(fallback) ? config.Key : fallback.Trim();
            }

            // [Optimization] Shared handler for Taskbar items
            EventHandler onTaskbarItemCheck = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is MonitorItemConfig conf)
                {
                    conf.VisibleInTaskbar = item.Checked;
                    cfg.Save();
                    // 在菜单交互中，无需重建菜单 (rebuildMenus: false)
                    AppActions.ApplyMonitorLayout(ui, form, rebuildMenus: false);

                    if (item.Checked && IsHardwareItem(conf.Key))
                    {
                        // [Refactor] Use unified resolver instead of outdated DisplayLabel property
                        CheckAndRemind(ResolveMenuLabel(conf));
                    }
                }
            };

            // [Optimization] Shared handler for Panel items
            EventHandler onPanelItemCheck = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is MonitorItemConfig conf)
                {
                    conf.VisibleInPanel = item.Checked;
                    cfg.Save();
                    // 在菜单交互中，无需重建菜单 (rebuildMenus: false)
                    AppActions.ApplyMonitorLayout(ui, form, rebuildMenus: false);

                    if (item.Checked && IsHardwareItem(conf.Key))
                    {
                        CheckAndRemind(ResolveMenuLabel(conf));
                    }
                }
            };

            if (isTaskbarMode)
            {
                // --- 模式 A: 任务栏 (平铺排序 + 显示全称和简称) ---
                var sortedItems = cfg.MonitorItems.OrderBy(x => x.TaskbarSortIndex).ToList();

                foreach (var itemConfig in sortedItems)
                {
                    string full = ResolveMenuLabel(itemConfig);
                    string shortName = ResolveMenuLabel(itemConfig, shortLabel: true);

                    // 2. 构造菜单显示文本
                    string finalLabel = string.Equals(full, shortName, StringComparison.OrdinalIgnoreCase)
                        ? full
                        : $"{full} ({shortName})";

                    // 2. 创建菜单
                    var itemMenu = new ToolStripMenuItem(finalLabel)
                    {
                        Checked = itemConfig.VisibleInTaskbar,
                        CheckOnClick = true,
                        Tag = itemConfig // Store context
                    };

                    // 3. 事件与提示
                    itemMenu.CheckedChanged += onTaskbarItemCheck;

                    // 4. 鼠标悬停提示
                    if (IsHardwareItem(itemConfig.Key))
                        itemMenu.ToolTipText = LanguageManager.T("Menu.CalibrationTip");

                    monitorRoot.DropDownItems.Add(itemMenu);
                }
            }
            else
            {
                // --- 模式 B: 主界面 (HOST分组 + 组内排序) ---
                var sortedItems = cfg.MonitorItems.OrderBy(x => x.SortIndex).ToList();
                var groups = sortedItems.GroupBy(x => x.UIGroup); // 利用 UIGroup 自动识别 HOST

                // 辅助函数：创建单个菜单项
                ToolStripMenuItem CreateItemMenu(MonitorItemConfig itemConfig)
                {
                    string finalLabel = ResolveMenuLabel(itemConfig);

                    var itemMenu = new ToolStripMenuItem(finalLabel)
                    {
                        Checked = itemConfig.VisibleInPanel,
                        CheckOnClick = true,
                        Tag = itemConfig // Store context
                    };

                    itemMenu.CheckedChanged += onPanelItemCheck;

                    if (IsHardwareItem(itemConfig.Key))
                        itemMenu.ToolTipText = LanguageManager.T("Menu.CalibrationTip");

                    return itemMenu;
                }

                // 定义需要纯开关模式的组 (点击组名即全开/全关，无子项)
                var toggleGroups = new HashSet<string> { "DISK", "NET", "DATA" };

                foreach (var g in groups)
                {
                    // 分组标题
                    string gName = LanguageManager.T(UIUtils.Intern("Groups." + g.Key));
                    if (cfg.GroupAliases.ContainsKey(g.Key)) gName = cfg.GroupAliases[g.Key];

                    if (g.Key == "STEAMDT" || g.Key == "CSQAQ")
                    {
                        foreach (var itemConfig in g)
                        {
                            monitorRoot.DropDownItems.Add(CreateItemMenu(itemConfig));
                        }
                    }
                    else if (g.Key == "BAT")
                    {
                        // 电池组：保持折叠子项模式
                        var batRoot = new ToolStripMenuItem(gName);
                        foreach (var itemConfig in g)
                        {
                            batRoot.DropDownItems.Add(CreateItemMenu(itemConfig));
                        }
                        monitorRoot.DropDownItems.Add(batRoot);
                    }
                    else if (toggleGroups.Contains(g.Key))
                    {
                        // 磁盘/网络/流量：纯开关模式 (无子项)
                        // 使用 CheckOnClick = true 简化逻辑，自动处理 UI 勾选状态
                        var groupItem = new ToolStripMenuItem(gName)
                        {
                            CheckOnClick = true,
                            Checked = g.Any(x => x.VisibleInPanel)
                        };

                        // 事件: 状态改变时同步到所有子项
                        groupItem.CheckedChanged += (s, e) =>
                        {
                            bool newState = groupItem.Checked;
                            foreach (var itemConfig in g)
                                itemConfig.VisibleInPanel = newState;

                            cfg.Save();
                            // 在菜单交互中，无需重建菜单 (rebuildMenus: false)
                            AppActions.ApplyMonitorLayout(ui, form, rebuildMenus: false);
                        };

                        monitorRoot.DropDownItems.Add(groupItem);
                    }
                    else
                    {
                        // 其他组：平铺模式 (标题不可点 + 子项列表)
                        monitorRoot.DropDownItems.Add(new ToolStripMenuItem(gName) { Enabled = false, ForeColor = Color.Gray });
                        foreach (var itemConfig in g)
                        {
                            monitorRoot.DropDownItems.Add(CreateItemMenu(itemConfig));
                        }
                    }

                    monitorRoot.DropDownItems.Add(new ToolStripSeparator());
                }

                // 删掉最后多余的分割线
                if (monitorRoot.DropDownItems.Count > 0 && monitorRoot.DropDownItems[monitorRoot.DropDownItems.Count - 1] is ToolStripSeparator)
                    monitorRoot.DropDownItems.RemoveAt(monitorRoot.DropDownItems.Count - 1);
            }

            return monitorRoot;
        }
    }
}
