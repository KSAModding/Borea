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
}

/// <param name="Id">The upload id, which the publish names.</param>
/// <param name="Checks">What the host checked in the archive.</param>
/// <param name="ProposedVersion">The version the host proposes, or null.</param>
/// <param name="ListingText">The listing text the host shows, or null.</param>
public sealed record ShareUpload(string Id, IReadOnlyList<ShareCheck> Checks, string? ProposedVersion, string? ListingText);

public sealed record ShareCheck(string Name, bool Passed, string? Message);

/// <param name="PullRequest">
/// The listing pull request the host opened on GitHub, or null. A new version whose dependency list did not change needs none,
/// and a host may open the pull request of a new listing later, together with others.
/// </param>
public sealed record SharePublished(string Id, string Version, Uri? PullRequest);

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
}
