using System.ComponentModel;
using System.Security.Cryptography;
using Borea.Core.Logging;
using Borea.Core.Mods;
using Borea.Core.Updates;

namespace Borea.Storage.Updates;

/// <summary>
/// <see cref="ISelfUpdater"/> for a build that was unpacked from a release archive. The new build is
/// unpacked in a folder of its own inside the folder Borea runs in and then takes the place of the
/// files there, so Borea keeps the path it was started from and a shortcut to it keeps working. The
/// running program file is never written to, only moved aside, and every file an update moved is put
/// back when a step fails. One update at a time changes the folder, which <see cref="SelfUpdateLock"/>
/// holds for it.
/// </summary>
public sealed class FileSelfUpdater : ISelfUpdater
{
    /// <summary>
    /// A packaging recipe writes this file next to the program to keep Borea from replacing itself.
    /// Its first line names the package manager. docs/packaging.md describes it.
    /// </summary>
    public const string PackageMarkerFileName = "borea-package.txt";

    /// <summary>How much of the marker file is read, which is more than a manager name ever needs.</summary>
    private const int MaxPackageManagerLength = 64;

    private readonly IBoreaReleaseFiles _files;
    private readonly IBoreaLog _log;
    private readonly BoreaProduct _product;
    private readonly IHandoverStarter _starter;
    private readonly string? _programPath;
    private readonly string? _platform;
    private readonly bool _isReleaseBuild;
    private readonly bool _fromCommandLine;

    /// <param name="product">Which archive this build came from. The App archive runs the commands too.</param>
    /// <param name="programPath">The running program. Null reads it from the process.</param>
    /// <param name="platform">The platform in the archive names. Null means that no release is published for this system.</param>
    /// <param name="isReleaseBuild">Whether this build is a published single file. Null reads it from the assembly.</param>
    /// <param name="fromCommandLine">Whether a command runs, so that the new build finishes without a window.</param>
    public FileSelfUpdater(
        IBoreaReleaseFiles files,
        IBoreaLog log,
        BoreaProduct product,
        IHandoverStarter? starter = null,
        string? programPath = null,
        string? platform = null,
        bool? isReleaseBuild = null,
        bool fromCommandLine = false)
    {
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _product = product;
        _starter = starter ?? new HandoverStarter();
        _programPath = programPath ?? Environment.ProcessPath;
        _platform = platform;
        _fromCommandLine = fromCommandLine;

        // A published build is one single file, so the assemblies inside it have no file of their own.
        _isReleaseBuild = isReleaseBuild ?? string.IsNullOrEmpty(typeof(FileSelfUpdater).Assembly.Location);
    }

