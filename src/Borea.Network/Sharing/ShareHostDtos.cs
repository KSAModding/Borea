using System.Text.Json.Serialization;

namespace Borea.Network.Sharing;

/// <summary>
/// The paths and the client id of the upload API below the api of a share host. RFC 0094 names the device authorization
/// grant of RFC 8628 at api and the two upload steps, but neither the paths of the grant nor a client id.
/// </summary>
internal static class ShareHostApi
{
    public const string ClientId = "borea";

    public const string DeviceAuthorizationPath = "oauth/device_authorization";

    public const string TokenPath = "oauth/token";

    public const string UploadsPath = "uploads";

    public const string PublishPath = "publish";

    /// <summary>The kept secret says which kind of token it holds, because a host may give no refresh token.</summary>
    public const string RefreshPrefix = "refresh:";

    public const string AccessPrefix = "access:";
}

/// <summary>Every JSON and form name of the upload API. RFC 0094 lists what each step returns, but no names.</summary>
internal static class ShareHostJson
{
    public const string Archive = "archive";

    public const string Dependencies = "dependencies";

    public const string Listing = "listing";

    public const string Version = "version";

    public const string Status = "status";

    public sealed class DeviceCode
    {
        [JsonPropertyName("device_code")]
        public string? DeviceCodeValue { get; set; }

        [JsonPropertyName("user_code")]
        public string? UserCode { get; set; }

        [JsonPropertyName("verification_uri")]
        public string? VerificationUri { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }
    }

    public sealed class Token
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    public sealed class Upload
    {
        [JsonPropertyName("upload")]
        public string? Id { get; set; }

        [JsonPropertyName("checks")]
        public List<Check>? Checks { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("listing")]
        public string? Listing { get; set; }
    }

    public sealed class Check
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("passed")]
        public bool Passed { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    public sealed class Published
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("pull_request")]
        public string? PullRequest { get; set; }
    }

    public sealed class Error
    {
        [JsonPropertyName("error")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
