using System.Collections.Concurrent;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Mods;
using Borea.Storage.Toml;

namespace Borea.Storage.Instances;

/// <summary>
/// Keeps the install records of an instance in installed-content.toml in its root. A file that cannot
/// be read throws <see cref="InvalidOperationException"/> naming its path, and nothing writes over it.
/// </summary>
public sealed class FileInstalledContentRecords : IInstalledContentRecords
{
    private readonly IGamePathProvider _paths;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _instanceLocks = new();

    public FileInstalledContentRecords(IGamePathProvider paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public Task<IReadOnlyList<InstalledContentRecord>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        LockedAsync<IReadOnlyList<InstalledContentRecord>>(instanceId, async () => await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false), cancellationToken);

    public Task<InstalledContentRecord?> FindAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) =>
        LockedAsync(instanceId, async () => Find(await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false), type, folderName), cancellationToken);

    public Task AddAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return LockedAsync(instanceId, async () =>
        {
            var records = await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false);
            if (Find(records, record.Type, record.FolderName) is not null)
                throw new InvalidOperationException($"Instance '{instanceId}' already has a record of the {MetadataEnumMapper.ToDto(record.Type)} folder '{record.FolderName}'.");

            records.Add(record);
            await WriteAsync(instanceId, records, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task ReplaceAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return LockedAsync(instanceId, async () =>
        {
            var records = await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false);
            var index = records.FindIndex(item => Matches(item, record.Type, record.FolderName));
            if (index < 0)
                throw new InvalidOperationException($"Instance '{instanceId}' has no record of the {MetadataEnumMapper.ToDto(record.Type)} folder '{record.FolderName}'.");

            records[index] = record;
            await WriteAsync(instanceId, records, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<bool> RemoveAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) =>
        LockedAsync(instanceId, async () =>
        {
            var records = await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false);
            if (records.RemoveAll(item => Matches(item, type, folderName)) == 0)
                return false;

            await WriteAsync(instanceId, records, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    public Task<bool> RenameFolderAsync(Guid instanceId, ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default) =>
        LockedAsync(instanceId, async () =>
        {
            var records = await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false);
            var index = records.FindIndex(item => Matches(item, type, oldFolderName));
            if (index < 0)
                return false;

            if (records.Where((_, position) => position != index).Any(item => Matches(item, type, newFolderName)))
                throw new InvalidOperationException($"Instance '{instanceId}' already has a record of the {MetadataEnumMapper.ToDto(type)} folder '{newFolderName}'.");

            records[index] = records[index].WithFolderName(newFolderName);
            await WriteAsync(instanceId, records, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    public async Task<InstalledContentChanges> CompareAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var folder = FindItemFolder(instanceId, record.Type, record.FolderName);
        var current = folder is null
            ? new Dictionary<string, string>()
            : await InstalledContentHashes.ComputeAsync(folder, cancellationToken).ConfigureAwait(false);

        return new InstalledContentChanges(
            Sorted(current.Keys.Where(path => !record.Files.ContainsKey(path))),
            Sorted(record.Files.Keys.Where(path => !current.ContainsKey(path))),
            Sorted(record.Files.Where(file => current.TryGetValue(file.Key, out var hash) && hash != file.Value).Select(file => file.Key)));
    }

    public Task<IReadOnlyList<InstalledContentRecord>> DropMissingAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
        LockedAsync<IReadOnlyList<InstalledContentRecord>>(instanceId, async () =>
        {
            var records = await ReadAsync(instanceId, cancellationToken).ConfigureAwait(false);
            var missing = records.Where(record => FindItemFolder(instanceId, record.Type, record.FolderName) is null).ToList();
            if (missing.Count > 0)
                await WriteAsync(instanceId, records.Except(missing).ToList(), cancellationToken).ConfigureAwait(false);

            return missing;
        }, cancellationToken);

    /// <summary>The folder in the letter case found on disk, or null when it is not there.</summary>
    private string? FindItemFolder(Guid instanceId, ContentType type, string folderName)
    {
        var kindFolder = type == ContentType.Save ? _paths.GetInstanceSavesFolder(instanceId) : _paths.GetInstanceVehiclesFolder(instanceId);
        var folder = FileGameSaveStore.FindFolder(Path.Combine(FileGameSaveStore.FindFolder(kindFolder), folderName));
        return Directory.Exists(folder) ? folder : null;
    }

    private static InstalledContentRecord? Find(IEnumerable<InstalledContentRecord> records, ContentType type, string folderName) =>
        records.FirstOrDefault(record => Matches(record, type, folderName));

    private static bool Matches(InstalledContentRecord record, ContentType type, string folderName) =>
        record.Type == type && string.Equals(record.FolderName, folderName, StringComparison.OrdinalIgnoreCase);

    private static string[] Sorted(IEnumerable<string> paths) => paths.Order(StringComparer.Ordinal).ToArray();

    /// <summary>Takes the gate of the instance once; nothing inside takes it again, because it is not reentrant.</summary>
    private async Task<T> LockedAsync<T>(Guid instanceId, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var gate = _instanceLocks.GetOrAdd(instanceId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<InstalledContentRecord>> ReadAsync(Guid instanceId, CancellationToken cancellationToken)
    {
        var path = _paths.GetInstanceInstalledContentPath(instanceId);
        var dto = await TomlFileStore.ReadAsync<InstalledContentDto>(path, cancellationToken).ConfigureAwait(false);
        try
        {
            return (dto?.Items ?? []).Select(FromDto).ToList();
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NullReferenceException)
        {
            throw new InvalidOperationException($"{path} holds a record Borea cannot read. {exception.Message}", exception);
        }
    }

    private async Task WriteAsync(Guid instanceId, List<InstalledContentRecord> records, CancellationToken cancellationToken)
    {
        var path = _paths.GetInstanceInstalledContentPath(instanceId);
        if (records.Count == 0)
        {
            TomlFileStore.DeleteIfExists(path);
            return;
        }

        await TomlFileStore.WriteAsync(path, new InstalledContentDto { Items = records.Select(ToDto).ToList() }, cancellationToken).ConfigureAwait(false);
    }

    private static InstalledContentItemDto ToDto(InstalledContentRecord record) => new()
    {
        Type = MetadataEnumMapper.ToDto(record.Type),
        ListingId = record.ListingId,
        Version = record.Version.ToString(),
        Folder = record.FolderName,
        InstalledAt = record.InstalledAt,
        Metadata = ModVersionMetadataMapper.ToDto(record.Metadata),
        Files = record.Files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new InstalledFileDto { Path = file.Key, Sha256 = file.Value }).ToList(),
    };

    private static InstalledContentRecord FromDto(InstalledContentItemDto dto)
    {
        var metadata = ModVersionMetadataMapper.FromDto(dto.Metadata);
        if (MetadataEnumMapper.ParseContentType(dto.Type) != metadata.Type
            || !ModIds.Equals(dto.ListingId, metadata.ModId)
            || !ModVersion.TryParse(dto.Version, out var version) || version != metadata.Version)
        {
            throw new FormatException($"The record of folder '{dto.Folder}' does not agree with the release it keeps.");
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in dto.Files)
        {
            if (!files.TryAdd(file.Path, file.Sha256))
                throw new FormatException($"The record of folder '{dto.Folder}' lists '{file.Path}' twice.");
        }

        return new InstalledContentRecord(metadata, dto.Folder, dto.InstalledAt, files);
    }
}
