using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Text.Json.Serialization;
using CS2TradeMonitor.src.Core;
namespace CS2TradeMonitor
{
    public class Settings
    {
        public const int DefaultMarketRefreshSec = 500;

        // ====== 基础设置 ======
        public string Skin { get; set; } = "DarkFlat_Classic";
        public bool TopMost { get; set; } = false;
        public bool AutoStart { get; set; } = true;
        public bool ShowMainWindowInTaskbar { get; set; } = false;
        public int RefreshMs { get; set; } = 1000;
        public double AnimationSpeed { get; set; } = 0.35;
        public Point Position { get; set; } = new Point(-1, -1);

        // ====== SteamDT 市场监控 ======
        public string SteamDtApiKey { get; set; } = "";
        public int SteamDtRefreshSec { get; set; } = DefaultMarketRefreshSec;
        public bool SteamDtCompactMode { get; set; } = false;
        public bool SteamDtShowPercent { get; set; } = true;
        public string SteamDtPositiveColor { get; set; } = "#FF4444";
        public string SteamDtNegativeColor { get; set; } = "#00CC66";
        public string SteamDtWarningColor { get; set; } = "#FFFF00";
        public string SteamDtNeutralColor { get; set; } = "#FFFFFF";
        public string SteamDtBackgroundColor { get; set; } = "#3C3C3C";

        // ====== CSQAQ 市场监控 ======
        public int CsqaqRefreshSec { get; set; } = DefaultMarketRefreshSec;
        public string CsqaqApiToken { get; set; } = "";

        // ====== 界面与行为 ======
        public bool HorizontalMode { get; set; } = false;
        public double Opacity { get; set; } = 1.0;
        public double PanelBackgroundOpacity { get; set; } = 0.0;
        public double TextOpacity { get; set; } = 1.0;
        public string Language { get; set; } = "zh";
        public bool ClickThrough { get; set; } = false;
        public bool AutoHide { get; set; } = false;
        public bool ClampToScreen { get; set; } = true;
        public bool LockPosition { get; set; } = false;
        public int PanelWidth { get; set; } = 220;
        public double UIScale { get; set; } = 1.0;
        public string PanelBackgroundColor { get; set; } = "";

        // ====== 硬件相关 ======
        public string PreferredNetwork { get; set; } = "";
        public string LastAutoNetwork { get; set; } = "";
        public string PreferredDisk { get; set; } = "";
        public string LastAutoDisk { get; set; } = "";
        public string PreferredGpu { get; set; } = "";

        // ★★★ [新增] 首选风扇 ★★★
        public string PreferredCpuFan { get; set; } = "";
        public string PreferredCpuPump { get; set; } = ""; // 保存用户选的水冷接口
        public string PreferredCaseFan { get; set; } = "";
        public string PreferredMoboTemp { get; set; } = "";

        // 主窗体所在的屏幕设备名 (用于记忆上次位置)
        public string ScreenDevice { get; set; } = "";

        // ====== 任务栏 ======
        public bool ShowTaskbar { get; set; } = true;
        // ★★★ 新增：横条模式是否跟随任务栏布局？ ★★★
        public bool HorizontalFollowsTaskbar { get; set; } = false;
        public bool HideMainForm { get; set; } = false;
        public bool HideTrayIcon { get; set; } = false;
        public bool TaskbarAlignLeft { get; set; } = false;

        // ★★★ 大盘监控格式：0=Price+Rate, 1=Price+Change, 2=RateOnly, 3=PriceOnly, 4=Full, 5=ShortLabel
        public int MarketFormat { get; set; } = 0;

        // ★★★ 是否开启高级设置选项 (折叠显示) ★★★
        public bool AdvancedMode { get; set; } = false;
        public bool SettingsPanelDarkMode { get; set; } = true;
        public int SettingsPanelWindowWidth { get; set; } = 0;
        public int SettingsPanelWindowHeight { get; set; } = 0;
        public bool SettingsPanelWindowMaximized { get; set; } = false;

        // ★★★ 任务栏：预设模式选择 (1=粗字模式, 0=细字模式) ★★★
        public int TaskbarPresetStyle { get; set; } = 1;

        // ★★★ 任务栏：自定义布局参数 ★★★
        // 开启后，将忽略预设的"粗体/细体"逻辑，强制使用以下参数
        public bool TaskbarCustomLayout { get; set; } = true;

        public string TaskbarFontFamily { get; set; } = DEFAULT_TB_FONT;
        public float TaskbarFontSize { get; set; } = DEFAULT_TB_SIZE_BOLD;
        public bool TaskbarFontBold { get; set; } = true;

        // 间距配置 (单位: px, 会自动随 DPI 缩放)
        public int TaskbarItemSpacing { get; set; } = DEFAULT_TB_GAP;      // 组与组之间的间距
        public int TaskbarInnerSpacing { get; set; } = DEFAULT_TB_INNER_BOLD;     // 标签与数值之间的间距
        public int TaskbarVerticalPadding { get; set; } = DEFAULT_TB_VOFF;  // 垂直方向的微调/行间距

