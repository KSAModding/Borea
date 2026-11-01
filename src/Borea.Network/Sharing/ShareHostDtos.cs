using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borea.Network.Sharing;

/// <summary>
/// The paths and the client id of the upload API below the api of a share host, as rule 15 of spec/share-host.md names them.
/// </summary>
internal static class ShareHostApi
{
    public const string ClientId = "borea";

    public const string DeviceAuthorizationPath = "oauth/device_authorization";

    public const string TokenPath = "oauth/token";

    public const string UploadsPath = "uploads";

    public const string PublishPath = "publish";

    public const string ListingsPath = "listings";

    public const string ImagesPath = "images";

    /// <summary>The kept secret says which kind of token it holds, because a host may give no refresh token.</summary>
    public const string RefreshPrefix = "refresh:";

    public const string AccessPrefix = "access:";
}

/// <summary>Every JSON and form name of the upload API, from rules 15 and 16 of spec/share-host.md.</summary>
internal static class ShareHostJson
{
    public const string Archive = "archive";

    public const string Image = "image";

    public const string Dependencies = "dependencies";

    public const string Listing = "listing";

    public const string Version = "version";

    public const string Status = "status";

    public const string Id = "id";

    public const string Name = "name";

    public const string Authors = "authors";

    public const string Abstract = "abstract";

    public const string License = "license";

    public const string Description = "description";

    public const string Tags = "tags";

    /// <summary>The text of a member that takes any JSON value, or null when it holds anything else.</summary>
    public static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public sealed class DeviceCode
    {
        [JsonPropertyName("device_code")]
        public string? DeviceCodeValue { get; set; }

        [JsonPropertyName("user_code")]
        public string? UserCode { get; set; }

        [JsonPropertyName("verification_uri")]
        public string? VerificationUri { get; set; }

        // This takes any JSON value, because a bad value must not stop a sign-in that the plain page can finish.
        [JsonPropertyName("verification_uri_complete")]
        public JsonElement VerificationUriComplete { get; set; }

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
        // A 2xx is a release that is live, so these take any JSON value, and a malformed one costs only itself.
        [JsonPropertyName("id")]
        public JsonElement Id { get; set; }

        [JsonPropertyName("version")]
        public JsonElement Version { get; set; }

        [JsonPropertyName("pull_request")]
        public JsonElement PullRequest { get; set; }

        [JsonPropertyName("page")]
        public JsonElement Page { get; set; }
    }

    public sealed class Error
    {
        [JsonPropertyName("error")]
        public string? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        // These three take any JSON value, so a malformed one costs neither the message nor the others.
        [JsonPropertyName("checks")]
        public JsonElement Checks { get; set; }

        [JsonPropertyName("retry_after")]
        public JsonElement RetryAfter { get; set; }

        [JsonPropertyName("errors")]
        public JsonElement FieldErrors { get; set; }
    }
}
