using System.Globalization;

namespace CS2TradeMonitor.Application.YouPin.PurchaseMonitoring.Links;

public static class YouPinMarketPurchaseLink
{
    public static Uri Create(long templateId)
    {
        if (templateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(templateId),
                "模板 ID 必须为正整数。");
        }

        return new Uri(
            "https://www.youpin898.com/market/goods-list?listType=20&templateId="
            + templateId.ToString(CultureInfo.InvariantCulture)
            + "&gameId=730",
            UriKind.Absolute);
    }
}
