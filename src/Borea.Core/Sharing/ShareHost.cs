namespace Borea.Core.Sharing;

/// <summary>A share host of RFC 0094, one of the hosts of the snapshot (RFC 0104).</summary>
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

    /// <summary>
    /// The address when it is https on the host of the api, or on a sub-domain or a parent of it, because a host may serve its pages
    /// and its api from different names. A top-level name such as gg counts as no parent.
    /// </summary>
    public Uri? PageOnHost(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps)
            return null;

        var api = Api.Host;
        var site = page.Host;
        return string.Equals(site, api, StringComparison.OrdinalIgnoreCase)
            || site.EndsWith("." + api, StringComparison.OrdinalIgnoreCase)
            || (site.Contains('.') && api.EndsWith("." + site, StringComparison.OrdinalIgnoreCase))
                ? page
                : null;
    }
}

/// <summary>The share hosts that content-index registers.</summary>
public interface IShareHostSource
{
    /// <summary>The hosts of the snapshot (RFC 0104), or none when the snapshot has none or cannot be read.</summary>
    Task<IReadOnlyList<ShareHost>> GetAsync(CancellationToken cancellationToken = default);
}