        // ★★★ [新增] 横条/桌面模式 间距配置 ★★★
        public int HorizontalItemSpacing { get; set; } = 12;  // 组间距 (默认 12)
        public int HorizontalInnerSpacing { get; set; } = 8;  // 标签与值间距 (默认 8)
        public bool HorizontalSingleLine { get; set; } = false; // [新增] 横条模式单行显示

        // ★★★ 常量定义：用于 GetStyle 中的默认策略 ★★★
        public const string DEFAULT_TB_FONT = "Microsoft YaHei UI"; // 中文版任务栏默认字体
        public const float DEFAULT_TB_SIZE_BOLD = 10f; // 粗字模式默认字号（提升可读性）
        public const float DEFAULT_TB_SIZE_REGULAR = 9f; // 细字模式默认字号（节省空间，更精致）
        public const int DEFAULT_TB_GAP = 6; // 任务栏组间距默认值（不同监控项之间的距离）
        public const int DEFAULT_TB_INNER_BOLD = 8; // 粗字模式标签与值间距默认值（更宽的间距适配粗体）
        public const int DEFAULT_TB_INNER_REGULAR = 6; // 细字模式标签与值间距默认值（紧凑布局）
        public const int DEFAULT_TB_VOFF = 2; // 任务栏垂直内边距默认值（用于垂直居中微调）

        public static bool HasNoInteractiveEntry(Settings s)
        {
            return (s.HideMainForm || s.ClickThrough)
                && !s.ShowTaskbar
                && s.HideTrayIcon;
        }


        // ★★★ 新增：指定任务栏显示的屏幕设备名 ("" = 自动/主屏) ★★★
        public string TaskbarMonitorDevice { get; set; } = "";

        // 任务栏行为配置
        public bool TaskbarClickThrough { get; set; } = false; // 仅保留旧配置兼容；任务栏始终可交互。
        public bool TaskbarSingleLine { get; set; } = false;// 单行显示
        public bool TaskbarHoverShowAll { get; set; } = true; // [新增] 悬浮显示所有监控项
        public int TaskbarManualOffset { get; set; } = 0;// 手动偏移量 (像素)

        // ====== 任务栏：高级自定义外观 ======
        public bool TaskbarCustomStyle { get; set; } = true; // 兼容旧配置，任务栏颜色现在默认生效
        public string TaskbarColorLabel { get; set; } = "#FFFFFF"; // 标签颜色
        public string TaskbarColorSafe { get; set; } = "#00CC66";  // 正常 (淡绿)
        public string TaskbarColorWarn { get; set; } = "#FFFF00";  // 警告 (金黄)
        public string TaskbarColorCrit { get; set; } = "#FF4444";  // 严重 (红色)
        public string TaskbarColorBg { get; set; } = "#001E3D";    // 防杂边背景色 (透明键)

        // 双击动作配置
        public int MainFormDoubleClickAction { get; set; } = 2; // 默认直接打开设置面板
        public int TaskbarDoubleClickAction { get; set; } = 2; // 默认打开任务栏设置

        // 内存/显存显示模式
        public int MemoryDisplayMode { get; set; } = 1;

        // ★ 2. 运行时缓存：存储探测到的总容量 (GB)
        [JsonIgnore] public static float DetectedRamTotalGB { get; set; } = 0;
        [JsonIgnore] public static float DetectedGpuVramTotalGB { get; set; } = 0;

        // 开启后：CPU使用率、CPU频率、内存占用、磁盘读写 将优先从 Windows 计数器读取
        public bool UseWinPerCounters { get; set; } = true;

        // ====== 记录与报警 ======
        public float RecordedMaxCpuPower { get; set; } = 65.0f;
        public float RecordedMaxCpuClock { get; set; } = 4200.0f;
        public float RecordedMaxGpuPower { get; set; } = 100.0f;
        public float RecordedMaxGpuClock { get; set; } = 1800.0f;

        // ★★★ [新增] FPS 固定最大值 (用于进度条上限，推荐 144) ★★★
        public float RecordedMaxFps { get; set; } = 144.0f;

        // ★★★ [新增] 风扇最大值记录 ★★★
        public float RecordedMaxCpuFan { get; set; } = 4000;
        public float RecordedMaxCpuPump { get; set; } = 5000; // 水冷最大转速 (用于百分比计算)
        public float RecordedMaxGpuFan { get; set; } = 3500;
        public float RecordedMaxChassisFan { get; set; } = 3000;

        public bool MaxLimitTipShown { get; set; } = false;

        public bool AlertTempEnabled { get; set; } = true;
        public int AlertTempThreshold { get; set; } = 80;

