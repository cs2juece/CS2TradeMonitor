using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.SystemServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CS2TradeMonitor.Application.YouPin
{
    internal sealed class YouPinGridStrategyFileStore : IYouPinGridStrategyStore
    {
        internal const string FileName = "youpin_grid_state.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _path;
        private readonly IAppDiagnostics _diagnostics;

        public YouPinGridStrategyFileStore(
            IAppDataPathProvider pathProvider,
            IAppDiagnostics diagnostics)
            : this(
                (pathProvider ?? throw new ArgumentNullException(nameof(pathProvider))).GetDataFilePath(FileName),
                diagnostics)
        {
        }

        internal YouPinGridStrategyFileStore(string path, IAppDiagnostics diagnostics)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? throw new ArgumentException("Grid state path is required.", nameof(path))
                : path;
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        }

        public YouPinGridState Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return new YouPinGridState();

                YouPinGridState? state = JsonSerializer.Deserialize<YouPinGridState>(
                    File.ReadAllText(_path),
                    JsonOptions);
                return state ?? new YouPinGridState();
            }
            catch (Exception ex)
            {
                _diagnostics.Ignored(
                    "YouPinGrid",
                    "LoadState",
                    ex,
                    retryable: true,
                    category: "Storage");
                return new YouPinGridState();
            }
        }

        public bool Save(YouPinGridState state)
        {
            try
            {
                string json = JsonSerializer.Serialize(state ?? new YouPinGridState(), JsonOptions);
                RuntimeDataPaths.WriteTextAtomic(_path, json);
                return true;
            }
            catch (Exception ex)
            {
                _diagnostics.Ignored(
                    "YouPinGrid",
                    "SaveState",
                    ex,
                    retryable: true,
                    category: "Storage");
                return false;
            }
        }
    }
}
