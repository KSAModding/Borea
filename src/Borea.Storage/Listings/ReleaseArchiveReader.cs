using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Core.Stewardship;

namespace Borea.Storage.Listings;

/// <summary>
/// Downloads the archive of a stamped release into the temporary folder, reads what its mod.toml declares, and deletes it again.
/// The bytes of a sha256 never change, so each answer is kept for the next question about the same archive.
/// </summary>
public sealed class ReleaseArchiveReader : IReleaseArchiveReader
{
    private readonly IModDownloader _downloader;
    private readonly string _temporaryFolder;
    private readonly long _maxArchiveBytes;
    private readonly ConcurrentDictionary<string, IReadOnlyList<LocalModDependency>> _read = new(StringComparer.Ordinal);

    public ReleaseArchiveReader(IModDownloader downloader)
        : this(downloader, Path.GetTempPath(), IListingSourceReader.MaxArchiveBytes)
    {
    }

    internal ReleaseArchiveReader(IModDownloader downloader, string temporaryFolder, long maxArchiveBytes)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _temporaryFolder = temporaryFolder;
        _maxArchiveBytes = maxArchiveBytes;
    }

    public async Task<IReadOnlyList<LocalModDependency>> DeclaredDependenciesAsync(string releaseFileText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(releaseFileText);
        var (release, isMod, root) = Release(releaseFileText);
        var sha256 = release.Download.Sha256!;
        var key = $"{sha256}\n{isMod}\n{root}";
        if (_read.TryGetValue(key, out var known))
            return known;

        var size = release.Download.SizeBytes ?? _maxArchiveBytes;
        if (size > _maxArchiveBytes)
            throw new ReleaseArchiveException(string.Create(CultureInfo.InvariantCulture, $"the archive is {size} bytes, above the limit of {_maxArchiveBytes} bytes"));

        var archivePath = Path.Combine(_temporaryFolder, $"borea-release-{Guid.NewGuid():N}.zip");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = new SizeLimit(size, stop);
        try
        {
            var result = await _downloader.DownloadAsync(release, archivePath, limit, stop.Token).ConfigureAwait(false);
            if (limit.Exceeded)
                throw new ReleaseArchiveException(string.Create(CultureInfo.InvariantCulture, $"the archive sends more than the {size} bytes that the release states"));
            if (!release.Download.HashMatches(result.Sha256))
                throw new ReleaseArchiveException($"the archive at {result.Url} no longer matches the stamped sha256");

            IReadOnlyList<LocalModDependency> declared = isMod ? ListingArchive.DeclaredDependencies(archivePath, root) : [];
            _read[key] = declared;
            return declared;
        }
        catch (OperationCanceledException exception) when (limit.Exceeded && !cancellationToken.IsCancellationRequested)
        {
            throw new ReleaseArchiveException(string.Create(CultureInfo.InvariantCulture, $"the archive sends more than the {size} bytes that the release states"), exception);
        }
        catch (Exception exception) when (exception is DownloadFailedException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new ReleaseArchiveException(exception.Message, exception);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    /// <summary>The release as the downloader takes it, whether it is a mod, and its install root.</summary>
    /// <exception cref="ReleaseArchiveException">The file names no download URL or no sha256.</exception>
    private static (ModVersionMetadata Release, bool IsMod, string Root) Release(string text)
    {
        JsonObject document;
        try
        {
            document = JsonNode.Parse(text) as JsonObject ?? throw new ReleaseArchiveException("the release file is not a JSON object");
        }
        catch (JsonException exception)
        {
            throw new ReleaseArchiveException($"the release file is not readable JSON: {exception.Message}", exception);
        }

        var download = document["download"] as JsonObject;
        var url = Text(download?["url"]);
        var sha256 = Text(download?["sha256"]);
        if (string.IsNullOrWhiteSpace(url))
            throw new ReleaseArchiveException("the release file names no download URL");
        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
            throw new ReleaseArchiveException("the release file names no sha256, so no archive matches it");

        var size = download!["size"] is JsonValue value && value.TryGetValue<long>(out var bytes) && bytes >= 0 ? bytes : (long?)null;
        var mirrors = (download["mirrors"] as JsonArray ?? []).Select(Text).OfType<string>().Where(mirror => mirror.Length > 0).ToList();
        var contentType = Text(download["content_type"]) is { Length: > 0 } type ? type : "application/zip";
        var id = Text(document["id"]) is { } named && ModIds.IsValid(named) ? named : "release";
        var version = ModVersion.TryParse(Text(document["version"]) ?? string.Empty, out var parsed) ? parsed : ModVersion.Parse("0.0.0");
        var release = new ModVersionMetadata(
            specVersion: SpecVersions.Highest,
            modId: id,
            version: version,
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UnixEpoch,
            gameMin: "unknown",
            gameMinRevision: 0,
            download: new DownloadInfo(url, sha256, size, contentType, mirrors),
            installSizeBytes: null,
            dependencies: []);
        return (release, Text(document["type"]) == "mod", Text((document["install"] as JsonObject)?["root"]) ?? string.Empty);
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

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

    /// <summary>Stops the download once it sends more bytes than the release states.</summary>
    private sealed class SizeLimit(long maxBytes, CancellationTokenSource stop) : IProgress<DownloadProgress>
    {
        public bool Exceeded { get; private set; }

        public void Report(DownloadProgress value)
        {
            if (value.BytesDownloaded <= maxBytes || Exceeded)
                return;

            Exceeded = true;
            stop.Cancel();
        }
    }
}
