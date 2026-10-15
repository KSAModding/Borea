using System.ComponentModel;
using System.Reflection;
using Borea.Core.Logging;
using Borea.Core.Updates;

namespace Borea.Storage.Updates;

/// <summary>
/// <see cref="ISelfUpdater"/> for a build that runs inside a macOS app bundle. macOS signs the bundle
/// as one piece, so nothing inside it is written to. The new bundle is unpacked in a folder of its
/// own beside the installed one, the installed bundle is moved aside, the new one takes its place
/// and is started, and the new build removes the old bundle once it has shown that it runs. A step
/// that fails puts the old bundle back. One update at a time changes the folder, which
/// <see cref="SelfUpdateLock"/> holds for it.
/// </summary>
public sealed class MacOsBundleSelfUpdater : ISelfUpdater
{
    private readonly IBoreaReleaseFiles _files;
    private readonly IBoreaLog _log;
    private readonly IHandoverStarter _starter;
    private readonly MacOsBundle? _bundle;
    private readonly string? _platform;
    private readonly bool _isReleaseBuild;
    private readonly bool _fromCommandLine;

    /// <param name="programPath">The running program, inside its bundle. Null reads it from the process.</param>
    /// <param name="platform">The platform in the archive names. Null means that no release is published for this system.</param>
    /// <param name="isReleaseBuild">Whether this build is a published single file. Null reads it from the assembly.</param>
    /// <param name="fromCommandLine">Whether a command runs, so that the new build finishes without a window.</param>
    public MacOsBundleSelfUpdater(
        IBoreaReleaseFiles files,
        IBoreaLog log,
        IHandoverStarter? starter = null,
        string? programPath = null,
        string? platform = null,
        bool? isReleaseBuild = null,
        bool fromCommandLine = false)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _starter = starter ?? new HandoverStarter();
        _bundle = MacOsBundle.ForProgram(programPath ?? Environment.ProcessPath);
        _platform = platform;
        _fromCommandLine = fromCommandLine;

