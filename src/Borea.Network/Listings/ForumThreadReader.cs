using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Borea.Core.Listings;

namespace Borea.Network.Listings;

/// <summary>Reads the thread prefixes of a forums.ahwoo.com thread from its page, as XenForo renders them in the title.</summary>
public sealed partial class ForumThreadReader : IForumThreadReader
{
    private const string ForumsHost = "forums.ahwoo.com";

    private const int MaxPageBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    public ForumThreadReader(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<IReadOnlyList<ForumPrefix>> GetPrefixesAsync(string threadUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(threadUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
            || !string.Equals(url.Host, ForumsHost, StringComparison.OrdinalIgnoreCase))
            return [];

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Timeout);
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var page = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                page.Write(buffer, 0, read);
                if (page.Length > MaxPageBytes)
                    return [];
            }

            return ParsePrefixes(Encoding.UTF8.GetString(page.GetBuffer(), 0, (int)page.Length));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    /// <summary>
    /// The labels in front of the thread title of a thread page, each with the prefix id that the threads listed on
    /// the page give its text. Empty when the page has no title or no prefix.
    /// </summary>
    public static IReadOnlyList<ForumPrefix> ParsePrefixes(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (Title().Match(html) is not { Success: true } title)
            return [];

        var ids = ListedPrefixIds(html);
        return Label().Matches(title.Groups[1].Value)
            .Select(LabelText)
            .Where(text => text.Length > 0)
            .Select(text => new ForumPrefix(text, ids.GetValueOrDefault(text)))
            .ToList();
    }

    /// <summary>
    /// The prefix id of each label text in the thread lists of the page, such as its similar threads. XenForo renders
    /// no id on the title of a thread page, but it marks every listed thread with the is-prefix class of its prefix.
    /// Only the listed threads of the forum that holds the page count, because the similar threads also come from
    /// other forums, and a prefix there can have the same text and a different id. A text that two listed threads
    /// give different ids gets none, and a page that does not name its forum in its canonical link gives no ids.
    /// </summary>
    private static Dictionary<string, int?> ListedPrefixIds(string html)
    {
        var ids = new Dictionary<string, int?>(StringComparer.Ordinal);
        if (Canonical().Match(html) is not { Success: true } canonical || ForumPath(canonical.Groups[1].Value) is not { } forum)
            return ids;

        var threads = ListedThread().Matches(html);
        for (var index = 0; index < threads.Count; index++)
        {
            var start = threads[index].Index + threads[index].Length;
            var end = index + 1 < threads.Count ? threads[index + 1].Index : html.Length;
            if (ListedPrefixOf(html, threads[index], start, end, forum) is not { } prefix)
                continue;

            ids[prefix.Text] = ids.TryGetValue(prefix.Text, out var known) && known != prefix.Id ? null : prefix.Id;
        }

        return ids;
    }

    /// <summary>The label text and prefix id of one listed thread, whose title lies between <paramref name="start"/> and <paramref name="end"/>, or null when the thread does not give both or is in another forum than <paramref name="forum"/>.</summary>
    private static (string Text, int Id)? ListedPrefixOf(string html, Match thread, int start, int end, string forum)
    {
        if (PrefixClass().Match(thread.Groups[1].Value) is not { Success: true } prefixClass
            || !int.TryParse(prefixClass.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ListedTitle().Match(html, start, end - start) is not { Success: true } listedTitle
            || Label().Matches(listedTitle.Groups[1].Value) is not [var label]
            || ListedLink().Match(listedTitle.Groups[1].Value) is not { Success: true } link
            || !string.Equals(ForumPath(link.Groups[1].Value), forum, StringComparison.OrdinalIgnoreCase))
            return null;

        return (LabelText(label), id);
    }

    /// <summary>
    /// The path of the forum that holds a thread, from a link to the thread such as
    /// /forums/kitten-space-agency/mod-releases/deltavmap.978/, or null when the link does not point at a thread.
    /// </summary>
    private static string? ForumPath(string link)
    {
        var decoded = WebUtility.HtmlDecode(link);
        var path = decoded.StartsWith('/') ? decoded : Uri.TryCreate(decoded, UriKind.Absolute, out var url) ? url.AbsolutePath : null;
        return path is not null && ThreadPath().Match(path) is { Success: true } thread ? thread.Groups[1].Value : null;
    }

    private static string LabelText(Match label) => WebUtility.HtmlDecode(Tags().Replace(label.Groups[1].Value, string.Empty)).Trim();

    [GeneratedRegex("""<h1\s[^>]*class="[^"]*\bp-title-value\b[^"]*"[^>]*>([\s\S]*?)</h1>""", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    [GeneratedRegex("""<span\s[^>]*class="[^"]*\blabel\b(?![-])[^"]*"[^>]*>([\s\S]*?)</span>""", RegexOptions.IgnoreCase)]
    private static partial Regex Label();

    [GeneratedRegex("""<div\s[^>]*class="([^"]*\bstructItem--thread\b[^"]*)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex ListedThread();

    [GeneratedRegex(@"(?:^|\s)is-prefix(\d+)(?:\s|$)")]
    private static partial Regex PrefixClass();

    [GeneratedRegex("""<div\s[^>]*class="[^"]*\bstructItem-title\b[^"]*"[^>]*>([\s\S]*?)</div>""", RegexOptions.IgnoreCase)]
    private static partial Regex ListedTitle();

    [GeneratedRegex("""<a\s[^>]*\bhref="([^"]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ListedLink();

    [GeneratedRegex("""<link\s[^>]*\brel="canonical"[^>]*\bhref="([^"]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex Canonical();

    [GeneratedRegex(@"^((?:/[^/?#]+)*/)[^/?#]+\.\d+/(?:page-\d+/?)?(?:[?#].*)?$")]
    private static partial Regex ThreadPath();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
