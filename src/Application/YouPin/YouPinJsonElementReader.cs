using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin
{
    internal static class YouPinJsonElementReader
    {
        public static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                    return true;
            }

            value = default;
            return false;
        }

        public static string? GetString(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return null;
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString(),
                JsonValueKind.Number => prop.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.ToString()
            };
        }

        public static string GetImageUrl(JsonElement element)
        {
            return NormalizeImageUrl(FirstText(
                GetString(element, "imageUrl", "ImageUrl", "imgUrl", "ImgUrl", "iconUrl", "IconUrl", "coverUrl", "CoverUrl", "picUrl", "PicUrl", "pictureUrl", "PictureUrl", "thumbUrl", "ThumbUrl"),
                GetString(element, "image", "Image", "img", "Img", "icon", "Icon", "cover", "Cover", "pic", "Pic", "picture", "Picture", "thumbnail", "Thumbnail"),
                GetString(element, "commodityImage", "CommodityImage", "commodityImg", "CommodityImg", "goodsImage", "GoodsImage", "goodsImg", "GoodsImg")));
        }

        public static string FindImageUrl(JsonElement element, int depth = 0)
        {
            if (depth > 5)
                return "";

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (LooksLikeImageField(property.Name)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        string candidate = NormalizeImageUrl(property.Value.GetString() ?? "");
                        if (!string.IsNullOrWhiteSpace(candidate))
                            return candidate;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array)
                    {
                        string nested = FindImageUrl(property.Value, depth + 1);
                        if (!string.IsNullOrWhiteSpace(nested))
                            return nested;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in element.EnumerateArray())
                {
                    string nested = FindImageUrl(child, depth + 1);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            return "";
        }

        public static int GetInt(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)) return value;
            if (int.TryParse(prop.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
            return 0;
        }

        public static double GetDouble(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out var prop, names)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var num)) return num;

            string text = prop.ToString()
                .Replace("¥", "", StringComparison.Ordinal)
                .Replace("￥", "", StringComparison.Ordinal)
                .Replace(",", "", StringComparison.Ordinal)
                .Trim();

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) return value;
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value)) return value;
            return 0;
        }

        public static string FirstText(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }

        public static string JoinText(string separator, params string?[] values)
        {
            return string.Join(separator, values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        }

        public static double FirstPositive(params double[] values)
        {
            foreach (var value in values)
            {
                if (value > 0)
                    return value;
            }

            return 0;
        }

        private static bool LooksLikeImageField(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string lower = name.ToLowerInvariant();
            return lower.Contains("image", StringComparison.Ordinal)
                || lower.Contains("img", StringComparison.Ordinal)
                || lower.Contains("pic", StringComparison.Ordinal)
                || lower.Contains("picture", StringComparison.Ordinal)
                || lower.Contains("thumb", StringComparison.Ordinal)
                || lower.Contains("icon", StringComparison.Ordinal)
                || lower.Contains("cover", StringComparison.Ordinal);
        }

        private static string NormalizeImageUrl(string value)
        {
            string text = value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return "";

            if (text.StartsWith("//", StringComparison.Ordinal))
                text = "https:" + text;

            return Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri.ToString()
                : "";
        }
    }
}
