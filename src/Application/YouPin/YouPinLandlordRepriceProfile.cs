using System.Globalization;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinJsonElementReader;

namespace CS2TradeMonitor.Application.YouPin
{
    // PriceChangeWithLeaseV2 also carries rental activity settings. Keep the item's
    // initialization profile; shelf type and account defaults cannot substitute for it.
    internal sealed record YouPinLandlordRepriceProfile(
        int CompensationType,
        int SupportZeroCd,
        int? PricingType = null,
        decimal? MinCoefficient = null,
        decimal? ShortRentPrice = null,
        decimal? LongRentPrice = null,
        int? SupportSubletIncentive = null)
    {
        public static YouPinLandlordRepriceProfile? Parse(JsonElement root, string listingId)
        {
            if (!TryGetProperty(root, out JsonElement data, "data", "Data")
                || !TryGetProperty(data, out JsonElement compensationMap, "normalLeaseCompensationMap")
                || !TryGetProperty(compensationMap, out JsonElement compensation, listingId)
                || !TryReadInt(compensation, "compensationTypeCode", out int compensationType)
                || compensationType < 0
                || !TryGetProperty(data, out JsonElement zeroCdMap, "zeroCDRentConfigMap")
                || !TryGetProperty(zeroCdMap, out JsonElement zeroCd, listingId)
                || !TryReadInt(zeroCd, "zeroCDRentSwitch", out int enabled)
                || enabled is not (0 or 1))
            {
                return null;
            }

            int? incentive = null;
            if (TryGetProperty(data, out JsonElement incentiveMap, "subletIncentiveConfigMap")
                && TryGetProperty(incentiveMap, out JsonElement incentiveConfig, listingId)
                && incentiveConfig.ValueKind != JsonValueKind.Null)
            {
                if (incentiveConfig.ValueKind != JsonValueKind.Object)
                    return null;
                // This optional activity is absent when the platform returns null or
                // omits its switch. It must not invalidate the mandatory 0CD profile.
                if (TryGetProperty(incentiveConfig, out JsonElement incentiveSwitch, "subletIncentiveSwitch")
                    && incentiveSwitch.ValueKind != JsonValueKind.Null)
                {
                    if (!TryReadInt(incentiveConfig, "subletIncentiveSwitch", out int value)
                        || value is not (0 or 1))
                        return null;
                    incentive = value;
                }
            }

            var profile = new YouPinLandlordRepriceProfile(compensationType, enabled,
                SupportSubletIncentive: incentive);
            if (enabled == 0)
                return profile;

            // Renewals require a separate user decision, never an implicit price edit.
            if (TryGetProperty(zeroCd, out JsonElement renewal, "needReNew")
                && renewal.ValueKind != JsonValueKind.False)
                return null;
            if (!TryReadInt(zeroCd, "pricingType", out int pricingType))
                return null;
            if (pricingType == 0)
            {
                return TryReadDecimal(zeroCd, "marketDynamicPricingMinCoefficient", out decimal coefficient)
                    && coefficient > 0m
                    ? profile with { PricingType = 0, MinCoefficient = coefficient }
                    : null;
            }
            if (pricingType != 1 || !TryReadDecimal(zeroCd, "shortRentPrice", out decimal shortRent)
                || shortRent <= 0m)
                return null;

            decimal? longRent = null;
            if (TryGetProperty(zeroCd, out JsonElement longPrice, "longRentPrice")
                && longPrice.ValueKind != JsonValueKind.Null)
            {
                if (!TryReadDecimal(zeroCd, "longRentPrice", out decimal value) || value < 0m)
                    return null;
                longRent = value;
            }
            return profile with { PricingType = 1, ShortRentPrice = shortRent, LongRentPrice = longRent };
        }

        public void ApplyTo(Dictionary<string, object> commodity)
        {
            commodity["CompensationType"] = CompensationType;
            commodity["SupportZeroCD"] = SupportZeroCd;
            if (SupportSubletIncentive.HasValue)
                commodity["supportSubletIncentive"] = SupportSubletIncentive.Value;
            if (SupportZeroCd == 0)
                return;

            var config = new Dictionary<string, object> { ["PricingType"] = PricingType!.Value };
            AddPrice(config, "MinCoefficient", MinCoefficient);
            AddPrice(config, "ShortRentPrice", ShortRentPrice);
            AddPrice(config, "LongRentPrice", LongRentPrice);
            commodity["ZeroCDConfig"] = config;
        }

        private static void AddPrice(Dictionary<string, object> config, string key, decimal? value)
        {
            if (value.HasValue)
                config[key] = value.Value.ToString("G29", CultureInfo.InvariantCulture);
        }

        private static bool TryReadInt(JsonElement parent, string key, out int value)
        {
            value = 0;
            return TryGetProperty(parent, out JsonElement element, key)
                && (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)
                    || element.ValueKind == JsonValueKind.String
                        && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value));
        }

        private static bool TryReadDecimal(JsonElement parent, string key, out decimal value)
        {
            value = 0m;
            return TryGetProperty(parent, out JsonElement element, key)
                && (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value)
                    || element.ValueKind == JsonValueKind.String
                        && decimal.TryParse(element.GetString(), NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out value));
        }
    }
}
