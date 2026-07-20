using CS2TradeMonitor.Domain.YouPin;
using System.Globalization;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin
{
    public static class YouPinGridMarketParser
    {
        public static YouPinGridMarketQuote ParseLowestValidListing(
            string json,
            string templateId,
            string itemName,
            DateTime capturedAt)
        {
            using JsonDocument document = YouPinMobileApiClient.ParseJson(json, "读取悠悠同款在售");
            JsonElement root = document.RootElement;
            int code = ReadInt(root, "code", "Code");
            if (code != 0)
            {
                return Unavailable(templateId, itemName, capturedAt, ReadText(root, "msg", "Msg"));
            }

            if (!TryReadProperty(root, out JsonElement data, "data", "Data")
                || !TryReadProperty(data, out JsonElement rows, "commodityList", "CommodityList")
                || rows.ValueKind != JsonValueKind.Array)
            {
                return Unavailable(templateId, itemName, capturedAt, "悠悠未返回同款在售列表");
            }

            decimal lowestPrice = 0m;
            string lowestListingId = "";
            int count = 0;
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string rowTemplateId = ReadText(row, "templateId", "TemplateId");
                string rowName = ReadText(row, "commodityName", "CommodityName", "name", "Name");
                decimal price = ReadDecimal(row, "price", "Price");
                bool isMine = ReadBool(row, "isMine", "IsMine");
                if (isMine
                    || price <= 0m
                    || !string.Equals(rowTemplateId, templateId, StringComparison.Ordinal)
                    || !string.Equals(rowName, itemName, StringComparison.Ordinal))
                {
                    continue;
                }

                count++;
                if (lowestPrice > 0m && price >= lowestPrice)
                    continue;

                lowestPrice = price;
                lowestListingId = ReadText(row, "id", "Id", "commodityNo", "CommodityNo");
            }

            if (lowestPrice <= 0m)
                return Unavailable(templateId, itemName, capturedAt, "当前没有同款有效在售价");

            return new YouPinGridMarketQuote
            {
                Available = true,
                TemplateId = templateId,
                ItemName = itemName,
                ListingId = lowestListingId,
                LowestPrice = lowestPrice,
                ValidListingCount = count,
                CapturedAt = capturedAt,
                Message = $"已读取 {count} 条同款有效在售"
            };
        }

        private static YouPinGridMarketQuote Unavailable(
            string templateId,
            string itemName,
            DateTime capturedAt,
            string message)
        {
            return new YouPinGridMarketQuote
            {
                TemplateId = templateId,
                ItemName = itemName,
                CapturedAt = capturedAt,
                Message = string.IsNullOrWhiteSpace(message) ? "悠悠同款在售价不可用" : message.Trim()
            };
        }

        private static bool TryReadProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in names)
                {
                    if (element.TryGetProperty(name, out value))
                        return true;
                }
            }

            value = default;
            return false;
        }

        private static string ReadText(JsonElement element, params string[] names)
        {
            if (!TryReadProperty(element, out JsonElement value, names))
                return "";
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? ""
                : value.ToString().Trim();
        }

        private static int ReadInt(JsonElement element, params string[] names)
        {
            if (!TryReadProperty(element, out JsonElement value, names))
                return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;
            return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
        }

        private static decimal ReadDecimal(JsonElement element, params string[] names)
        {
            if (!TryReadProperty(element, out JsonElement value, names))
                return 0m;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number))
                return number;
            return decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)
                ? number
                : 0m;
        }

        private static bool ReadBool(JsonElement element, params string[] names)
        {
            if (!TryReadProperty(element, out JsonElement value, names))
                return false;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
            return bool.TryParse(value.ToString(), out bool result) && result;
        }
    }
}
