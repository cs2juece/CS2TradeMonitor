using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CS2TradeMonitor.src.SystemServices;

namespace CS2TradeMonitor.src.Core
{
    public static class LanguageManager
    {
        public static string CurrentLang { get; private set; } = "zh";
        private static Dictionary<string, string> _texts = new();

        // ★★★ 1. 新增：用户自定义覆盖字典 ★★★
        private static Dictionary<string, string> _overrides = new();

        private static string LangDir
        {
            get
            {
                var dir = Path.Combine(InstallationPaths.ResourcesDirectory, "lang");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static void Load(string? langCode = null)
        {
            langCode = "zh";

            // [优化] 如果请求的语言与当前已加载语言一致，且字典不为空，则跳过加载
            // 使用 OrdinalIgnoreCase 忽略大小写差异 (如 "zh" vs "ZH")
            if (string.Equals(CurrentLang, langCode, StringComparison.OrdinalIgnoreCase) && _texts.Count > 0)
            {
                return;
            }

            try
            {
                var path = Path.Combine(LangDir, $"{langCode}.json");
                if (!File.Exists(path))
                {
                    DiagnosticsLogger.Info("Language", $"Missing lang file: {langCode}.json");
                    return;
                }
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                _texts = Flatten(doc.RootElement);
                CurrentLang = langCode;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Language", "Load failed", ex);
            }
        }

        // ★★★ 2. 新增：注入/清除覆盖的方法 ★★★
        public static void SetOverride(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(value))
            {
                if (_overrides.ContainsKey(key)) _overrides.Remove(key);
            }
            else
            {
                _overrides[key] = value;
            }
        }

        public static void ClearOverrides() => _overrides.Clear();

        // [新增] 获取原始翻译值（这是基础逻辑）
        public static string GetOriginal(string key)
        {
            // 1. 查原始字典
            if (_texts.TryGetValue(key, out var val)) return val;

            // 2. 没找到则回退到 Key 的后缀
            int dot = key.IndexOf('.');
            return dot >= 0 ? key[(dot + 1)..] : key;
        }
        public static string T(string key)
        {
            // 1. 优先检查用户自定义覆盖
            if (_overrides.TryGetValue(key, out var overrideVal)) return UIUtils.Intern(overrideVal);

            // 2. 没有覆盖，就直接复用基础逻辑并驻留结果
            return UIUtils.Intern(GetOriginal(key));
        }

        // ★★★ 优化: Intern 驻留 Key 字符串，防止 Items.CPU.XXX 重复 ★★★
        private static Dictionary<string, string> Flatten(JsonElement element, string prefix = "")
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in element.EnumerateObject())
            {
                // 同样驻留 Key
                string fullKey = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var kv in Flatten(prop.Value, fullKey))
                        dict[kv.Key] = kv.Value;
                }
                else
                {
                    dict[UIUtils.Intern(fullKey)] = prop.Value.GetString() ?? "";
                }
            }
            return dict;
        }
    }
}
