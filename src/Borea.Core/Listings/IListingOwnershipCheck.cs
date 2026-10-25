using Borea.Core.GitHub;

namespace Borea.Core.Listings;

/// <summary>
/// The ownership proof of tools/ownership.py in content-index for any GitHub account, so a steward can check the author of a pull request.
/// It reads only, and the checks of content-index stay the authority.
/// </summary>
public interface IListingOwnershipCheck
{
    /// <summary>
    /// Which proof <paramref name="author"/> has on the release host of a listing. An edit proves control of the host
    /// that <paramref name="listed"/> names, and of the new one when it moves (RFC 0048).
    /// </summary>
    /// <param name="listed">The listed document that the edit changes, or null for a new listing.</param>
    Task<ListingOwnership> CheckAsync(GitHubAccount author, ListingDraft submitted, ListingDraft? listed, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same for the listing at <paramref name="path"/> of content-index, as <paramref name="headCommit"/> submits it
    /// and the tip of <paramref name="baseBranch"/> lists it, which is how the checks read a pull request.
    /// </summary>
    /// <param name="path">A listing document in one of <see cref="ListingFolders.All"/>, such as listings/MyMod.toml or vehicles/ExampleRover.toml.</param>
    /// <exception cref="ListingPublishException">Signed out, or GitHub refused the token.</exception>
    Task<ListingOwnership> CheckPullRequestAsync(GitHubAccount author, string path, string baseBranch, string headCommit, CancellationToken cancellationToken = default);

    /// <summary>
    /// The logins the proofs name as owners of the release host of <paramref name="listed"/>: the owner of a personal repository,
    /// and the ksa-index-&lt;login&gt; topics and the marker file login of an organization repository. Empty when the host names nobody or does not answer.
    /// </summary>
    Task<IReadOnlyList<string>> OwnersAsync(ListingDraft listed, CancellationToken cancellationToken = default);
}
