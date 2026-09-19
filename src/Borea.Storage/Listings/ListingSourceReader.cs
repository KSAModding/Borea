using System.Globalization;
using System.Text.RegularExpressions;
using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Storage.Listings;

/// <summary>Reads the release host, then downloads the latest archive into the temporary folder, reads it, and deletes it.</summary>
public sealed partial class ListingSourceReader : IListingSourceReader
{
    private readonly IListingHostClient _hosts;
    private readonly IModDownloader _downloader;
    private readonly string _temporaryFolder;
    private readonly long _maxArchiveBytes;

    public ListingSourceReader(IListingHostClient hosts, IModDownloader downloader)
        : this(hosts, downloader, Path.GetTempPath(), IListingSourceReader.MaxArchiveBytes)
    {
    }

    internal ListingSourceReader(IListingHostClient hosts, IModDownloader downloader, string temporaryFolder, long maxArchiveBytes)
    {
        _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _temporaryFolder = temporaryFolder;
        _maxArchiveBytes = maxArchiveBytes;
    }

    public async Task<ListingSource> ReadAsync(ListingSourceReference source, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var host = await _hosts.ReadAsync(source, cancellationToken).ConfigureAwait(false);
        if (host.Latest is not { } latest)
            return new ListingSource(host, null, null);

        if (latest.DownloadUrl is null)
        {
            var problem = latest.Candidates.Count == 0
                ? $"the release {latest.Tag} carries no zip archive"
                : $"the release {latest.Tag} carries {latest.Candidates.Count} archives and none of them is named after the listing ({string.Join(", ", latest.Candidates)})";
            return new ListingSource(host, null, problem);
        }

        if (latest.SizeBytes > _maxArchiveBytes)
            return new ListingSource(host, null, TooLarge(latest.SizeBytes.Value));

        var archivePath = Path.Combine(_temporaryFolder, $"borea-listing-{Guid.NewGuid():N}.zip");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = new SizeLimit(progress, _maxArchiveBytes, stop);
        try
        {
            await _downloader.DownloadAsync(Release(host, latest), archivePath, limit, stop.Token).ConfigureAwait(false);
            return limit.Exceeded is { } received
                ? new ListingSource(host, null, TooLarge(received))
                : new ListingSource(host, ListingArchive.Read(archivePath), null);
        }
        catch (OperationCanceledException) when (limit.Exceeded is { } received && !cancellationToken.IsCancellationRequested)
        {
            return new ListingSource(host, null, TooLarge(received));
        }
        catch (Exception exception) when (exception is DownloadFailedException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ListingSource(host, null, exception.Message);
        }
        finally
        {
            TryDeleteFile(archivePath);
        }
    }

    private string TooLarge(long bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"the release archive is {bytes} bytes, above the limit of {_maxArchiveBytes} bytes");

    private static ModVersionMetadata Release(ListingHostFacts host, ListingHostRelease latest)
    {
        var name = (host.Source as ListingSourceReference.GitHub)?.Repository ?? host.Name ?? string.Empty;
        var id = NotId().Replace(name, "-");
        id = id[..Math.Min(id.Length, 64)].Trim('-', '.', '_');
        if (!ModIds.IsValid(id))
            id = "listing";
        return new ModVersionMetadata(
            specVersion: SpecVersions.Highest,
            modId: id,
            version: ModVersion.Parse(latest.Version),
            releaseStatus: ReleaseStatus.Stable,
            releaseDate: DateTimeOffset.UnixEpoch,
            gameMin: "unknown",
            gameMinRevision: 0,
            download: new DownloadInfo(latest.DownloadUrl!, sha256: null, latest.SizeBytes, "application/zip"),
            installSizeBytes: null,
            dependencies: []);
    }

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

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex NotId();

    /// <summary>Passes the progress on, and stops the download once it is larger than the checks accept.</summary>
    private sealed class SizeLimit(IProgress<DownloadProgress>? inner, long maxBytes, CancellationTokenSource stop) : IProgress<DownloadProgress>
    {
        public long? Exceeded { get; private set; }

        public void Report(DownloadProgress value)
        {
            inner?.Report(value);
            if (value.BytesDownloaded <= maxBytes || Exceeded is not null)
                return;

            Exceeded = value.BytesDownloaded;
            stop.Cancel();
        }
    }
}
