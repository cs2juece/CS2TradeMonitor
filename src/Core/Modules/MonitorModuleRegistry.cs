using System;
using System.Collections.Generic;
using System.Linq;

namespace CS2TradeMonitor.src.Core.Modules
{
    public static class MonitorModuleRegistry
    {
        public static readonly MonitorModuleDescriptor MarketMonitor = new(
            "market",
            "大盘监控",
            "SteamDT/QAQ 大盘指数、预警和显示管线。",
            new[] { "Data", "MarketAlerts", "MainPanel" },
            new[] { "SteamDT 大盘", "QAQ 大盘", "大盘预警" },
            new[] { "MarketAlert" });

        public static readonly MonitorModuleDescriptor ItemMonitor = new(
            "item",
            "单品监控",
            "SteamDT 单品搜索、价格刷新、显示和单品提醒。",
            new[] { "ItemMonitor" },
            new[] { "SteamDT 单品价格", "单品显示项" },
            new[] { "ItemPriceAlert" });

        public static readonly MonitorModuleDescriptor YouPinInventory = new(
            "youpin-inventory",
            "悠悠库存",
            "悠悠有品库存涨跌、库存快照和止盈止损提醒。",
            new[] { "YouPin", "YouPinStopProfitLoss" },
            new[] { "悠悠库存读取", "库存涨跌快照", "止盈止损扫描" },
            new[] { "InventoryTrendAlert", "StopProfitLossAlert" });

        public static readonly MonitorModuleDescriptor YouPinTodo = new(
            "youpin-todo",
            "悠悠待办",
            "悠悠有品待办、待发货/报价处理和消息中心提醒。",
            Array.Empty<string>(),
            new[] { "悠悠待办", "待发货/报价处理", "自动发货诊断" },
            new[] { "YouPinTodoAlert", "YouPinMessageAlert" });

        public static readonly MonitorModuleDescriptor SteamOffers = new(
            "steam-offers",
            "Steam 报价",
            "Steam 令牌、登录状态、报价列表和纯收货报价处理。",
            new[] { "SteamOffers" },
            new[] { "Steam 移动确认", "Steam 报价安全判定" },
            new[] { "SteamOfferAlert" },
            isHighRisk: true,
            processIsolationCandidate: true);

        public static readonly MonitorModuleDescriptor Notification = new(
            "notification",
            "通知通道",
            "桌面弹窗、托盘气泡、提示音和手机提醒统一发送通道。",
            new[] { "Cs2UpdatePhoneReminder", "MarketAlerts" },
            new[] { "本地提醒", "手机提醒", "CS2 更新提醒", "勿扰/全屏保护" },
            new[] { "DesktopToast", "TrayBalloon", "PhonePush" });

        public static IReadOnlyList<MonitorModuleDescriptor> Descriptors { get; } =
            new[]
            {
                MarketMonitor,
                ItemMonitor,
                YouPinInventory,
                YouPinTodo,
                SteamOffers,
                Notification
            };

        public static IReadOnlyList<IMonitorModule> CreateModules()
        {
            var modules = new IMonitorModule[]
            {
                new MarketMonitorModule(),
                new ItemMonitorModule(),
                new YouPinInventoryModule(),
                new YouPinTodoModule(),
                new SteamOffersModule(),
                new NotificationModule()
            };

            Validate(modules);
            return modules;
        }

        private static void Validate(IReadOnlyList<IMonitorModule> modules)
        {
            var duplicateIds = modules
                .GroupBy(module => module.Id, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            if (duplicateIds.Length > 0)
                throw new InvalidOperationException("监控模块 ID 重复: " + string.Join(", ", duplicateIds));

            var descriptorIds = new HashSet<string>(Descriptors.Select(descriptor => descriptor.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules)
            {
                if (!descriptorIds.Contains(module.Id))
                    throw new InvalidOperationException("监控模块没有注册描述: " + module.Id);
            }
        }
    }
}
