using System.Linq;
using CS2TradeMonitor.src.Core;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
namespace CS2TradeMonitor.src.Core.Actions
{
    /// <summary>
    /// 封装所有修改 Settings 对象的逻辑。
    /// 支持草稿/提交 (Draft/Commit) 架构。
    /// 使用反射的简化版本。
    /// </summary>
    public static class SettingsChanger
    {
        private static readonly HashSet<string> RuntimeProperties = new(StringComparer.Ordinal)
        {
            "LastAutoNetwork", "LastAutoDisk",
            "ScreenDevice", "MaxLimitTipShown",
            "TotalUpload", "TotalDownload",
            "SessionUploadBytes", "SessionDownloadBytes",
            "LastAutoSaveTime", "LastAlertTime"
        };

        /// <summary>
        /// 使用反射将草稿设置 (Draft) 合并到实时设置 (Live) 中。
        /// 保留在黑名单中定义的仅运行时属性。
        /// </summary>
        public static void Merge(Settings live, Settings draft)
        {
            if (live == null || draft == null) return;

            var props = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var p in props)
            {
                if (!p.CanWrite || !p.CanRead) continue;

                // 1. 跳过黑名单中的运行时属性
                if (RuntimeProperties.Contains(p.Name)) continue;

                ApplyProperty(live, draft, p);
            }
        }

        public static void MergeChangedProperties(Settings live, Settings draft, Settings baseline)
        {
            ArgumentNullException.ThrowIfNull(live);
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(baseline);

            var properties = typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanWrite || !property.CanRead || RuntimeProperties.Contains(property.Name))
                    continue;
                if (PropertyValuesEqual(property, draft, baseline))
                    continue;

