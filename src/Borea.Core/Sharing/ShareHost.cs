namespace Borea.Core.Sharing;

/// <summary>A share host of RFC 0094, as a table of hosts.toml in content-index registers it.</summary>
/// <param name="Key">The table name, which a listing names under [releases].</param>
/// <param name="Name">The name to show, such as ksamods.gg.</param>
/// <param name="Api">The base URL of the upload API, which ends with a slash.</param>
/// <param name="Types">The content types the host takes.</param>
/// <param name="MaxBytes">The largest archive the host takes, per type.</param>
/// <param name="Policy">The page of the host's policy, or null.</param>
public sealed record ShareHost(string Key, string Name, Uri Api, IReadOnlyList<string> Types, IReadOnlyDictionary<string, long> MaxBytes, Uri? Policy = null)
{
    public bool Accepts(string type) => Types.Contains(type, StringComparer.Ordinal);

    /// <summary>The largest archive of <paramref name="type"/>, or null when the host names no limit for it.</summary>
    public long? MaxBytesFor(string type) => MaxBytes.TryGetValue(type, out var max) ? max : null;

    /// <summary>The site of the host. hosts.toml has no key for it, so it is the origin of <see cref="Api"/>.</summary>
    public Uri Page => new(Api.GetLeftPart(UriPartial.Authority) + "/");
}

/// <summary>The share hosts that content-index registers.</summary>
public interface IShareHostSource
{
    /// <summary>The registered hosts, or none when content-index registers none and Borea has no copy of hosts.toml.</summary>
    Task<IReadOnlyList<ShareHost>> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Downloads hosts.toml of content-index.</summary>
public interface IShareHostFetcher
{
    /// <summary>The text of hosts.toml, or null when content-index has no such file.</summary>
    Task<string?> FetchHostsAsync(CancellationToken cancellationToken = default);
}
