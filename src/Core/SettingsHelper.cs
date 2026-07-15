using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.State;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor
{
    public readonly record struct SettingsSaveResult(bool Succeeded, string? FailureType)
    {
        public static SettingsSaveResult Success { get; } = new(true, null);

        public static SettingsSaveResult Failed(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return new SettingsSaveResult(false, exception.GetType().Name);
        }
    }

    public sealed class SettingsPersistenceException : IOException
    {
        public SettingsPersistenceException(string? failureType)
            : base("配置保存失败，原配置已保留。")
        {
            FailureType = string.IsNullOrWhiteSpace(failureType) ? "Unknown" : failureType;
        }

        public string FailureType { get; }
    }

    public static class SettingsHelper
    {
        // 缓存路径
        private static readonly string _cachedPath = RuntimeDataPaths.GetDataFilePath("settings.json");
        private static readonly string _backupPath = RuntimeDataPaths.GetDataFilePath("settings.json.bak");
        private static readonly string _tempPath = Path.Combine(
            Path.GetDirectoryName(_cachedPath) ?? RuntimeDataPaths.DataDirectory,
            "settings.json.tmp");
        private static readonly object _ioLock = new object();
        private static SettingsHelperRuntimeServices? _runtimeServices;
        public static string FilePath => _cachedPath;

        // 全局保存阻断开关
        public static bool GlobalBlockSave { get; set; } = false;

        public static Settings Load(bool forceReload = false)
        {
            // 单例实例由 Settings.Load() facade 或调用方管理，本方法只负责磁盘读取和默认值创建。

            Settings s;
            lock (_ioLock)
            {
                s = TryLoadFile(FilePath, backupCorrupt: true)
                    ?? TryLoadFile(_backupPath, backupCorrupt: true)
                    ?? new Settings();
            }

            NormalizeSettings(s);
            var items = s.MonitorItems!;

            // 1. 新安装检查
            if (items.Count == 0)
            {
                s.InitDefaultItems();
                items = s.MonitorItems!;
                // 确保 TaskbarSortIndex 有初始值
                foreach (var item in items)
                {
                    if (item.TaskbarSortIndex == 0)
                        item.TaskbarSortIndex = item.SortIndex;
                }
            }
            else
            {
                // 2. 版本检查
                bool isLegacyConfig = items.All(x => x.TaskbarSortIndex == 0);

                if (isLegacyConfig)
                {
                    s.RebuildAndMigrateSettings();
                }
                else
                {
                    s.CheckAndAppendMissingItems();
                }
            }

            // [修复] 移除重复的 SyncToLanguage 调用。
            // 调用方 AppActions.ApplyAllSettings 负责同步语言状态。
            // s.SyncToLanguage();

            // [Migration] 兼容老用户：如果未设置 PresetStyle (-1)，根据旧的 FontSize 推断
            // 未来1.2.9几个版本后，将TaskbarPresetStyle 默认配置改成1即可
            if (s.TaskbarPresetStyle == -1)
            {
                // 9.0pt => 小字模式(0), 其他 => 大字模式(1)
                s.TaskbarPresetStyle = (Math.Abs(s.TaskbarFontSize - Settings.DEFAULT_TB_SIZE_REGULAR) < 0.1f) ? 0 : 1;
            }

            if (s.TaskbarAlignLeft && IsOnlyMarketTaskbar(s))
            {
                s.TaskbarAlignLeft = false;
            }

            s.InternAllStrings();

            return s;
        }

        public static SettingsSaveResult Save(this Settings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (GlobalBlockSave) return SettingsSaveResult.Success;

            return SaveCore(
                settings,
                FilePath,
                _backupPath,
                _tempPath,
                saved => RuntimeServices.AppConfigState.PublishFrom(saved));
        }

        internal static SettingsSaveResult SaveCore(
            Settings settings,
            string filePath,
            string backupPath,
            string tempPath,
            Action<Settings>? publish = null)
        {
            try
            {
                NormalizeSettings(settings);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                lock (_ioLock)
                {
                    AtomicWrite(filePath, backupPath, tempPath, json);
                }
            }
            catch (Exception ex)
            {
                CleanupTempFile(tempPath);
                DiagnosticsLogger.Error(
                    "Settings",
                    $"Configuration save failed. Category=UserSettings; FailureType={ex.GetType().Name}");
                return SettingsSaveResult.Failed(ex);
            }

            publish?.Invoke(settings);
            return SettingsSaveResult.Success;
        }

        private static SettingsHelperRuntimeServices RuntimeServices => _runtimeServices ??= SettingsHelperRuntimeServices.Resolve();

        public static void DeleteStoredSettings()
        {
            lock (_ioLock)
            {
                DeleteFileIfExists(FilePath);
                DeleteFileIfExists(_backupPath);
                DeleteFileIfExists(_tempPath);
            }
        }

        private static Settings? TryLoadFile(string path, bool backupCorrupt)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                if (backupCorrupt)
                {
                    BackupCorruptFile(path, ex);
                }
                return null;
            }
        }

        private static void BackupCorruptFile(string path, Exception ex)
        {
            try
            {
                if (!File.Exists(path)) return;

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backupPath = path + ".corrupt-" + stamp + ".bak";
                File.Copy(path, backupPath, overwrite: false);
                File.WriteAllText(backupPath + ".error.txt", ex.Message);
            }
            catch
            {
                // Loading must continue even if diagnostics cannot be written.
            }
        }

        private static void NormalizeSettings(Settings s)
        {
            s.GroupAliases ??= new Dictionary<string, string>();
            s.MonitorItems ??= new List<MonitorItemConfig>();
            s.ItemConfigs ??= new List<ItemMonitorConfig>();
            s.MarketAlertRules ??= new List<MarketAlertRule>();
            s.Thresholds ??= new ThresholdsSet();
            if (s.DefaultItemRefreshIntervalSec <= 0) s.DefaultItemRefreshIntervalSec = 600;
            s.DefaultItemRefreshIntervalSec = Math.Max(60, s.DefaultItemRefreshIntervalSec);
            s.DefaultItemPriceAlertRisePercent = Math.Clamp(s.DefaultItemPriceAlertRisePercent, 0, 1000);
            s.DefaultItemPriceAlertFallPercent = Math.Clamp(s.DefaultItemPriceAlertFallPercent, 0, 1000);
            s.DefaultItemPriceAlertWindowMinutes = Math.Clamp(s.DefaultItemPriceAlertWindowMinutes <= 0 ? 10 : s.DefaultItemPriceAlertWindowMinutes, 1, 10080);
            s.DefaultItemPriceAlertCooldownMinutes = Math.Clamp(s.DefaultItemPriceAlertCooldownMinutes <= 0 ? 10 : s.DefaultItemPriceAlertCooldownMinutes, 1, 1440);
            foreach (var item in s.ItemConfigs)
            {
                item.RefreshIntervalSec = Math.Max(60, item.RefreshIntervalSec <= 0 ? s.DefaultItemRefreshIntervalSec : item.RefreshIntervalSec);
                item.PriceAlertRisePercent = Math.Clamp(item.PriceAlertRisePercent, 0, 1000);
                item.PriceAlertFallPercent = Math.Clamp(item.PriceAlertFallPercent, 0, 1000);
                item.PriceAlertWindowMinutes = Math.Clamp(item.PriceAlertWindowMinutes <= 0 ? s.DefaultItemPriceAlertWindowMinutes : item.PriceAlertWindowMinutes, 1, 10080);
                item.PriceAlertCooldownMinutes = Math.Clamp(item.PriceAlertCooldownMinutes <= 0 ? s.DefaultItemPriceAlertCooldownMinutes : item.PriceAlertCooldownMinutes, 1, 1440);
                if (!Enum.IsDefined(typeof(ItemPriceAlertTriggerMode), item.PriceAlertTriggerMode))
                    item.PriceAlertTriggerMode = ItemPriceAlertTriggerMode.Auto;
            }
            if (s.YouPinInventoryRefreshSec <= 0) s.YouPinInventoryRefreshSec = 1800;
            s.YouPinInventoryRefreshSec = Math.Max(300, s.YouPinInventoryRefreshSec);
            s.YouPinInventoryToken ??= "";
            s.YouPinInventoryDeviceToken ??= "";
            if (s.YouPinSaleReminderRefreshSec <= 0 || s.YouPinSaleReminderRefreshSec == 60) s.YouPinSaleReminderRefreshSec = 180;
            s.YouPinSaleReminderRefreshSec = Math.Max(30, s.YouPinSaleReminderRefreshSec);
            if (!Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), s.YouPinSaleReminderNotificationMode))
                s.YouPinSaleReminderNotificationMode = YouPinSaleReminderNotificationMode.Bubble;
            if (s.YouPinMsgCenterRefreshSec <= 0) s.YouPinMsgCenterRefreshSec = 60;
            s.YouPinMsgCenterRefreshSec = Math.Max(30, s.YouPinMsgCenterRefreshSec);
            if (!Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), s.YouPinMsgCenterNotificationMode))
                s.YouPinMsgCenterNotificationMode = YouPinSaleReminderNotificationMode.Bubble;
            if (s.YouPinInventoryRisePercentThreshold <= 0) s.YouPinInventoryRisePercentThreshold = 3;
            if (s.YouPinInventoryFallPercentThreshold <= 0) s.YouPinInventoryFallPercentThreshold = 3;
            s.YouPinInventoryRisePercentThreshold = Math.Clamp(s.YouPinInventoryRisePercentThreshold, 0.01, 1000);
            s.YouPinInventoryFallPercentThreshold = Math.Clamp(s.YouPinInventoryFallPercentThreshold, 0.01, 1000);
            s.YouPinInventoryChangeAmountThreshold = Math.Max(0, s.YouPinInventoryChangeAmountThreshold);
            if (s.YouPinInventoryChangeAlertCooldownMinutes <= 0) s.YouPinInventoryChangeAlertCooldownMinutes = 30;
            s.YouPinInventoryChangeAlertCooldownMinutes = Math.Clamp(s.YouPinInventoryChangeAlertCooldownMinutes, 1, 1440);
            if (!Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), s.YouPinInventoryChangeAlertNotificationMode))
                s.YouPinInventoryChangeAlertNotificationMode = YouPinSaleReminderNotificationMode.Bubble;
            if (s.YouPinStopProfitLossWindowMinutes <= 0) s.YouPinStopProfitLossWindowMinutes = 180;
            s.YouPinStopProfitLossWindowMinutes = Math.Clamp(s.YouPinStopProfitLossWindowMinutes, 5, 10080);
            if (s.YouPinStopProfitPercentThreshold <= 0) s.YouPinStopProfitPercentThreshold = 30;
            if (s.YouPinStopLossPercentThreshold <= 0) s.YouPinStopLossPercentThreshold = 30;
            s.YouPinStopProfitPercentThreshold = Math.Clamp(s.YouPinStopProfitPercentThreshold, 0.01, 1000);
            s.YouPinStopLossPercentThreshold = Math.Clamp(s.YouPinStopLossPercentThreshold, 0.01, 1000);
            if (s.YouPinStopProfitLossCooldownMinutes <= 0) s.YouPinStopProfitLossCooldownMinutes = 30;
            s.YouPinStopProfitLossCooldownMinutes = Math.Clamp(s.YouPinStopProfitLossCooldownMinutes, 1, 1440);
            if (!Enum.IsDefined(typeof(YouPinSaleReminderNotificationMode), s.YouPinStopProfitLossNotificationMode))
                s.YouPinStopProfitLossNotificationMode = YouPinSaleReminderNotificationMode.BubbleAndSound;
            s.YouPinStopProfitLossSpecifiedItems = NormalizeKeywordList(s.YouPinStopProfitLossSpecifiedItems);
            s.YouPinStopProfitLossExcludedItems = NormalizeKeywordList(s.YouPinStopProfitLossExcludedItems);
            s.YouPinStopProfitLossItemRulesJson ??= "";
            s.YouPinLandlordPolicyVersion = Math.Max(1, s.YouPinLandlordPolicyVersion);
            s.YouPinLandlordZeroCdTargetRank = Math.Clamp(s.YouPinLandlordZeroCdTargetRank, 1, 20);
            s.YouPinLandlordZeroCdScanIntervalMinutes = Math.Clamp(
                s.YouPinLandlordZeroCdScanIntervalMinutes,
                20,
                1440);
            s.YouPinLandlordZeroCdExecutionIntervalMinutes = Math.Clamp(
                s.YouPinLandlordZeroCdExecutionIntervalMinutes,
                20,
                1440);
            s.YouPinLandlordZeroCdLastExecutionUnixMilliseconds = Math.Max(
                0,
                s.YouPinLandlordZeroCdLastExecutionUnixMilliseconds);
            s.YouPinLandlordZeroCdSelectionScope = NormalizeLandlordSelectionScope(
                s.YouPinLandlordZeroCdSelectionScope);
            s.YouPinLandlordZeroCdSelectedAssetIds = NormalizeLandlordSelectionList(
                s.YouPinLandlordZeroCdSelectedAssetIds,
                splitLegacySeparators: true);
            s.YouPinLandlordZeroCdSelectedItemNames = NormalizeLandlordSelectionList(
                s.YouPinLandlordZeroCdSelectedItemNames);
            (s.YouPinLandlordZeroCdWeeklyFreeMinimumValue,
                s.YouPinLandlordZeroCdWeeklyFreeMaximumValue) = NormalizeLandlordValueRange(
                    s.YouPinLandlordZeroCdWeeklyFreeMinimumValue,
                    s.YouPinLandlordZeroCdWeeklyFreeMaximumValue);
            s.YouPinLandlordZeroCdCooldownStartMinute = Math.Clamp(
                s.YouPinLandlordZeroCdCooldownStartMinute, 0, 1439);
            s.YouPinLandlordZeroCdCooldownEndMinute = Math.Clamp(
                s.YouPinLandlordZeroCdCooldownEndMinute, 0, 1439);
            s.YouPinLandlordInventoryRentalTargetRank = Math.Clamp(
                s.YouPinLandlordInventoryRentalTargetRank,
                1,
                20);
            s.YouPinLandlordInventoryRentalScanIntervalMinutes = Math.Clamp(
                s.YouPinLandlordInventoryRentalScanIntervalMinutes,
                20,
                1440);
            s.YouPinLandlordInventoryRentalExecutionIntervalMinutes = Math.Clamp(
                s.YouPinLandlordInventoryRentalExecutionIntervalMinutes,
                20,
                1440);
            s.YouPinLandlordInventoryRentalLastExecutionUnixMilliseconds = Math.Max(
                0,
                s.YouPinLandlordInventoryRentalLastExecutionUnixMilliseconds);
            s.YouPinLandlordInventoryRentalSelectionScope = NormalizeLandlordSelectionScope(
                s.YouPinLandlordInventoryRentalSelectionScope);
            s.YouPinLandlordInventoryRentalSelectedAssetIds = NormalizeLandlordSelectionList(
                s.YouPinLandlordInventoryRentalSelectedAssetIds,
                splitLegacySeparators: true);
            s.YouPinLandlordInventoryRentalSelectedItemNames = NormalizeLandlordSelectionList(
                s.YouPinLandlordInventoryRentalSelectedItemNames);
            (s.YouPinLandlordInventoryRentalWeeklyFreeMinimumValue,
                s.YouPinLandlordInventoryRentalWeeklyFreeMaximumValue) = NormalizeLandlordValueRange(
                    s.YouPinLandlordInventoryRentalWeeklyFreeMinimumValue,
                    s.YouPinLandlordInventoryRentalWeeklyFreeMaximumValue);
            s.YouPinLandlordInventoryRentalCooldownStartMinute = Math.Clamp(
                s.YouPinLandlordInventoryRentalCooldownStartMinute, 0, 1439);
            s.YouPinLandlordInventoryRentalCooldownEndMinute = Math.Clamp(
                s.YouPinLandlordInventoryRentalCooldownEndMinute, 0, 1439);
            s.YouPinLandlordUnifiedTargetRank = Math.Clamp(
                s.YouPinLandlordUnifiedTargetRank, 1, 20);
            s.YouPinLandlordUnifiedScanIntervalMinutes = Math.Clamp(
                s.YouPinLandlordUnifiedScanIntervalMinutes, 20, 1440);
            s.YouPinLandlordUnifiedExecutionIntervalMinutes = Math.Clamp(
                s.YouPinLandlordUnifiedExecutionIntervalMinutes, 20, 1440);
            s.YouPinLandlordUnifiedSelectionScope = NormalizeLandlordSelectionScope(
                s.YouPinLandlordUnifiedSelectionScope);
            s.YouPinLandlordUnifiedSelectedAssetIds = NormalizeLandlordSelectionList(
                s.YouPinLandlordUnifiedSelectedAssetIds,
                splitLegacySeparators: true);
            s.YouPinLandlordUnifiedSelectedItemNames = NormalizeLandlordSelectionList(
                s.YouPinLandlordUnifiedSelectedItemNames);
            (s.YouPinLandlordUnifiedWeeklyFreeMinimumValue,
                s.YouPinLandlordUnifiedWeeklyFreeMaximumValue) = NormalizeLandlordValueRange(
                    s.YouPinLandlordUnifiedWeeklyFreeMinimumValue,
                    s.YouPinLandlordUnifiedWeeklyFreeMaximumValue);
            s.YouPinLandlordUnifiedCooldownStartMinute = Math.Clamp(
                s.YouPinLandlordUnifiedCooldownStartMinute, 0, 1439);
            s.YouPinLandlordUnifiedCooldownEndMinute = Math.Clamp(
                s.YouPinLandlordUnifiedCooldownEndMinute, 0, 1439);
            s.YouPinLandlordInventoryAutoRentScanIntervalMinutes = Math.Clamp(
                s.YouPinLandlordInventoryAutoRentScanIntervalMinutes, 20, 1440);
            s.YouPinLandlordInventoryAutoRentExecutionIntervalMinutes = Math.Clamp(
                s.YouPinLandlordInventoryAutoRentExecutionIntervalMinutes, 20, 1440);
            s.YouPinLandlordInventoryAutoRentLastExecutionUnixMilliseconds = Math.Max(
                0,
                s.YouPinLandlordInventoryAutoRentLastExecutionUnixMilliseconds);
            s.YouPinLandlordInventoryAutoRentListMode =
                string.Equals(
                    s.YouPinLandlordInventoryAutoRentListMode,
                    "Blacklist",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Blacklist"
                    : "Whitelist";
            s.YouPinLandlordInventoryAutoRentSelectionScope = NormalizeLandlordSelectionScope(
                s.YouPinLandlordInventoryAutoRentSelectionScope);
            s.YouPinLandlordInventoryAutoRentSelectedAssetIds = NormalizeLandlordSelectionList(
                s.YouPinLandlordInventoryAutoRentSelectedAssetIds,
                splitLegacySeparators: true);
            s.YouPinLandlordInventoryAutoRentSelectedItemNames = NormalizeLandlordSelectionList(
                s.YouPinLandlordInventoryAutoRentSelectedItemNames);
            (s.YouPinLandlordInventoryAutoRentWeeklyFreeMinimumValue,
                s.YouPinLandlordInventoryAutoRentWeeklyFreeMaximumValue) = NormalizeLandlordValueRange(
                    s.YouPinLandlordInventoryAutoRentWeeklyFreeMinimumValue,
                    s.YouPinLandlordInventoryAutoRentWeeklyFreeMaximumValue);
            s.YouPinLandlordInventoryAutoRentCooldownStartMinute = Math.Clamp(
                s.YouPinLandlordInventoryAutoRentCooldownStartMinute, 0, 1439);
            s.YouPinLandlordInventoryAutoRentCooldownEndMinute = Math.Clamp(
                s.YouPinLandlordInventoryAutoRentCooldownEndMinute, 0, 1439);
            if (s.SteamOfferRefreshSec <= 0) s.SteamOfferRefreshSec = 180;
            s.SteamOfferRefreshSec = Math.Clamp(s.SteamOfferRefreshSec, 30, 3600);
            if (s.SteamOfferAutoCheckSec <= 0) s.SteamOfferAutoCheckSec = 180;
            s.SteamOfferAutoCheckSec = Math.Clamp(s.SteamOfferAutoCheckSec, 30, 3600);
            s.SteamOfferRedesignRule = NormalizeSteamOfferRedesignRule(s.SteamOfferRedesignRule);
            if (s.SteamOfferRedesignRefreshMinutes <= 0) s.SteamOfferRedesignRefreshMinutes = 5;
            s.SteamOfferRedesignRefreshMinutes = Math.Clamp(s.SteamOfferRedesignRefreshMinutes, 1, 60);
            if (s.SteamAutoTradeIntervalSeconds <= 0) s.SteamAutoTradeIntervalSeconds = Math.Max(300, s.SteamOfferRedesignRefreshMinutes * 60);
            s.SteamAutoTradeIntervalSeconds = Math.Clamp(s.SteamAutoTradeIntervalSeconds, 30, 3600);
            s.YouPinTrendFontSize = Math.Clamp(s.YouPinTrendFontSize <= 0 ? 9f : s.YouPinTrendFontSize, 7f, 16f);
            s.YouPinTrendRiseColor = NormalizeHexColor(s.YouPinTrendRiseColor, "#DC465A");
            s.YouPinTrendFallColor = NormalizeHexColor(s.YouPinTrendFallColor, "#50A087");
            s.YouPinTrendTextColor = NormalizeHexColor(s.YouPinTrendTextColor, "#202020");
            s.YouPinTrendSubTextColor = NormalizeHexColor(s.YouPinTrendSubTextColor, "#5A5A5A");
            s.YouPinTrendCurveColor = NormalizeHexColor(s.YouPinTrendCurveColor, "#0078D7");
            s.YouPinTrendIndicatorDisplayMode = Math.Clamp(s.YouPinTrendIndicatorDisplayMode, 0, 2);
            s.YouPinTrendIndicatorSignMode = Math.Clamp(s.YouPinTrendIndicatorSignMode, 0, 1);
            s.YouPinTrendIndicatorFontSize = Math.Clamp(s.YouPinTrendIndicatorFontSize <= 0 ? 9f : s.YouPinTrendIndicatorFontSize, 7f, 18f);
            s.YouPinTrendIndicatorProfitColor = NormalizeHexColor(s.YouPinTrendIndicatorProfitColor, "#DC465A");
            s.YouPinTrendIndicatorLossColor = NormalizeHexColor(s.YouPinTrendIndicatorLossColor, "#50A087");
            s.YouPinTrendIndicatorZeroColor = NormalizeHexColor(s.YouPinTrendIndicatorZeroColor, "#FFFFFF");
            s.YouPinTrendIndicatorSubTextColor = NormalizeHexColor(s.YouPinTrendIndicatorSubTextColor, "#8D9BAB");

            // 先从 MonitorItems 同步回 ItemConfigs，保留用户在 UI 编辑过的排序和可见性。
            if (s.ItemConfigs != null && s.MonitorItems != null)
            {
                foreach (var item in s.ItemConfigs)
                {
                    if (string.IsNullOrEmpty(item.ItemKey))
                        item.ItemKey = "ITEM." + item.ItemId;

                    var mItem = s.MonitorItems.FirstOrDefault(x => x.Key.Equals(item.ItemKey, StringComparison.OrdinalIgnoreCase));
                    if (mItem != null)
                    {
                        item.VisibleInPanel = mItem.VisibleInPanel;
                        item.VisibleInTaskbar = mItem.VisibleInTaskbar;
                        item.SortIndex = mItem.SortIndex;
                        item.TaskbarSortIndex = mItem.TaskbarSortIndex;
                    }
                }

                // 再从 ItemConfigs 同步到 MonitorItems，补齐缺失项并让可见性跟随启用状态。
                foreach (var item in s.ItemConfigs)
                {
                    var mItem = s.MonitorItems.FirstOrDefault(x => x.Key.Equals(item.ItemKey, StringComparison.OrdinalIgnoreCase));
                    if (mItem == null)
                    {
                        mItem = new MonitorItemConfig
                        {
                            Key = item.ItemKey,
                            VisibleInPanel = item.Enabled && item.VisibleInPanel,
                            VisibleInTaskbar = item.Enabled && item.VisibleInTaskbar,
                            SortIndex = item.SortIndex,
                            TaskbarSortIndex = item.TaskbarSortIndex
                        };
                        s.MonitorItems.Add(mItem);
                    }
                    else
                    {
                        mItem.VisibleInPanel = item.Enabled && item.VisibleInPanel;
                        mItem.VisibleInTaskbar = item.Enabled && item.VisibleInTaskbar;
                    }
                }

                // 移除 ItemConfigs 中已不存在的旧 ITEM. 键。
                s.MonitorItems.RemoveAll(mItem =>
                    mItem.Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase) &&
                    !s.ItemConfigs.Any(item => item.ItemKey.Equals(mItem.Key, StringComparison.OrdinalIgnoreCase) || ("ITEM." + item.ItemId).Equals(mItem.Key, StringComparison.OrdinalIgnoreCase))
                );
            }


            s.Language = "zh";
            s.Opacity = Math.Clamp(s.Opacity, 0.0, 1.0);
            if (Math.Abs(s.PanelBackgroundOpacity - 0.85) < 0.001 &&
                Math.Abs(s.Opacity - 0.85) > 0.001)
            {
                s.PanelBackgroundOpacity = s.Opacity;
            }
            s.PanelBackgroundOpacity = Math.Clamp(s.PanelBackgroundOpacity, 0.0, 1.0);
            s.TextOpacity = Math.Clamp(s.TextOpacity, 0.1, 1.0);
            if (s.HorizontalMode) s.HorizontalSingleLine = true;
            s.TaskbarCustomLayout = true;
            s.TaskbarCustomStyle = true;
            s.UIScale = Math.Clamp(s.UIScale, 0.5, 2.0);
            s.SettingsPanelWindowWidth = Math.Max(0, s.SettingsPanelWindowWidth);
            s.SettingsPanelWindowHeight = Math.Max(0, s.SettingsPanelWindowHeight);
            if (s.SettingsPanelWindowWidth == 0 || s.SettingsPanelWindowHeight == 0)
            {
                s.SettingsPanelWindowWidth = 0;
                s.SettingsPanelWindowHeight = 0;
            }
            s.PanelWidth = Math.Clamp(s.PanelWidth, 180, 1200);
            if (s.SteamDtRefreshSec <= 0)
                s.SteamDtRefreshSec = Settings.DefaultMarketRefreshSec;
            if (s.CsqaqRefreshSec <= 0)
                s.CsqaqRefreshSec = Settings.DefaultMarketRefreshSec;
            s.SteamDtRefreshSec = Math.Max(Settings.DefaultMarketRefreshSec, s.SteamDtRefreshSec);
            s.CsqaqRefreshSec = Math.Max(Settings.DefaultMarketRefreshSec, s.CsqaqRefreshSec);
            if (!Enum.IsDefined(typeof(MarketAlertNotificationMode), s.MarketAlertNotificationMode)
                || s.MarketAlertNotificationMode == MarketAlertNotificationMode.InAppToast)
            {
                s.MarketAlertNotificationMode = MarketAlertNotificationMode.DesktopToast;
            }
            s.MarketAlertDefaultWindowMinutes = Math.Clamp(s.MarketAlertDefaultWindowMinutes, 1, 1440);
            s.MarketAlertDefaultCooldownMinutes = Math.Clamp(s.MarketAlertDefaultCooldownMinutes, 1, 1440);
            SettingsPhoneAlertNormalizer.Normalize(s);
            s.Cs2UpdateReminderRefreshSec = Math.Clamp(s.Cs2UpdateReminderRefreshSec <= 0 ? 600 : s.Cs2UpdateReminderRefreshSec, 60, 86400);
            s.Cs2UpdateBaselineKey ??= "";
            s.Cs2UpdateBaselineTitle ??= "";
            s.Cs2UpdateLastStatus ??= "未检查";
            EnsureBuiltinMarketAlertRules(s.MarketAlertRules);
            NormalizeMarketAlertRules(s.MarketAlertRules, s.MarketAlertDefaultWindowMinutes, s.MarketAlertDefaultCooldownMinutes);
            s.TaskbarFontSize = Math.Clamp(s.TaskbarFontSize, 7f, 18f);
            s.TaskbarItemSpacing = Math.Clamp(s.TaskbarItemSpacing, -20, 80);
            s.TaskbarInnerSpacing = Math.Clamp(s.TaskbarInnerSpacing, -20, 80);
            s.TaskbarVerticalPadding = Math.Clamp(s.TaskbarVerticalPadding, -10, 30);
            s.HorizontalItemSpacing = Math.Clamp(s.HorizontalItemSpacing, -20, 80);
            s.HorizontalInnerSpacing = Math.Clamp(s.HorizontalInnerSpacing, -20, 80);
            s.TaskbarManualOffset = Math.Clamp(s.TaskbarManualOffset, -1200, 1200);
            if (s.MarketFormat < 0 || s.MarketFormat > 5) s.MarketFormat = 0;
        }

        private static void NormalizeMarketAlertRules(List<MarketAlertRule> rules, int defaultWindowMinutes, int defaultCooldownMinutes)
        {
            foreach (var rule in rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Id))
                    rule.Id = Guid.NewGuid().ToString("N");

                if (!string.Equals(rule.SourceId, CS2TradeMonitor.src.Core.MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(rule.SourceId, CS2TradeMonitor.src.Core.MarketDataSourceManager.QaqId, StringComparison.OrdinalIgnoreCase))
                {
                    rule.SourceId = CS2TradeMonitor.src.Core.MarketDataSourceManager.QaqId;
                }

                if (!Enum.IsDefined(typeof(MarketAlertRuleType), rule.RuleType))
                    rule.RuleType = MarketAlertRuleType.CrossAbove;

                rule.Threshold = Math.Max(0, rule.Threshold);
                rule.WindowMinutes = Math.Clamp(rule.WindowMinutes <= 0 ? defaultWindowMinutes : rule.WindowMinutes, 1, 1440);
                rule.CooldownMinutes = Math.Clamp(rule.CooldownMinutes <= 0 ? defaultCooldownMinutes : rule.CooldownMinutes, 1, 1440);
                rule.Name ??= "";
            }
        }

        private static string NormalizeHexColor(string? value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;

            string text = value.Trim();
            if (!text.StartsWith("#", StringComparison.Ordinal))
                text = "#" + text;

            try
            {
                ColorTranslator.FromHtml(text);
                return text.ToUpperInvariant();
            }
            catch
            {
                return fallback;
            }
        }

        private static string NormalizeKeywordList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            var parts = value
                .Replace('，', ',')
                .Replace('；', ',')
                .Replace('、', ',')
                .Replace(';', ',')
                .Split(new[] { ',', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join(", ", parts);
        }

        private static string NormalizeSteamOfferRedesignRule(string? value)
        {
            string text = (value ?? "").Trim();
            return text switch
            {
                "Any" => "Any",
                "YouPinPurchase" => "YouPinPurchase",
                "YouPinSale" => "YouPinSale",
                "YouPinRental" => "YouPinRental",
                _ => "Pure"
            };
        }

        private static void EnsureBuiltinMarketAlertRules(List<MarketAlertRule> rules)
        {
            var defaults = Settings.CreateDefaultMarketAlertRules();
            foreach (var defaultRule in defaults)
            {
                var existing = rules.FirstOrDefault(r => string.Equals(r.Id, defaultRule.Id, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    rules.Add(defaultRule);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existing.Name) || IsLegacyBuiltinMarketAlertRuleName(existing))
                    existing.Name = defaultRule.Name;

                if (string.IsNullOrWhiteSpace(existing.SourceId))
                    existing.SourceId = defaultRule.SourceId;
            }
        }

        private static bool IsLegacyBuiltinMarketAlertRuleName(MarketAlertRule rule)
        {
            if (rule.RuleType != MarketAlertRuleType.RiseByPercent
                && rule.RuleType != MarketAlertRuleType.FallByPercent)
            {
                return false;
            }

            string source = string.Equals(rule.SourceId, CS2TradeMonitor.src.Core.MarketDataSourceManager.SteamDtId, StringComparison.OrdinalIgnoreCase)
                ? "SteamDT"
                : "QAQ";
            string legacyName = rule.RuleType == MarketAlertRuleType.RiseByPercent
                ? $"{source} 指定时间内上涨"
                : $"{source} 指定时间内下跌";

            return string.Equals(rule.Name?.Trim(), legacyName, StringComparison.Ordinal);
        }

        private static void AtomicWrite(
            string filePath,
            string backupPath,
            string tempPath,
            string json)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(tempPath, json);

            if (File.Exists(filePath))
                File.Copy(filePath, backupPath, true);

            File.Move(tempPath, filePath, true);
        }

        private static void CleanupTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (System.Exception ex) { CS2TradeMonitor.src.SystemServices.DiagnosticsLogger.Ignored(ex); }
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public static void InitDefaultItems(this Settings settings)
        {
            settings.MonitorItems = new List<MonitorItemConfig>
            {
                // SteamDT
                new MonitorItemConfig { Key = "CSQAQ.Display", SortIndex = 501, TaskbarSortIndex = 5001, VisibleInPanel = true, VisibleInTaskbar = true, UserLabel = " ", TaskbarLabel = " " },
                new MonitorItemConfig { Key = "STEAMDT.Display", SortIndex = 502, TaskbarSortIndex = 5002, VisibleInPanel = true, VisibleInTaskbar = true, UserLabel = " ", TaskbarLabel = " " },
            };
        }

        private static bool IsOnlyMarketTaskbar(Settings settings)
        {
            var taskbarItems = settings.MonitorItems?
                .Where(x => x.VisibleInTaskbar)
                .ToList();

            return taskbarItems != null
                && taskbarItems.Count > 0
                && taskbarItems.All(x => IsMarketDisplayKey(x.Key));
        }

        private static bool IsMarketDisplayKey(string key)
        {
            return MarketDisplayFormatter.IsMarketDisplayKey(key);
        }
        // [同步] 同步到语言设置
        // 作用：将配置中的组别名和监控项标签同步到语言管理器
        // 注意：这会清除所有当前的语言覆盖
        public static void SyncToLanguage(this Settings settings)
        {
            LanguageManager.ClearOverrides();
            if (settings.GroupAliases != null)
            {
                foreach (var kv in settings.GroupAliases)
                    LanguageManager.SetOverride(UIUtils.Intern("Groups." + kv.Key), kv.Value);
            }
            if (settings.MonitorItems != null)
            {
                foreach (var item in settings.MonitorItems)
                {
                    if (!string.IsNullOrEmpty(item.UserLabel))
                        LanguageManager.SetOverride(UIUtils.Intern("Items." + item.Key), item.UserLabel);
                    if (!string.IsNullOrEmpty(item.TaskbarLabel))
                        LanguageManager.SetOverride(UIUtils.Intern("Short." + item.Key), item.TaskbarLabel);
                }
            }
        }


        // 缓存标准键集合，避免重复分配。
        private static readonly Lazy<HashSet<string>> _standardKeys = new Lazy<HashSet<string>>(() =>
        {
            var s = new Settings();
            s.InitDefaultItems();
            return new HashSet<string>(s.MonitorItems.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        });

        public static HashSet<string> GetStandardKeys() => _standardKeys.Value;

        public static void InternAllStrings(this Settings settings)
        {
            if (settings.MonitorItems != null)
            {
                // [清理] 移除孤立项，使用 StandardKeys 白名单避免额外分配。
                var whitelist = GetStandardKeys();
                var keysToRemove = new List<MonitorItemConfig>();

                foreach (var item in settings.MonitorItems)
                {
                    if (item == null) continue;

                    item.Key = UIUtils.Intern(item.Key);

                    // 1. 是否是标准项
                    if (whitelist.Contains(item.Key)) continue;

                    // 2. 是否是自定义监控项
                    if (item.Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase))
                    {
                        if (settings.ItemConfigs != null && settings.ItemConfigs.Any(x => string.Equals(x.ItemKey, item.Key, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }

                    keysToRemove.Add(item);
                }
            }

            settings.PreferredDisk = UIUtils.Intern(settings.PreferredDisk);
            settings.LastAutoDisk = UIUtils.Intern(settings.LastAutoDisk);
            settings.PreferredNetwork = UIUtils.Intern(settings.PreferredNetwork);
            settings.LastAutoNetwork = UIUtils.Intern(settings.LastAutoNetwork);
            settings.PreferredGpu = UIUtils.Intern(settings.PreferredGpu);

            settings.PreferredCpuFan = UIUtils.Intern(settings.PreferredCpuFan);
            settings.PreferredCpuPump = UIUtils.Intern(settings.PreferredCpuPump);
            settings.PreferredCaseFan = UIUtils.Intern(settings.PreferredCaseFan);
            settings.PreferredMoboTemp = UIUtils.Intern(settings.PreferredMoboTemp);

            settings.TaskbarFontFamily = UIUtils.Intern(settings.TaskbarFontFamily);
        }

        public static void RebuildAndMigrateSettings(this Settings settings)
        {
            // 1. 获取最新版本的标准清单 (确保包含新版本增加的监控项)
            var temp = new Settings();
            temp.InitDefaultItems();
            var migratedList = temp.MonitorItems;

            // 2. 遍历新标准清单，回填用户的个性化设置
            // 同时构建标准键集合，用于后续过滤
            var standardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var newItem in migratedList)
            {
                standardKeys.Add(newItem.Key);

                var oldItem = settings.MonitorItems.FirstOrDefault(x => x.Key.Equals(newItem.Key, StringComparison.OrdinalIgnoreCase));
                if (oldItem != null)
                {
                    newItem.VisibleInPanel = oldItem.VisibleInPanel;
                    newItem.VisibleInTaskbar = oldItem.VisibleInTaskbar;
                    newItem.UserLabel = oldItem.UserLabel;
                    newItem.TaskbarLabel = oldItem.TaskbarLabel;
                    newItem.UnitPanel = oldItem.UnitPanel;
                    newItem.UnitTaskbar = oldItem.UnitTaskbar;
                }
                // 数据修复：老版本没有 TaskbarSortIndex，默认初始化
                if (newItem.TaskbarSortIndex == 0) newItem.TaskbarSortIndex = newItem.SortIndex;
            }

            settings.MonitorItems = migratedList;
        }

        public static void CheckAndAppendMissingItems(this Settings settings)
        {
            var temp = new Settings();
            temp.InitDefaultItems();
            var defaultList = temp.MonitorItems;

            // 找出缺失项 (保持 DefaultList 的相对顺序)
            var newItems = defaultList
                .Where(std => !settings.MonitorItems.Any(usr => usr.Key.Equals(std.Key, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (newItems.Count == 0) return;

            bool listChanged = false;

            foreach (var newItem in newItems)
            {
                // [智能锚点逻辑]
                // 不使用硬编码的 SortIndex，而是寻找该项在 DefaultList 中的"前一个邻居" (Anchor)
                // 如果用户把邻居移到了别处，新项会紧随其后。

                MonitorItemConfig? anchor = null;
                int myDefIdx = defaultList.FindIndex(x => x.Key == newItem.Key);

                // 往前回溯寻找最近的有效锚点
                for (int k = myDefIdx - 1; k >= 0; k--)
                {
                    var prevKey = defaultList[k].Key;
                    var existing = settings.MonitorItems.FirstOrDefault(x => x.Key.Equals(prevKey, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        anchor = existing;
                        break;
                    }
                }

                // === A. Panel SortIndex ===
                int targetIndex;
                if (anchor != null)
                {
                    targetIndex = anchor.SortIndex + 1;
                }
                else
                {
                    // 没锚点 (说明是队首)，插在当前最小值前面
                    int min = settings.MonitorItems.Count > 0 ? settings.MonitorItems.Min(x => x.SortIndex) : 0;
                    targetIndex = min - 1;
                }

                // 挤开后续项
                foreach (var item in settings.MonitorItems.Where(x => x.SortIndex >= targetIndex))
                    item.SortIndex++;

                newItem.SortIndex = targetIndex;

                // === B. Taskbar SortIndex ===
                int targetTbIndex;
                if (anchor != null)
                {
                    targetTbIndex = anchor.TaskbarSortIndex + 1;
                }
                else
                {
                    // [Fix] 没锚点 (如 DASH 这种排在队首的) 或锚点未开启任务栏
                    // 策略：看该项在 Default 中的原始意图。
                    // 如果 Default 中 TaskbarSortIndex 很大 (>1000)，说明意图是放后面 -> 插在 Max + 1
                    // 如果 Default 中 TaskbarSortIndex 很小 (<1000)，说明意图是放前面 -> 插在 Min - 1

                    var validItems = settings.MonitorItems.Where(x => x.TaskbarSortIndex != 0).ToList();

                    if (newItem.TaskbarSortIndex > 1000)
                    {
                        // 意图：放后面
                        int max = validItems.Count > 0 ? validItems.Max(x => x.TaskbarSortIndex) : 0;
                        targetTbIndex = max + 1;
                    }
                    else
                    {
                        // 意图：放前面
                        int min = validItems.Count > 0 ? validItems.Min(x => x.TaskbarSortIndex) : 1;
                        targetTbIndex = min - 1;
                        if (targetTbIndex == 0) targetTbIndex = -1; // 避开 0
                    }
                }

                foreach (var item in settings.MonitorItems.Where(x => x.TaskbarSortIndex >= targetTbIndex))
                    item.TaskbarSortIndex++;

                newItem.TaskbarSortIndex = targetTbIndex;

                settings.MonitorItems.Add(newItem);
                listChanged = true;
            }

            if (listChanged)
            {
                settings.MonitorItems = settings.MonitorItems.OrderBy(x => x.SortIndex).ToList();
            }
        }

        public static Settings.TBStyle GetStyle(this Settings settings)
        {
            return new Settings.TBStyle
            {
                Font = settings.TaskbarFontFamily,
                Size = settings.TaskbarFontSize,
                Bold = settings.TaskbarFontBold,
                Gap = settings.TaskbarItemSpacing,
                Inner = settings.TaskbarInnerSpacing,
                VOff = settings.TaskbarVerticalPadding
            };
        }

        private static (decimal Minimum, decimal Maximum) NormalizeLandlordValueRange(
            decimal minimum,
            decimal maximum)
        {
            minimum = Math.Max(0m, minimum);
            maximum = Math.Max(0m, maximum);
            return minimum <= maximum
                ? (minimum, maximum)
                : (maximum, minimum);
        }

        private static string NormalizeLandlordSelectionList(
            string? value,
            bool splitLegacySeparators = false)
        {
            char[] separators = splitLegacySeparators
                ? new[] { '\r', '\n', ',', ';' }
                : new[] { '\r', '\n' };
            return string.Join(
                '\n',
                (value ?? string.Empty)
                    .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => splitLegacySeparators ? item.Trim() : item)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal));
        }

        private static string NormalizeLandlordSelectionScope(string? value)
        {
            return string.Equals(value, "SameItemName", StringComparison.OrdinalIgnoreCase)
                ? "SameItemName"
                : "PerAsset";
        }

        public static bool IsAnyEnabled(this Settings settings, string keyPrefix)
        {
            return settings.MonitorItems.Any(x => x.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) && (x.VisibleInPanel || x.VisibleInTaskbar));
        }

        public static void UpdateMaxRecord(this Settings settings, string key, float val)
        {
            bool changed = false;
            if (val <= 0 || float.IsNaN(val) || float.IsInfinity(val)) return;

            if (key.Contains("Clock") && val > 10000) return;
            if (key.Contains("Power") && val > 1000) return;
            if ((key.Contains("Fan") || key.Contains("Pump")) && val > 10000) return;

            if (key == "CPU.Power" && val > settings.RecordedMaxCpuPower) { settings.RecordedMaxCpuPower = val; changed = true; }
            else if (key == "CPU.Clock" && val > settings.RecordedMaxCpuClock) { settings.RecordedMaxCpuClock = val; changed = true; }
            else if (key == "GPU.Power" && val > settings.RecordedMaxGpuPower) { settings.RecordedMaxGpuPower = val; changed = true; }
            else if (key == "GPU.Clock" && val > settings.RecordedMaxGpuClock) { settings.RecordedMaxGpuClock = val; changed = true; }

            else if (key == "CPU.Fan" && val > settings.RecordedMaxCpuFan) { settings.RecordedMaxCpuFan = val; changed = true; }
            else if (key == "CPU.Pump" && val > settings.RecordedMaxCpuPump) { settings.RecordedMaxCpuPump = val; changed = true; }
            else if (key == "GPU.Fan" && val > settings.RecordedMaxGpuFan) { settings.RecordedMaxGpuFan = val; changed = true; }
            else if (key == "CASE.Fan" && val > settings.RecordedMaxChassisFan) { settings.RecordedMaxChassisFan = val; changed = true; }

            if (changed && (DateTime.Now - settings.LastAutoSaveTime).TotalSeconds > 30)
            {
                settings.Save();
                settings.LastAutoSaveTime = DateTime.Now;
            }
        }
    }
}
