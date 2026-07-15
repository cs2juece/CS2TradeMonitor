using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System;
using System.IO;
using System.Text.Json;
using static CS2TradeMonitor.Application.YouPin.YouPinSaleReminderHistoryHelper;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinSaleReminderHistoryStore
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _options;
        private readonly Action<string, string> _writeTextAtomic;
        private readonly Func<DateTime> _now;

        public YouPinSaleReminderHistoryStore(string path, JsonSerializerOptions options)
            : this(path, options, RuntimeDataPaths.WriteTextAtomic, () => DateTime.Now)
        {
        }

        internal YouPinSaleReminderHistoryStore(
            string path,
            JsonSerializerOptions options,
            Action<string, string> writeTextAtomic,
            Func<DateTime> now)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _writeTextAtomic = writeTextAtomic ?? throw new ArgumentNullException(nameof(writeTextAtomic));
            _now = now ?? throw new ArgumentNullException(nameof(now));
        }

        public bool HasPendingSave { get; private set; }
        public string LastError { get; private set; } = "";
        public string LastCorruptBackupPath { get; private set; } = "";

        public YouPinSaleReminderHistory Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new YouPinSaleReminderHistory();

                string json = File.ReadAllText(_path);
                var history = JsonSerializer.Deserialize<YouPinSaleReminderHistory>(json, _options) ?? new YouPinSaleReminderHistory();
                PruneHistory(history);
                LastError = "";
                return history;
            }
            catch (Exception ex)
            {
                LastError = "悠悠报价历史读取失败：" + ex.Message;
                DiagnosticsLogger.Ignored("YouPinQuote", "LoadHistory", ex, retryable: true, category: "Storage");
                BackupCorruptHistoryFile();
                return new YouPinSaleReminderHistory();
            }
        }

        public bool Save(YouPinSaleReminderHistory history)
        {
            try
            {
                string json = JsonSerializer.Serialize(history ?? new YouPinSaleReminderHistory(), _options);
                _writeTextAtomic(_path, json);
                HasPendingSave = false;
                LastError = "";
                return true;
            }
            catch (Exception ex)
            {
                HasPendingSave = true;
                LastError = "悠悠报价历史保存失败：" + ex.Message;
                DiagnosticsLogger.Ignored("YouPinQuote", "SaveHistory", ex, retryable: true, category: "Storage");
                return false;
            }
        }

        private void BackupCorruptHistoryFile()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                string directory = Path.GetDirectoryName(_path) ?? global::CS2TradeMonitor.src.SystemServices.InstallationPaths.InstallDirectory;
                string fileName = Path.GetFileName(_path);
                string timestamp = _now().ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(directory, fileName + ".corrupt_" + timestamp);
                int suffix = 1;
                while (File.Exists(backupPath))
                {
                    backupPath = Path.Combine(directory, fileName + ".corrupt_" + timestamp + "_" + suffix.ToString("00"));
                    suffix++;
                }

                File.Move(_path, backupPath);
                LastCorruptBackupPath = backupPath;
                DiagnosticsLogger.Info("YouPinQuote", "已备份损坏的悠悠报价历史文件: " + backupPath);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("YouPinQuote", "BackupCorruptHistory", ex, retryable: true, category: "Storage");
            }
        }
    }
}
