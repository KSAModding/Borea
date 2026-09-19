namespace Borea.Core.GitHub;

public sealed record GitHubSignInResult(GitHubSignInOutcome Outcome, string? Login = null)
{
    public bool SignedIn => Outcome == GitHubSignInOutcome.SignedIn;
}
