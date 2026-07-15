using CS2TradeMonitor.Domain.Steam;
using System;
using System.Collections.Generic;

namespace CS2TradeMonitor.Application.Steam
{
    internal static class SteamOfferMockOfferFactory
    {
        public static List<SteamOfferItem> Create(DateTime now)
        {
            return new List<SteamOfferItem>
            {
                new()
                {
                    TradeOfferId = "MOCK-IN-10001",
                    ConfirmationId = "MOCK-CID-10001",
                    ConfirmationKey = "MOCK-CK-10001",
                    Title = "纯收货报价",
                    ItemSummary = "对方发送 1 件饰品，我方不转出库存",
                    Type = SteamOfferType.IncomingGift,
                    Source = "模拟数据",
                    Status = SteamOfferStatus.Pending,
                    CreatedAt = now.AddMinutes(-3)
                },
                new()
                {
                    TradeOfferId = "MOCK-UU-10002",
                    ConfirmationId = "MOCK-CID-10002",
                    ConfirmationKey = "MOCK-CK-10002",
                    Title = "悠悠有品已校验报价",
                    ItemSummary = "驾驶手套（★） | 月色织物 (久经沙场) ¥2698.50",
                    Type = SteamOfferType.Outgoing,
                    Source = "悠悠有品待办",
                    Status = SteamOfferStatus.Pending,
                    VerifiedByYouPin = true,
                    YouPinOrderNo = "MOCK-UU-ORDER-10002",
                    PlatformOrderNo = "MOCK-UU-ORDER-10002",
                    YouPinItemName = "驾驶手套（★） | 月色织物 (久经沙场)",
                    YouPinPrice = 2698.50,
                    Amount = 2698.50,
                    CreatedAt = now.AddMinutes(-10)
                },
                new()
                {
                    TradeOfferId = "MOCK-RISK-10003",
                    ConfirmationId = "MOCK-CID-10003",
                    ConfirmationKey = "MOCK-CK-10003",
                    Title = "未校验发货报价",
                    ItemSummary = "该报价会转出你的库存，未匹配到平台订单",
                    Type = SteamOfferType.Outgoing,
                    Source = "Steam移动确认",
                    Status = SteamOfferStatus.Pending,
                    CreatedAt = now.AddMinutes(-14)
                }
            };
        }
    }
}
