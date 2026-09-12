using System.Security.Cryptography;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Tomlyn;
using Tomlyn.Serialization;

namespace Borea.Storage.Mods;

public sealed class FileForeignModAdopter : IForeignModAdopter
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IInstanceRepository _instances;
    private readonly IModArchiveReleaseLookup _releaseLookup;
    private readonly TimeProvider _timeProvider;

    public FileForeignModAdopter(
        IGamePathProvider pathProvider,
        IInstanceRepository instances,
        IModArchiveReleaseLookup releaseLookup,
        TimeProvider? timeProvider = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _releaseLookup = releaseLookup ?? throw new ArgumentNullException(nameof(releaseLookup));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<ForeignMod>> ScanAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        _ = await GetInstanceAsync(instanceId).ConfigureAwait(false);
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        var foreignMods = new List<ForeignMod>();

        if (Directory.Exists(modsFolder))
        {
            foreach (var directory in Directory.EnumerateDirectories(modsFolder)
                .OrderBy(path => Path.GetFileName(path), ModIds.Comparer)
                .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var definitionPath = Path.Combine(directory, ModFolders.DefinitionFileName);
                if (!File.Exists(definitionPath))
                    continue;

                var folderName = Path.GetFileName(directory);
                foreignMods.Add(await ReadForeignModAsync(folderName, definitionPath, cancellationToken).ConfigureAwait(false));
            }
        }

        return await _instances.UpdateAsync(
            instanceId,
            instance =>
            {
                var current = foreignMods
                    .Where(foreign => !instance.Mods.Any(mod => ModIds.Equals(mod.ModId, foreign.ModId)))
                    .ToList();
                instance.ReplaceForeignMods(current);
                return instance.ForeignMods;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ForeignModAdoptionResult> AdoptArchiveAsync(
        Guid instanceId,
        string folderName,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        _ = new ForeignMod(folderName);
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("Archive path cannot be null or whitespace.", nameof(archivePath));

        _ = await GetInstanceAsync(instanceId).ConfigureAwait(false);
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        var matchingFolders = Directory.Exists(modsFolder)
            ? Directory.EnumerateDirectories(modsFolder)
                .Where(path => ModIds.Equals(Path.GetFileName(path), folderName))
                .ToList()
            : new List<string>();
        if (matchingFolders.Count > 1)
            throw new InvalidOperationException($"More than one foreign folder has the ID '{folderName}'. Use unique folder spelling before adoption.");

        var folder = matchingFolders.SingleOrDefault();
        if (folder is null || !File.Exists(Path.Combine(folder, ModFolders.DefinitionFileName)))
            throw new InvalidOperationException($"The instance has no foreign mod folder '{folderName}'.");

        var actualFolderName = Path.GetFileName(folder);
        if (!Path.IsPathFullyQualified(archivePath))
            throw new ArgumentException("Archive path must be absolute.", nameof(archivePath));

        var fullArchivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullArchivePath))
            throw new FileNotFoundException("The imported archive does not exist.", fullArchivePath);

        var sha256 = await ComputeSha256Async(fullArchivePath, cancellationToken).ConfigureAwait(false);
        var release = await _releaseLookup.FindBySha256Async(actualFolderName, sha256, cancellationToken).ConfigureAwait(false);
        if (release is null
            || release.Type != ContentType.Mod
            || !ModIds.Equals(release.ModId, actualFolderName)
            || !string.Equals(release.Download.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            var foreign = await ReadForeignModAsync(
                actualFolderName,
                Path.Combine(folder, ModFolders.DefinitionFileName),
                cancellationToken).ConfigureAwait(false);
            await StoreForeignAsync(instanceId, folder, foreign, cancellationToken).ConfigureAwait(false);
            return new ForeignModAdoptionResult(sha256, foreign, null);
        }

        var installed = new InstalledMod(
            actualFolderName,
            release.Version,
            InstallReason.Manual,
            _timeProvider.GetUtcNow(),
            release,
            sha256,
            ModInstallOwnership.Foreign);

        var scannedForeign = await ReadForeignModAsync(
            actualFolderName,
            Path.Combine(folder, ModFolders.DefinitionFileName),
            cancellationToken).ConfigureAwait(false);
        await _instances.UpdateAsync(
            instanceId,
            instance =>
            {
                RequireForeignFolder(folder, actualFolderName);

                if (instance.Mods.Any(mod => ModIds.Equals(mod.ModId, actualFolderName)))
                    throw new InvalidOperationException($"Mod '{actualFolderName}' is already installed in this instance.");

                if (!instance.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, actualFolderName)))
                {
                    var current = instance.ForeignMods.ToList();
                    current.Add(scannedForeign);
                    instance.ReplaceForeignMods(current);
                }

                instance.AdoptForeignMod(installed);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
        return new ForeignModAdoptionResult(sha256, null, installed);
    }

    private async Task<Instance> GetInstanceAsync(Guid instanceId)
        => await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");

    private async Task StoreForeignAsync(
        Guid instanceId,
        string folder,
        ForeignMod foreign,
        CancellationToken cancellationToken)
    {
        await _instances.UpdateAsync(
            instanceId,
            instance =>
            {
                RequireForeignFolder(folder, foreign.ModId);

                if (instance.Mods.Any(mod => ModIds.Equals(mod.ModId, foreign.ModId)))
                    throw new InvalidOperationException($"Mod '{foreign.ModId}' is already installed in this instance.");

                var foreignMods = instance.ForeignMods
                    .Where(mod => !ModIds.Equals(mod.ModId, foreign.ModId))
                    .Append(foreign)
                    .OrderBy(mod => mod.FolderName, ModIds.Comparer)
                    .ThenBy(mod => mod.FolderName, StringComparer.Ordinal)
                    .ToList();
                instance.ReplaceForeignMods(foreignMods);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void RequireForeignFolder(string folder, string folderName)
    {
        if (!Directory.Exists(folder) || !File.Exists(Path.Combine(folder, ModFolders.DefinitionFileName)))
            throw new InvalidOperationException($"The instance no longer has the foreign mod folder '{folderName}'.");
    }

    private static async Task<ForeignMod> ReadForeignModAsync(
        string folderName,
        string definitionPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(definitionPath, cancellationToken).ConfigureAwait(false);
            var manifest = TomlSerializer.Deserialize<LocalModManifestDto>(text);
            var dependencies = manifest?.StarMap?.ModDependencies?
                .Where(dependency => !string.IsNullOrWhiteSpace(dependency.ModId))
                .Select(dependency => new LocalModDependency(dependency.ModId, dependency.Optional))
                .ToList() ?? new List<LocalModDependency>();
            return new ForeignMod(folderName, dependencies);
        }
        catch (Exception exception) when (exception is TomlException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ForeignMod(folderName, dependencyReadError: exception.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private sealed class LocalModManifestDto
    {
        [TomlPropertyName("StarMap")]
        public LocalStarMapDto? StarMap { get; set; }
    }

    private sealed class LocalStarMapDto
    {
        [TomlPropertyName("ModDependencies")]
        public List<LocalDependencyDto>? ModDependencies { get; set; }
    }

    private sealed class LocalDependencyDto
    {
        [TomlPropertyName("ModId")]
        public string ModId { get; set; } = string.Empty;

        [TomlPropertyName("Optional")]
        public bool Optional { get; set; }
    }
}
