using System.Net;
using Borea.Core.Game;

namespace Borea.Network.GitHub;

/// <summary>
/// IGamePatchNotesFetcher against the KSAModding/ksa-versions repository, which uploads the Content/Versions files of every public build.
/// A file is taken from the cache when it parses there, and a downloaded file is cached, because a published file never changes.
/// </summary>
public sealed class GamePatchNotesFetcher : IGamePatchNotesFetcher
{
    internal const string VersionsUrl = "https://raw.githubusercontent.com/KSAModding/ksa-versions/main/Content/Versions/";

    private const int ChunkSize = 16 * 1024;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly IGamePatchNotesCache _cache;
    private readonly TimeSpan _timeout;

    public GamePatchNotesFetcher(HttpClient httpClient, IGamePatchNotesCache cache)
        : this(httpClient, cache, DefaultTimeout)
    {
    }

    internal GamePatchNotesFetcher(HttpClient httpClient, IGamePatchNotesCache cache, TimeSpan timeout)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeout = timeout;
    }

    public async Task<GamePatchNotesFetch> FetchAsync(IReadOnlyList<GameVersion> builds, int installedRevision, int maxFiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builds);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFiles);

        var pending = new Dictionary<int, Candidate>();
        foreach (var build in builds.Where(build => build.Revision > installedRevision))
            pending.TryAdd(build.Revision, new Candidate(build.Revision, build.Year, build.Month, FromChain: false));

        var visited = new HashSet<int>();
        var notes = new List<GamePatchNotes>();
        var complete = true;
        var capped = false;
        var reachable = true;
        var looked = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = pending[pending.Keys.Max()];
            pending.Remove(candidate.Revision);
            visited.Add(candidate.Revision);
            if (notes.Any(entry => entry.FromRevision > 0 && entry.FromRevision < candidate.Revision && candidate.Revision < entry.Revision))
                continue;

            if (looked == maxFiles)
            {
                capped = true;
                break;
            }

            looked++;
            var (outcome, loaded) = await LoadAsync(candidate, reachable, cancellationToken).ConfigureAwait(false);
            if (outcome == Outcome.Unreachable)
                reachable = false;

            if (outcome is not (Outcome.Loaded or Outcome.Broken))
                complete = false;

            if (loaded is null)
                continue;

            notes.Add(loaded);
            var from = loaded.FromRevision;
            if (from > installedRevision && from < candidate.Revision && !visited.Contains(from) && !pending.ContainsKey(from))
            {
                var (year, month) = GameVersion.TryParse(loaded.Build, out var build) ? (build.Year, build.Month) : (candidate.Year, candidate.Month);
                pending[from] = new Candidate(from, year, month, FromChain: true);
            }
        }

        return new GamePatchNotesFetch(notes.OrderByDescending(entry => entry.Revision).ToList(), complete, capped);
    }

    /// <summary>A build from the chain of a loaded file has no known month, so the month before its successor's is tried as well.</summary>
    private static IReadOnlyList<string> FileNamesOf(Candidate candidate)
    {
        var sameMonth = GamePatchNotesFile.NameOf(candidate.Year, candidate.Month, candidate.Revision);
        if (!candidate.FromChain)
            return [sameMonth];

        return candidate.Month == 1
            ? [sameMonth, GamePatchNotesFile.NameOf(candidate.Year - 1, 12, candidate.Revision)]
            : [sameMonth, GamePatchNotesFile.NameOf(candidate.Year, candidate.Month - 1, candidate.Revision)];
    }

    private async Task<(Outcome Outcome, GamePatchNotes? Notes)> LoadAsync(Candidate candidate, bool reachable, CancellationToken cancellationToken)
    {
        var fileNames = FileNamesOf(candidate);
        foreach (var fileName in fileNames)
        {
            if (await _cache.ReadAsync(fileName, cancellationToken).ConfigureAwait(false) is { } cached
                && GamePatchNotesFile.Parse(cached) is { } entry)
            {
                return (Outcome.Loaded, entry);
            }
        }

        if (!reachable)
            return (Outcome.Missing, null);

        foreach (var fileName in fileNames)
        {
            var download = await DownloadAsync(fileName, cancellationToken).ConfigureAwait(false);
            if (download.Outcome == Outcome.Missing)
                continue;

            if (download.Bytes is not { } bytes)
                return (download.Outcome, null);

            if (GamePatchNotesFile.Parse(bytes) is not { } fetched)
                return (Outcome.Broken, null);

            await TryWriteCacheAsync(fileName, bytes).ConfigureAwait(false);
            return (Outcome.Loaded, fetched);
        }

        return (Outcome.Missing, null);
    }

    /// <summary>A missing or too large file leaves the host usable for the next file; a timeout, a failed connection or a server error does not.</summary>
    private async Task<Download> DownloadAsync(string fileName, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, VersionsUrl + fileName);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return new Download(Outcome.Missing, null);

            if (response.StatusCode != HttpStatusCode.OK)
                return new Download(Outcome.Unreachable, null);

            if (response.Content.Headers.ContentLength > GamePatchNotesFile.MaxDownloadBytes)
                return new Download(Outcome.TooLarge, null);

            return await ReadBoundedAsync(response, deadline.Token).ConfigureAwait(false) is { } bytes
                ? new Download(Outcome.Loaded, bytes)
                : new Download(Outcome.TooLarge, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Download(Outcome.Unreachable, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            return new Download(Outcome.Unreachable, null);
        }
    }

    /// <summary>Null when the body is larger than <see cref="GamePatchNotesFile.MaxDownloadBytes"/>.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var received = new MemoryStream();
        var chunk = new byte[ChunkSize];
        int read;
        while ((read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, GamePatchNotesFile.MaxDownloadBytes + 1 - received.Length)), cancellationToken).ConfigureAwait(false)) > 0)
        {
            received.Write(chunk, 0, read);
            if (received.Length > GamePatchNotesFile.MaxDownloadBytes)
                return null;
        }

        return received.ToArray();
    }

    /// <summary>A failed write only costs a later download.</summary>
    private async Task TryWriteCacheAsync(string fileName, byte[] bytes)
    {
        try
        {
            await _cache.WriteAsync(fileName, bytes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private enum Outcome
    {
        Loaded,
        Broken,
        Missing,
        TooLarge,
        Unreachable,
    }

    private sealed record Candidate(int Revision, int Year, int Month, bool FromChain);

    private sealed record Download(Outcome Outcome, byte[]? Bytes);
}
