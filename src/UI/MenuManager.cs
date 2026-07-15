using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Actions;
using CS2TradeMonitor.src.UI;
using CS2TradeMonitor.src.UI.Helpers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CS2TradeMonitor.src.SystemServices.InfoService;

namespace CS2TradeMonitor
{
    public static class MenuManager
    {
        /// <summary>
        /// 构建 CS2交易监控 主菜单（右键菜单 + 托盘菜单）
        /// </summary>
        public static ContextMenuStrip Build(MainForm form, Settings cfg, UIController? ui, string? targetPage = null)
        {
            var menu = new ContextMenuStrip();
            string settingsTab = targetPage == AppActions.MainPanelTaskbarTab
                ? AppActions.MainPanelTaskbarTab
                : AppActions.MainPanelFloatTab;
            // 标记是否为任务栏模式 (影响监控项的勾选逻辑)
            bool isTaskbarMode = settingsTab == AppActions.MainPanelTaskbarTab;

            void OpenSettingsPanel()
            {
                try
                {
                    AppActions.ShowInterfaceSettings(cfg, ui, form, settingsTab, modal: true);
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

            bool ConfirmAction(string message)
            {
                return GlobalPromptService.Show(message, "CS2交易监控", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
            }

            void PostMenuAction(Action action, string source)
            {
                void Run()
                {
                    if (!form.IsDisposed)
                    {
                        action();
                    }
                }

                MenuLifetime.PostAfterMenuMessage(form, Run, source);
            }

            void PostAsyncMenuAction(Func<Task> action, string source)
            {
                async Task Run()
                {
                    if (!form.IsDisposed)
                    {
                        await action();
                    }
                }

                MenuLifetime.PostAfterMenuMessage(form, Run, source);
            }

            var quickSettings = new ToolStripMenuItem(LanguageManager.T("Menu.MainFormSettings"))
            {
                Font = new Font(menu.Font, FontStyle.Bold)
            };
            quickSettings.Click += (_, __) => PostMenuAction(OpenSettingsPanel, "OpenSettingsPanel");
            if (!isTaskbarMode)
            {
                menu.Items.Add(quickSettings);
            }

            var quickHideMainForm = new ToolStripMenuItem(LanguageManager.T("Menu.HideMainForm"))
            {
                Checked = cfg.HideMainForm,
                CheckOnClick = true
            };
            quickHideMainForm.CheckedChanged += (_, __) =>
            {
                bool hideMainForm = quickHideMainForm.Checked;
                PostMenuAction(() => AppActions.ApplyMainFormVisibility(cfg, form, hideMainForm), "ApplyMainFormVisibility");
            };
            menu.Items.Add(quickHideMainForm);

            var taskbarMode = new ToolStripMenuItem(LanguageManager.T("Menu.TaskbarShow"))
            {
                Checked = cfg.ShowTaskbar
            };
            taskbarMode.Click += (_, __) =>
            {
                bool showTaskbar = !cfg.ShowTaskbar;
                PostMenuAction(() => AppActions.ApplyTaskbarVisibility(cfg, form, showTaskbar), "ApplyTaskbarVisibility");
            };
            menu.Items.Add(taskbarMode);

            var topMost = new ToolStripMenuItem(LanguageManager.T("Menu.TopMost"))
            {
                Checked = cfg.TopMost,
                CheckOnClick = true
            };
            topMost.CheckedChanged += (_, __) =>
            {
                bool isTopMost = topMost.Checked;
                PostMenuAction(() =>
                {
                    cfg.TopMost = isTopMost;
                    cfg.Save();
                    AppActions.ApplyWindowAttributes(cfg, form);
                }, "ApplyTopMost");
            };
            menu.Items.Add(topMost);

            if (!isTaskbarMode)
            {
                var quickClickThrough = new ToolStripMenuItem(LanguageManager.T("Menu.ClickThrough"))
                {
                    Checked = cfg.ClickThrough,
                    CheckOnClick = true
                };
                quickClickThrough.CheckedChanged += (_, __) =>
                {
                    bool clickThrough = quickClickThrough.Checked;
                    PostMenuAction(() => AppActions.ApplyMainClickThrough(cfg, form, clickThrough), "ApplyMainClickThrough");
                };
                menu.Items.Add(quickClickThrough);
            }

            var quickDnd = new ToolStripMenuItem(cfg.DoNotDisturbEnabled ? "勿扰模式：开启" : "勿扰模式：关闭")
            {
                Checked = cfg.DoNotDisturbEnabled
            };
            quickDnd.Click += (_, __) =>
            {
                cfg.DoNotDisturbEnabled = !cfg.DoNotDisturbEnabled;
                cfg.Save();
                PostMenuAction(form.RebuildMenus, "RebuildMenus.DoNotDisturb");
            };
            menu.Items.Add(quickDnd);

            var quickRefresh = new ToolStripMenuItem(LanguageManager.T("Menu.RefreshNow") ?? "立即刷新");
            quickRefresh.Click += (_, __) => PostAsyncMenuAction(async () =>
            {
                await AppActions.ForceRefreshMarketDataAsync(form);
                form.RebuildMenus();
            }, "ForceRefreshMarketData");
            menu.Items.Add(quickRefresh);
            menu.Items.Add(new ToolStripSeparator());

            // ==================================================================================
            // 1. 基础功能区 (显示模式、外观设置、数据源状态)
            // ==================================================================================

            // === 显示模式 ===
            var modeRoot = new ToolStripMenuItem(LanguageManager.T("Menu.DisplayMode"));

            var vertical = new ToolStripMenuItem(LanguageManager.T("Menu.Vertical") ?? "竖向")
            {
                Checked = !cfg.HorizontalMode
            };
            var horizontalSingle = new ToolStripMenuItem(LanguageManager.T("Menu.HorizontalSingleLineMode") ?? "横向单行")
            {
                Checked = cfg.HorizontalMode
            };

            void SetMode(bool horizontalMode)
            {
                bool oldMode = ui?.IsLayoutHorizontal ?? cfg.HorizontalMode;
                cfg.HorizontalMode = horizontalMode;
                cfg.HorizontalSingleLine = horizontalMode;
                cfg.Save();
                PostMenuAction(() => AppActions.ApplyThemeAndLayout(cfg, ui, form, oldMode), "SetDisplayMode");
            }

            vertical.Click += (_, __) => SetMode(false);
            horizontalSingle.Click += (_, __) => SetMode(true);

            modeRoot.DropDownItems.Add(vertical);
            modeRoot.DropDownItems.Add(horizontalSingle);
            menu.Items.Add(modeRoot);

            // === 外观设置 ===
            var appearanceRoot = new ToolStripMenuItem(LanguageManager.T("Menu.AppearanceSettings") ?? "外观设置");

            // === 透明度 ===
            var opacityRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Opacity"));
            double[] backgroundTransparencies = { 0.0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
            double[] textTransparencies = { 0.0, 0.05, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.6, 0.7 };

            void AddTransparencyItems(ToolStripMenuItem root, IEnumerable<double> transparencies, Func<double> getOpacity, Action<double> setOpacity)
            {
                foreach (var transparency in transparencies)
                {
                    double opacity = 1.0 - transparency;
                    var item = new ToolStripMenuItem($"{transparency * 100:0}%")
                    {
                        Checked = Math.Abs(getOpacity() - opacity) < 0.01,
                        Tag = opacity
                    };
                    item.Click += (_, __) =>
                    {
                        setOpacity(opacity);
                        cfg.Save();
                        PostMenuAction(() =>
                        {
                            AppActions.ApplyWindowAttributes(cfg, form);
                            AppActions.ApplyThemeAndLayout(cfg, ui, form, retainData: true);
                        }, "ApplyTransparency");
                    };
                    root.DropDownItems.Add(item);
                }
            }

            var bgOpacityRoot = new ToolStripMenuItem(LanguageManager.T("Menu.PanelBackgroundOpacity") ?? "背景透明度");
            AddTransparencyItems(bgOpacityRoot, backgroundTransparencies, () => cfg.PanelBackgroundOpacity, val =>
            {
                cfg.PanelBackgroundOpacity = val;
                cfg.Opacity = val;
            });
            opacityRoot.DropDownItems.Add(bgOpacityRoot);

            var textOpacityRoot = new ToolStripMenuItem(LanguageManager.T("Menu.TextOpacity") ?? "文字透明度");
            AddTransparencyItems(textOpacityRoot, textTransparencies, () => cfg.TextOpacity, val => cfg.TextOpacity = val);
            opacityRoot.DropDownItems.Add(textOpacityRoot);

            appearanceRoot.DropDownItems.Add(opacityRoot);

            // === 界面宽度 ===
            string widthText = LanguageManager.T("Menu.Width");
            var widthRoot = new ToolStripMenuItem(
                string.IsNullOrWhiteSpace(widthText) || string.Equals(widthText, "Menu.Width", StringComparison.Ordinal)
                    ? "界面宽度"
                    : widthText);
            int[] presetWidths = { 180, 200, 220, 240, 260, 280, 300, 360, 420, 480, 540, 600, 660, 720, 780, 840, 900, 960, 1020, 1080, 1140, 1200 };
            int currentW = cfg.PanelWidth;

            EventHandler onWidthClick = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is int w)
                {
                    cfg.PanelWidth = w;
                    cfg.Save();
                    PostMenuAction(() => AppActions.ApplyThemeAndLayout(cfg, ui, form, retainData: true), "ApplyPanelWidth");
                }
            };

            foreach (var w in presetWidths)
            {
                var item = new ToolStripMenuItem($"{w}px")
                {
                    Checked = Math.Abs(currentW - w) < 1,
                    Tag = w
                };
                item.Click += onWidthClick;
                widthRoot.DropDownItems.Add(item);
            }
            appearanceRoot.DropDownItems.Add(widthRoot);

            // === 界面缩放 ===
            var scaleRoot = new ToolStripMenuItem(LanguageManager.T("Menu.Scale"));
            (double val, string key)[] presetScales =
            {
                (2.00, "200%"), (1.75, "175%"), (1.50, "150%"), (1.25, "125%"),
                (1.00, "100%"), (0.90, "90%"),  (0.85, "85%"),  (0.80, "80%"),
                (0.75, "75%"),  (0.70, "70%"),  (0.60, "60%"),  (0.50, "50%")
            };

            double currentScale = cfg.UIScale;

            EventHandler onScaleClick = (s, e) =>
            {
                if (s is ToolStripMenuItem item && item.Tag is double scale)
                {
                    cfg.UIScale = scale;
                    cfg.Save();
                    PostMenuAction(() => AppActions.ApplyThemeAndLayout(cfg, ui, form, retainData: true), "ApplyScale");
                }
            };

            foreach (var (scale, label) in presetScales)
            {
                var item = new ToolStripMenuItem(label)
                {
                    Checked = Math.Abs(currentScale - scale) < 0.01,
                    Tag = scale
                };
                item.Click += onScaleClick;
                scaleRoot.DropDownItems.Add(item);
            }
            appearanceRoot.DropDownItems.Add(scaleRoot);
            menu.Items.Add(appearanceRoot);

            // === 数据源状态 ===
            var dataSourceStatusRoot = new ToolStripMenuItem(LanguageManager.T("Menu.DataSourceStatus") ?? "数据源状态");

            var summary = AppActions.GetDataSourceSummary(cfg);

            var qaqStatusItem = new ToolStripMenuItem(summary.QaqTypeLine) { Enabled = false };
            var qaqDetailItem = new ToolStripMenuItem(summary.QaqStatusLine) { Enabled = false };

            var steamDtStatusItem = new ToolStripMenuItem(summary.SteamDtTypeLine) { Enabled = false };
            var steamDtDetailItem = new ToolStripMenuItem(summary.SteamDtStatusLine) { Enabled = false };

            dataSourceStatusRoot.DropDownItems.Add(qaqStatusItem);
            dataSourceStatusRoot.DropDownItems.Add(qaqDetailItem);
            dataSourceStatusRoot.DropDownItems.Add(new ToolStripSeparator());
            dataSourceStatusRoot.DropDownItems.Add(steamDtStatusItem);
            dataSourceStatusRoot.DropDownItems.Add(steamDtDetailItem);
            dataSourceStatusRoot.DropDownItems.Add(new ToolStripSeparator());

            var dsRefresh = new ToolStripMenuItem(LanguageManager.T("Menu.RefreshNow") ?? "立即刷新");
            dsRefresh.Click += (_, __) => PostAsyncMenuAction(async () =>
            {
                await AppActions.ForceRefreshMarketDataAsync(form);
                form.RebuildMenus();
            }, "DataSourceRefresh");
            dataSourceStatusRoot.DropDownItems.Add(dsRefresh);

            var dsSettings = new ToolStripMenuItem(LanguageManager.T("Menu.OpenDataSourceSettings") ?? "打开数据源设置");
            dsSettings.Click += (_, __) =>
            {
                PostMenuAction(() => AppActions.ShowSettingsPage(cfg, ui, form, "Data", modal: true), "OpenDataSourceSettings");
            };
            dataSourceStatusRoot.DropDownItems.Add(dsSettings);
            menu.Items.Add(dataSourceStatusRoot);

            // 调用新 Helper 生成监控项菜单
            var monitorRoot = MenuMonitorHelper.Build(form, cfg, ui, isTaskbarMode);
            menu.Items.Add(monitorRoot);
            menu.Items.Add(new ToolStripSeparator());

            // === 更多 (More) ===
            var moreRoot = new ToolStripMenuItem(LanguageManager.T("Menu.More"));

            // 将开机启动、锁定位置、靠边吸附、自动隐藏等移动到“更多”顶部
            var autoStart = new ToolStripMenuItem(LanguageManager.T("Menu.AutoStart"))
            {
                Checked = cfg.AutoStart,
                CheckOnClick = true
            };
            autoStart.CheckedChanged += (_, __) =>
            {
                bool enabled = autoStart.Checked;
                PostMenuAction(() =>
                {
                    cfg.AutoStart = enabled;
                    cfg.Save();
                    AppActions.ApplyAutoStart(cfg);
                }, "ApplyAutoStart");
            };
            moreRoot.DropDownItems.Add(autoStart);

            var lockPosition = new ToolStripMenuItem(LanguageManager.T("Menu.LockPosition"))
            {
                Checked = cfg.LockPosition,
                CheckOnClick = true
            };
            lockPosition.CheckedChanged += (_, __) =>
            {
                bool locked = lockPosition.Checked;
                PostMenuAction(() =>
                {
                    cfg.LockPosition = locked;
                    cfg.Save();
                    form.RebuildMenus();
                }, "ApplyLockPosition");
            };
            moreRoot.DropDownItems.Add(lockPosition);

            var autoHide = new ToolStripMenuItem(LanguageManager.T("Menu.AutoHide"))
            {
                Checked = cfg.AutoHide,
                CheckOnClick = true
            };
            autoHide.CheckedChanged += (_, __) =>
            {
                bool enabled = autoHide.Checked;
                PostMenuAction(() =>
                {
                    cfg.AutoHide = enabled;
                    cfg.Save();
                    AppActions.ApplyWindowAttributes(cfg, form);
                }, "ApplyAutoHide");
            };
            moreRoot.DropDownItems.Add(autoHide);

            var clampItem = new ToolStripMenuItem(LanguageManager.T("Menu.ClampToScreen"))
            {
                Checked = cfg.ClampToScreen,
                CheckOnClick = true
            };
            clampItem.CheckedChanged += (_, __) =>
            {
                bool enabled = clampItem.Checked;
                PostMenuAction(() =>
                {
                    cfg.ClampToScreen = enabled;
                    cfg.Save();
                }, "ApplyClampToScreen");
            };
            moreRoot.DropDownItems.Add(clampItem);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            if (isTaskbarMode)
            {
                var taskbarSettings = new ToolStripMenuItem(LanguageManager.T("Menu.MainFormSettings"));
                taskbarSettings.Click += (_, __) => PostMenuAction(OpenSettingsPanel, "OpenTaskbarSettingsPanel");
                moreRoot.DropDownItems.Add(taskbarSettings);
                moreRoot.DropDownItems.Add(new ToolStripSeparator());
            }

            // 1. 打开任务管理器
            var itemTaskMgr = new ToolStripMenuItem(LanguageManager.T("Menu.ActionTaskMgr"));
            itemTaskMgr.Click += (_, __) => PostMenuAction(SystemActions.OpenTaskManager, "OpenTaskManager");
            moreRoot.DropDownItems.Add(itemTaskMgr);

            // 2. 重启资源管理器
            var itemRestartExp = new ToolStripMenuItem(LanguageManager.T("Menu.RestartExplorer"));
            itemRestartExp.Click += (_, __) =>
            {
                PostMenuAction(() =>
                {
                    if (ConfirmAction("即将重启 Windows 资源管理器，任务栏和桌面会短暂消失，是否继续？"))
                    {
                        SystemActions.RestartExplorer();
                    }
                }, "RestartExplorer");
            };
            moreRoot.DropDownItems.Add(itemRestartExp);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            // 2.1 刷新桌面图标缓存
            var itemRefreshIcons = new ToolStripMenuItem(LanguageManager.T("Menu.RefreshIcons"));
            itemRefreshIcons.Click += (_, __) => PostMenuAction(SystemActions.RefreshIconCache, "RefreshIconCache");
            moreRoot.DropDownItems.Add(itemRefreshIcons);

            // 2.4 清理临时文件
            var itemCleanTemp = new ToolStripMenuItem(LanguageManager.T("Menu.CleanTemp"));
            itemCleanTemp.Click += (_, __) => PostAsyncMenuAction(async () =>
            {
                if (ConfirmAction("将清理当前用户临时目录中可删除的文件，是否继续？"))
                {
                    await SystemActions.CleanTempFilesAsync();
                }
            }, "CleanTempFiles");
            moreRoot.DropDownItems.Add(itemCleanTemp);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            // 3. 禁止自动休眠 (Toggle)
            var itemNoSleep = new ToolStripMenuItem(LanguageManager.T("Menu.PreventSleep"))
            {
                Checked = SystemActions.IsPreventSleep,
                CheckOnClick = true
            };
            itemNoSleep.Click += (_, __) =>
            {
                PostMenuAction(() =>
                {
                    SystemActions.TogglePreventSleep();
                }, "TogglePreventSleep");
            };
            moreRoot.DropDownItems.Add(itemNoSleep);

            // 4. 关闭显示器
            var itemOffScreen = new ToolStripMenuItem(LanguageManager.T("Menu.TurnOffMonitor"));
            itemOffScreen.Click += (_, __) =>
            {
                PostMenuAction(() =>
                {
                    if (ConfirmAction("即将关闭显示器，移动鼠标或按键可唤醒，是否继续？"))
                    {
                        SystemActions.TurnOffMonitor(form.Handle);
                    }
                }, "TurnOffMonitor");
            };
            moreRoot.DropDownItems.Add(itemOffScreen);

            // 5. 定时关机 (Submenu)
            var itemShutdown = new ToolStripMenuItem(LanguageManager.T("Menu.ScheduledShutdown"));

            void AddShutdownItem(string label, int seconds)
            {
                var sub = new ToolStripMenuItem(label);
                sub.Click += (_, __) =>
                {
                    string prompt = seconds <= 0
                        ? "即将取消已计划的系统关机，是否继续？"
                        : $"即将安排 Windows 在 {label} 关机，是否继续？";
                    PostMenuAction(() =>
                    {
                        if (ConfirmAction(prompt))
                        {
                            SystemActions.ScheduleShutdown(seconds);
                        }
                    }, "ScheduleShutdown");
                };
                itemShutdown.DropDownItems.Add(sub);
            }

            int[] minutes = { 5, 10, 15, 30, 45 };
            foreach (var m in minutes)
            {
                AddShutdownItem(m + " " + LanguageManager.T("Menu.MinutesLater"), m * 60);
            }

            int[] hours = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 24 };
            foreach (var h in hours)
            {
                AddShutdownItem(h + " " + LanguageManager.T("Menu.HoursLater"), h * 3600);
            }

            itemShutdown.DropDownItems.Add(new ToolStripSeparator());
            AddShutdownItem(LanguageManager.T("Menu.CancelShutdown"), 0);

            moreRoot.DropDownItems.Add(itemShutdown);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            // 5.5 隐藏托盘图标
            var hideTrayIcon = new ToolStripMenuItem(LanguageManager.T("Menu.HideTrayIcon") ?? "隐藏托盘图标")
            {
                Checked = cfg.HideTrayIcon,
                CheckOnClick = true
            };
            hideTrayIcon.CheckedChanged += (_, __) =>
            {
                bool hideTray = hideTrayIcon.Checked;
                PostMenuAction(() =>
                {
                    if (hideTray)
                    {
                        if (!ConfirmAction("确定要隐藏托盘图标吗？隐藏后只能通过快捷键或设置恢复（若主界面也被隐藏）。"))
                        {
                            return;
                        }
                    }
                    AppActions.ApplyTrayIconVisibility(cfg, form, hideTray);
                }, "ApplyTrayIconVisibility");
            };
            moreRoot.DropDownItems.Add(hideTrayIcon);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());
            // 6. 重启软件 (App)
            var itemRestartApp = new ToolStripMenuItem(LanguageManager.T("Menu.RestartApp"));
            itemRestartApp.Click += (_, __) =>
            {
                PostMenuAction(() =>
                {
                    if (ConfirmAction("即将重启 CS2交易监控，是否继续？"))
                    {
                        SystemActions.RestartApplication();
                    }
                }, "RestartApplication");
            };
            moreRoot.DropDownItems.Add(itemRestartApp);

            moreRoot.DropDownItems.Add(new ToolStripSeparator());

            var itemJoinQq = new ToolStripMenuItem(LanguageManager.T("Menu.JoinQqGroup") ?? "加入 QQ 群");
            itemJoinQq.Click += (_, __) =>
            {
                PostMenuAction(() =>
                {
                    try
                    {
                        SupportInfo.OpenQqGroup();
                    }
                    catch (Exception ex)
                    {
                        SupportInfo.ShowOpenFailure(ex, form);
                    }
                }, "OpenQqGroup");
            };
            moreRoot.DropDownItems.Add(itemJoinQq);

            var itemCopyQq = new ToolStripMenuItem(LanguageManager.T("Menu.CopyQqGroup") ?? "复制 QQ 群号");
            itemCopyQq.Click += (_, __) =>
            {
                PostMenuAction(() =>
                {
                    try
                    {
                        SupportInfo.CopyQqGroupNumber();
                        SupportInfo.ShowCopySuccess(form);
                    }
                    catch (Exception ex)
                    {
                        GlobalPromptService.Show(form, "复制 QQ 群号失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }, "CopyQqGroup");
            };
            moreRoot.DropDownItems.Add(itemCopyQq);

            menu.Items.Add(moreRoot);
            menu.Items.Add(new ToolStripSeparator());



            var itemExit = new ToolStripMenuItem(LanguageManager.T("Menu.Exit"));
            itemExit.Click += (_, __) => PostMenuAction(form.Close, "ExitApplication");
            menu.Items.Add(itemExit);

            return menu;
        }
    }
}
