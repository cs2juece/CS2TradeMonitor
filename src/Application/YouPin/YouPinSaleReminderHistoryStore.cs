using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.Shared.Trading;
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
        private readonly string _fallbackDirectory;
        private readonly Action<string, string> _info;
        private readonly Action<string, string, Exception, bool, string> _ignored;

        public YouPinSaleReminderHistoryStore(string path, JsonSerializerOptions options)
            : this(
                path,
                options,
                YouPinSaleReminderPlatform.Host.WriteTextAtomic,
                () => DateTime.Now,
                YouPinSaleReminderPlatform.Host.InstallDirectory,
                YouPinSaleReminderPlatform.Host.Info,
                YouPinSaleReminderPlatform.Host.Ignored)
        {
        }

        internal YouPinSaleReminderHistoryStore(
            string path,
            JsonSerializerOptions options,
            Action<string, string> writeTextAtomic,
            Func<DateTime> now)
            : this(
                path,
                options,
                writeTextAtomic,
                now,
                AppContext.BaseDirectory,
                static (_, _) => { },
                static (_, _, _, _, _) => { })
        {
        }

        internal YouPinSaleReminderHistoryStore(
            string path,
            JsonSerializerOptions options,
            Action<string, string> writeTextAtomic,
            Func<DateTime> now,
            string fallbackDirectory,
            Action<string, string> info,
            Action<string, string, Exception, bool, string> ignored)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _writeTextAtomic = writeTextAtomic ?? throw new ArgumentNullException(nameof(writeTextAtomic));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _fallbackDirectory = fallbackDirectory ?? throw new ArgumentNullException(nameof(fallbackDirectory));
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _ignored = ignored ?? throw new ArgumentNullException(nameof(ignored));
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
                PruneHistory(history, message => _info("YouPinTodo", message));
                LastError = "";
                return history;
            }
            catch (Exception ex)
            {
                LastError = "悠悠报价历史读取失败：" + ex.Message;
                _ignored("YouPinQuote", "LoadHistory", ex, true, "Storage");
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
                _ignored("YouPinQuote", "SaveHistory", ex, true, "Storage");
                return false;
            }
        }

        private void BackupCorruptHistoryFile()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                string directory = Path.GetDirectoryName(_path) ?? _fallbackDirectory;
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
                _info("YouPinQuote", "已备份损坏的悠悠报价历史文件: " + backupPath);
            }
            catch (Exception ex)
            {
                _ignored("YouPinQuote", "BackupCorruptHistory", ex, true, "Storage");
            }
        }
    }
}
