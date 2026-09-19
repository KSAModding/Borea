namespace Borea.Core.GitHub;

public enum GitHubSignInOutcome
{
    SignedIn,
    Expired,
    AccessDenied,
    IncorrectClientCredentials,
    IncorrectDeviceCode,
    UnsupportedGrantType,
    DeviceFlowDisabled,
    NetworkError,
    UnexpectedResponse,
}
