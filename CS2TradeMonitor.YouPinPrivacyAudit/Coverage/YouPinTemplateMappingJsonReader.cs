using System.Text.Json;

namespace CS2TradeMonitor.Application.YouPin.PrivacyAudit.Coverage
{
    /// <summary>
    /// Reads the minimal output of CsWeaponManager's local
    /// getYyypIdBySteamHashName endpoint. Price and inventory fields are ignored.
    /// </summary>
    public static class YouPinTemplateMappingJsonReader
    {
        public const int MaximumMappingCount = YouPinHotCatalog.MaximumItemCount;
        public const int MaximumSourceBytes = 4 * 1024 * 1024;

        public static async Task<IReadOnlyList<YouPinTemplateMapping>> LoadAsync(
            Stream source,
            CancellationToken cancellationToken = default)
        {
            byte[] bytes = await BoundedInputReader
                .ReadAllBytesAsync(source, MaximumSourceBytes, cancellationToken)
                .ConfigureAwait(false);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(bytes);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("模板映射不是有效的 JSON。", exception);
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("success", out JsonElement success)
                    || success.ValueKind != JsonValueKind.True)
                {
                    throw new InvalidDataException("模板映射响应未成功完成。");
                }

                if (!root.TryGetProperty("data", out JsonElement data)
                    || data.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("模板映射响应缺少 data 对象。");
                }

                var mappings = new List<YouPinTemplateMapping>();
                int entryCount = 0;
                foreach (JsonProperty property in data.EnumerateObject())
                {
                    entryCount++;
                    if (entryCount > MaximumMappingCount)
                    {
                        throw new InvalidDataException(
                            $"模板映射最多允许 {MaximumMappingCount} 个条目。");
                    }

                    if (property.Value.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("模板映射条目格式无效。");

                    string marketHashName = property.Name;
                    if (!property.Value.TryGetProperty(
                            "steam_hash_name",
                            out JsonElement nameElement)
                        || nameElement.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidDataException("模板映射条目缺少有效名称。");
                    }

                    string? embeddedName = nameElement.GetString();
                    if (!string.Equals(marketHashName, embeddedName, StringComparison.Ordinal))
                        throw new InvalidDataException("模板映射的名称键与条目内容不一致。");

                    if (!property.Value.TryGetProperty("yyyp_id", out JsonElement idElement)
                        || idElement.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    long templateId = ReadPositiveTemplateId(idElement);
                    mappings.Add(YouPinTemplateMapping.Create(marketHashName, templateId));
                }

                return Array.AsReadOnly(mappings.ToArray());
            }
        }

        private static long ReadPositiveTemplateId(JsonElement element)
        {
            long templateId;
            if (element.ValueKind == JsonValueKind.Number)
            {
                if (!element.TryGetInt64(out templateId))
                    throw new InvalidDataException("悠悠模板 ID 格式无效。");
            }
            else if (element.ValueKind == JsonValueKind.String
                && long.TryParse(
                    element.GetString(),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out templateId))
            {
            }
            else
            {
                throw new InvalidDataException("悠悠模板 ID 格式无效。");
            }

            if (templateId <= 0)
                throw new InvalidDataException("悠悠模板 ID 必须为正整数。");
            return templateId;
        }
    }
}
