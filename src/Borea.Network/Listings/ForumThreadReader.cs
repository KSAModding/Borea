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

    public async Task<IReadOnlyList<string>> GetPrefixesAsync(string threadUrl, CancellationToken cancellationToken = default)
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

    /// <summary>The labels in front of the thread title of a thread page. Empty when the page has no title or no prefix.</summary>
    public static IReadOnlyList<string> ParsePrefixes(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (Title().Match(html) is not { Success: true } title)
            return [];

        return Label().Matches(title.Groups[1].Value)
            .Select(label => WebUtility.HtmlDecode(Tags().Replace(label.Groups[1].Value, string.Empty)).Trim())
            .Where(prefix => prefix.Length > 0)
            .ToList();
    }

    [GeneratedRegex("""<h1\s[^>]*class="[^"]*\bp-title-value\b[^"]*"[^>]*>([\s\S]*?)</h1>""", RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    [GeneratedRegex("""<span\s[^>]*class="[^"]*\blabel\b(?![-])[^"]*"[^>]*>([\s\S]*?)</span>""", RegexOptions.IgnoreCase)]
    private static partial Regex Label();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}
