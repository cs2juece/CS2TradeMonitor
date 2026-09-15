using System.Text.Json;
using System.Text.Json.Serialization;
using YouPinPurchaseMonitor.Models;

namespace YouPinPurchaseMonitor.Infrastructure;

public sealed class StoreObservationRegistryStore
{
    public const int MaximumStoreCount = 3;
    public const int MaximumRegistrationCount = MaximumStoreCount * 2;
    public const int MaximumBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public StoreObservationRegistryStore(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        _path = Path.Combine(Path.GetFullPath(applicationDirectory), "observations.json");
    }

    public async Task<IReadOnlyList<StoreObservationRegistration>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
            return [];
        var info = new FileInfo(_path);
        if (info.Length > MaximumBytes)
            throw new InvalidDataException("店铺观察项注册表超过安全大小上限。");
        await using FileStream stream = File.OpenRead(_path);
        StoreObservationRegistration[] registrations =
            await JsonSerializer.DeserializeAsync<StoreObservationRegistration[]>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? [];
        ValidateCollection(registrations);
        return Array.AsReadOnly(registrations.OrderBy(item => item.CreatedAt).ToArray());
    }

    public async Task SaveAsync(
        IReadOnlyCollection<StoreObservationRegistration> registrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ValidateCollection(registrations);
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    registrations.OrderBy(item => item.CreatedAt),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumBytes)
                    throw new InvalidDataException("店铺观察项注册表超过安全大小上限。");
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateCollection(
        IReadOnlyCollection<StoreObservationRegistration> registrations)
    {
        if (registrations.Count > MaximumRegistrationCount)
            throw new InvalidDataException($"最多允许 {MaximumRegistrationCount} 个店铺观察项。");
        foreach (StoreObservationRegistration registration in registrations)
            registration.Validate();
        if (registrations.Select(item => item.WatchId).Distinct().Count() != registrations.Count)
            throw new InvalidDataException("店铺观察项包含重复 Watch ID。");
        int storeCount = registrations.Count(item => item.ScheduleLane != ObservationScheduleLane.Priority);
        if (storeCount > MaximumStoreCount)
            throw new InvalidDataException($"最多允许 {MaximumStoreCount} 个店铺观察目标。");
        IReadOnlyDictionary<Guid, StoreObservationRegistration> byId =
            registrations.ToDictionary(item => item.WatchId);
        foreach (StoreObservationRegistration registration in registrations.Where(
                     item => item.ScheduleLane != ObservationScheduleLane.Standalone))
        {
            if (registration.PartnerWatchId is not Guid partnerId
                || !byId.TryGetValue(partnerId, out StoreObservationRegistration? partner)
                || partner.PartnerWatchId != registration.WatchId
                || partner.ScheduleLane == registration.ScheduleLane
                || partner.ScheduleLane == ObservationScheduleLane.Standalone
                || !string.Equals(partner.TargetMask, registration.TargetMask, StringComparison.Ordinal)
                || partner.Purpose != registration.Purpose)
            {
                throw new InvalidDataException("双层调度通道必须成对、同目标且用途一致。");
            }
        }
    }
}
