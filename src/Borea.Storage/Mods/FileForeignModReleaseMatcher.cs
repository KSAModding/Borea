using System.Security.Cryptography;
using Borea.Core.Index;
using Borea.Core.Mods;
using Borea.Core.Paths;

namespace Borea.Storage.Mods;

public sealed class FileForeignModReleaseMatcher : IForeignModReleaseMatcher
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IModDownloader _downloader;
    private readonly IForeignModAdopter _adopter;
    private readonly IContentIndexSnapshotProvider _snapshots;

    public FileForeignModReleaseMatcher(
        IGamePathProvider pathProvider,
        IModDownloader downloader,
        IForeignModAdopter adopter,
        IContentIndexSnapshotProvider snapshots)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _adopter = adopter ?? throw new ArgumentNullException(nameof(adopter));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
    }

    public async Task<ForeignModAdoptionResult?> AdoptMatchingReleaseAsync(
        Guid instanceId,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        _ = new ForeignMod(folderName);

        var folder = ModFolders.Find(_pathProvider.GetInstanceModsFolder(instanceId), folderName)
            ?? throw new InvalidOperationException($"The instance has no foreign mod folder '{folderName}'.");

        var snapshot = await _snapshots.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var releases = snapshot.Listings
            .Where(listing => ModIds.Equals(listing.Id, folderName) && listing.IndexStatus?.State != IndexStatusState.Delisted)
            .SelectMany(listing => listing.Releases)
            .Where(release => release.Type == ContentType.Mod)
            .OrderByDescending(release => release.Version)
            .ToList();

        foreach (var release in releases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsInstallable(release))
                continue;

            var archivePath = Path.Combine(Path.GetTempPath(), $"borea-download-{Guid.NewGuid():N}.zip");
            var unpackedFolder = Path.Combine(Path.GetTempPath(), $"borea-match-{Guid.NewGuid():N}");
            try
            {
                await _downloader.DownloadAsync(release, archivePath, cancellationToken: cancellationToken).ConfigureAwait(false);
                try
                {
                    FileModInstaller.Unpack(archivePath, release, unpackedFolder);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (await HoldsFilesAsync(folder, unpackedFolder, cancellationToken).ConfigureAwait(false))
                    return await _adopter.AdoptArchiveAsync(instanceId, folderName, archivePath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(archivePath, unpackedFolder);
            }
        }

        return null;
    }

    private static bool IsInstallable(ModVersionMetadata release)
    {
        try
        {
            FileModInstaller.RequireInstallable(release);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static async Task<bool> HoldsFilesAsync(string folder, string expected, CancellationToken cancellationToken)
    {
        foreach (var expectedFile in Directory.EnumerateFiles(expected, "*", SearchOption.AllDirectories))
        {
            var actualFile = Path.Combine(folder, Path.GetRelativePath(expected, expectedFile));
            if (!File.Exists(actualFile) || new FileInfo(actualFile).Length != new FileInfo(expectedFile).Length)
                return false;

            var actualHash = await HashAsync(actualFile, cancellationToken).ConfigureAwait(false);
            var expectedHash = await HashAsync(expectedFile, cancellationToken).ConfigureAwait(false);
            if (!actualHash.AsSpan().SequenceEqual(expectedHash))
                return false;
        }

        return true;
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static void TryDelete(string archivePath, string unpackedFolder)
    {
        try
        {
            if (File.Exists(archivePath))
                File.Delete(archivePath);
            if (Directory.Exists(unpackedFolder))
                Directory.Delete(unpackedFolder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
