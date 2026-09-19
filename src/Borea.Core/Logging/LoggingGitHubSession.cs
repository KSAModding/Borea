using System.Net;
using Borea.Core.GitHub;

namespace Borea.Core.Logging;

public sealed class LoggingGitHubSession : IGitHubSession
{
    private readonly IBoreaLog _log;

    public IGitHubSession Inner { get; }

    public LoggingGitHubSession(IGitHubSession inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsAvailable => Inner.IsAvailable;

    public string ManageAccessUrl => Inner.ManageAccessUrl;

    public string InstallUrl => Inner.InstallUrl;

    public GitHubSessionState State => Inner.State;

    public event EventHandler? StateChanged
    {
        add => Inner.StateChanged += value;
        remove => Inner.StateChanged -= value;
    }

    public async Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = await Inner.SignInAsync(progress, cancellationToken).ConfigureAwait(false);
        _log.Write(result.SignedIn
            ? $"Signed in to GitHub as {result.Login}."
            : $"GitHub sign-in failed, {result.Outcome}.");
        return result;
    }

    public void SignOut()
    {
        var wasSignedIn = Inner.State.Status == GitHubSessionStatus.SignedIn;
        Inner.SignOut();
        if (wasSignedIn)
            _log.Write("Signed out of GitHub.");
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var wasSignedIn = Inner.State.Status == GitHubSessionStatus.SignedIn;
        var response = await Inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (wasSignedIn && response.StatusCode == HttpStatusCode.Unauthorized && Inner.State.Status != GitHubSessionStatus.SignedIn)
            _log.Write("Signed out of GitHub, because GitHub refused the token.");

        return response;
    }
}
