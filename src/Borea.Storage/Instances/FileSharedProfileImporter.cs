using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.State;
using Borea.Storage.Mods;
using Borea.Storage.State;
using Borea.Storage.Toml;

namespace Borea.Storage.Instances;

/// <summary>
/// A copy that matches a release keeps foreign ownership, because its files came
/// from outside Borea and can hold more than the release.
/// </summary>
public sealed class FileSharedProfileImporter : ISharedProfileImporter
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IInstanceRepository _instances;
    private readonly IModStateRepository _modState;
    private readonly IForeignModAdopter _adopter;
    private readonly IForeignModReleaseMatcher _matcher;

    public FileSharedProfileImporter(
        IGamePathProvider pathProvider,
        IInstanceRepository instances,
        IModStateRepository modState,
        IForeignModAdopter adopter,
        IForeignModReleaseMatcher matcher)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _adopter = adopter ?? throw new ArgumentNullException(nameof(adopter));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    }

    /// <summary>ModLibrary.LocalModsFolderPath.</summary>
    private string ModsFolder => Path.Combine(_pathProvider.GetSharedProfileRoot(), "mods");

    public async Task<IReadOnlyList<SharedProfileMod>> GetModsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ModsFolder))
            return [];

        var folders = Directory.EnumerateDirectories(ModsFolder)
            .Where(directory => File.Exists(Path.Combine(directory, ModFolders.DefinitionFileName)))
            .Select(directory => Path.GetFileName(directory))
            .OrderBy(name => name, ModIds.Comparer)
            .ThenBy(name => name, StringComparer.Ordinal)
            .DistinctBy(name => name, ModIds.Comparer)
            .ToList();
        if (folders.Count == 0)
            return [];

        var manifestPath = Path.Combine(_pathProvider.GetSharedProfileRoot(), "manifest.toml");
        var manifest = await TomlFileStore.ReadAsync<ManifestDto>(manifestPath, cancellationToken).ConfigureAwait(false) ?? new ManifestDto();

        var mods = new List<SharedProfileMod>();
        foreach (var entry in manifest.Mods)
        {
            var folder = folders.FirstOrDefault(name => ModIds.Equals(name, entry.Id));
            if (folder is null || mods.Any(mod => ModIds.Equals(mod.FolderName, folder)))
                continue;

            mods.Add(new SharedProfileMod(folder, manifest.Mods.Any(other => other.Enabled && ModIds.Equals(other.Id, folder))));
        }

        mods.AddRange(folders
            .Where(folder => !mods.Any(mod => ModIds.Equals(mod.FolderName, folder)))
            .Select(folder => new SharedProfileMod(folder, Enabled: false)));
        return mods;
    }

    public async Task<SharedProfileImportResult> ImportAsync(string instanceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentException("Instance name cannot be null or whitespace.", nameof(instanceName));

        var mods = await GetModsAsync(cancellationToken).ConfigureAwait(false);
        if (mods.Count == 0)
            throw new InvalidOperationException("The shared profile has no mods to import.");

        var created = await _instances.CreateAsync(new Instance(instanceName, InstanceSource.Custom.Value), InstanceOrigin.GameProfileImport).ConfigureAwait(false);
        try
        {
            return await FillAsync(created.Instance.InstanceId, mods, created.Activated, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await TryDeleteInstanceAsync(created.Instance.InstanceId).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<SharedProfileImportResult> FillAsync(Guid instanceId, IReadOnlyList<SharedProfileMod> mods, bool activated, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(_pathProvider.GetInstanceRoot(instanceId), $".borea-staging-{Guid.NewGuid():N}");
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        await Task.Run(
            () =>
            {
                foreach (var mod in mods)
                    ModFolders.CopyWithoutMarker(Path.Combine(ModsFolder, mod.FolderName), Path.Combine(staging, mod.FolderName), cancellationToken);
                Directory.Move(staging, modsFolder);
            },
            cancellationToken).ConfigureAwait(false);

        var hasEntry = new Dictionary<string, bool>(ModIds.Comparer);
        foreach (var mod in mods)
        {
            hasEntry[mod.FolderName] = ModIds.IsValid(mod.FolderName)
                && await _modState.AddEntryAsync(instanceId, mod.FolderName, mod.Enabled, cancellationToken).ConfigureAwait(false) == ModEntryAddResult.Added;
        }

        await _adopter.ScanAsync(instanceId, cancellationToken).ConfigureAwait(false);

        var imported = new List<SharedProfileImportedMod>();
        foreach (var mod in mods)
        {
            InstalledMod? release = null;
            string? matchError = null;
            try
            {
                var result = await _matcher.AdoptMatchingReleaseAsync(instanceId, mod.FolderName, cancellationToken).ConfigureAwait(false);
                release = result?.InstalledMod;
            }
            catch (Exception exception) when (IsMatchFailure(exception, cancellationToken))
            {
                matchError = exception.Message;
            }

            cancellationToken.ThrowIfCancellationRequested();
            imported.Add(new SharedProfileImportedMod(mod.FolderName, mod.Enabled, hasEntry[mod.FolderName], release, matchError));
        }

        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");
        return new SharedProfileImportResult(instance, imported, activated);
    }

    private static bool IsMatchFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException or DownloadFailedException or IOException or UnauthorizedAccessException
                or InvalidOperationException or FormatException or NotSupportedException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Borea created every file of the new instance in this import, so removing
    /// it deletes nothing that came from anywhere else.
    /// </summary>
    private async Task TryDeleteInstanceAsync(Guid instanceId)
    {
        try
        {
            await _instances.DeleteAsync(instanceId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