        // ====== 大盘预警 ======
        public bool DoNotDisturbEnabled { get; set; } = false;
        public bool MarketAlertsEnabled { get; set; } = false;
        public bool MarketAlertDeferWhenFullscreen { get; set; } = true;
        public MarketAlertNotificationMode MarketAlertNotificationMode { get; set; } = MarketAlertNotificationMode.DesktopToast;
        public int MarketAlertDefaultWindowMinutes { get; set; } = 10;
        public int MarketAlertDefaultCooldownMinutes { get; set; } = 5;
        public List<MarketAlertRule> MarketAlertRules { get; set; } = CreateDefaultMarketAlertRules();

        // ====== 手机提醒 ======
        public bool PhoneAlertEnabled { get; set; } = false;
        public string PhoneAlertProvider { get; set; } = "ServerChan";
        public string ServerChanSendKey { get; set; } = "";
        public string WxPusherSpt { get; set; } = "";
        public PhoneAlertDispatchMode PhoneAlertDispatchMode { get; set; } = PhoneAlertDispatchMode.Failover;
        public List<PhoneAlertChannelConfig> PhoneAlertChannels { get; set; } = new();

        // ====== CS2 更新提醒 ======
        public bool Cs2UpdateReminderEnabled { get; set; } = true;
        public int Cs2UpdateReminderRefreshSec { get; set; } = 600;
        public bool Cs2UpdateReminderWechatEnabled { get; set; } = true;
        public bool Cs2UpdateReminderSoundEnabled { get; set; } = false;
        public string Cs2UpdateBaselineKey { get; set; } = "";
        public string Cs2UpdateBaselineTitle { get; set; } = "";
        public long Cs2UpdateBaselinePublishedAt { get; set; } = 0;
        public long Cs2UpdateLastCheckTime { get; set; } = 0;
        public string Cs2UpdateLastStatus { get; set; } = "未检查";

        public ThresholdsSet Thresholds { get; set; } = new ThresholdsSet();

        [JsonIgnore] public DateTime LastAlertTime { get; set; } = DateTime.MinValue;
        [JsonIgnore] public long SessionUploadBytes { get; set; } = 0;
        [JsonIgnore] public long SessionDownloadBytes { get; set; } = 0;
        [JsonIgnore] public DateTime LastAutoSaveTime { get; set; } = DateTime.MinValue;

        public Dictionary<string, string> GroupAliases { get; set; } = new Dictionary<string, string>();
        public List<MonitorItemConfig> MonitorItems { get; set; } = new List<MonitorItemConfig>();
        public List<ItemMonitorConfig> ItemConfigs { get; set; } = new List<ItemMonitorConfig>();
        public int DefaultItemRefreshIntervalSec { get; set; } = 600;
        public bool ItemMonitorDefaultVisibleInPanel { get; set; } = false;
        public bool ItemMonitorDefaultVisibleInTaskbar { get; set; } = false;
        public double DefaultItemPriceAlertRisePercent { get; set; } = 0;
        public double DefaultItemPriceAlertFallPercent { get; set; } = 0;
        public int DefaultItemPriceAlertWindowMinutes { get; set; } = 10;
        public int DefaultItemPriceAlertCooldownMinutes { get; set; } = 10;

