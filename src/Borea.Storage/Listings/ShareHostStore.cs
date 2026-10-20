using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Core.Sharing;
using Borea.Storage.Files;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Listings;

/// <summary>
/// The share hosts of hosts.toml in content-index (RFC 0094): downloaded and cached, else the cached copy, else none.
/// No copy ships with Borea, so a build never offers a host that the index does not register. A text that is no TOML is
/// never used or cached, and a table that Borea cannot use is skipped and not the whole file.
/// </summary>
public sealed class ShareHostStore : IShareHostSource
{
    private readonly IShareHostFetcher _fetcher;
    private readonly IGamePathProvider _paths;

    public ShareHostStore(IShareHostFetcher fetcher, IGamePathProvider paths)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>The answer of content-index, which holds for the rest of the run. A cached copy is read again on the next call.</summary>
    private IReadOnlyList<ShareHost>? _downloaded;

    public async Task<IReadOnlyList<ShareHost>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_downloaded is { } known)
            return known;

        var path = _paths.GetShareHostsPath();
        try
        {
            var downloaded = await _fetcher.FetchHostsAsync(cancellationToken).ConfigureAwait(false);
            if (downloaded is null)
            {
                TryDelete(path);
                return _downloaded = [];
            }

            if (Parse(downloaded) is { } hosts)
            {
                await TryWriteAsync(path, downloaded, cancellationToken).ConfigureAwait(false);
                return _downloaded = hosts;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            if (File.Exists(path) && Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) is { } cached)
                return cached;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return [];
    }

    /// <summary>The usable hosts of a hosts.toml text by key, or null when the text is no TOML.</summary>
    internal static IReadOnlyList<ShareHost>? Parse(string text)
    {
        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (Exception)
        {
            return null;
        }

        var hosts = new List<ShareHost>();
        foreach (var (key, value) in root)
        {
            if (value is TomlTable table && Read(key, table) is { } host)
                hosts.Add(host);
        }

        return hosts.OrderBy(host => host.Key, StringComparer.Ordinal).ToList();
    }

    // The token of a player goes to api, so only an https address is taken.
    private static ShareHost? Read(string key, TomlTable table)
    {
        if (!ReleaseHost.IsShareKey(key) || !Equals(Value(table, "kind"), "share"))
            return null;
        if (HttpsUri(Value(table, "api")) is not { } api)
            return null;
        if (Value(table, "types") is not TomlArray typeArray || typeArray.Any(type => type is not string))
            return null;

        var maxBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        if (Value(table, "max_bytes") is { } limits)
        {
            if (limits is not TomlTable limitTable)
                return null;

            foreach (var (type, limit) in limitTable)
            {
                if (limit is not long bytes || bytes <= 0)
                    return null;

                maxBytes[type] = bytes;
            }
        }

        var name = Value(table, "name") is string { Length: > 0 } text ? text : key;
        var apiBase = api.AbsoluteUri.EndsWith('/') ? api : new Uri(api.AbsoluteUri + "/");
        return new ShareHost(key, name, apiBase, typeArray.Cast<string>().ToList(), maxBytes, HttpsUri(Value(table, "policy")));
    }

    private static object? Value(TomlTable table, string key) => table.TryGetValue(key, out var value) ? value : null;

    private static Uri? HttpsUri(object? value) =>
        value is string text && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 ? uri : null;

    private static async Task TryWriteAsync(string path, string text, CancellationToken cancellationToken)
    {
        try
        {
            await AtomicFile.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
