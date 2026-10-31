using System.Net;
using Borea.Core.Sharing;

namespace Borea.Network.Tests;

/// <summary>The answers of a share host, from the API page of ksamods.gg and from the live host, so that the client runs against the real contract.</summary>
public static class ShareHostReplies
{
    /// <summary>The live answer to an upload whose checks pass.</summary>
    public const string Upload = """
        {
         "upload": "01M69W4FDJX6ZHS6XEER5V8468",
         "type": "vehicle",
         "checks": [
          { "name": "archive", "passed": true, "message": "A zip archive with safe paths." },
          { "name": "root", "passed": true, "message": "meta.toml and the game file are in the folder Borea Upload Probe." },
          { "name": "type", "passed": true, "message": "A vehicle, with its vehicle.xml." },
          { "name": "size", "passed": true, "message": "The archive is 0 MB." },
          { "name": "meta.toml", "passed": true, "message": "meta.toml parses." },
          { "name": "name", "passed": true, "message": "The name \"Borea Upload Probe\" follows the game's name rule." },
          { "name": "version", "passed": true, "message": "Written by game build 2026.10.25.5707, the lowest build a player needs." },
          { "name": "game file", "passed": true, "message": "vehicle.xml is well-formed XML." },
          { "name": "folder", "passed": true, "message": "No other vehicle installs as \"Borea Upload Probe\"." }
         ],
         "version": "1.0.0",
         "listing": "type = \"vehicle\"\n",
         "expires": "2026-11-02T20:59:02Z"
        }
        """;

    /// <summary>The live answer to a token that the host no longer takes.</summary>
    public const string Unauthorized = """{"error":"unauthorized","message":"Sign in first: the token is missing or has expired."}""";

    private const string FailedChecks = """[{"name":"archive","passed":true,"message":"A zip archive with safe paths."},{"name":"folder","passed":false,"message":"Another vehicle installs as \"Example Rover\"."}]""";

    private static readonly ShareCheck[] FailedCheckList =
    [
        new("archive", true, "A zip archive with safe paths."),
        new("folder", false, "Another vehicle installs as \"Example Rover\"."),
    ];

    /// <summary>Every row of the error table of the API page, with each of its statuses. The rows without extra members are the live shape.</summary>
    public static TheoryData<ErrorReply> Errors => new()
    {
        new(HttpStatusCode.BadRequest, """{"error":"invalid_request","message":"The archive is missing."}""", "invalid_request", "The archive is missing."),
        new(HttpStatusCode.UnprocessableEntity, """{"error":"invalid_request","message":"The version must be a SemVer version with three numbers, such as 1.0.0."}""", "invalid_request", "The version must be a SemVer version with three numbers, such as 1.0.0."),
        new(HttpStatusCode.UnprocessableEntity, """{"error":"invalid_request","message":"Two fields are wrong.","errors":{"version":"Three numbers, please.","license":["Not SPDX.","Too long."]}}""", "invalid_request", "Two fields are wrong.")
        {
            FieldErrors = new Dictionary<string, IReadOnlyList<string>> { ["version"] = ["Three numbers, please."], ["license"] = ["Not SPDX.", "Too long."] },
        },
        new(HttpStatusCode.UnsupportedMediaType, $$"""{"error":"type_not_accepted","message":"This host takes no saves.","checks":{{FailedChecks}}}""", "type_not_accepted", "This host takes no saves.") { Checks = FailedCheckList },
        new(HttpStatusCode.RequestEntityTooLarge, $$"""{"error":"too_large","message":"The archive is too large.","checks":{{FailedChecks}}}""", "too_large", "The archive is too large.") { Checks = FailedCheckList },
        new(HttpStatusCode.UnprocessableEntity, $$"""{"error":"invalid_archive","message":"A check failed.","checks":{{FailedChecks}}}""", "invalid_archive", "A check failed.") { Checks = FailedCheckList },
        new(HttpStatusCode.Conflict, """{"error":"id_taken","message":"The id ExampleRover is taken."}""", "id_taken", "The id ExampleRover is taken."),
        new(HttpStatusCode.Conflict, """{"error":"version_taken","message":"ExampleRover already had 1.0.0."}""", "version_taken", "ExampleRover already had 1.0.0."),
        new(HttpStatusCode.Forbidden, """{"error":"not_owner","message":"Your account does not own TheGuy."}""", "not_owner", "Your account does not own TheGuy."),
        new(HttpStatusCode.NotFound, """{"error":"not_owner","message":"No such listing."}""", "not_owner", "No such listing."),
        new(HttpStatusCode.TooManyRequests, """{"error":"rate_limited","message":"Too many new listings today.","retry_after":3600}""", "rate_limited", "Too many new listings today.") { RetryAfter = TimeSpan.FromHours(1) },
        new(HttpStatusCode.Gone, """{"error":"upload_expired","message":"The upload is past its time."}""", "upload_expired", "The upload is past its time."),
        new(HttpStatusCode.NotFound, """{"error":"upload_expired","message":"No such upload."}""", "upload_expired", "No such upload."),
    };

    /// <summary>A malformed extra member is dropped alone, and the message and the other members stay.</summary>
    public static TheoryData<ErrorReply> MalformedExtras => new()
    {
        new(HttpStatusCode.UnprocessableEntity, """{"error":"invalid_archive","message":"m","checks":"none","retry_after":"soon","errors":5}""", "invalid_archive", "m"),
        new(HttpStatusCode.UnprocessableEntity, $$"""{"error":"invalid_archive","message":"m","checks":{{FailedChecks}},"errors":[]}""", "invalid_archive", "m") { Checks = FailedCheckList },
        new(HttpStatusCode.UnprocessableEntity, """{"error":"invalid_archive","message":"m","checks":[null,{"passed":true},{"name":"folder","passed":false}]}""", "invalid_archive", "m") { Checks = [new ShareCheck("folder", false, null)] },
        new(HttpStatusCode.UnprocessableEntity, """{"error":"invalid_archive","message":"m","checks":[{"name":"folder","passed":"no"}]}""", "invalid_archive", "m"),
        new(HttpStatusCode.UnprocessableEntity, """{"error":"invalid_request","message":"m","errors":{"version":5,"license":["Not SPDX.",7],"tags":{}}}""", "invalid_request", "m")
        {
            FieldErrors = new Dictionary<string, IReadOnlyList<string>> { ["license"] = ["Not SPDX."] },
        },
        new(HttpStatusCode.TooManyRequests, """{"error":"rate_limited","message":"m","retry_after":-5}""", "rate_limited", "m"),
        new(HttpStatusCode.TooManyRequests, """{"error":"rate_limited","message":"m","retry_after":1e30}""", "rate_limited", "m"),
    };

    public sealed record ErrorReply(HttpStatusCode Status, string Body, string Code, string Message)
    {
        public IReadOnlyList<ShareCheck>? Checks { get; init; }

        public TimeSpan? RetryAfter { get; init; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>>? FieldErrors { get; init; }
    }
}