        // ====== 悠悠有品库存读取 ======
        public bool YouPinInventoryEnabled { get; set; } = false;
        public string YouPinInventoryToken { get; set; } = "";
        public string YouPinInventoryDeviceToken { get; set; } = "";
        public int YouPinInventoryRefreshSec { get; set; } = 1800;
        public int YouPinTrendPageRefreshSec { get; set; } = 300;
        public string YouPinCcLastTab { get; set; } = "InventoryTrend";
        public bool YouPinSaleReminderEnabled { get; set; } = false;
        public int YouPinSaleReminderRefreshSec { get; set; } = 180;
        public bool YouPinSaleReminderIncludeAllTodos { get; set; } = false;
        public YouPinSaleReminderNotificationMode YouPinSaleReminderNotificationMode { get; set; } = YouPinSaleReminderNotificationMode.Bubble;
        public bool YouPinQuoteAutoRefreshEnabled { get; set; } = true;
        public int YouPinQuoteAutoRefreshSec { get; set; } = 180;
        public bool YouPinMsgCenterEnabled { get; set; } = false;
        public int YouPinMsgCenterRefreshSec { get; set; } = 60;
        public YouPinSaleReminderNotificationMode YouPinMsgCenterNotificationMode { get; set; } = YouPinSaleReminderNotificationMode.Bubble;
        public bool YouPinInventoryChangeAlertEnabled { get; set; } = false;
        public double YouPinInventoryRisePercentThreshold { get; set; } = 3;
        public double YouPinInventoryFallPercentThreshold { get; set; } = 3;
        public double YouPinInventoryChangeAmountThreshold { get; set; } = 0;
        public int YouPinInventoryChangeAlertCooldownMinutes { get; set; } = 30;
        public YouPinSaleReminderNotificationMode YouPinInventoryChangeAlertNotificationMode { get; set; } = YouPinSaleReminderNotificationMode.Bubble;
        public bool YouPinStopProfitLossEnabled { get; set; } = false;
        public int YouPinStopProfitLossWindowMinutes { get; set; } = 180;
        public double YouPinStopProfitPercentThreshold { get; set; } = 30;
        public double YouPinStopLossPercentThreshold { get; set; } = 30;
        public int YouPinStopProfitLossCooldownMinutes { get; set; } = 30;
        public YouPinSaleReminderNotificationMode YouPinStopProfitLossNotificationMode { get; set; } = YouPinSaleReminderNotificationMode.BubbleAndSound;
        public bool YouPinStopProfitLossOnlySpecifiedItems { get; set; } = false;
        public string YouPinStopProfitLossSpecifiedItems { get; set; } = "";
        public string YouPinStopProfitLossExcludedItems { get; set; } = "";
        public string YouPinStopProfitLossItemRulesJson { get; set; } = "";
        public string YouPinStopProfitLossManualCostJson { get; set; } = "";
        public int YouPinLandlordPolicyVersion { get; set; } = 1;
        public bool YouPinLandlordZeroCdEnabled { get; set; } = false;
        public int YouPinLandlordZeroCdTargetRank { get; set; } = 3;
        public int YouPinLandlordZeroCdScanIntervalMinutes { get; set; } = 30;
        public int YouPinLandlordZeroCdExecutionIntervalMinutes { get; set; } = 30;
        public long YouPinLandlordZeroCdLastExecutionUnixMilliseconds { get; set; }
        public bool YouPinLandlordZeroCdSelectionInitialized { get; set; } = false;
        public string YouPinLandlordZeroCdSelectionScope { get; set; } = "PerAsset";
        public string YouPinLandlordZeroCdSelectedAssetIds { get; set; } = "";
        public string YouPinLandlordZeroCdSelectedItemNames { get; set; } = "";
        public bool YouPinLandlordZeroCdWeeklyFreeEnabled { get; set; } = false;
        public decimal YouPinLandlordZeroCdWeeklyFreeMinimumValue { get; set; } = 0m;
        public decimal YouPinLandlordZeroCdWeeklyFreeMaximumValue { get; set; } = 0m;
        public bool YouPinLandlordZeroCdCooldownEnabled { get; set; } = false;
        public int YouPinLandlordZeroCdCooldownStartMinute { get; set; } = 0;
        public int YouPinLandlordZeroCdCooldownEndMinute { get; set; } = 480;
        public bool YouPinLandlordInventoryRentalEnabled { get; set; } = false;
        public int YouPinLandlordInventoryRentalTargetRank { get; set; } = 5;
        public int YouPinLandlordInventoryRentalScanIntervalMinutes { get; set; } = 30;
        public int YouPinLandlordInventoryRentalExecutionIntervalMinutes { get; set; } = 30;
        public long YouPinLandlordInventoryRentalLastExecutionUnixMilliseconds { get; set; }
        public bool YouPinLandlordInventoryRentalSelectionInitialized { get; set; } = false;
        public string YouPinLandlordInventoryRentalSelectionScope { get; set; } = "PerAsset";
        public string YouPinLandlordInventoryRentalSelectedAssetIds { get; set; } = "";
        public string YouPinLandlordInventoryRentalSelectedItemNames { get; set; } = "";
        public bool YouPinLandlordInventoryRentalWeeklyFreeEnabled { get; set; } = false;
        public decimal YouPinLandlordInventoryRentalWeeklyFreeMinimumValue { get; set; } = 0m;
        public decimal YouPinLandlordInventoryRentalWeeklyFreeMaximumValue { get; set; } = 0m;
        public bool YouPinLandlordInventoryRentalCooldownEnabled { get; set; } = false;
        public int YouPinLandlordInventoryRentalCooldownStartMinute { get; set; } = 0;
        public int YouPinLandlordInventoryRentalCooldownEndMinute { get; set; } = 480;
        public bool YouPinLandlordUseUnifiedRepriceSettings { get; set; } = false;
        public bool YouPinLandlordUnifiedRepriceInitialized { get; set; } = false;
        public bool YouPinLandlordUnifiedEnabled { get; set; } = false;
        public int YouPinLandlordUnifiedTargetRank { get; set; } = 3;
        public int YouPinLandlordUnifiedScanIntervalMinutes { get; set; } = 30;
        public int YouPinLandlordUnifiedExecutionIntervalMinutes { get; set; } = 30;
        public bool YouPinLandlordUnifiedSelectionInitialized { get; set; } = false;
        public string YouPinLandlordUnifiedSelectionScope { get; set; } = "PerAsset";
        public string YouPinLandlordUnifiedSelectedAssetIds { get; set; } = "";
        public string YouPinLandlordUnifiedSelectedItemNames { get; set; } = "";
        public bool YouPinLandlordUnifiedWeeklyFreeEnabled { get; set; } = false;
        public decimal YouPinLandlordUnifiedWeeklyFreeMinimumValue { get; set; } = 0m;
        public decimal YouPinLandlordUnifiedWeeklyFreeMaximumValue { get; set; } = 0m;
        public bool YouPinLandlordUnifiedCooldownEnabled { get; set; } = false;
        public int YouPinLandlordUnifiedCooldownStartMinute { get; set; } = 0;
        public int YouPinLandlordUnifiedCooldownEndMinute { get; set; } = 480;
        public bool YouPinLandlordInventoryAutoRentEnabled { get; set; } = false;
        public int YouPinLandlordInventoryAutoRentScanIntervalMinutes { get; set; } = 30;
        public int YouPinLandlordInventoryAutoRentExecutionIntervalMinutes { get; set; } = 30;
        public long YouPinLandlordInventoryAutoRentLastExecutionUnixMilliseconds { get; set; }
        public string YouPinLandlordInventoryAutoRentListMode { get; set; } = "Whitelist";
        public string YouPinLandlordInventoryAutoRentSelectionScope { get; set; } = "PerAsset";
        public string YouPinLandlordInventoryAutoRentSelectedAssetIds { get; set; } = "";
        public string YouPinLandlordInventoryAutoRentSelectedItemNames { get; set; } = "";
        public bool YouPinLandlordInventoryAutoRentWeeklyFreeEnabled { get; set; } = false;
        public decimal YouPinLandlordInventoryAutoRentWeeklyFreeMinimumValue { get; set; } = 0m;
        public decimal YouPinLandlordInventoryAutoRentWeeklyFreeMaximumValue { get; set; } = 0m;
        public bool YouPinLandlordInventoryAutoRentCooldownEnabled { get; set; } = false;
        public int YouPinLandlordInventoryAutoRentCooldownStartMinute { get; set; } = 0;
        public int YouPinLandlordInventoryAutoRentCooldownEndMinute { get; set; } = 480;

