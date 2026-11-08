using System.Security.Cryptography;
using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Files;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Mods;

public sealed class FileForeignModAdopter : IForeignModAdopter
{
    private const int RestoreAttempts = 20;
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(100);

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
        var foreignMods = await ReadModsFolderAsync(_pathProvider.GetInstanceModsFolder(instanceId), cancellationToken).ConfigureAwait(false);

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

    private static async Task<List<ForeignMod>> ReadModsFolderAsync(string modsFolder, CancellationToken cancellationToken)
    {
        var foreignMods = new List<ForeignMod>();
        if (!Directory.Exists(modsFolder))
            return foreignMods;

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

        return foreignMods;
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

    public async Task ReplaceFolderAsync(
        Guid instanceId,
        string folderName,
        Func<CancellationToken, Task> install,
        CancellationToken cancellationToken = default)
    {
        _ = new ForeignMod(folderName);
        ArgumentNullException.ThrowIfNull(install);
        _ = await GetInstanceAsync(instanceId).ConfigureAwait(false);
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        var recoveryFolder = Path.Combine(_pathProvider.GetInstanceRoot(instanceId), $".borea-recovery-{Guid.NewGuid():N}");
        string? folder = null;
        ForeignMod? record = null;

        try
        {
            await _instances.UpdateAsync(
                instanceId,
                instance =>
                {
                    if (instance.Mods.Any(mod => ModIds.Equals(mod.ModId, folderName)))
                        throw new InvalidOperationException($"Mod '{folderName}' is recorded in this instance, so it is not a foreign folder.");

                    var folders = Directory.Exists(modsFolder)
                        ? Directory.EnumerateDirectories(modsFolder)
                            .Where(path => ModIds.Equals(Path.GetFileName(path), folderName))
                            .ToList()
                        : new List<string>();
                    if (folders.Count == 0)
                        throw new InvalidOperationException($"The instance has no foreign mod folder '{folderName}'.");
                    if (folders.Count > 1)
                        throw new InvalidOperationException($"More than one foreign folder has the ID '{folderName}'.");

                    cancellationToken.ThrowIfCancellationRequested();
                    record = instance.ForeignMods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, folderName));
                    Directory.Move(folders[0], recoveryFolder);
                    folder = folders[0];
                    instance.ReplaceForeignMods(instance.ForeignMods
                        .Where(mod => !ModIds.Equals(mod.ModId, folderName))
                        .ToList());
                    return true;
                },
                cancellationToken).ConfigureAwait(false);

            await install(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception operationError) when (folder is not null)
        {
            try
            {
                await RestoreFolderAsync(instanceId, folder, recoveryFolder, record).ConfigureAwait(false);
            }
            catch (Exception recoveryError)
            {
                throw new ModReplacementRecoveryException(operationError, recoveryError, Directory.Exists(recoveryFolder) ? recoveryFolder : folder);
            }

            throw;
        }

        // A foreign folder can be a link of the player's own, and then only the link goes.
        LeftoverCleanup.TryDeleteTree(recoveryFolder);
    }

    private async Task RestoreFolderAsync(Guid instanceId, string folder, string recoveryFolder, ForeignMod? record)
    {
        // A size walk of the instance can still read the recovery folder, and Windows does not move a
        // folder while a file or folder below it is open, so the move back is tried again for about two seconds.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await MoveBackAsync(instanceId, folder, recoveryFolder, record).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                && attempt < RestoreAttempts
                && Directory.Exists(recoveryFolder))
            {
                await Task.Delay(RestoreRetryDelay, _timeProvider).ConfigureAwait(false);
            }
        }
    }

    private async Task MoveBackAsync(Guid instanceId, string folder, string recoveryFolder, ForeignMod? record)
    {
        await _instances.UpdateAsync(
            instanceId,
            instance =>
            {
                if (Directory.Exists(folder))
                    throw new InvalidOperationException($"The folder '{Path.GetFileName(folder)}' exists again, so Borea cannot move the previous files back.");

                Directory.Move(recoveryFolder, folder);
                if (record is null
                    || instance.Mods.Any(mod => ModIds.Equals(mod.ModId, record.ModId))
                    || instance.ForeignMods.Any(mod => ModIds.Equals(mod.ModId, record.ModId)))
                    return true;

                instance.ReplaceForeignMods(instance.ForeignMods
                    .Append(record)
                    .OrderBy(mod => mod.FolderName, ModIds.Comparer)
                    .ThenBy(mod => mod.FolderName, StringComparer.Ordinal)
                    .ToList());
                return true;
            }).ConfigureAwait(false);
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
            var manifest = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
            return new ForeignMod(folderName, LoaderDependencies(manifest));
        }
        catch (Exception exception) when (exception is TomlException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new ForeignMod(folderName, dependencyReadError: exception.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The [[ModDependencies]] blocks in the table of the first known loader that the mod.toml has, without the blocks
    /// whose ModId is blank.
    /// </summary>
    /// <exception cref="InvalidDataException">The table or a block does not have the shape the loader reads.</exception>
    private static List<LocalModDependency> LoaderDependencies(TomlTable manifest)
    {
        if (LoaderProfiles.Known.ForModToml(manifest.ContainsKey)?.ModTomlSection is not { } name)
            return [];
        if (manifest[name] is not TomlTable section)
            throw new InvalidDataException($"[{name}] of the mod.toml is not a table.");
        if (!section.TryGetValue("ModDependencies", out var value))
            return [];

        IEnumerable<object?> blocks = value switch
        {
            TomlTableArray tables => tables,
            TomlArray array => array,
            _ => throw new InvalidDataException($"{name}.ModDependencies of the mod.toml is not a list of [[{name}.ModDependencies]] blocks."),
        };

        var dependencies = new List<LocalModDependency>();
        foreach (var item in blocks)
        {
            if (item is not TomlTable block)
                throw new InvalidDataException($"a [[{name}.ModDependencies]] block is not a table.");

            var modId = block.TryGetValue("ModId", out var id) ? id : null;
            var optional = block.TryGetValue("Optional", out var flag) ? flag : false;
            if (modId is not (null or string) || optional is not bool isOptional)
                throw new InvalidDataException($"a [[{name}.ModDependencies]] block has a ModId that is not text or an Optional that is not true or false.");

            if (modId is string text && !string.IsNullOrWhiteSpace(text))
                dependencies.Add(new LocalModDependency(text, isOptional));
        }

        return dependencies;
    }
}