    public SelfUpdateReadiness GetReadiness()
    {
        var folder = Folder();
        if (!_isReleaseBuild || folder is null)
            return new SelfUpdateReadiness(SelfUpdateBlock.NotAReleaseBuild);

        if (ReadPackageManager(folder) is { } manager)
            return new SelfUpdateReadiness(SelfUpdateBlock.PackageManaged, manager.Length == 0 ? null : manager);

        if (_platform is null)
            return new SelfUpdateReadiness(SelfUpdateBlock.UnsupportedPlatform);

        if (!IsWritable(folder))
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

        var folder = Folder()!;
        var programName = Path.GetFileName(_programPath!);
        var platform = _platform!;
        var archive = BoreaArchive.Find(release, _product, platform)
            ?? throw new SelfUpdateFailedException(SelfUpdateFailure.NoArchive, $"Borea {release.Version} publishes no {_product} archive for {platform}.");

        // An update that never finished leaves its unpacked build behind, and this is where it goes.
        if (SelfUpdateCleanup.SweepStaging(folder) is { } swept)
            _log.Write(swept);

        var expected = await ReadChecksumAsync(release, archive, cancellationToken).ConfigureAwait(false);
        var archivePath = Path.Combine(Path.GetTempPath(), $"borea-update-{Guid.NewGuid():N}{ArchiveExtension(archive.Name)}");
        var staging = Path.Combine(folder, $"{SelfUpdateCleanup.StagingPrefix}{Guid.NewGuid():N}");
        _log.Write($"Self-update: downloading {archive.Name} of Borea {release.Version}.");

        try
        {
            await DownloadAsync(archive, archivePath, expected, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new SelfUpdateProgress(SelfUpdatePhase.Unpacking));
            var (unpacked, programHash) = Unpack(archivePath, archive.Name, expected, staging, programName);
            cancellationToken.ThrowIfCancellationRequested();
            _log.Write($"Self-update: Borea {release.Version} is unpacked and waits in {staging}.");

            var programPath = Path.Combine(folder, programName);
            var replaced = SelfUpdateCleanup.ReplacedPath(programPath);
            return new StagedSelfUpdate(
                release.Version,
                folder,
                programPath,
                replaced,
                () => Install(staging, unpacked, programPath, replaced, programHash),
                () => HandOver(programPath, replaced, programHash));
        }
        catch
        {
            TryDeleteFolder(staging);
            throw;
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    private async Task<string> ReadChecksumAsync(BoreaRelease release, BoreaReleaseAsset archive, CancellationToken cancellationToken)
    {
        var checksums = BoreaArchive.FindChecksums(release)
            ?? throw new SelfUpdateFailedException(
                SelfUpdateFailure.Checksum,
                $"Borea {release.Version} publishes no {BoreaArchive.ChecksumsFileName}, so the download cannot be checked.");

        // A listing that did not arrive is a failed download, and not an archive that does not match.
        var listing = await _files.ReadTextAsync(checksums.Url, cancellationToken).ConfigureAwait(false)
            ?? throw new SelfUpdateFailedException(
                SelfUpdateFailure.Download,
                $"The {BoreaArchive.ChecksumsFileName} of Borea {release.Version} could not be downloaded, so the archive cannot be checked.");

        return Sha256Sums.Find(listing, archive.Name)
            ?? throw new SelfUpdateFailedException(
                SelfUpdateFailure.Checksum,
                $"The {BoreaArchive.ChecksumsFileName} of Borea {release.Version} does not record {archive.Name}, so the download cannot be checked.");
    }

    private async Task DownloadAsync(
        BoreaReleaseAsset archive,
        string archivePath,
        string expected,
        IProgress<SelfUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        string hash;
        try
        {
            hash = await _files.DownloadAsync(archive.Url, archivePath, new DownloadReport(progress), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            throw new SelfUpdateFailedException(SelfUpdateFailure.Download, $"{archive.Name} could not be downloaded. {exception.Message}", exception);
        }

        progress?.Report(new SelfUpdateProgress(SelfUpdatePhase.Verifying));
        if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            throw new SelfUpdateFailedException(SelfUpdateFailure.Checksum, $"{archive.Name} arrived with SHA-256 {hash} where the release records {expected}.");
    }

    /// <summary>
    /// Checks the downloaded archive once more and unpacks it into a staging folder, where it waits
    /// until <see cref="Install"/> moves it into the folder Borea runs in.
    /// </summary>
    /// <returns>The folder the archive holds, and the SHA-256 of the program file in it.</returns>
    private static (string Unpacked, string ProgramHash) Unpack(
        string archivePath,
        string archiveName,
        string expected,
        string staging,
        string programName)
    {
        string unpacked;
        try
        {
            using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.None);

            // One open file is checked and unpacked, so nothing can put other bytes between the two steps.
            var hash = Convert.ToHexString(SHA256.HashData(file));
            if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                throw new SelfUpdateFailedException(SelfUpdateFailure.Checksum, $"{archiveName} holds SHA-256 {hash} where the release records {expected}.");

            file.Position = 0;
            CreateStaging(staging);
            unpacked = ReleaseArchive.Unpack(file, archiveName, staging);
        }
        catch (Exception exception) when (exception is not SelfUpdateFailedException
            && exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new SelfUpdateFailedException(SelfUpdateFailure.Unpack, $"The downloaded archive could not be unpacked. {exception.Message}", exception);
        }

        var program = Path.Combine(unpacked, programName);
        if (!File.Exists(program))
            throw new SelfUpdateFailedException(SelfUpdateFailure.Unpack, $"The downloaded archive holds no {programName}.");

        // Only files take the place of the files in the install folder, so a build of folders is refused.
        if (Directory.GetDirectories(unpacked).Length != 0)
            throw new SelfUpdateFailedException(SelfUpdateFailure.Unpack, $"The archive '{archiveName}' holds more than the files of a Borea build.");

        MakeRunnable(program);
        return (unpacked, FileHash(program));
    }

    /// <summary>
    /// Puts the unpacked build in the folder Borea runs in, and takes that folder for the time it
    /// needs it, so that a second update waits. The program file is checked once more, the files
    /// beside it go first and keep their old copy in the staging folder, and the running program is
    /// moved aside last. A step that fails puts every file of the build that runs back.
    /// </summary>
    private void Install(string staging, string unpacked, string programPath, string replaced, string programHash)
    {
        var newProgram = Path.Combine(unpacked, Path.GetFileName(programPath));
        var folder = Path.GetDirectoryName(programPath)!;
        try
        {
            using var claim = SelfUpdateLock.TryTake(folder)
                ?? throw new SelfUpdateFailedException(
                    SelfUpdateFailure.Install,
                    $"Another Borea update is changing {folder} right now, so this one stopped before it changed anything.");

            // where the files of the build that runs wait until the new build is in place
            var aside = Path.Combine(staging, "replaced");
            var moved = new List<string>();
            var programIsAside = false;
            try
            {
                var hash = FileHash(newProgram);
                if (!string.Equals(hash, programHash, StringComparison.OrdinalIgnoreCase))
                    throw new SelfUpdateFailedException(SelfUpdateFailure.Install, "The new build changed after it was checked, so Borea kept the build that runs.");

                Directory.CreateDirectory(aside);
                foreach (var file in OtherFiles(unpacked, newProgram))
                {
                    var name = Path.GetFileName(file);
                    var target = Path.Combine(folder, name);
                    moved.Add(name);
                    if (File.Exists(target))
                        File.Move(target, Path.Combine(aside, name));

                    File.Move(file, target);
                }

                // an update whose new build never started left this file, and only this one
                File.Delete(replaced);

                File.Move(programPath, replaced);
                programIsAside = true;
                File.Move(newProgram, programPath, overwrite: true);
            }
            catch (Exception exception) when (IsFileFailure(exception) || exception is SelfUpdateFailedException)
            {
                PutFilesBack(folder, aside, moved);
                if (programIsAside)
                    PutBack(replaced, programPath);

                if (exception is SelfUpdateFailedException)
                    throw;

                throw new SelfUpdateFailedException(
                    SelfUpdateFailure.Install,
                    $"The new build could not be put in {folder}, so Borea kept the build that runs. {exception.Message}",
                    exception);
            }

            MakeRunnable(programPath);
            _log.Write($"Self-update: the new build is in {folder} and the replaced program waits as {Path.GetFileName(replaced)}.");
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    /// <summary>The files of the new build beside the program, in one order on every system.</summary>
    private static IEnumerable<string> OtherFiles(string unpacked, string newProgram)
        => Directory.GetFiles(unpacked)
            .Where(file => !string.Equals(file, newProgram, SelfUpdateCleanup.PathComparison))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// Gives the files beside the program back to the build that runs, which is what an update that
    /// stopped owes it. Every step tries, because one file that will not move back must not keep the
    /// others from coming back.
    /// </summary>
    private static void PutFilesBack(string folder, string aside, IEnumerable<string> moved)
    {
        foreach (var name in moved)
        {
            try
            {
                var old = Path.Combine(aside, name);
                var target = Path.Combine(folder, name);
                if (File.Exists(old))
                    File.Move(old, target, overwrite: true);
                else
                    File.Delete(target);
            }
            catch (Exception exception) when (IsFileFailure(exception))
            {
            }
        }
    }

    /// <summary>
    /// Starts the new build. The folder is open to everything this user runs, so the program file is
    /// checked once more, as late as that can be done, and the replaced build is put back when the
    /// new one is not the build that was checked or does not start.
    /// </summary>
    private void HandOver(string programPath, string replaced, string programHash)
    {
        var handover = new SelfUpdateHandover(replaced, Environment.ProcessId, SelfUpdateHandover.NewToken(), _fromCommandLine);
        try
        {
            var hash = FileHash(programPath);
            if (!string.Equals(hash, programHash, StringComparison.OrdinalIgnoreCase))
                throw new SelfUpdateFailedException(SelfUpdateFailure.Start, $"The new build in {Path.GetDirectoryName(programPath)} changed after it was checked, so Borea did not start it.");

            // The receipt tells the new build which file it may remove, so an argument alone removes nothing.
            SelfUpdateReceipt.Write(Path.GetDirectoryName(programPath)!, replaced, handover.Token);
            _starter.Start(programPath, handover.ToArguments());
        }
        catch (SelfUpdateFailedException)
        {
            Undo(programPath, replaced);
            throw;
        }
        catch (Exception exception) when (IsFileFailure(exception) || exception is InvalidOperationException or Win32Exception)
        {
            Undo(programPath, replaced);
            throw new SelfUpdateFailedException(
                SelfUpdateFailure.Start,
                $"The new build in {Path.GetDirectoryName(programPath)} did not start, so Borea put the build that ran back. {exception.Message}",
                exception);
        }

        _log.Write($"Self-update: {programPath} was started and takes over from process {handover.PreviousProcessId}.");
    }

    /// <summary>Takes back an update whose new build did not start, so that the build which ran runs again.</summary>
    private static void Undo(string programPath, string replaced)
    {
        SelfUpdateReceipt.Delete(Path.GetDirectoryName(programPath)!);
        PutBack(replaced, programPath);
    }

    /// <summary>Gives the program file its path back, which is the whole undo of an update that did not finish.</summary>
    /// <exception cref="SelfUpdateFailedException">Borea has no program file at its own path any more.</exception>
    private static void PutBack(string replaced, string programPath)
    {
        try
        {
            File.Move(replaced, programPath, overwrite: true);
        }
        catch (Exception exception) when (IsFileFailure(exception))
        {
            throw new SelfUpdateFailedException(
                SelfUpdateFailure.Restore,
                $"Borea could not put {replaced} back to {programPath}. Rename it by hand to start Borea again. {exception.Message}",
                exception);
        }
    }

    /// <summary>The SHA-256 of one file, read through a handle that nobody else may write to.</summary>
    private static string FileHash(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    /// <summary>The folder the program runs in, or null when the program has no folder of its own.</summary>
    private string? Folder()
    {
        var program = _programPath is null ? null : Path.GetFullPath(_programPath);
        return program is null ? null : Path.GetDirectoryName(program);
    }

    /// <summary>The package manager the marker names, an empty string when it names none, or null when there is no marker.</summary>
    private static string? ReadPackageManager(string folder)
    {
        var marker = Path.Combine(folder, PackageMarkerFileName);
        if (!File.Exists(marker))
            return null;

        try
        {
            using var reader = new StreamReader(marker);
            var name = reader.ReadLine()?.Trim() ?? string.Empty;
            return name.Length > MaxPackageManagerLength ? name[..MaxPackageManagerLength] : name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a marker that cannot be read still says that somebody else owns this build
            return string.Empty;
        }
    }

    private static bool IsWritable(string folder)
    {
        var probe = Path.Combine(folder, $".borea-write-test-{Guid.NewGuid():N}");
        try
        {
            using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsFileFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException;

    /// <summary>The ".tar.gz" of a tar archive, which Path.GetExtension shortens to ".gz".</summary>
    private static string ArchiveExtension(string assetName)
        => assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(assetName);

    /// <summary>A zip archive carries no file mode, and a tar archive is unpacked with its own, so this only makes sure.</summary>
    private static void MakeRunnable(string programPath)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(programPath);
            File.SetUnixFileMode(programPath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    /// <summary>Passes the download on as it arrives, so the caller's own progress decides which thread it lands on.</summary>
    private sealed class DownloadReport(IProgress<SelfUpdateProgress>? progress) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value)
            => progress?.Report(new SelfUpdateProgress(SelfUpdatePhase.Downloading, value.BytesDownloaded, value.TotalBytes));
    }

    /// <summary>
    /// Makes the folder the new build is unpacked into. It is hidden on Windows, which reads the
    /// attribute and not the leading dot of the name, so a player does not meet it in the folder
    /// their shortcut points at.
    /// </summary>
    private static void CreateStaging(string staging)
    {
        var folder = Directory.CreateDirectory(staging);
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            folder.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
