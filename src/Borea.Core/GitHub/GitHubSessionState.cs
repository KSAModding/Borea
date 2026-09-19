namespace Borea.Core.GitHub;

public sealed record GitHubSessionState
{
    public static GitHubSessionState SignedOut { get; } = new(GitHubSessionStatus.SignedOut, login: null, deviceCode: null);

    public GitHubSessionStatus Status { get; }

    /// <summary>The GitHub login while signed in, otherwise null.</summary>
    public string? Login { get; }

    /// <summary>The code to enter while waiting for it, otherwise null.</summary>
    public GitHubDeviceCode? DeviceCode { get; }

    private GitHubSessionState(GitHubSessionStatus status, string? login, GitHubDeviceCode? deviceCode)
    {
        Status = status;
        Login = login;
        DeviceCode = deviceCode;
    }

    public static GitHubSessionState WaitingFor(GitHubDeviceCode deviceCode) =>
        new(GitHubSessionStatus.WaitingForCode, login: null, deviceCode ?? throw new ArgumentNullException(nameof(deviceCode)));

    public static GitHubSessionState SignedInAs(string login) =>
        new(GitHubSessionStatus.SignedIn, string.IsNullOrWhiteSpace(login) ? throw new ArgumentException("A signed-in session needs a login.", nameof(login)) : login, deviceCode: null);
}
