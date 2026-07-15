using CS2TradeMonitor.Domain.YouPin;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinSaleReminderMockOrderFactory
    {
        public static List<YouPinSaleOrder> CreateTodoOrders(int id, DateTime now)
        {
            string suffix = BuildSuffix(id, now);
            return new List<YouPinSaleOrder>
            {
                new()
                {
                    OrderNo = "MOCK-UU-" + suffix,
                    Name = "驾驶手套（★） | 月色织物 (久经沙场)",
                    Message = "有买家下单，待您发送报价",
                    Price = 2698.50,
                    DetectedAt = now,
                    Source = "模拟数据"
                },
                new()
                {
                    OrderNo = "MOCK-TODO-" + suffix,
                    Name = "驾驶手套（★） | 月色织物 (久经沙场)",
                    Message = "无法接收归还，待处理",
                    Price = 2698.50,
                    DetectedAt = now,
                    Source = "模拟数据"
                }
            };
        }

        public static List<YouPinSaleOrder> CreateWaitDeliverOrders(int id, DateTime now)
        {
            return new List<YouPinSaleOrder>
            {
                new()
                {
                    OrderNo = "MOCK-WAIT-" + BuildSuffix(id, now),
                    Name = "刺刀（★） | 渐变大理石 (崭新出厂)",
                    Message = "有买家购买，等待处理",
                    Price = 4500.00,
                    DetectedAt = now,
                    Source = "模拟待发货"
                }
            };
        }

        public static List<YouPinSaleOrder> CreateMsgCenterNotices(int id, DateTime now)
        {
            string suffix = BuildSuffix(id, now);
            return new List<YouPinSaleOrder>
            {
                new()
                {
                    OrderNo = "MOCK-MSG-LEASE-" + suffix,
                    Name = "物品租赁成功通知",
                    Message = "您的 运动手套（★） | 潘多拉之盒 (久经沙场) 已被成功承租，租期 7 天。",
                    Price = 120.00,
                    DetectedAt = now,
                    Source = "模拟租赁"
                },
                new()
                {
                    OrderNo = "MOCK-MSG-SYS-" + suffix,
                    Name = "悠悠有品系统通知",
                    Message = "安全提醒：官方客服不会私下向您索要密码或验证码，请提高警惕。",
                    Price = 0,
                    DetectedAt = now,
                    Source = "模拟系统"
                },
                new()
                {
                    OrderNo = "MOCK-MSG-ACT-" + suffix,
                    Name = "平台活动消息",
                    Message = "【特惠活动】发布转租可免除2%服务费，点击查看规则。",
                    Price = 0,
                    DetectedAt = now,
                    Source = "模拟活动"
                }
            };
        }

        private static string BuildSuffix(int id, DateTime now)
        {
            return now.ToString("HHmmss") + "-" + id;
        }
    }
}