                ApplyProperty(live, draft, property);
            }
        }

        public static void RebaseDraftFromLive(Settings live, Settings draft)
        {
            ArgumentNullException.ThrowIfNull(live);
            ArgumentNullException.ThrowIfNull(draft);

            Settings clone = live.DeepClone();
            foreach (PropertyInfo property in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanWrite && property.CanRead)
                    property.SetValue(draft, property.GetValue(clone));
            }

            RebaseDraftMonitorItems(live, draft);
        }

        private static bool PropertyValuesEqual(PropertyInfo property, Settings left, Settings right)
        {
            object? leftValue = property.GetValue(left);
            object? rightValue = property.GetValue(right);
            if (ReferenceEquals(leftValue, rightValue))
                return true;
            if (leftValue is null || rightValue is null)
                return false;
            if (property.PropertyType.IsValueType || property.PropertyType == typeof(string))
                return Equals(leftValue, rightValue);

            string leftJson = JsonSerializer.Serialize(leftValue, property.PropertyType);
            string rightJson = JsonSerializer.Serialize(rightValue, property.PropertyType);
            return string.Equals(leftJson, rightJson, StringComparison.Ordinal);
        }

        private static void ApplyProperty(Settings live, Settings draft, PropertyInfo property)
        {
            if (property.Name == nameof(Settings.MonitorItems))
            {
                UpdateMonitorList(live, draft.MonitorItems, draft.HorizontalFollowsTaskbar);
                return;
            }
            if (property.Name == nameof(Settings.Thresholds))
            {
                string json = JsonSerializer.Serialize(draft.Thresholds);
                live.Thresholds = JsonSerializer.Deserialize<ThresholdsSet>(json) ?? new ThresholdsSet();
                return;
            }
            if (property.Name == nameof(Settings.ItemConfigs))
            {
                string json = JsonSerializer.Serialize(draft.ItemConfigs);
                live.ItemConfigs = JsonSerializer.Deserialize<List<ItemMonitorConfig>>(json) ?? [];
                SyncItemConfigsToMonitorItems(live);
                return;
            }
            if (property.Name == nameof(Settings.MarketAlertRules))
            {
                string json = JsonSerializer.Serialize(draft.MarketAlertRules);
                live.MarketAlertRules = JsonSerializer.Deserialize<List<MarketAlertRule>>(json) ?? [];
                return;
            }
            if (property.Name == nameof(Settings.PhoneAlertChannels))
            {
                string json = JsonSerializer.Serialize(draft.PhoneAlertChannels);
                live.PhoneAlertChannels = JsonSerializer.Deserialize<List<PhoneAlertChannelConfig>>(json) ?? [];
                return;
            }
            if (property.Name == nameof(Settings.GroupAliases))
            {
                live.GroupAliases = new Dictionary<string, string>(draft.GroupAliases);
                return;
            }

            property.SetValue(live, property.GetValue(draft));
        }

        /// <summary>
        /// 基于 UI 的工作列表更新目标 Settings 对象中的 MonitorItems 列表。
        /// 处理合并逻辑以保留动态属性 (如 DynamicLabel)。
        /// </summary>
        public static void UpdateMonitorList(Settings target, List<MonitorItemConfig> workingList, bool horizontalFollowsTaskbar)
        {
            if (target == null || workingList == null) return;

            target.HorizontalFollowsTaskbar = horizontalFollowsTaskbar;

            // ★★★ 1. 深拷贝 Draft 列表，断开引用 ★★★
            // 防止修改 Draft 属性时直接影响 Live 对象
            var json = System.Text.Json.JsonSerializer.Serialize(workingList);
            var safeWorkingList = System.Text.Json.JsonSerializer.Deserialize<List<MonitorItemConfig>>(json)
                                  ?? new List<MonitorItemConfig>();

            // ★★★ 2. 恢复运行时状态 (Dynamic Labels) ★★★
            // 因为 JSON 序列化丢失了 [JsonIgnore] 的属性，需要从 Live 对象中恢复它们
            var liveMap = target.MonitorItems.ToDictionary(x => x.Key);
            foreach (var item in safeWorkingList)
            {
                if (liveMap.TryGetValue(item.Key, out var liveItem))
                {
                    item.DynamicLabel = liveItem.DynamicLabel;
                    item.DynamicTaskbarLabel = liveItem.DynamicTaskbarLabel;
                }
            }

            // 合并逻辑
            var activeKeys = new HashSet<string>(target.MonitorItems.Select(x => x.Key));

            // 3. 获取配置中存在的项 (保留 UI 排序/更改)
            var mergedList = safeWorkingList.Where(x => activeKeys.Contains(x.Key)).ToList();

            // 4. 添加配置中出现但工作列表中缺失的新项
            var workingKeys = new HashSet<string>(safeWorkingList.Select(x => x.Key));
            var newItems = target.MonitorItems.Where(x => !workingKeys.Contains(x.Key)).ToList();

            if (newItems.Count > 0)
            {
                mergedList.AddRange(newItems);
            }

            target.MonitorItems = mergedList;
        }

        private static void SyncItemConfigsToMonitorItems(Settings target)
        {
            target.ItemConfigs ??= new List<ItemMonitorConfig>();
            target.MonitorItems ??= new List<MonitorItemConfig>();

            foreach (var item in target.ItemConfigs)
            {
                if (string.IsNullOrWhiteSpace(item.ItemId) && string.IsNullOrWhiteSpace(item.ItemKey)) continue;
                if (string.IsNullOrWhiteSpace(item.ItemKey))
                    item.ItemKey = "ITEM." + item.ItemId.Trim();

                var monitor = target.MonitorItems.FirstOrDefault(x => x.Key.Equals(item.ItemKey, StringComparison.OrdinalIgnoreCase));
                if (monitor == null)
                {
                    monitor = new MonitorItemConfig { Key = item.ItemKey };
                    target.MonitorItems.Add(monitor);
                }

                monitor.VisibleInPanel = item.Enabled && item.VisibleInPanel;
                monitor.VisibleInTaskbar = item.Enabled && item.VisibleInTaskbar;
                monitor.SortIndex = item.SortIndex;
                monitor.TaskbarSortIndex = item.TaskbarSortIndex;
                monitor.UserLabel = !string.IsNullOrWhiteSpace(item.ShortName) ? item.ShortName : item.Name;
                monitor.TaskbarLabel = !string.IsNullOrWhiteSpace(item.ShortName) ? item.ShortName : item.Name;
            }

            target.MonitorItems.RemoveAll(x =>
                x.Key.StartsWith("ITEM.", StringComparison.OrdinalIgnoreCase) &&
                !target.ItemConfigs.Any(item =>
                    string.Equals(item.ItemKey, x.Key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals("ITEM." + item.ItemId, x.Key, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// 在应用设置后，将 Live 环境的最新监控项（包含插件生成的新项）回写同步到 Draft。
        /// 必须处理深拷贝和动态属性的恢复。
        /// </summary>
        public static void RebaseDraftMonitorItems(Settings live, Settings draft)
        {
            if (live?.MonitorItems == null || draft == null) return;

            // 1. 通过序列化进行深拷贝，断开引用关联
            // 注意：这会丢失 [JsonIgnore] 的动态属性
            var json = System.Text.Json.JsonSerializer.Serialize(live.MonitorItems);
            var newItems = System.Text.Json.JsonSerializer.Deserialize<List<MonitorItemConfig>>(json)
                           ?? new List<MonitorItemConfig>();

            // 2. 恢复动态属性 (Runtime State)
            // 因为 Draft 是给 UI 用的，必须包含当前的显示名称
            var liveMap = live.MonitorItems.ToDictionary(x => x.Key);

            foreach (var item in newItems)
            {
                if (liveMap.TryGetValue(item.Key, out var liveItem))
                {
                    item.DynamicLabel = liveItem.DynamicLabel;
                    item.DynamicTaskbarLabel = liveItem.DynamicTaskbarLabel;
                }
            }

            draft.MonitorItems = newItems;
        }
    }
}
