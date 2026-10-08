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

    /// <summary>
    /// <c>owner/name</c> of a GitHub repository URL, or null. It splits the URL as urlparse of Python does for github_repository
    /// of tools/ownership.py, so that a source code link on SpaceDock names the same repository here and in the checks.
    /// </summary>
    public static string? GitHubRepositoryOf(string? url)
    {
        if (UrlParts(url ?? string.Empty) is not ({ } netloc, { } path)
            || !netloc.All(char.IsAscii)
            || !(netloc.Equals("github.com", StringComparison.OrdinalIgnoreCase) || netloc.Equals("www.github.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var owner = parts[0];
        var name = parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1];
        return GitHubName().IsMatch(owner) && GitHubName().IsMatch(name) ? $"{owner}/{name}" : null;
    }

    /// <summary>The network location and the path that urlparse of Python finds in <paramref name="url"/>, or null where it raises an error.</summary>
    private static (string Netloc, string Path)? UrlParts(string url)
    {
        url = url.TrimStart(ControlOrSpace)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        var scheme = string.Empty;
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && char.IsAsciiLetter(url[0]) && url[..colon].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
        {
            scheme = url[..colon].ToLowerInvariant();
            url = url[(colon + 1)..];
        }

        var netloc = string.Empty;
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            var end = url.IndexOfAny(['/', '?', '#'], 2);
            netloc = end < 0 ? url[2..] : url[2..end];
            url = end < 0 ? string.Empty : url[end..];
            if (netloc.Contains('[', StringComparison.Ordinal) != netloc.Contains(']', StringComparison.Ordinal))
                return null;
        }

        var cut = url.IndexOfAny(['#', '?']);
        var path = cut < 0 ? url : url[..cut];
        if (UrlParamSchemes.Contains(scheme) && path.Contains(';', StringComparison.Ordinal))
        {
            var semicolon = path.IndexOf(';', Math.Max(path.LastIndexOf('/'), 0));
            if (semicolon >= 0)
                path = path[..semicolon];
        }

        return (netloc, path);
    }

    /// <summary>What urlsplit of Python strips from the start of a URL.</summary>
    private static readonly char[] ControlOrSpace = [.. Enumerable.Range(0, 33).Select(code => (char)code)];

    /// <summary>The schemes whose last path segment urlparse of Python splits at a semicolon.</summary>
    private static readonly HashSet<string> UrlParamSchemes =
        ["", "ftp", "hdl", "prospero", "http", "imap", "https", "shttp", "rtsp", "rtsps", "rtspu", "sip", "sips", "mms", "sftp", "tel"];

    public bool IsSameHost(ListingAuthority? other) =>
        other is not null
        && string.Equals(Kind, other.Kind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Target, other.Target, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$")]
    private static partial Regex GitHubName();
}
