namespace Borea.Core.GitHub;

/// <summary>The code the user enters at <see cref="VerificationUri"/> to confirm a sign-in.</summary>
public sealed record GitHubDeviceCode(string UserCode, string VerificationUri);
