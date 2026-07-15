using System.Text.Json;

namespace CS2TradeMonitor.src.SystemServices
{
    /// <summary>
    /// 共享的 Service 基础设施：JSON 序列化选项。
    /// 各 Service 通过复用这些共享实例减少重复代码和资源开销。
    /// </summary>
    public static class ServiceInfra
    {
        public static readonly JsonSerializerOptions DefaultJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }
}
