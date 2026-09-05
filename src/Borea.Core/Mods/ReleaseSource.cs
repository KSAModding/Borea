using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// Where a listing's releases appear: one or more hosts, one of them the authority.
/// </summary>
public sealed class ReleaseSource
{
    /// <summary>
    /// The hosts, keyed as the authored file writes them ("github", "spacedock").
    /// </summary>
    public IReadOnlyList<ReleaseHost> Hosts { get; }

    /// <summary>
    /// The host key that defines which releases exist, in the casing of the host entry it names.
    /// </summary>
    public string Authority { get; }

    public ReleaseSource(IReadOnlyList<ReleaseHost> hosts, string? authority = null)
    {
        if (hosts is null || hosts.Count == 0)
            throw new ArgumentException("A release source needs at least one host.", nameof(hosts));

        var duplicateHost = hosts.GroupBy(h => h.Host, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateHost is not null)
            throw new ArgumentException($"Host '{duplicateHost}' appears more than once.", nameof(hosts));

        if (authority is null)
        {
            if (hosts.Count > 1)
                throw new ArgumentException("More than one host requires an authority naming one of them.", nameof(authority));

            Authority = hosts[0].Host;
        }
        else
        {
            var match = hosts.FirstOrDefault(h => string.Equals(h.Host, authority, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Authority '{authority}' does not name one of the hosts.", nameof(authority));

            Authority = match.Host;
        }

        Hosts = new ReadOnlyCollection<ReleaseHost>(hosts.ToArray());
    }

    /// <summary>
    /// The host entry the authority key names.
    /// </summary>
    public ReleaseHost AuthorityHost => Hosts.First(h => string.Equals(h.Host, Authority, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One release host entry: the host key and the reference on that host.
/// </summary>
public sealed class ReleaseHost
{
    /// <summary>
    /// The host key, such as "github" or "spacedock".
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// The reference on that host, such as "owner/repo" or a SpaceDock mod id.
    /// </summary>
    public string Reference { get; }

    public ReleaseHost(string host, string reference)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be empty.", nameof(host));

        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Host reference cannot be empty.", nameof(reference));

        Host = host;
        Reference = reference;
    }
}
