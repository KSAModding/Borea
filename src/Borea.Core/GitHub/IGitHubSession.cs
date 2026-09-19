namespace Borea.Core.GitHub;

/// <summary>
/// The user's GitHub sign-in through the device flow. The token stays in memory,
/// is never shown, and goes only to https://api.github.com.
/// </summary>
public interface IGitHubSession
{
    /// <summary>False when this build of Borea has no GitHub App to sign in with.</summary>
    bool IsAvailable { get; }

    /// <summary>The GitHub page where the user revokes Borea's access.</summary>
    string ManageAccessUrl { get; }

    /// <summary>The GitHub page where the user installs the App on a repository.</summary>
    string InstallUrl { get; }

    GitHubSessionState State { get; }

    /// <summary>Raised on any thread when <see cref="State"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Asks GitHub for a code, reports it, and waits until the user confirmed or refused it.
    /// Throws <see cref="OperationCanceledException"/> when cancelled or signed out meanwhile,
    /// and <see cref="InvalidOperationException"/> when unavailable, already signed in, or a sign-in already runs.
    /// </summary>
    Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the token and stops a running sign-in. The grant stays on GitHub until the user revokes it.</summary>
    void SignOut();

    /// <summary>
    /// Sends <paramref name="request"/> with the token. Only https://api.github.com is accepted.
    /// A 401 answer to the token signs the session out. Throws <see cref="InvalidOperationException"/> when signed out.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
