using CS2TradeMonitor.src.SystemServices.InfoService;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core
{
    public static class MetricLabelResolver
    {
        /// <summary>
        /// 统一解析监控项的主标签 (Name)
        /// 优先级：UserLabel > InfoService(Runtime) > DynamicLabel(Config)
        /// </summary>
        public static string ResolveLabel(MonitorItemConfig? config)
        {
            return ResolveLabel(config, MetricRuntimeServices.ResolveInfoService());
        }

        internal static string ResolveLabel(MonitorItemConfig? config, IInfoService infoService)
        {
            if (config == null) return "";
            ArgumentNullException.ThrowIfNull(infoService);

            // 1. 用户自定义 (最高优先级)
            if (!string.IsNullOrEmpty(config.UserLabel))
                return config.UserLabel;

            // 2. 运行时动态注入 (高优先级)
            // 插件运行过程中会实时向 InfoService 注入 PROP.Label.Key
            // [Optimization] Use cached key if available
            string dynKey = !string.IsNullOrEmpty(config.CachedPropLabelKey)
                ? config.CachedPropLabelKey
                : "PROP.Label." + config.Key;

            string dynVal = infoService.GetValue(dynKey);

            // 业务约束：如果动态值为 ERROR 或空，则尝试降级 (避免闪烁或显示错误)
            // 但 InfoService 通常只存最新有效值(除非插件显式写入Error)
            if (!string.IsNullOrEmpty(dynVal))
                return dynVal;

            // 3. 配置中的动态标签 (中优先级)
            // 这是由 SyncService 定期同步并持久化保存的快照
            if (!string.IsNullOrEmpty(config.DynamicLabel))
                return config.DynamicLabel;

            // 4. 默认/失败
            return "";
        }

        /// <summary>
        /// 统一解析监控项的简略标签 (Taskbar)
        /// 优先级：TaskbarLabel > InfoService(Runtime) > DynamicTaskbarLabel(Config)
        /// </summary>
        public static string ResolveShortLabel(MonitorItemConfig? config)
        {
            return ResolveShortLabel(config, MetricRuntimeServices.ResolveInfoService());
        }

        internal static string ResolveShortLabel(MonitorItemConfig? config, IInfoService infoService)
        {
            if (config == null) return "";
            ArgumentNullException.ThrowIfNull(infoService);

            // 1. 用户自定义
            if (!string.IsNullOrEmpty(config.TaskbarLabel))
                return config.TaskbarLabel;

            // 2. 运行时动态注入
            string dynKey = !string.IsNullOrEmpty(config.CachedPropShortLabelKey)
                ? config.CachedPropShortLabelKey
                : "PROP.ShortLabel." + config.Key;

            string dynVal = infoService.GetValue(dynKey);
            if (!string.IsNullOrEmpty(dynVal))
                return dynVal;

            // 3. 配置中的动态标签
            if (!string.IsNullOrEmpty(config.DynamicTaskbarLabel))
                return config.DynamicTaskbarLabel;

            return "";
        }
    }
}
