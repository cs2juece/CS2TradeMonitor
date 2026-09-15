using System.Text.Json;
using System.Text.Json.Serialization;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly MonitorPaths _paths;

    public AppSettingsStore(MonitorPaths paths) => _paths = paths;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SettingsPath))
            return _paths.CreateDefaultSettings();

        await using FileStream stream = File.OpenRead(_paths.SettingsPath);
        AppSettings? value = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        return (value ?? throw new InvalidDataException("设置文件为空。")).Validate();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsPath)!);
        string temporaryPath = _paths.SettingsPath + ".tmp";
        await using (FileStream stream = new(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _paths.SettingsPath, overwrite: true);
    }
}
