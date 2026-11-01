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
/// IShareHostClient over the upload API of RFC 0094. Its paths, client id and field names come from rules 15 and 16 of
/// spec/share-host.md and sit in <see cref="ShareHostApi"/> and <see cref="ShareHostJson"/>.
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
    private readonly IListingFormat _format;
    private readonly TimeProvider _time;
    private readonly ISecretStore? _secrets;
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    /// <param name="format">Writes the dependency list in the TOML of a listing, as the upload API takes it.</param>
    /// <param name="secrets">Keeps a token between starts. Null keeps every token in memory only.</param>
    public ShareHostClient(HttpClient http, IListingFormat format, TimeProvider? time = null, ISecretStore? secrets = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _format = format ?? throw new ArgumentNullException(nameof(format));
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
        if (device.Status != HttpStatusCode.OK && Parse<ShareHostJson.Token>(device.Body) is { Error: { } refused } oauth)
            throw SignInError(host, refused, oauth.ErrorDescription);

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
                    throw SignInError(host, error, answer.ErrorDescription);
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
        var list = new AuthoredTable();
        if (dependencies.Count > 0)
            list.Set("dependencies", dependencies.Select(ListingDraft.WriteDependency).Cast<object>().ToList());
        content.Add(new StringContent(_format.Write(list), Encoding.UTF8), ShareHostJson.Dependencies);
        if (listingId is not null)
            content.Add(new StringContent(listingId, Encoding.UTF8), ShareHostJson.Listing);

        var reply = await SendAsync(host, HttpMethod.Post, ShareHostApi.UploadsPath, content, cancellationToken).ConfigureAwait(false);
        var upload = Parse<ShareHostJson.Upload>(reply.Body);
        if (upload?.Id is not { Length: > 0 } id)
            throw Unexpected(host, reply);

        return new ShareUpload(id, ToChecks(upload.Checks ?? []), upload.Version, upload.Listing);
    }

    public async Task<SharePublished> PublishAsync(ShareHost host, string uploadId, string version, string status, ListingDraft? listing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrEmpty(uploadId);
        var body = new JsonObject { [ShareHostJson.Version] = version, [ShareHostJson.Status] = status };
        if (listing is not null)
        {
            body[ShareHostJson.Id] = listing.Id;
            body[ShareHostJson.Name] = listing.Name;
            body[ShareHostJson.Authors] = new JsonArray([.. listing.Authors.Select(author => JsonValue.Create(author))]);
            body[ShareHostJson.Abstract] = listing.Abstract;
            body[ShareHostJson.License] = listing.License;
            if (!string.IsNullOrEmpty(listing.Description))
                body[ShareHostJson.Description] = listing.Description;
            if (listing.Tags.Count > 0)
                body[ShareHostJson.Tags] = new JsonArray([.. listing.Tags.Select(tag => JsonValue.Create(tag))]);
        }

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var reply = await SendAsync(host, HttpMethod.Post, $"{ShareHostApi.UploadsPath}/{Uri.EscapeDataString(uploadId)}/{ShareHostApi.PublishPath}", content, cancellationToken).ConfigureAwait(false);
        var published = Parse<ShareHostJson.Published>(reply.Body) ?? new ShareHostJson.Published();

        // Borea opens this page in the browser, and the host opens every listing pull request on GitHub.
        var pullRequest = Https(ShareHostJson.Text(published.PullRequest)) is { } link && string.Equals(link.Host, "github.com", StringComparison.OrdinalIgnoreCase) ? link : null;
        return new SharePublished(
            ShareHostJson.Text(published.Id) is { Length: > 0 } id ? id : listing?.Id,
            ShareHostJson.Text(published.Version) is { Length: > 0 } released ? released : version,
            pullRequest,
            PageOnHost(host, ShareHostJson.Text(published.Page)));
    }

    /// <summary>
    /// The address when it is https on the host of the api, or on a sub-domain or a parent of it, because a host may serve its pages
    /// and its api from different names. A top-level name such as gg counts as no parent.
    /// </summary>
    internal static Uri? PageOnHost(ShareHost host, string? address)
    {
        if (Https(address) is not { } page)
            return null;

        var api = host.Api.Host;
        var site = page.Host;
        return string.Equals(site, api, StringComparison.OrdinalIgnoreCase)
            || site.EndsWith("." + api, StringComparison.OrdinalIgnoreCase)
            || (site.Contains('.') && api.EndsWith("." + site, StringComparison.OrdinalIgnoreCase))
                ? page
                : null;
    }

    private static Uri? Https(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    /// <summary>
    /// Signs in with the kept token. A refresh token is deleted only on invalid_grant, because a busy host also answers with
    /// an OAuth error, and the token still works after that.
    /// </summary>
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

        var keptRefresh = kept[ShareHostApi.RefreshPrefix.Length..];
        KeyValuePair<string, string>[] refresh = [new("grant_type", "refresh_token"), new("refresh_token", keptRefresh), new("client_id", ShareHostApi.ClientId)];
        var reply = await PostFormAsync(host, ShareHostApi.TokenPath, refresh, cancellationToken).ConfigureAwait(false);
        var answer = reply is null ? null : Parse<ShareHostJson.Token>(reply.Body);
        if (reply?.Status == HttpStatusCode.OK && answer?.AccessToken is { Length: > 0 } token)
        {
            Enter(host, token, answer.RefreshToken is { Length: > 0 } rotated ? rotated : keptRefresh, keep: true);
            return true;
        }

        if (answer?.Error == "invalid_grant")
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

    private void ForgetKeptAccessToken(ShareHost host) => RunSecret(secrets =>
    {
        if (secrets.Read(SecretName(host))?.StartsWith(ShareHostApi.AccessPrefix, StringComparison.Ordinal) == true)
            secrets.Delete(SecretName(host));
    });

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

            // A kept refresh token can outlive the access token, so the next sign-in tries it before it asks for a code.
            ForgetKeptAccessToken(host);
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
        !string.IsNullOrWhiteSpace(code.UserCode) && OnApiHost(host, code.VerificationUri) is { } page
            ? new GitHubDeviceCode(code.UserCode, page.AbsoluteUri, OnApiHost(host, ShareHostJson.Text(code.VerificationUriComplete))?.AbsoluteUri)
            : null;

    private static Uri? OnApiHost(ShareHost host, string? address) =>
        Https(address) is { } page && string.Equals(page.Host, host.Api.Host, StringComparison.OrdinalIgnoreCase) ? page : null;

    private static ShareHostException Refused(ShareHost host, Reply reply) =>
        Parse<ShareHostJson.Error>(reply.Body) is { Message.Length: > 0 } error
            ? new ShareHostException(ShareHostFailure.Host, error.Message, error.Code)
            {
                Checks = ReadChecks(error.Checks),
                RetryAfter = error.RetryAfter.ValueKind == JsonValueKind.Number && error.RetryAfter.TryGetInt32(out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null,
                FieldErrors = ReadFieldErrors(error.FieldErrors),
                IsServerError = (int)reply.Status >= 500,
            }
            : Unexpected(host, reply);

    private static IReadOnlyList<ShareCheck>? ReadChecks(JsonElement checks) =>
        checks.ValueKind == JsonValueKind.Array && Parse<List<ShareHostJson.Check>>(checks.GetRawText()) is { } list ? ToChecks(list) : null;

    // A JSON null in the list reads as a null entry, which OfType leaves out.
    private static List<ShareCheck> ToChecks(List<ShareHostJson.Check> checks) =>
        [.. checks.OfType<ShareHostJson.Check>().Where(check => check.Name is { Length: > 0 }).Select(check => new ShareCheck(check.Name!, check.Passed, check.Message))];

    /// <summary>Takes a string or a list of strings for each field and skips anything else, because the API names no exact shape.</summary>
    private static Dictionary<string, IReadOnlyList<string>>? ReadFieldErrors(JsonElement errors)
    {
        if (errors.ValueKind != JsonValueKind.Object)
            return null;

        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var field in errors.EnumerateObject())
        {
            List<string> lines = field.Value.ValueKind switch
            {
                JsonValueKind.String => [field.Value.GetString()!],
                JsonValueKind.Array => [.. field.Value.EnumerateArray().Where(line => line.ValueKind == JsonValueKind.String).Select(line => line.GetString()!)],
                _ => [],
            };
            if (lines.Count > 0)
                fields[field.Name] = lines;
        }

        return fields;
    }

    private static ShareHostException SignInError(ShareHost host, string error, string? description) =>
        new(ShareHostFailure.Host, description ?? $"{host.Name} refused the sign-in with {error}.", error);

    private static ShareHostException Unexpected(ShareHost host, Reply reply) =>
        new((int)reply.Status >= 500 ? ShareHostFailure.Unavailable : ShareHostFailure.UnexpectedResponse, $"{host.Name} answered with {(int)reply.Status} and no answer of the upload API.");

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
