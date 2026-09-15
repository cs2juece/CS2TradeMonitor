using System.Globalization;
using CS2TradeMonitor.Domain.InventoryMonitoring;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal static class LocalInventoryMonitorPageModel
    {
        public static int NormalizeRefreshMinutes(int value) => Math.Clamp(value <= 0 ? 5 : value, 5, 1440);

        public static int NormalizeMinimumChangeCount(int value) => Math.Clamp(value <= 0 ? 1 : value, 1, 100000);

        public static string BuildStatusDetail(LocalInventoryMonitorSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.Error))
                return snapshot.Error;
            if (snapshot.LastRefreshAt is DateTimeOffset refreshed)
                return $"上次检查：{refreshed.LocalDateTime:yyyy-MM-dd HH:mm:ss} · 观察对象 {snapshot.Targets.Count} 个";
            return snapshot.Enabled ? "等待首次自动检查。" : "启用后，软件将在本机后台定时检查。";
        }

        public static string BuildTargetsText(LocalInventoryMonitorSnapshot snapshot)
        {
            if (snapshot.Targets.Count == 0)
                return "暂无观察对象。保存 SteamID 名单并刷新后，这里会显示库存数量和检查状态。";

            return string.Join(
                Environment.NewLine,
                snapshot.Targets.Select(target =>
                {
                    string checkedText = target.LastCheckedAt?.LocalDateTime.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "未检查";
                    string name = string.IsNullOrWhiteSpace(target.DisplayName) ? target.SteamId : target.DisplayName;
                    string detail = string.IsNullOrWhiteSpace(target.Error) ? target.Status : target.Status + " · " + target.Error;
                    return $"{name}  |  库存 {target.TotalItemCount} 件  |  {checkedText}  |  {detail}";
                }));
        }

        public static string BuildEventsText(LocalInventoryMonitorSnapshot snapshot)
        {
            if (snapshot.RecentEvents.Count == 0)
                return "尚无新异动。本机第一次读取只建立基线，不会把历史记录误报成新事件。";

            IReadOnlyDictionary<string, string> displayNames = snapshot.Targets
                .ToDictionary(item => item.SteamId, item => item.DisplayName, StringComparer.Ordinal);
            return string.Join(
                Environment.NewLine,
                snapshot.RecentEvents.Take(80).Select(item =>
                {
                    string name = displayNames.TryGetValue(item.SteamId, out string? displayName)
                        && !string.IsNullOrWhiteSpace(displayName)
                            ? displayName
                            : string.IsNullOrWhiteSpace(item.SteamName) ? item.SteamId : item.SteamName;
                    return $"{item.OccurredAt.LocalDateTime:MM-dd HH:mm:ss}  {name}  {item.KindText} {item.Count} 件  {item.MarketName}";
                }));
        }
    }
}
