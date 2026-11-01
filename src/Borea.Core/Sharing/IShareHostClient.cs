using Borea.Core.GitHub;
using Borea.Core.Listings;

namespace Borea.Core.Sharing;

/// <summary>
/// The upload API that every share host offers at its api (RFC 0094): a sign-in with the OAuth 2.0 device authorization grant,
/// an upload that the host checks, and a publish that opens the listing pull request. The token of a host goes only to its api.
/// </summary>
public interface IShareHostClient
{
    bool IsSignedIn(ShareHost host);

    /// <summary>Signs in with the token kept from an earlier sign-in, or else with a code the player enters on the host.</summary>
    /// <param name="progress">Gets the code and the page to enter it on.</param>
    /// <param name="keepSignedIn">Keeps the token in the secret store of the system, for the next start.</param>
    /// <exception cref="ShareHostException">The host refused the sign-in, or it could not be reached.</exception>
    Task SignInAsync(ShareHost host, IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = false, CancellationToken cancellationToken = default);

    /// <summary>Forgets the token of the host, also the kept one.</summary>
    void SignOut(ShareHost host);

    /// <summary>Sends the archive and the dependency list, which the host checks before anything is published.</summary>
    /// <param name="listingId">The id of the listing on the host, for a new version of it, or null for a new listing.</param>
    /// <exception cref="ShareHostException">The host refused the upload, or it could not be reached.</exception>
    Task<ShareUpload> UploadAsync(ShareHost host, Stream archive, string fileName, IReadOnlyList<ListingDependency> dependencies, string? listingId, CancellationToken cancellationToken = default);

    /// <summary>Publishes an upload as a release, and for a new listing sends the fields of <paramref name="listing"/> for its pull request.</summary>
    /// <param name="status">stable, testing or dev.</param>
    /// <param name="listing">The new listing, or null for a new version of a listing on the host.</param>
    /// <exception cref="ShareHostException">The host refused it, or it could not be reached.</exception>
    Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, ListingDraft? listing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a picture after the pictures that a listing has, so the first picture of a listing is its cover. Only the owner of the listing
    /// may do this. RFC 0094 does not ask for this request, so a host can have none, and it then answers with <see cref="ShareHostException.IsNotFound"/>.
    /// </summary>
    /// <param name="listingType">vehicle or save.</param>
    /// <param name="listingId">The id of the listing on the host.</param>
    /// <param name="image">A PNG, JPEG or WebP image, within <see cref="ShareImageLimits"/>.</param>
    /// <exception cref="ShareHostException">The host refused the picture, or it could not be reached.</exception>
    Task UploadImageAsync(ShareHost host, string listingType, string listingId, ReadOnlyMemory<byte> image, CancellationToken cancellationToken = default);

    /// <summary>Changes the text of a listing for its owner. A host need not offer this request, and then the failure is <see cref="ShareHostFailure.NotOffered"/>.</summary>
    /// <param name="type">vehicle or save.</param>
    /// <exception cref="ShareHostException">The host refused it, or it could not be reached.</exception>
    Task<ShareListingChanged> ChangeListingAsync(ShareHost host, string type, string id, ShareListingChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes one version of a listing down for its owner, which cannot be undone, so a caller sends it only after the player confirmed that version.
    /// A host need not offer this request, and then the failure is <see cref="ShareHostFailure.NotOffered"/>.
    /// </summary>
    /// <param name="type">vehicle or save.</param>
    /// <exception cref="ShareHostException">The host refused it, or it could not be reached.</exception>
    Task TakeDownReleaseAsync(ShareHost host, string type, string id, string version, CancellationToken cancellationToken = default);
}

/// <param name="Id">The upload id, which the publish names.</param>
/// <param name="Checks">What the host checked in the archive.</param>
/// <param name="ProposedVersion">The version the host proposes, or null.</param>
/// <param name="ListingText">The listing text the host shows, or null.</param>
public sealed record ShareUpload(string Id, IReadOnlyList<ShareCheck> Checks, string? ProposedVersion, string? ListingText);

public sealed record ShareCheck(string Name, bool Passed, string? Message);

/// <summary>A release that is live, because the host answered the publish with a 2xx, also when the rest of its answer is odd.</summary>
/// <param name="Id">The id the host names, else the id of the listing that was sent. Null for a new version when the host names none, because that publish sends no id.</param>
/// <param name="Version">The version the host names, else the version that was sent.</param>
/// <param name="PullRequest">
/// The listing pull request the host opened on GitHub, or null. A new version whose dependency list did not change needs none,
/// and a host may open the pull request of a new listing later, together with others.
/// </param>
/// <param name="Page">The public page of the listing on the host, or null when the host names none that Borea can trust.</param>
public sealed record SharePublished(string? Id, string Version, Uri? PullRequest, Uri? Page = null);

/// <summary>The fields of a listing to change. A null field stays as it is on the host, and an empty description or tag list clears it.</summary>
public sealed record ShareListingChange(string? Name = null, string? Abstract = null, string? Description = null, string? License = null, IReadOnlyList<string>? Tags = null);

/// <param name="PullRequest">The pull request that brings the change to the index, or null when the host could not reach GitHub and tries again later.</param>
/// <param name="Page">The public page of the listing on the host, or null when the host names none that Borea can trust.</param>
public sealed record ShareListingChanged(Uri? PullRequest, Uri? Page);

public enum ShareHostFailure
{
    /// <summary>The host answered with an error, whose message is for the player.</summary>
    Host,

    /// <summary>The host no longer takes the token, so the player signs in again.</summary>
    SignedOut,

    /// <summary>The player refused the sign-in, or its code expired.</summary>
    SignInRefused,

    NetworkError,

    /// <summary>The host answered with something that does not follow the API.</summary>
    UnexpectedResponse,

    /// <summary>The host answered with a 5xx and no message for the player, so it has a problem right now.</summary>
    Unavailable,

    /// <summary>The host answered an optional request with a 404 that no code of that request explains, so it does not offer the request.</summary>
    NotOffered,
}

public sealed class ShareHostException : Exception
{
    public ShareHostException(ShareHostFailure failure, string message, string? error = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        Error = error;
    }

    public ShareHostFailure Failure { get; }

    /// <summary>The machine-readable error of the host, or null.</summary>
    public string? Error { get; }

    /// <summary>What the host checked in a refused archive, or null when the answer names no checks.</summary>
    public IReadOnlyList<ShareCheck>? Checks { get; init; }

    /// <summary>How long the host asks the player to wait, or null when the answer names no time.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>The lines of the host for each field of a refused request, or null when the answer has none.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? FieldErrors { get; init; }

    /// <summary>The host answered with a 5xx and a message, so the request can have taken effect although it reads as refused.</summary>
    public bool IsServerError { get; init; }

    /// <summary>The host answered with a 404, which for an optional request can mean that the host does not have that request.</summary>
    public bool IsNotFound { get; init; }
}
