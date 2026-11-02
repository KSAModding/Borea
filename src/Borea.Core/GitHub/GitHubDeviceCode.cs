namespace Borea.Core.GitHub;

/// <summary>The code the user enters at <see cref="VerificationUri"/> to confirm a sign-in.</summary>
/// <param name="VerificationUriComplete">The page with the code filled in, or null when the sign-in names none.</param>
public sealed record GitHubDeviceCode(string UserCode, string VerificationUri, string? VerificationUriComplete = null);
