using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.State;

namespace Borea.Storage.Mods;

/// <summary>
/// File-backed <see cref="IModInstaller"/>. The archive goes to a temporary
/// file first, so nothing reaches the instance until the bytes are verified,
/// and a failed install removes the folder and the record it created.
/// </summary>
public sealed class FileModInstaller : IModInstaller
{
    private static readonly string[] ZipContentTypes = { "application/zip", "application/x-zip-compressed" };

    private readonly IGamePathProvider _pathProvider;
    private readonly IModDownloader _downloader;
    private readonly IInstanceRepository _instances;
    private readonly IModStateRepository _modState;
    private readonly TimeProvider _timeProvider;

    public FileModInstaller(
        IGamePathProvider pathProvider,
        IModDownloader downloader,
        IInstanceRepository instances,
        IModStateRepository modState,
        TimeProvider? timeProvider = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        _modState = modState ?? throw new ArgumentNullException(nameof(modState));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<InstallResult> InstallAsync(
        Guid instanceId,
        ModVersionMetadata release,
        InstallReason reason,
        bool enable,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        RequireInstallable(release);

        var instance = await _instances.GetByIdAsync(instanceId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No instance with ID '{instanceId}' exists.");

        if (instance.Mods.Any(m => ModIds.Equals(m.ModId, release.ModId)))
            throw new InvalidOperationException($"Mod '{release.ModId}' is already installed in this instance.");

        // Adopting a folder Borea did not install is not supported, so its
        // presence stops the install.
        var modsFolder = _pathProvider.GetInstanceModsFolder(instanceId);
        var foreignFolder = ModFolders.Find(modsFolder, release.ModId);
        if (foreignFolder is not null)
        {
            throw new InvalidOperationException(
                $"The instance already holds a folder '{Path.GetFileName(foreignFolder)}' that Borea did not install.");
        }

        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
        var modFolder = Path.Combine(modsFolder, release.ModId);
        var recorded = false;

        try
        {
            var download = await _downloader.DownloadAsync(release, archivePath, progress, cancellationToken).ConfigureAwait(false);

            Unpack(archivePath, release, modFolder);

            var installed = new InstalledMod(release.ModId, release.Version, reason, _timeProvider.GetUtcNow(), release, download.Sha256);
            instance.AddMod(installed);
            await _instances.SaveAsync(instance).ConfigureAwait(false);
            recorded = true;

            var entry = await _modState.AddEntryAsync(instanceId, release.ModId, enable, cancellationToken).ConfigureAwait(false);

            return new InstallResult(installed, download, entry);
        }
        catch
        {
            TryDeleteDirectory(modFolder);

            if (recorded)
                await TryForgetAsync(instance, release.ModId).ConfigureAwait(false);

            throw;
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>
    /// Only a mod archive into the mods folder. A loader installs by its own
    /// mechanism, and a target or path the type default does not name would put
    /// the folder where the game does not look, since ModLibrary.AddMods scans
    /// the top level of the mods folder and nothing below it.
    /// </summary>
    private static void RequireInstallable(ModVersionMetadata release)
    {
        if (release.Type != ContentType.Mod)
            throw new NotSupportedException($"'{release.ModId}' is a {release.Type}, and only a mod installs into the mods folder.");

        if (release.Install is { } install)
        {
            if (install.Target is not (null or InstallAnchor.Mods))
                throw new NotSupportedException($"'{release.ModId}' installs to '{install.Target}', and a mod can only go into the mods folder.");

            if (install.Path is not null)
                throw new NotSupportedException($"'{release.ModId}' installs below '{install.Path}', where the game does not look for a mod.");
        }

        if (!ZipContentTypes.Contains(release.Download.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException($"'{release.ModId}' is served as '{release.Download.ContentType}', and only a zip archive can be unpacked.");
    }

    /// <summary>
    /// The folder is named by the id whatever the archive calls its directory,
    /// because the game assigns the id from the folder name (Mod.MakeUsing),
    /// and it needs a mod.toml at its root to be seen as a mod at all
    /// (ModLibrary.AddMods). The stated root decides where the content starts;
    /// a release that states none gets the root RFC 0035 rule 9 derives.
    /// </summary>
    private static void Unpack(string archivePath, ModVersionMetadata release, string modFolder)
    {
        var root = release.Install?.Root ?? ModArchive.DeriveRoot(archivePath);
        var files = ModArchive.Extract(archivePath, root, modFolder);

        if (files == 0)
        {
            throw new InvalidOperationException(root is null
                ? $"The archive of '{release.ModId}' {release.Version} holds no files."
                : $"The archive of '{release.ModId}' {release.Version} holds nothing under '{root}'.");
        }

        if (!File.Exists(Path.Combine(modFolder, ModFolders.DefinitionFileName)))
        {
            throw new InvalidOperationException(
                $"The archive of '{release.ModId}' {release.Version} has no {ModFolders.DefinitionFileName} at its install root, so the game would not see a mod.");
        }
    }

    /// <summary>
    /// Takes the record back off the instance after a later step failed. A
    /// failure here is swallowed so the error that caused the rollback is the
    /// one the caller sees.
    /// </summary>
    private async Task TryForgetAsync(Instance instance, string modId)
    {
        instance.RemoveMod(modId);
        try
        {
            await _instances.SaveAsync(instance).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Cleanup that must not replace the outcome: a folder the rollback cannot
    /// remove stays behind, and the error that caused the rollback still wins.
    /// </summary>
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Same for the temporary archive, which a scanner may still hold open
    /// after a finished install.
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
