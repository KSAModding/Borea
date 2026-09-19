using Borea.Core.Index;

namespace Borea.Core.Listings;

/// <summary>
/// Checks an authored document before its pull request, with the rules of the checks of content-index.
/// The checks stay the authority: a document they accept must pass here, and they can still reject what passes here.
/// </summary>
public interface IListingValidator
{
    /// <summary>Where the schema in use came from. Until <see cref="LoadSchemaAsync"/> finishes, it is the copy in this build.</summary>
    ListingSchemaOrigin SchemaOrigin { get; }

    /// <summary>Loads the newest schema of content-index, and keeps the one in use when that fails.</summary>
    Task<ListingSchemaOrigin> LoadSchemaAsync(CancellationToken cancellationToken = default);

    ListingCheckResult Validate(AuthoredTable document, ListingCheckContext context);
}

/// <param name="Snapshot">The content index, for the id collision, the references and the curated tags. Null skips those rules.</param>
/// <param name="ListedId">The id of the listed document an edit changes, which is no collision with itself.</param>
/// <param name="Archive">The latest release archive the author named, when Borea read one.</param>
public sealed record ListingCheckContext(ContentIndexSnapshot? Snapshot, string? ListedId = null, ListingArchiveFacts? Archive = null);
