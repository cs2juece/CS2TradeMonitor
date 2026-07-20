using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Application.YouPin;
using System;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.src.UI;
using CS2TradeMonitor.src.SystemServices;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using CS2TradeMonitor.src.UI.Framework;

namespace CS2TradeMonitor.src.Core.Actions
{
    /// <summary>
    /// 全局动作执行器
    /// 封装所有“修改配置后需要立即生效”的业务逻辑
    /// 供 MenuManager (右键菜单) 和 SettingsForm (设置中心) 共同调用
    /// </summary>
    public static class AppActions
    {
        public const string MainPanelFloatTab = "Float";
        public const string MainPanelTaskbarTab = "Taskbar";

        public static void ShowInterfaceSettings(Settings cfg, UIController? ui, MainForm form, string mainPanelTab, bool modal)
        {
            if (ui == null)
            {
                GlobalPromptService.Show("设置面板暂未初始化完成，请稍后重试。", "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string targetTab = string.IsNullOrWhiteSpace(mainPanelTab)
                ? MainPanelFloatTab
                : mainPanelTab;

            Form? openSettingsWindow = FindOpenSettingsWindow();
            if (openSettingsWindow != null)
            {
                ActivateSettingsWindow(openSettingsWindow, form);
                SwitchSettingsMainPanelTab(openSettingsWindow, targetTab);
                return;
            }

            Form settings = CreateSettingsWindow(cfg, ui, form, "MainPanel");
            PrepareSettingsWindow(settings, form);
            settings.Shown += (_, __) => SwitchSettingsMainPanelTab(settings, targetTab);
            if (modal)
            {
                using (settings)
                {
                    settings.ShowDialog();
                }
            }
            else
            {
                settings.Show();
                ActivateSettingsWindow(settings, form);
                DiagnosticsLogger.Info("Settings", $"Opened interface settings window. Tab={DiagnosticsLogger.Redact(targetTab)}");
            }
        }

        public static void ShowSettingsPage(Settings cfg, UIController? ui, MainForm form, string pageKey, bool modal)
        {
            pageKey = SettingsPageRegistry.NormalizeKey(pageKey);

            if (ui == null)
            {
                GlobalPromptService.Show("设置面板暂未初始化完成，请稍后重试。", "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Form? openSettingsWindow = FindOpenSettingsWindow();
            if (openSettingsWindow != null)
            {
                ActivateSettingsWindow(openSettingsWindow, form);
                SwitchSettingsPage(openSettingsWindow, pageKey);
                return;
            }

            Form settings = CreateSettingsWindow(cfg, ui, form, pageKey);
            PrepareSettingsWindow(settings, form);
            if (modal)
            {
                using (settings)
                {
                    settings.ShowDialog();
                }
            }
            else
            {
                settings.Show();
                ActivateSettingsWindow(settings, form);
                DiagnosticsLogger.Info("Settings", $"Opened settings window. Page={DiagnosticsLogger.Redact(pageKey)}");
            }
        }

        public static bool HasOpenSettingsWindow()
        {
            return FindOpenSettingsWindow() != null;
        }

        public static bool TrySwitchOpenSettingsMainPanelTab(string tabKey)
        {
            Form? openSettingsWindow = FindOpenSettingsWindow();
            if (openSettingsWindow == null)
                return false;

            SwitchSettingsMainPanelTab(openSettingsWindow, tabKey);
            return true;
        }

        private static Form CreateSettingsWindow(Settings cfg, UIController ui, MainForm form, string initialPageKey = "MainPanel")
        {
            return new SettingsForm(cfg, ui, form, initialPageKey, SettingsFormRuntimeServices.Resolve());
        }

        private static Form? FindOpenSettingsWindow()
        {
            foreach (Form openForm in System.Windows.Forms.Application.OpenForms)
            {
                if (IsSettingsWindow(openForm))
                    return openForm;
            }

            return null;
        }

        private static bool IsSettingsWindow(Form form)
        {
            return form is SettingsForm;
        }

        private static void PrepareSettingsWindow(Form settingsWindow, MainForm owner)
        {
            settingsWindow.Owner = null;
            // 主悬浮窗可能是 TopMost；设置窗必须跟随置顶层级，否则会被实际悬浮窗遮挡控件。
            settingsWindow.TopMost = owner.TopMost;
        }

        private static void ActivateSettingsWindow(Form settingsWindow, MainForm owner)
        {
            PrepareSettingsWindow(settingsWindow, owner);
            if (settingsWindow.WindowState == FormWindowState.Minimized)
            {
                settingsWindow.WindowState = FormWindowState.Normal;
            }

            settingsWindow.BringToFront();
            settingsWindow.Activate();
        }

        private static void SwitchSettingsPage(Form settingsWindow, string pageKey)
        {
            if (settingsWindow is SettingsForm settingsForm)
            {
                settingsForm.SwitchPage(pageKey);
            }
        }

        private static void SwitchSettingsMainPanelTab(Form settingsWindow, string tabKey)
        {
            if (settingsWindow is SettingsForm settingsForm)
            {
                settingsForm.SwitchMainPanelTab(tabKey);
            }
        }

        public static void ApplyHorizontalMode(Settings cfg, UIController? ui, MainForm form, bool horizontalMode)
        {
            bool oldMode = ui?.IsLayoutHorizontal ?? cfg.HorizontalMode;
            cfg.HorizontalMode = horizontalMode;
            cfg.Save();
            ApplyThemeAndLayout(cfg, ui, form, oldMode);
        }

        public static void ApplyTaskbarVisibility(Settings cfg, MainForm form, bool showTaskbar)
        {
            cfg.ShowTaskbar = showTaskbar;
            cfg.Save();
            ApplyVisibility(cfg, form);
            cfg.Save();
        }

        public static void ApplyMainFormVisibility(Settings cfg, MainForm form, bool hideMainForm)
        {
            cfg.HideMainForm = hideMainForm;
            cfg.Save();
            ApplyVisibility(cfg, form);
            cfg.Save();
        }

        public static void ApplyTrayIconVisibility(Settings cfg, MainForm form, bool hideTrayIcon)
        {
            cfg.HideTrayIcon = hideTrayIcon;
            cfg.Save();
            ApplyVisibility(cfg, form);
            cfg.Save();
        }

        public static void ApplyMainClickThrough(Settings cfg, MainForm form, bool clickThrough)
        {
            cfg.ClickThrough = clickThrough;
            cfg.Save();
            ApplyVisibility(cfg, form);
            ApplyWindowAttributes(cfg, form);
            cfg.Save();
        }

        public static void ApplyTaskbarClickThrough(Settings cfg, UIController? ui, MainForm form, bool clickThrough)
        {
            cfg.TaskbarClickThrough = clickThrough;
            cfg.Save();
            ApplyVisibility(cfg, form);
            ApplyTaskbarStyle(cfg, ui);
            cfg.Save();
        }

        // ★★★ 新增：全局应用入口 ★★★
        public static void ApplyAllSettings(Settings cfg, MainForm mainForm, UIController ui, bool applyAutoStart = false)
        {
            ApplyAllSettings(cfg, mainForm, ui, AppActionRuntimeServices.Resolve(), applyAutoStart);
        }

        private static void ApplyAllSettings(Settings cfg, MainForm mainForm, UIController ui, AppActionRuntimeServices services, bool applyAutoStart)
        {
            // 0. [核心修复] 在任何逻辑执行前，记录当前的渲染模式
            // 防止 ApplyLanguage -> ApplyTheme 提前重置了 ui 的状态导致无法判断是否切换了模式
            bool wasHorizontal = ui.IsLayoutHorizontal;

            // 1. 语言变更 (注意：传 null 给 ui，防止它在此提前触发 ApplyTheme)
            ApplyLanguage(cfg, null, mainForm);

            // 2. 系统级设置
            // 开机启动会调用 schtasks 写计划任务，只能在开关变化时执行，避免每次应用设置都触发杀软拦截。
            if (applyAutoStart) ApplyAutoStart(cfg);
            mainForm.ConfigureAutomaticSoftwareUpdateChecks(cfg.AutoCheckSoftwareUpdates);
            MarketDataSourceManager.Configure(cfg);
            services.YouPinInventory.Configure(cfg);
            services.YouPinSaleReminders.Configure(cfg);
            services.YouPinGridTrading.Configure(cfg);
            services.MarketAlerts.ApplySettings(cfg);
            _ = RefreshMarketDataAsync(mainForm, services.RenderScheduler);
            ApplyWindowAttributes(cfg, mainForm); // 基础窗口属性

            // 3. 界面布局与主题 (传入原始状态以判断是否需要居中)
            ApplyThemeAndLayout(cfg, ui, mainForm, wasHorizontal);

            // 4. 子模块特定设置
            ApplyMonitorLayout(ui, mainForm); // 显示项目与市场数据源状态变更
            ApplyTaskbarStyle(cfg, ui);       // 任务栏样式

            // 6. 应用插件设置 (重载实例并清除缓存)
            // 旧 PluginManager 已移除，保留步骤位置便于对照历史应用顺序。

            // 7. 可见性 (最后执行，避免闪烁)
            ApplyVisibility(cfg, mainForm);
        }


        // =============================================================
        // 1. 核心系统动作 (语言、开机自启)
        // =============================================================

        public static void ApplyLanguage(Settings cfg, UIController? ui, MainForm form)
        {
            // 1. 加载语言资源
            LanguageManager.Load(cfg.Language);

            // 2. 同步自定义名称 (防止语言包覆盖了用户的自定义重命名)
            cfg.SyncToLanguage();

            // 3. 刷新主题（这也同时刷新了字体、布局计算、Timer间隔等）
            ui?.ApplyTheme(cfg.Skin);

            // 4. 重建右键菜单（更新文字）
            form.RebuildMenus();

            // 5. 刷新任务栏窗口（如果有）
            ReloadTaskbarWindows();
        }

        public static void ApplyAutoStart(Settings cfg)
        {
            AutoStart.Set(cfg.AutoStart);
        }

        // =============================================================
        // 2. 窗口行为与属性 (置顶、穿透、自动隐藏、透明度)
        // =============================================================

        public static void ApplyWindowAttributes(Settings cfg, MainForm form)
        {
            // 置顶
            if (form.TopMost != cfg.TopMost) form.TopMost = cfg.TopMost;
            form.RefreshTopMost(forceReinsert: true);
            SyncOpenSettingsFormsTopMost(form);

            // 系统任务栏按钮。
            if (form.ShowInTaskbar != cfg.ShowMainWindowInTaskbar)
                form.SetWindowsTaskbarButton(cfg.ShowMainWindowInTaskbar);

            // 鼠标穿透
            form.SetClickThrough(cfg.ClickThrough);

            // 自动隐藏 (需要启动或停止 Timer)
            if (cfg.AutoHide) form.InitAutoHideTimer();
            else form.StopAutoHideTimer(restoreHidden: true);

            // 透明度由绘制层分别处理背景/文字 alpha；不要使用 Form.Opacity，避免干扰 per-pixel alpha。

            ApplyPanelBackground(cfg, form);

            // 5. 刷新菜单 (确保透明度、置顶等勾选状态同步更新)
            form.RebuildMenus();
        }

        private static void SyncOpenSettingsFormsTopMost(MainForm owner)
        {
            foreach (Form openForm in System.Windows.Forms.Application.OpenForms)
            {
                if (!IsSettingsWindow(openForm)) continue;

                PrepareSettingsWindow(openForm, owner);
                if (openForm.Visible && openForm.WindowState != FormWindowState.Minimized)
                {
                    openForm.BringToFront();
                }
            }
        }

        private static void ApplyPanelBackground(Settings cfg, MainForm form)
        {
            string background = !string.IsNullOrWhiteSpace(cfg.PanelBackgroundColor)
                ? cfg.PanelBackgroundColor
                : ThemeManager.Current.Color.Background;

            form.BackColor = ThemeManager.ParseColor(background);
            form.RequestLayeredRender();
        }

        // =============================================================
        // 3. 窗口可见性管理 (主界面、托盘、任务栏) - 含防呆
        // =============================================================

        public static void ApplyVisibility(Settings cfg, MainForm form)
        {
            // --- 防呆逻辑 ---
            // 检查是否存在交互死锁（界面隐藏或穿透 + 任务栏关闭或穿透 + 托盘隐藏）
            bool noInteractiveWindow = Settings.HasNoInteractiveEntry(cfg);

            if (noInteractiveWindow)
            {
                // 如果全关了，强制打开托盘图标
                cfg.HideTrayIcon = false;
                cfg.Save();
                DiagnosticsLogger.Info("Visibility", "Unsafe hidden state corrected by keeping tray icon visible.");
            }

            // --- 执行动作 ---

            // 1. 托盘
            if (cfg.HideTrayIcon) form.HideTrayIcon();
            else form.ShowTrayIcon();

            // 2. 主窗口
            // 当 HideMainForm = true 时，这里需要执行 Hide()。
            if (cfg.HideMainForm) form.HideMainWindow();
            else form.ShowMainWindow();

            // 3. 任务栏窗口
            form.ToggleTaskbar(cfg.ShowTaskbar);

            // 4. 刷新菜单
            // 因为可见性改变可能影响菜单项的勾选状态（尤其是防呆逻辑修正后），也可能影响“任务栏显示”等选项的状态
            form.RebuildMenus();
        }

        // =============================================================

        public static void ApplyThemeAndLayout(Settings cfg, UIController? ui, MainForm form, bool? wasHorizontal = null, bool retainData = false)
        {
            // 1. 确定是否发生了模式切换
            // 如果外部没传 wasHorizontal，则尝试从 ui 获取当前渲染状态
            bool oldMode = wasHorizontal ?? (ui?.IsLayoutHorizontal ?? cfg.HorizontalMode);
            bool modeChanged = (oldMode != cfg.HorizontalMode);

            Point? center = null;
            if (modeChanged && form.Visible && form.WindowState == FormWindowState.Normal)
            {
                center = new Point(form.Left + form.Width / 2, form.Top + form.Height / 2);
            }

            // 2. 应用主题
            ui?.ApplyTheme(cfg.Skin, retainData);

            // 3. 强制立即刷新布局以获得正确的新尺寸 (关键：否则 form.Width 还是旧的)
            ui?.RebuildLayout();
            form.RequestLayeredRender();


            // 如果切换了横竖屏模式，菜单结构会变，需要重建
            form.RebuildMenus();
            ReloadTaskbarWindows();

            // 4. 执行居中重定位
            if (center.HasValue)
            {
                form.ApplyRoundedCorners();
                form.Location = new Point(center.Value.X - form.Width / 2, center.Value.Y - form.Height / 2);
                form.EnsureVisibleAndSavePos();
            }
        }

        // =============================================================
        // 5. 数据源与监控项 (磁盘/网络源、监控开关)
        // =============================================================

        public static void ApplyMonitorLayout(UIController? ui, MainForm form, bool rebuildMenus = true)
        {
            // 重新计算哪些格子要显示 (主界面和任务栏的数据列都会重建)
            ui?.RebuildLayout();

            // 因为监控项变了（比如开启了GPU），菜单里的勾选状态也得变
            // 优化：在右键菜单直接操作时，无需重建菜单，避免冗余
            if (rebuildMenus)
            {
                form.RebuildMenus();
            }

            // 任务栏窗口的内容也取决于监控项配置，必须刷新
            ReloadTaskbarWindows();
        }

        // =============================================================
        // 6. 任务栏样式 (字体、对齐、紧凑模式)
        // =============================================================

        public static void ApplyTaskbarStyle(Settings cfg, UIController? ui)
        {
            // 1. 刷新所有任务栏窗口
            // 这一步会触发 TaskbarForm.ReloadLayout()，进而自动读取 cfg 中的新颜色和穿透设置
            ReloadTaskbarWindows();

            // 如果样式影响了主程序计算（极少情况），可解开下面注释
            ui?.ApplyTheme(cfg.Skin);
        }

        // --- 内部辅助 ---
        private static void ReloadTaskbarWindows()
        {
            // [Fix] 使用 ToList() 创建副本，防止 ReloadLayout -> TooltipHelper -> Dispose 修改 OpenForms 集合导致 Z-order 异常
            var targets = System.Windows.Forms.Application.OpenForms.OfType<TaskbarForm>().ToList();
            foreach (var tf in targets)
            {
                tf.ReloadLayout();
            }
        }

        private static async Task RefreshMarketDataAsync(MainForm mainForm, IRenderScheduler renderScheduler)
        {
            try
            {
                await MarketDataSourceManager.RefreshAllAsync();
            }
            catch
            {
                // 设置应用时只做静默刷新，失败状态由数据源页和下次定时刷新呈现。
            }
            finally
            {
                InvalidateMarketSurfaces(mainForm, renderScheduler);
            }
        }

        public static async Task ForceRefreshMarketDataAsync(MainForm mainForm)
        {
            await ForceRefreshMarketDataAsync(mainForm, AppActionRuntimeServices.Resolve().RenderScheduler);
        }

        private static async Task ForceRefreshMarketDataAsync(MainForm mainForm, IRenderScheduler renderScheduler)
        {
            try
            {
                await MarketDataSourceManager.RefreshAllAsync(waitForSteamDtLock: true);
            }
            catch (Exception ex)
            {
                CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored("MarketData", "ForceRefreshMarketData", ex, retryable: true, category: "Refresh");
            }
            finally
            {
                InvalidateMarketSurfaces(mainForm, renderScheduler);
            }
        }

        private static void InvalidateMarketSurfaces(MainForm mainForm, IRenderScheduler renderScheduler)
        {
            void RefreshUi()
            {
                mainForm.RequestLayeredRender();
                foreach (var tf in System.Windows.Forms.Application.OpenForms.OfType<TaskbarForm>().ToList())
                {
                    renderScheduler.RequestRender(tf);
                }
            }

            if (mainForm.IsDisposed) return;
            if (mainForm.InvokeRequired)
            {
                try { mainForm.BeginInvoke(new Action(RefreshUi)); } catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
            }
            else
            {
                RefreshUi();
            }
        }

        public static string SanitizeError(string err)
        {
            if (string.IsNullOrWhiteSpace(err)) return "";

            // 1. 去掉换行，替换为单个空格
            err = err.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");

            // 2. 清除 URL 里的 query string 及其敏感细节 (如: ?key=xxx)
            int queryIdx = err.IndexOf('?');
            if (queryIdx >= 0)
            {
                err = err.Substring(0, queryIdx) + "...";
            }

            // 3. 清理 Bearer/Token/Key/APIKey/Authorization/Secret/api_key/api-key 等敏感字段
            // 模式 1：键值对形式，例如 key: value、key = value、"key": "value"。
            string patternKv = @"(?i)([""']?)\b(bearer|token|key|apikey|api_key|api-key|authorization|secret)\b\1(\s*[:=]\s*)(([""'])(bearer\s+)?([^\5\r\n]+)\5|(bearer\s+)?([a-zA-Z0-9_\-\.\+=/~]+))";
            err = System.Text.RegularExpressions.Regex.Replace(err, patternKv, m =>
            {
                string g1 = m.Groups[1].Value; // 键名引号
                string g2 = m.Groups[2].Value; // 敏感字段名
                string g3 = m.Groups[3].Value; // 带空格的分隔符
                string g5 = m.Groups[5].Value; // 值引号（如果存在）

                if (!string.IsNullOrEmpty(g5))
                {
                    string bearer = m.Groups[6].Value;
                    return $"{g1}{g2}{g1}{g3}{g5}{bearer}***{g5}";
                }
                else
                {
                    string bearer = m.Groups[8].Value;
                    return $"{g1}{g2}{g1}{g3}{bearer}***";
                }
            });

            // 模式 2：Bearer/Token 后跟空格和值，例如 "bearer 12345" 或 "token 12345"。
            string patternBearer = @"(?i)\b(bearer|token)\b(\s+)(([""'])([^\4\r\n]+)\4|([a-zA-Z0-9_\-\.\+=/~]+))";
            err = System.Text.RegularExpressions.Regex.Replace(err, patternBearer, m =>
            {
                string g1 = m.Groups[1].Value; // 敏感字段名
                string g2 = m.Groups[2].Value; // 空格
                string g4 = m.Groups[4].Value; // 引号（如果存在）
                if (!string.IsNullOrEmpty(g4))
                {
                    return $"{g1}{g2}{g4}***{g4}";
                }
                return $"{g1}{g2}***";
            });

            // 4. 长度截断
            if (err.Length > 100)
            {
                err = err.Substring(0, 97) + "...";
            }

            return err.Trim();
        }

        public static DataSourceStatusSummary GetDataSourceSummary(Settings cfg)
        {
            var summary = new DataSourceStatusSummary();

            foreach (var state in MarketDataSourceManager.GetStates(cfg))
            {
                if (state.Id == MarketDataSourceManager.QaqId)
                {
                    summary.QaqType = state.TypeDescription;
                    summary.QaqStatus = state.Status;
                    summary.QaqLastRefresh = state.LastRefresh;
                    summary.QaqError = SanitizeError(state.LastError);
                }
                else if (state.Id == MarketDataSourceManager.SteamDtId)
                {
                    summary.SteamDtType = state.TypeDescription;
                    summary.SteamDtConfigState = state.ConfigState;
                    summary.SteamDtStatus = state.Status;
                    summary.SteamDtLastRefresh = state.LastRefresh;
                    summary.SteamDtError = SanitizeError(state.LastError);
                }
            }

            return summary;
        }
    }

    public class DataSourceStatusSummary
    {
        public string QaqType { get; set; } = "公开接口/无需 API";
        public string QaqStatus { get; set; } = "未获取"; // 正常 / 异常 / 未获取
        public string QaqLastRefresh { get; set; } = ""; // HH:mm:ss
        public string QaqError { get; set; } = "";

        public string SteamDtType { get; set; } = "需要 API Key";
        public string SteamDtConfigState { get; set; } = "未配置 API Key"; // 未配置 API Key / 已配置 API Key
        public string SteamDtStatus { get; set; } = "未获取"; // 正常 / 异常 / 未获取
        public string SteamDtLastRefresh { get; set; } = ""; // HH:mm:ss
        public string SteamDtError { get; set; } = "";

        // 已格式化为可直接给 UI 展示的字符串。
        public string QaqTypeLine => $"QAQ: {QaqType}";
        public string QaqStatusLine
        {
            get
            {
                if (QaqStatus == "正常")
                    return $"QAQ: 正常 (上次刷新 {QaqLastRefresh})";
                else if (QaqStatus == "异常")
                    return $"QAQ: 异常 ({QaqError})";
                else
                    return "QAQ: 未获取";
            }
        }

        public string SteamDtTypeLine => $"SteamDT: {SteamDtType} ({SteamDtConfigState})";
        public string SteamDtStatusLine
        {
            get
            {
                if (SteamDtStatus == "正常")
                    return $"SteamDT: 正常 ({SteamDtType}，上次刷新 {SteamDtLastRefresh})";
                else if (SteamDtStatus == "异常")
                    return $"SteamDT: 异常 ({SteamDtError})";
                else
                    return "SteamDT: 未获取";
            }
        }

        public string QaqTrayText
        {
            get
            {
                if (QaqStatus == "正常")
                    return $"QAQ: 正常 ({QaqLastRefresh})";
                else if (QaqStatus == "异常")
                    return $"QAQ: 异常({QaqError})";
                else
                    return "QAQ: 未获取";
            }
        }

        public string SteamDtTrayText
        {
            get
            {
                if (SteamDtStatus == "正常")
                    return $"SteamDT: 正常 ({SteamDtType}，{SteamDtLastRefresh})";
                else if (SteamDtStatus == "异常")
                    return $"SteamDT: 异常({SteamDtError})";
                else if (SteamDtConfigState == "未配置 API Key")
                    return "SteamDT: 等待公开页面接口刷新";
                else
                    return "SteamDT: 未获取";
            }
        }
    }
}
