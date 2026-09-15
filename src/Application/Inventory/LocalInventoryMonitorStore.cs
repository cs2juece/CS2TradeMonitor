using System.Text.Json;
using CS2TradeMonitor.Domain.InventoryMonitoring;
using CS2TradeMonitor.Shared.Inventory;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.Application.Inventory
{
    internal sealed class LocalInventoryMonitorStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        private readonly string _path;

        public LocalInventoryMonitorStore(string path)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("库存监控数据文件路径不能为空。", nameof(path))
                : path;
        }

        public LocalInventoryMonitorHistory Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new LocalInventoryMonitorHistory();

                string json = File.ReadAllText(_path);
                LocalInventoryMonitorHistory history =
                    JsonSerializer.Deserialize<LocalInventoryMonitorHistory>(json, JsonOptions)
                    ?? new LocalInventoryMonitorHistory();
                Normalize(history);
                return history;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("LocalInventory", "读取本机库存监控历史失败，已使用空历史：" + ex.GetType().Name);
                return new LocalInventoryMonitorHistory();
            }
        }

        public bool TrySave(LocalInventoryMonitorHistory history, out string error)
        {
            try
            {
                Normalize(history);
                string json = JsonSerializer.Serialize(history, JsonOptions);
                RuntimeDataPaths.WriteTextAtomic(_path, json);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Info("LocalInventory", "保存本机库存监控历史失败：" + ex.GetType().Name);
                error = "本机库存监控基线保存失败；为避免重启后重复提醒，请检查用户数据目录权限。";
                return false;
            }
        }

        internal static void Normalize(LocalInventoryMonitorHistory history)
            => LocalInventoryMonitorProcessor.Normalize(history);
    }
}
