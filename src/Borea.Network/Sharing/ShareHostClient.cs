using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Secrets;
using Borea.Core.Sharing;

namespace Borea.Network.Sharing;

/// <summary>
/// IShareHostClient over the upload API of RFC 0094. The RFC names the steps but no paths, client id or field names,
/// so they all sit in <see cref="ShareHostApi"/> and <see cref="ShareHostJson"/>.
/// </summary>
public sealed class ShareHostClient : IShareHostClient
{
    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private const int MaxReplyBytes = 1024 * 1024;

    private const int MaxFailedPolls = 3;

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ISecretStore? _secrets;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    /// <param name="secrets">Keeps a token between starts. Null keeps every token in memory only.</param>
    public ShareHostClient(HttpClient http, TimeProvider? time = null, ISecretStore? secrets = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time ?? TimeProvider.System;
        _secrets = secrets;
    }

    /// <summary>
    /// The secret of a host for its api. A successor operator keeps the key of the host but can have a new api, and the
    /// token of the old api must not go there.
    /// </summary>
    internal static string SecretName(ShareHost host) =>
        "share-host-" + host.Key + "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(host.Api.AbsoluteUri)))[..16];

    private static string TokenKey(ShareHost host) => host.Key + " " + host.Api.AbsoluteUri;

    public bool IsSignedIn(ShareHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_tokens)
            return _tokens.ContainsKey(TokenKey(host));
    }

    public async Task SignInAsync(ShareHost host, IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (IsSignedIn(host))
            return;

        if (keepSignedIn && await ResumeAsync(host, cancellationToken).ConfigureAwait(false))
            return;

        var device = await PostFormAsync(host, ShareHostApi.DeviceAuthorizationPath, [new("client_id", ShareHostApi.ClientId)], cancellationToken).ConfigureAwait(false)
            ?? throw Network();
        var code = Parse<ShareHostJson.DeviceCode>(device.Body);
        if (device.Status != HttpStatusCode.OK || code?.DeviceCodeValue is not { Length: > 0 } deviceCode || ToDeviceCode(host, code) is not { } shown)
            throw Unexpected(host, device);

        progress?.Report(shown);
        var interval = code.Interval > 0 ? TimeSpan.FromSeconds(code.Interval) : DefaultInterval;
        var expiresAt = _time.GetUtcNow() + (code.ExpiresIn > 0 ? TimeSpan.FromSeconds(code.ExpiresIn) : DefaultLifetime);
        KeyValuePair<string, string>[] poll = [new("grant_type", DeviceCodeGrantType), new("device_code", deviceCode), new("client_id", ShareHostApi.ClientId)];
        var failedPolls = 0;
        while (true)
        {
            await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
            if (_time.GetUtcNow() >= expiresAt)
                throw new ShareHostException(ShareHostFailure.SignInRefused, "The sign-in code expired.", "expired_token");

            var reply = await PostFormAsync(host, ShareHostApi.TokenPath, poll, cancellationToken).ConfigureAwait(false);
            var answer = reply is null ? null : Parse<ShareHostJson.Token>(reply.Body);
            if (reply?.Status == HttpStatusCode.OK && answer?.AccessToken is { Length: > 0 } token)
            {
                Enter(host, token, answer.RefreshToken, keepSignedIn);
                return;
            }

            // The player may still be typing the code, so a lost or garbled poll is tried again.
            if (answer?.Error is not { } error)
            {
                if (++failedPolls < MaxFailedPolls)
                    continue;

                throw reply is null ? Network() : Unexpected(host, reply);
            }

            failedPolls = 0;
            switch (error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval += SlowDownStep;
                    break;
                case "access_denied" or "expired_token":
                    throw new ShareHostException(ShareHostFailure.SignInRefused, $"The sign-in on {host.Name} ended with {error}.", error);
                default:
                    throw new ShareHostException(ShareHostFailure.Host, answer.ErrorDescription ?? $"{host.Name} refused the sign-in with {error}.", error);
            }
        }
    }

    public void SignOut(ShareHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_tokens)
            _tokens.Remove(TokenKey(host));
        Forget(host);
    }

    public async Task<ShareUpload> UploadAsync(ShareHost host, Stream archive, string fileName, IReadOnlyList<ListingDependency> dependencies, string? listingId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(dependencies);
        using var content = new MultipartFormDataContent();
        var file = new StreamContent(archive);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, ShareHostJson.Archive, fileName);
        content.Add(new StringContent(new JsonArray([.. dependencies.Select(DependencyJson)]).ToJsonString(), Encoding.UTF8, "application/json"), ShareHostJson.Dependencies);
        if (listingId is not null)
            content.Add(new StringContent(listingId, Encoding.UTF8), ShareHostJson.Listing);

        var reply = await SendAsync(host, HttpMethod.Post, ShareHostApi.UploadsPath, content, cancellationToken).ConfigureAwait(false);
        var upload = Parse<ShareHostJson.Upload>(reply.Body);
        if (upload?.Id is not { Length: > 0 } id)
            throw Unexpected(host, reply);

        var checks = upload.Checks?.Where(check => check.Name is { Length: > 0 }).Select(check => new ShareCheck(check.Name!, check.Passed, check.Message)).ToList() ?? [];
        return new ShareUpload(id, checks, upload.Version, upload.Listing);
    }

    public async Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, string? listingText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrEmpty(uploadId);
        var body = new JsonObject { [ShareHostJson.Version] = version, [ShareHostJson.Status] = status };
        if (listingText is not null)
            body[ShareHostJson.Listing] = listingText;

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var reply = await SendAsync(host, HttpMethod.Post, $"{ShareHostApi.UploadsPath}/{Uri.EscapeDataString(uploadId)}/{ShareHostApi.PublishPath}", content, cancellationToken).ConfigureAwait(false);
        var published = Parse<ShareHostJson.Published>(reply.Body);

        // Borea opens this page in the browser, and the host opens every listing pull request on GitHub.
        if (published?.Id is not { Length: > 0 } id
            || published.Version is not { Length: > 0 } released
            || !Uri.TryCreate(published.PullRequest, UriKind.Absolute, out var pullRequest)
            || pullRequest.Scheme != Uri.UriSchemeHttps
            || !string.Equals(pullRequest.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw Unexpected(host, reply);
        }

        return new SharePublished(id, released, pullRequest);
    }

    /// <summary>Signs in with the kept token. A refresh token that the host refuses is deleted.</summary>
    private async Task<bool> ResumeAsync(ShareHost host, CancellationToken cancellationToken)
    {
        if (_secrets is not { } secrets)
            return false;

        string? kept;
        try
        {
            kept = await Task.Run(() => secrets.Read(SecretName(host)), cancellationToken).ConfigureAwait(false);
        }
        catch (SecretStoreException)
        {
            return false;
        }

        if (kept is null)
            return false;

        if (kept.StartsWith(ShareHostApi.AccessPrefix, StringComparison.Ordinal))
        {
            Enter(host, kept[ShareHostApi.AccessPrefix.Length..], refreshToken: null, keep: false);
            return true;
        }

        if (!kept.StartsWith(ShareHostApi.RefreshPrefix, StringComparison.Ordinal))
        {
            Forget(host);
            return false;
        }

        KeyValuePair<string, string>[] refresh = [new("grant_type", "refresh_token"), new("refresh_token", kept[ShareHostApi.RefreshPrefix.Length..]), new("client_id", ShareHostApi.ClientId)];
        var reply = await PostFormAsync(host, ShareHostApi.TokenPath, refresh, cancellationToken).ConfigureAwait(false);
        var answer = reply is null ? null : Parse<ShareHostJson.Token>(reply.Body);
        if (reply?.Status == HttpStatusCode.OK && answer?.AccessToken is { Length: > 0 } token)
        {
            Enter(host, token, answer.RefreshToken, keep: true);
            return true;
        }

        // A busy or failing host keeps the token for the next start.
        if (answer?.Error is not null)
            Forget(host);
        return false;
    }

    private void Enter(ShareHost host, string token, string? refreshToken, bool keep)
    {
        lock (_tokens)
            _tokens[TokenKey(host)] = token;

        if (keep)
            Keep(host, refreshToken is { Length: > 0 } refresh ? ShareHostApi.RefreshPrefix + refresh : ShareHostApi.AccessPrefix + token);
    }

    private void Keep(ShareHost host, string secret) => RunSecret(secrets => secrets.Write(SecretName(host), secret));

    private void Forget(ShareHost host) => RunSecret(secrets => secrets.Delete(SecretName(host)));

    // A store that fails only costs the next start a new sign-in.
    private void RunSecret(Action<ISecretStore> work)
    {
        if (_secrets is not { } secrets)
            return;

        try
        {
            work(secrets);
        }
        catch (SecretStoreException)
        {
        }
    }

    private async Task<Reply> SendAsync(ShareHost host, HttpMethod method, string path, HttpContent content, CancellationToken cancellationToken)
    {
        string token;
        lock (_tokens)
        {
            if (!_tokens.TryGetValue(TokenKey(host), out var current))
                throw new ShareHostException(ShareHostFailure.SignedOut, $"Not signed in to {host.Name}.");
            token = current;
        }

        using var request = new HttpRequestMessage(method, Under(host, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var reply = await FetchAsync(request, cancellationToken).ConfigureAwait(false) ?? throw Network();
        if (reply.Status == HttpStatusCode.Unauthorized)
        {
            lock (_tokens)
            {
                if (_tokens.TryGetValue(TokenKey(host), out var current) && current == token)
                    _tokens.Remove(TokenKey(host));
            }

            Forget(host);
            throw new ShareHostException(ShareHostFailure.SignedOut, $"{host.Name} no longer takes the sign-in.");
        }

        if ((int)reply.Status is < 200 or > 299)
            throw Refused(host, reply);

        return reply;
    }

    private async Task<Reply?> PostFormAsync(ShareHost host, string path, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Under(host, path)) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await FetchAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A path below the api of the host, which is the only place its token goes.</summary>
    private static Uri Under(ShareHost host, string path)
    {
        var uri = new Uri(host.Api, path);
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.AbsoluteUri.StartsWith(host.Api.AbsoluteUri, StringComparison.Ordinal))
            throw new ArgumentException($"{uri} is not below the api of {host.Key}.", nameof(path));
        return uri;
    }

    /// <summary>The answer, or null when the host could not be reached.</summary>
    private async Task<Reply?> FetchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var text = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                text.Write(buffer, 0, read);
                if (text.Length > MaxReplyBytes)
                    return new Reply(response.StatusCode, string.Empty);
            }

            return new Reply(response.StatusCode, Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    // A verification page outside the host would send the player somewhere the host does not control.
    private static GitHubDeviceCode? ToDeviceCode(ShareHost host, ShareHostJson.DeviceCode code) =>
        !string.IsNullOrWhiteSpace(code.UserCode)
        && Uri.TryCreate(code.VerificationUri, UriKind.Absolute, out var page)
        && page.Scheme == Uri.UriSchemeHttps
        && string.Equals(page.Host, host.Api.Host, StringComparison.OrdinalIgnoreCase)
            ? new GitHubDeviceCode(code.UserCode, page.AbsoluteUri)
            : null;

    private static JsonObject DependencyJson(ListingDependency dependency)
    {
        if (dependency.Preserved is { } preserved)
            return (JsonObject)ToJson(preserved)!;

        var entry = new JsonObject { ["id"] = dependency.Id, ["kind"] = dependency.Kind };
        if (!string.IsNullOrEmpty(dependency.Min))
            entry["min"] = dependency.Min;
        if (!string.IsNullOrEmpty(dependency.Max))
            entry["max"] = dependency.Max;
        return entry;
    }

    private static JsonNode? ToJson(object? value) => value switch
    {
        AuthoredTable table => new JsonObject(table.Entries.Select(entry => KeyValuePair.Create(entry.Key, ToJson(entry.Value)))),
        IReadOnlyList<object> list => new JsonArray([.. list.Select(ToJson)]),
        string text => JsonValue.Create(text),
        long number => JsonValue.Create(number),
        double number => JsonValue.Create(number),
        bool flag => JsonValue.Create(flag),
        null => null,
        _ => JsonValue.Create(value.ToString()),
    };

    private static ShareHostException Refused(ShareHost host, Reply reply) =>
        Parse<ShareHostJson.Error>(reply.Body) is { Message.Length: > 0 } error
            ? new ShareHostException(ShareHostFailure.Host, error.Message, error.Code)
            : Unexpected(host, reply);

    private static ShareHostException Unexpected(ShareHost host, Reply reply) =>
        new(ShareHostFailure.UnexpectedResponse, $"{host.Name} answered with {(int)reply.Status} and no answer of the upload API.");

    private static ShareHostException Network() => new(ShareHostFailure.NetworkError, "The share host could not be reached.");

    private static T? Parse<T>(string body)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Reply(HttpStatusCode Status, string Body);
}