        // ====== Steam 报价 ======
        public bool SteamOfferEnabled { get; set; } = false;
        public int SteamOfferRefreshSec { get; set; } = 180;
        public bool SteamOfferAllowYouPinVerifiedAccept { get; set; } = true;
        public bool SteamOfferAutoCheck { get; set; } = false;
        public bool SteamOfferAutoAccept { get; set; } = false;
        public int SteamOfferAutoCheckSec { get; set; } = 180;
        public string SteamOfferRedesignRule { get; set; } = "Pure";
        public bool SteamOfferRedesignAutoRefresh { get; set; } = true;
        public int SteamOfferRedesignRefreshMinutes { get; set; } = 5;
        public bool SteamOfferRedesignSkipSingleConfirm { get; set; } = false;
        public bool SteamOfferRedesignSkipBatchConfirm { get; set; } = false;
        public bool SteamAutoTradeEnabled { get; set; } = false;
        public bool SteamAutoTradeAcceptPureIncomingEnabled { get; set; } = false;
        public bool SteamAutoTradeAcceptYouPinPurchaseEnabled { get; set; } = false;
        public bool SteamAutoTradeSendYouPinSaleEnabled { get; set; } = false;
        public bool SteamAutoTradeSendYouPinRentalEnabled { get; set; } = false;
        public int SteamAutoTradeIntervalSeconds { get; set; } = 300;

        public float YouPinTrendFontSize { get; set; } = 9f;
        public string YouPinTrendRiseColor { get; set; } = "#DC465A";
        public string YouPinTrendFallColor { get; set; } = "#50A087";
        public string YouPinTrendTextColor { get; set; } = "#202020";
        public string YouPinTrendSubTextColor { get; set; } = "#5A5A5A";
        public string YouPinTrendCurveColor { get; set; } = "#0078D7";
        public bool YouPinTrendIndicatorVisibleInPanel { get; set; } = false;
        public bool YouPinTrendIndicatorVisibleInTaskbar { get; set; } = false;
        public int YouPinTrendIndicatorDisplayMode { get; set; } = 0; // 0=金额+百分比, 1=仅金额, 2=仅百分比
        public int YouPinTrendIndicatorSignMode { get; set; } = 0; // 0=带+/-, 1=不带符号
        public float YouPinTrendIndicatorFontSize { get; set; } = 9f;
        public bool YouPinTrendIndicatorFontBold { get; set; } = true;
        public string YouPinTrendIndicatorProfitColor { get; set; } = "#DC465A";
        public string YouPinTrendIndicatorLossColor { get; set; } = "#50A087";
        public string YouPinTrendIndicatorZeroColor { get; set; } = "#FFFFFF";
        public string YouPinTrendIndicatorSubTextColor { get; set; } = "#8D9BAB";