        // A published build is one single file, so the assemblies inside it have no file of their own.
        _isReleaseBuild = isReleaseBuild ?? string.IsNullOrEmpty(typeof(MacOsBundleSelfUpdater).Assembly.Location);
    }

    public SelfUpdateReadiness GetReadiness()
    {
        if (!_isReleaseBuild || _bundle is null)
            return new SelfUpdateReadiness(SelfUpdateBlock.NotAReleaseBuild);

        if (FileSelfUpdater.ReadPackageManager(Path.GetDirectoryName(_bundle.ProgramPath)!) is { } manager)
            return new SelfUpdateReadiness(SelfUpdateBlock.PackageManaged, manager.Length == 0 ? null : manager);

        if (_platform is null)
            return new SelfUpdateReadiness(SelfUpdateBlock.UnsupportedPlatform);

        if (!FileSelfUpdater.IsWritable(_bundle.Folder))
            return new SelfUpdateReadiness(SelfUpdateBlock.ReadOnlyLocation);

        return SelfUpdateReadiness.Ready;
    }

    public async Task<StagedSelfUpdate> StageAsync(
        BoreaRelease release,
        IProgress<SelfUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var readiness = GetReadiness();
        if (!readiness.CanUpdate)
            throw new SelfUpdateFailedException(SelfUpdateFailure.Blocked, $"Borea cannot replace itself, because {readiness.Block} stops it.");

        var bundle = _bundle!;
        var platform = _platform!;

        // Only the App archive holds a bundle, and the App runs the commands too.
        var archive = BoreaArchive.Find(release, BoreaProduct.App, platform)
            ?? throw new SelfUpdateFailedException(SelfUpdateFailure.NoArchive, $"Borea {release.Version} publishes no {BoreaProduct.App} archive for {platform}.");

        // An update that never finished leaves its unpacked build behind, and this is where it goes.
        if (SelfUpdateCleanup.SweepStaging(bundle.Folder) is { } swept)
            _log.Write(swept);

        var expected = await FileSelfUpdater.ReadChecksumAsync(_files, release, archive, cancellationToken).ConfigureAwait(false);
        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-update-{Guid.NewGuid():N}{FileSelfUpdater.ArchiveExtension(archive.Name)}");
        var staging = Path.Combine(bundle.Folder, $"{SelfUpdateCleanup.StagingPrefix}{Guid.NewGuid():N}");
        _log.Write($"Self-update: downloading {archive.Name} of Borea {release.Version}.");

        try
        {
            await FileSelfUpdater.DownloadAsync(_files, archive, archivePath, expected, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new SelfUpdateProgress(SelfUpdatePhase.Unpacking));
            var (unpacked, programHash) = Unpack(archivePath, archive.Name, expected, staging, bundle);
            cancellationToken.ThrowIfCancellationRequested();
            _log.Write($"Self-update: Borea {release.Version} is unpacked and waits in {staging}.");

            return new StagedSelfUpdate(
                release.Version,
                bundle.Folder,
                bundle.BundlePath,
                bundle.ReplacedPath,
                () => Install(staging, unpacked, bundle, programHash),
                () => HandOver(staging, bundle, programHash));
        }
        catch
        {
            FileSelfUpdater.TryDeleteFolder(staging);
            throw;
        }
        finally
        {
            FileSelfUpdater.TryDeleteFile(archivePath);
        }
    }

    /// <summary>
    /// Checks the downloaded archive once more and unpacks it into a staging folder beside the
    /// installed bundle, where the new bundle waits until <see cref="Install"/> moves it into place.
    /// </summary>
    /// <returns>The unpacked bundle, and the SHA-256 of the program in it.</returns>
    private static (string Unpacked, string ProgramHash) Unpack(string archivePath, string archiveName, string expected, string staging, MacOsBundle bundle)
    {
        var unpacked = FileSelfUpdater.UnpackChecked(archivePath, archiveName, expected, staging);

        var program = bundle.ProgramIn(unpacked);
        if (!MacOsBundle.IsBundle(unpacked) || !File.Exists(program))
            throw new SelfUpdateFailedException(SelfUpdateFailure.Unpack, $"The downloaded archive holds no app bundle with {Path.GetFileName(program)} in it.");

        FileSelfUpdater.MakeRunnable(program);
        return (unpacked, FileSelfUpdater.FileHash(program));
    }

    /// <summary>
    /// Puts the new bundle in the place of the installed one, and takes the folder for the time it
    /// needs it, so that a second update waits. The program in the new bundle is checked once more,
    /// the installed bundle is moved aside, and the new bundle is moved into its place. A step that
    /// fails moves the installed bundle back, and the old bundle waits beside the new one until
    /// <see cref="HandOver"/> knows whether the new build starts.
    /// </summary>
    private void Install(string staging, string unpacked, MacOsBundle bundle, string programHash)
    {
        var installed = false;
        try
        {
            using var claim = SelfUpdateLock.TryTake(bundle.Folder)
                ?? throw new SelfUpdateFailedException(
                    SelfUpdateFailure.Install,
                    $"Another Borea update is changing {bundle.Folder} right now, so this one stopped before it changed anything.");

            ProgramAssemblies.LoadAll(Assembly.GetEntryAssembly() ?? typeof(MacOsBundleSelfUpdater).Assembly);

            var bundleIsAside = false;
            try
            {
                var hash = FileSelfUpdater.FileHash(bundle.ProgramIn(unpacked));
                if (!string.Equals(hash, programHash, StringComparison.OrdinalIgnoreCase))
                    throw new SelfUpdateFailedException(SelfUpdateFailure.Install, "The new build changed after it was checked, so Borea kept the build that runs.");

                // an update whose new build never started left this bundle, and only this one
                if (Directory.Exists(bundle.ReplacedPath))
                    Directory.Delete(bundle.ReplacedPath, recursive: true);

                Directory.Move(bundle.BundlePath, bundle.ReplacedPath);
                bundleIsAside = true;
                Directory.Move(unpacked, bundle.BundlePath);
            }
            catch (Exception exception) when (FileSelfUpdater.IsFileFailure(exception) || exception is SelfUpdateFailedException)
            {
                if (bundleIsAside)
                    PutBack(bundle);

                if (exception is SelfUpdateFailedException)
                    throw;

                throw new SelfUpdateFailedException(
                    SelfUpdateFailure.Install,
                    $"The new build could not be put in {bundle.Folder}, so Borea kept the build that runs. {exception.Message}",
                    exception);
            }

            _log.Write($"Self-update: the new build is in {bundle.Folder} and the replaced bundle waits as {Path.GetFileName(bundle.ReplacedPath)}.");
            installed = true;
        }
        finally
        {
            if (!installed)
                FileSelfUpdater.TryDeleteFolder(staging);
        }
    }

    /// <summary>
    /// Starts the new build. The folder is open to everything this user runs, so the program in the
    /// new bundle is checked once more, as late as that can be done, and the replaced bundle is put
    /// back when the new one is not the build that was checked or does not start.
    /// </summary>
    private void HandOver(string staging, MacOsBundle bundle, string programHash)
    {
        var handover = new SelfUpdateHandover(bundle.ReplacedPath, Environment.ProcessId, SelfUpdateHandover.NewToken(), _fromCommandLine);
        try
        {
            var hash = FileSelfUpdater.FileHash(bundle.ProgramPath);
            if (!string.Equals(hash, programHash, StringComparison.OrdinalIgnoreCase))
                throw new SelfUpdateFailedException(SelfUpdateFailure.Start, $"The new build in {bundle.BundlePath} changed after it was checked, so Borea did not start it.");

            // The receipt tells the new build which bundle it may remove, so an argument alone removes nothing.
            SelfUpdateReceipt.Write(bundle.Folder, bundle.ReplacedPath, handover.Token);
            _starter.Start(bundle.ProgramPath, handover.ToArguments());
        }
        catch (SelfUpdateFailedException)
        {
            Undo(bundle);
            throw;
        }
        catch (Exception exception) when (FileSelfUpdater.IsFileFailure(exception) || exception is InvalidOperationException or Win32Exception)
        {
            Undo(bundle);
            throw new SelfUpdateFailedException(
                SelfUpdateFailure.Start,
                $"The new build in {bundle.BundlePath} did not start, so Borea put the build that ran back. {exception.Message}",
                exception);
        }
        finally
        {
            FileSelfUpdater.TryDeleteFolder(staging);
        }

        _log.Write($"Self-update: {bundle.ProgramPath} was started and takes over from process {handover.PreviousProcessId}.");
    }

    /// <summary>Takes back an update whose new build did not start, so that the build which ran runs again.</summary>
    private static void Undo(MacOsBundle bundle)
    {
        SelfUpdateReceipt.Delete(bundle.Folder);
        FileSelfUpdater.TryDeleteFolder(bundle.BundlePath);
        PutBack(bundle);
    }

    /// <summary>Gives the replaced bundle its path back, which is the whole undo of an update that did not finish.</summary>
    /// <exception cref="SelfUpdateFailedException">Borea has no bundle at its own path any more.</exception>
    private static void PutBack(MacOsBundle bundle)
    {
        try
        {
            Directory.Move(bundle.ReplacedPath, bundle.BundlePath);
        }
        catch (Exception exception) when (FileSelfUpdater.IsFileFailure(exception))
        {
            throw new SelfUpdateFailedException(
                SelfUpdateFailure.Restore,
                $"Borea could not put {bundle.ReplacedPath} back to {bundle.BundlePath}. Rename it by hand to start Borea again. {exception.Message}",
                exception);
        }
    }
}
