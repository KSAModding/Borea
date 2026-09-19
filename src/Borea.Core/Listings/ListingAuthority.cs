using System.Text.RegularExpressions;

namespace Borea.Core.Listings;

/// <summary>The release host that ownership of a listing binds to, as the ownership check of content-index reads it.</summary>
public sealed partial record ListingAuthority(string Kind, string Target)
{
    public const string GitHub = "github";

    public const string SpaceDock = "spacedock";

    /// <summary>The single host of [releases], the one [releases].authority names, or else the GitHub repository link.</summary>
    public static ListingAuthority? Of(ListingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var hosts = new List<ListingAuthority>();
        if (draft.Releases?.GitHub is { } github)
            hosts.Add(new ListingAuthority(GitHub, github));
        if (draft.Releases?.SpaceDock is { } spaceDock)
            hosts.Add(new ListingAuthority(SpaceDock, spaceDock.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        if (hosts.Count == 1)
            return hosts[0];
        if (hosts.Count > 1)
            return hosts.FirstOrDefault(host => host.Kind == draft.Releases?.Authority);
        if (draft.Releases?.Authority is not null)
            return null;

        return GitHubRepositoryOf(draft.LinkOf("repository")) is { } repository ? new ListingAuthority(GitHub, repository) : null;
    }

    /// <summary><c>owner/name</c> of a GitHub repository URL, or null.</summary>
    public static string? GitHubRepositoryOf(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var owner = parts[0];
        var name = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
        return GitHubName().IsMatch(owner) && GitHubName().IsMatch(name) ? $"{owner}/{name}" : null;
    }

    public bool IsSameHost(ListingAuthority? other) =>
        other is not null
        && string.Equals(Kind, other.Kind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Target, other.Target, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$")]
    private static partial Regex GitHubName();
}
