namespace Borea.Core.Listings;

/// <summary>
/// The GitHub pages that start the pull request of one listing document in content-index.
/// For someone without write access, GitHub creates the fork and then shows the pull request form.
/// </summary>
public static class ListingPullRequestLinks
{
    public const string Repository = "KSAModding/content-index";

    public const string Branch = "main";

    /// <summary>A longer URL is not opened with the document in it, because browsers and GitHub cut long URLs.</summary>
    public const int MaxUrlLength = 8000;

    /// <summary>
    /// The new-file page of the listing. GitHub fills the file from the undocumented value parameter, so the
    /// text is left out when the URL would be longer than <see cref="MaxUrlLength"/>, and the author pastes it.
    /// </summary>
    public static ListingPullRequestPage NewFile(string id, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(text);

        var page = NewFileWithoutText(id);
        var withText = $"{page.Url.AbsoluteUri}&value={Uri.EscapeDataString(text)}";
        return withText.Length <= MaxUrlLength ? new ListingPullRequestPage(new Uri(withText), CarriesText: true) : page;
    }

    /// <summary>The new-file page of the listing with an empty file, for the author to paste the text into.</summary>
    public static ListingPullRequestPage NewFileWithoutText(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return new ListingPullRequestPage(new Uri($"https://github.com/{Repository}/new/{Branch}?filename={ListingDraft.ListingsFolder}/{Uri.EscapeDataString(id)}.toml"), CarriesText: false);
    }

    /// <summary>The edit page of a listed document. It never carries the text, so the author pastes it.</summary>
    public static ListingPullRequestPage Edit(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return new ListingPullRequestPage(new Uri($"https://github.com/{Repository}/edit/{Branch}/{ListingDraft.ListingsFolder}/{Uri.EscapeDataString(id)}.toml"), CarriesText: false);
    }
}

/// <param name="CarriesText">Whether the page opens with the document filled in.</param>
public sealed record ListingPullRequestPage(Uri Url, bool CarriesText);