        // ★★★ [新增] 极简样式封装（复制到 Settings 类里） ★★★
        public struct TBStyle
        {
            public string Font; public float Size; public bool Bold;
            public int Gap; public int Inner; public int VOff;
        }

        // ★★★ 核心修复：添加全局保存锁，防止重置时被自动保存覆盖 ★★★
        [JsonIgnore]
        public static bool GlobalBlockSave
        {
            get => SettingsHelper.GlobalBlockSave;
            set => SettingsHelper.GlobalBlockSave = value;
        }

        // 全局配置对象身份必须稳定：主窗体和后台服务会长期持有该引用。
        private static readonly SettingsInstanceCoordinator InstanceCoordinator = new();

        // ★★★ 优化 3：改造 Load 方法为单例模式 ★★★
        public static Settings Load(bool forceReload = false)
        {
            Settings settings = InstanceCoordinator.Load(
                () => SettingsHelper.Load(forceReload),
                forceReload);
            settings.Language = "zh";
            return settings;
        }

        // 任务栏样式读取逻辑已迁到 SettingsHelper。

        public Settings DeepClone()
        {
            using var measure = AppPerformanceProfiler.Measure(
                "Settings.DeepClone",
                $"MonitorItems={MonitorItems?.Count ?? 0}; ItemConfigs={ItemConfigs?.Count ?? 0}",
                thresholdMs: 1);
            var json = System.Text.Json.JsonSerializer.Serialize(this);
            return System.Text.Json.JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
        }

        public static string GetBuiltinMarketAlertRuleId(string sourceId, MarketAlertRuleType ruleType)
        {
            return $"builtin:{sourceId}:{ruleType}";
        }

