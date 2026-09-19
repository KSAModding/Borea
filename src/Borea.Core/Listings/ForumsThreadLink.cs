using System.Globalization;
using System.Text.RegularExpressions;

namespace Borea.Core.Listings;

/// <summary>A link to a thread on forums.ahwoo.com, in the forms the authored schema accepts.</summary>
public static partial class ForumsThreadLink
{
    /// <summary>The thread number the link names, or null when it is no thread link.</summary>
    public static long? ThreadOf(string? url) =>
        url is not null && Pattern().Match(url) is { Success: true } match
            && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var thread)
            ? thread
            : null;

    [GeneratedRegex(@"^https://forums\.ahwoo\.com/(?:index\.php\?)?(?:threads/|forums/(?:[^\s/?#\u0000-\u001f]+/)+)(?:[^\s/?#\u0000-\u001f]*\.)?([0-9]+)(?:[/?#][^\s\u0000-\u001f]*)?(?![\s\S])")]
    private static partial Regex Pattern();
}
