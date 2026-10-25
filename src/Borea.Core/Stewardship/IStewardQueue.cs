using Borea.Core.Listings;

namespace Borea.Core.Stewardship;

/// <summary>The open pull requests of content-index and content-index-releases that a steward decides.</summary>
public interface IStewardQueue
{
    /// <summary>
    /// Reads both repositories with the token of the signed-in account, oldest pull request first.
    /// A repository that cannot be read is named in <see cref="StewardQueue.Failures"/>, and the other one still shows.
    /// </summary>
    /// <exception cref="StewardException">The session is signed out, or GitHub signed it out during the read.</exception>
    Task<StewardQueue> ListAsync(StewardQueueFilter filter = StewardQueueFilter.NeedsSteward, CancellationToken cancellationToken = default);
}

public enum StewardQueueFilter
{
    /// <summary>The pull requests with the label needs-steward.</summary>
    NeedsSteward,

    AllOpen,
}

public sealed record StewardQueue(IReadOnlyList<StewardQueueItem> Items, IReadOnlyList<StewardQueueFailure> Failures)
{
    public static IReadOnlyList<string> Repositories { get; } = [ListingPullRequestLinks.Repository, StewardAccess.ReleasesRepository];
}

/// <param name="Repository">The repository that could not be read, as owner/name.</param>
public sealed record StewardQueueFailure(string Repository, StewardException Error);

/// <param name="Repository">The repository as owner/name.</param>
/// <param name="Author">The login that opened the pull request.</param>
/// <param name="Opened">When it was opened, which orders the queue.</param>
/// <param name="NeedsSteward">It has the label needs-steward.</param>
/// <param name="Kinds">What it changes, from the labels of the checks and from its changed paths.</param>
/// <param name="HasOtherFiles">It also changes a file that is none of <see cref="StewardQueueKind"/>, or it removes or renames a document, so a steward reviews it on GitHub.</param>
/// <param name="Verdict">The verdict comment of the checks without its marker, or null.</param>
public sealed record StewardQueueItem(
    string Repository,
    int Number,
    Uri Url,
    string Title,
    string? Author,
    DateTimeOffset Opened,
    bool IsDraft,
    bool NeedsSteward,
    IReadOnlyList<StewardQueueKind> Kinds,
    bool HasOtherFiles,
    string? Verdict)
{
    /// <summary>It changes index documents only, which a steward can review in Borea. Anything else is code or other files.</summary>
    public bool IsContent => Kinds.Count > 0 && !HasOtherFiles;

    public bool IsIn(StewardQueueScope scope) => scope switch
    {
        StewardQueueScope.Content => IsContent,
        StewardQueueScope.Other => !IsContent,
        _ => true,
    };
}

/// <summary>Which pull requests of the queue show, by what they change.</summary>
public enum StewardQueueScope
{
    Content,
    Other,
    All,
}

public enum StewardQueueKind
{
    Listing,
    Pack,

    /// <summary>vehicles/&lt;id&gt;.toml (RFC 0085).</summary>
    Vehicle,

    /// <summary>saves/&lt;id&gt;.toml (RFC 0085).</summary>
    Save,

    Release,
    Amendment,

    /// <summary>packs/&lt;id&gt;/owner.json.</summary>
    OwnerRecord,

    /// <summary>index-status.toml.</summary>
    IndexStatus,

    /// <summary>tags.toml.</summary>
    TagVocabulary,
}
