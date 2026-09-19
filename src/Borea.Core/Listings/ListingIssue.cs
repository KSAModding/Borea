namespace Borea.Core.Listings;

public enum ListingIssueSeverity
{
    /// <summary>The checks of content-index reject the document.</summary>
    Error = 0,

    /// <summary>The checks accept the document and print a note.</summary>
    Note = 1,
}

/// <param name="Location">The key the issue is about, such as "links.forums", or empty for the whole document.</param>
/// <param name="Message">What is wrong, in the words of the checks of content-index.</param>
public sealed record ListingIssue(ListingIssueSeverity Severity, string Location, string Message)
{
    public override string ToString() => Location.Length == 0 ? Message : $"{Location}: {Message}";
}

public sealed class ListingCheckResult
{
    public IReadOnlyList<ListingIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == ListingIssueSeverity.Error);

    public ListingCheckResult(IReadOnlyList<ListingIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues.OrderBy(issue => issue.Severity).ToList();
    }
}
