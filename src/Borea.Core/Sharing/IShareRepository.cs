namespace Borea.Core.Sharing;

/// <summary>
/// The repository in the signed-in GitHub account that holds the shares of the player (RFC 0094): one release per share, whose tag
/// starts with the prefix of its listing (RFC 0096), with the archive as its one asset.
/// </summary>
public interface IShareRepository
{
    /// <summary>The share repository of the signed-in account, which is created with a first commit when it is missing.</summary>
    /// <exception cref="ShareRepositoryException">GitHub refused it, or it could not be reached.</exception>
    Task<ShareRepository> EnsureAsync(CancellationToken cancellationToken = default);

    /// <summary>1.0.0 for a first release of the prefix, else the next minor version after the highest one.</summary>
    /// <exception cref="ShareRepositoryException">The releases could not be read.</exception>
    Task<string> NextVersionAsync(ShareRepository repository, string tagPrefix, CancellationToken cancellationToken = default);

    /// <summary>Makes the release <paramref name="tagPrefix"/><paramref name="version"/> and uploads the archive as its one asset.</summary>
    /// <returns>The page of the release.</returns>
    /// <exception cref="ShareRepositoryException">GitHub refused it, or it could not be reached. A release without its asset is deleted again.</exception>
    Task<Uri> PublishAsync(ShareRepository repository, string tagPrefix, string version, string assetName, Stream archive, CancellationToken cancellationToken = default);
}

/// <param name="FullName">owner/name.</param>
/// <param name="Id">The GitHub id, which the install page of the App takes.</param>
public sealed record ShareRepository(string FullName, long Id)
{
    /// <summary>The name of the share repository in the player's account.</summary>
    public const string Name = "ksa-shares";
}

public enum ShareRepositoryFailure
{
    SignedOut,

    /// <summary>The token may not create a repository, because the GitHub App has no permission for it.</summary>
    MayNotCreateRepository,

    /// <summary>The GitHub App is not installed on the repository, so the release cannot be made.</summary>
    AppNotOnRepository,

    RateLimited,
    NetworkError,

    /// <summary>GitHub refused the request with a message, which the exception carries.</summary>
    Refused,

    UnexpectedResponse,
}

/// <summary>A step of a share to the player's own repository failed. The message never holds the token.</summary>
public sealed class ShareRepositoryException : Exception
{
    public ShareRepositoryException(ShareRepositoryFailure failure, string? detail = null, long? repositoryId = null, Exception? innerException = null)
        : base(failure + (detail is null ? string.Empty : ", " + detail), innerException)
    {
        Failure = failure;
        Detail = detail;
        RepositoryId = repositoryId;
    }

    public ShareRepositoryFailure Failure { get; }

    /// <summary>What GitHub said, or null.</summary>
    public string? Detail { get; }

    /// <summary>The repository that the App is not installed on.</summary>
    public long? RepositoryId { get; }
}