        public static List<MarketAlertRule> CreateDefaultMarketAlertRules()
        {
            return new List<MarketAlertRule>
            {
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.QaqId, MarketAlertRuleType.CrossAbove, "QAQ 突破点位", false, 0),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.QaqId, MarketAlertRuleType.CrossBelow, "QAQ 跌破点位", false, 0),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.QaqId, MarketAlertRuleType.RiseByPercent, GetDefaultMarketAlertRuleName(MarketDataSourceManager.QaqId, MarketAlertRuleType.RiseByPercent), false, 3),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.QaqId, MarketAlertRuleType.FallByPercent, GetDefaultMarketAlertRuleName(MarketDataSourceManager.QaqId, MarketAlertRuleType.FallByPercent), false, 3),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.SteamDtId, MarketAlertRuleType.CrossAbove, "SteamDT 突破点位", false, 0),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.SteamDtId, MarketAlertRuleType.CrossBelow, "SteamDT 跌破点位", false, 0),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.SteamDtId, MarketAlertRuleType.RiseByPercent, GetDefaultMarketAlertRuleName(MarketDataSourceManager.SteamDtId, MarketAlertRuleType.RiseByPercent), false, 3),
                CreateBuiltinMarketAlertRule(MarketDataSourceManager.SteamDtId, MarketAlertRuleType.FallByPercent, GetDefaultMarketAlertRuleName(MarketDataSourceManager.SteamDtId, MarketAlertRuleType.FallByPercent), false, 3)
            };
        }

        public static string GetDefaultMarketAlertRuleName(string sourceId, MarketAlertRuleType ruleType)
        {
            string source = string.Equals(sourceId, MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase)
                ? "SteamDT"
                : "QAQ";

            return ruleType switch
            {
                MarketAlertRuleType.RiseByPercent => $"{source} 规定时间内上涨百分比报警",
                MarketAlertRuleType.FallByPercent => $"{source} 规定时间内下跌百分比报警",
                MarketAlertRuleType.CrossAbove => $"{source} 突破点位",
                MarketAlertRuleType.CrossBelow => $"{source} 跌破点位",
                _ => source
            };
        }

        private static MarketAlertRule CreateBuiltinMarketAlertRule(
            string sourceId,
            MarketAlertRuleType ruleType,
            string name,
            bool enabled,
            double threshold)
        {
            return new MarketAlertRule
            {
                Id = GetBuiltinMarketAlertRuleId(sourceId, ruleType),
                Name = name,
                Enabled = enabled,
                SourceId = sourceId,
                RuleType = ruleType,
                Threshold = threshold,
                WindowMinutes = 10,
                CooldownMinutes = 5
            };
        }
    }

    public class ItemMonitorConfig
    {
        public string ItemId { get; set; } = "";
        public string ItemKey { get; set; } = "";
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int RefreshIntervalSec { get; set; } = 600;
        public int DisplayFieldFlags { get; set; } = 0;

        public bool VisibleInPanel { get; set; } = true;
        public bool VisibleInTaskbar { get; set; } = false;
        public int SortIndex { get; set; } = 0;
        public int TaskbarSortIndex { get; set; } = 0;

        // 缓存的最近一次状态。
        public double LastPrice { get; set; } = 0;
        public double LastChange { get; set; } = 0;
        public double LastChangeRatio { get; set; } = 0;
        public long LastUpdateTime { get; set; } = 0;
        public string LastStatus { get; set; } = "";

        // 兼容 SteamDT BaseInfo 的字段。
        public string MarketHashName { get; set; } = "";
        public string PlatformItemId { get; set; } = "";
        public bool HasChangeData { get; set; } = false;

        // 单品价格报警规则，数值为 0 表示该条件关闭。
        public bool PriceAlertEnabled { get; set; } = false;
        public ItemPriceAlertTriggerMode PriceAlertTriggerMode { get; set; } = ItemPriceAlertTriggerMode.Auto;
        public double PriceAlertAbove { get; set; } = 0;
        public double PriceAlertBelow { get; set; } = 0;
        public double PriceAlertRisePercent { get; set; } = 0;
        public double PriceAlertFallPercent { get; set; } = 0;
        public int PriceAlertWindowMinutes { get; set; } = 10;
        public int PriceAlertCooldownMinutes { get; set; } = 10;
        public double PriceAlertBaselinePrice { get; set; } = 0;
        public long PriceAlertBaselineTime { get; set; } = 0;
        public long PriceAlertLastTriggerTime { get; set; } = 0;
        public string PriceAlertLastMessage { get; set; } = "";
    }

    public enum ItemPriceAlertTriggerMode
    {
        Auto = 0,
        Percent = 1,
        Breakthrough = 2
    }


    public enum MarketAlertRuleType
    {
        CrossAbove = 0,
        CrossBelow = 1,
        RiseByPercent = 2,
        FallByPercent = 3
    }

    public enum MarketAlertNotificationMode
    {
        TrayBalloon = 0,
        // 仅保留用于旧配置兼容，新界面会映射到 DesktopToast。
        InAppToast = 1,
        DesktopToast = 2
    }

    public enum YouPinSaleReminderNotificationMode
    {
        Bubble = 0,
        Sound = 1,
        BubbleAndSound = 2,
        Silent = 3
    }

    public enum PhoneAlertDispatchMode
    {
        Failover = 0,
        SendAll = 1,
        PrimaryOnly = 2
    }

    public enum PhoneAlertChannelType
    {
        ServerChan = 0,
        WxPusher = 1,
        PushPlus = 2,
        Bark = 3,
        Gotify = 4,
        Telegram = 5,
        Webhook = 6
    }

    public class PhoneAlertChannelConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public bool Enabled { get; set; } = false;
        public PhoneAlertChannelType Type { get; set; } = PhoneAlertChannelType.ServerChan;
        public string DisplayName { get; set; } = "";
        public int Priority { get; set; } = 0;
        public string Secret { get; set; } = "";
        public string ServerUrl { get; set; } = "";
        public string Extra { get; set; } = "";
        public string LastTestResult { get; set; } = "";
        public long LastTestTime { get; set; } = 0;
    }

    public class MarketAlertRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string SourceId { get; set; } = "CSQAQ";
        public MarketAlertRuleType RuleType { get; set; } = MarketAlertRuleType.CrossAbove;
        public double Threshold { get; set; } = 0;
        public int WindowMinutes { get; set; } = 10;
        public int CooldownMinutes { get; set; } = 5;
    }

    public class MonitorItemConfig
    {
        // ★★★ 核心优化：使用字符串驻留池解决内存浪费 ★★★
        private string _key = "";

        // 极简缓存：复用首个实例作为原型 (0 GC)
        private static readonly ConcurrentDictionary<string, MonitorItemConfig> _protoCache = new ConcurrentDictionary<string, MonitorItemConfig>();

        // [Optimization] Cache keys to avoid string concatenation in hot paths
        [JsonIgnore] public string CachedPropLabelKey { get; private set; } = "";
        [JsonIgnore] public string CachedPropShortLabelKey { get; private set; } = "";
        [JsonIgnore] public string CachedUIGroup { get; private set; } = "";
        [JsonIgnore] public string CachedItemsKey { get; private set; } = ""; // Items.Key
        [JsonIgnore] public string CachedGroupsKey { get; private set; } = ""; // Groups.UIGroup
        [JsonIgnore] public string CachedDashColorKey { get; private set; } = "";
        [JsonIgnore] public string CachedDashUnitKey { get; private set; } = "";

        public string Key
        {
            get => _key;
            set
            {
                string k = UIUtils.Intern(value ?? "");
                if (_key == k) return;
                _key = k;

                // 极简模式：直接从缓存的原型对象拷贝所有属性，无需额外对象分配
                if (_protoCache.TryGetValue(k, out var p))
                {
                    CachedPropLabelKey = p.CachedPropLabelKey;
                    CachedPropShortLabelKey = p.CachedPropShortLabelKey;
                    CachedItemsKey = p.CachedItemsKey;
                    CachedUIGroup = p.CachedUIGroup;
                    CachedGroupsKey = p.CachedGroupsKey;
                    CachedDashColorKey = p.CachedDashColorKey;
                    CachedDashUnitKey = p.CachedDashUnitKey;
                    return;
                }

                // 首次计算
                CachedPropLabelKey = UIUtils.Intern("PROP.Label." + _key);
                CachedPropShortLabelKey = UIUtils.Intern("PROP.ShortLabel." + _key);
                CachedItemsKey = UIUtils.Intern("Items." + _key);

                if (_key == "MEM.Load" || _key == "MOBO.Temp" || _key == "DISK.Temp" || _key == "CASE.Fan" || _key == "FPS")
                    CachedUIGroup = "HOST";
                else if (_key.StartsWith("DASH."))
                {
                    CachedUIGroup = "DASH";
                    string sub = _key.Substring(5);
                    CachedDashColorKey = UIUtils.Intern(sub + ".Color");
                    CachedDashUnitKey = UIUtils.Intern(sub + ".Unit");
                }
                else
                    CachedUIGroup = UIUtils.Intern(_key.Split('.')[0]);

                CachedGroupsKey = UIUtils.Intern("Groups." + CachedUIGroup);

                // 存入缓存
                _protoCache[k] = this;
            }
        }
        // ★★★ [优化] 分离用户配置与系统动态值 ★★★
        // 用户标签：用户手动设置的名称（持久化），为空表示跟随系统。
        public string UserLabel { get; set; } = "";
        public string TaskbarLabel { get; set; } = "";

        // 动态标签：插件运行时计算的名称（不持久化）。
        [JsonIgnore]
        public string DynamicLabel { get; set; } = "";

        [JsonIgnore]
        public string DynamicTaskbarLabel { get; set; } = "";

        // 显示标签：最终显示名称，优先显示用户设置，否则显示动态值。
        [JsonIgnore]
        public string DisplayLabel => !string.IsNullOrEmpty(UserLabel) ? UserLabel : DynamicLabel;

        [JsonIgnore]
        public string DisplayTaskbarLabel => !string.IsNullOrEmpty(TaskbarLabel) ? TaskbarLabel : DynamicTaskbarLabel;

        // ★★★ [新增] 自定义单位配置 ★★★
        // 单位格式：null/"Auto" 表示自动默认，"" 表示不显示，"{u}/s" 表示自定义格式。
        public string? UnitPanel { get; set; } = null;
        public string? UnitTaskbar { get; set; } = null;
        public bool VisibleInPanel { get; set; } = true;
        public bool VisibleInTaskbar { get; set; } = false;

        public int SortIndex { get; set; } = 0;
        // ★★★ 新增：任务栏独立排序索引 ★★★
        public int TaskbarSortIndex { get; set; } = 0;


        // ★★★ 新增：统一的分组属性 ★★★
        // 所有界面（主界面、设置页、菜单）都统一调用这个属性来决定它属于哪个组
        // 从而避免了在 UI 代码里到处写 if else
        [JsonIgnore]
        public string UIGroup
        {
            get
            {
                // 优先返回预计算缓存，避免热路径重复解析 Key。
                if (!string.IsNullOrEmpty(CachedUIGroup)) return CachedUIGroup;

                // 兜底处理未初始化缓存的旧对象，正常路径很少进入。
                if (Key == "MEM.Load" ||
                    Key == "MOBO.Temp" ||
                    Key == "DISK.Temp" ||
                    Key == "CASE.Fan" ||
                    Key == "FPS")
                {
                    return "HOST";
                }

                if (Key.StartsWith("DASH."))
                {
                    return "DASH";
                }

                return Key.Split('.')[0];
            }
        }
    }

    public class ThresholdsSet
    {
        public ValueRange Load { get; set; } = new ValueRange { Warn = 60, Crit = 85 };
        public ValueRange Temp { get; set; } = new ValueRange { Warn = 50, Crit = 70 };
        public ValueRange DiskIOMB { get; set; } = new ValueRange { Warn = 2, Crit = 8 };
        public ValueRange NetUpMB { get; set; } = new ValueRange { Warn = 1, Crit = 2 };
        public ValueRange NetDownMB { get; set; } = new ValueRange { Warn = 2, Crit = 8 };
        public ValueRange DataUpMB { get; set; } = new ValueRange { Warn = 512, Crit = 1024 };
        public ValueRange DataDownMB { get; set; } = new ValueRange { Warn = 2048, Crit = 5096 };
    }

    public class ValueRange
    {
        public double Warn { get; set; } = 0;
        public double Crit { get; set; } = 0;
    }

}
