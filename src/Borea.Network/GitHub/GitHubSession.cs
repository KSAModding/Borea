using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.GitHub;

namespace Borea.Network.GitHub;

/// <summary>
/// IGitHubSession through the device flow of <see cref="BoreaGitHubApp"/>,
/// which needs no client secret and no server.
/// </summary>
public sealed class GitHubSession : IGitHubSession
{
    internal const string DeviceCodeUrl = "https://github.com/login/device/code";

    internal const string AccessTokenUrl = "https://github.com/login/oauth/access_token";

    internal const string UserUrl = "https://api.github.com/user";

    internal const string ManageAccessPage = "https://github.com/settings/apps/authorizations";

    private const string ApiHost = "api.github.com";

    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    private const int MaxFailedPolls = 3;

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _slug;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private GitHubSessionState _state = GitHubSessionState.SignedOut;
    private string? _token;
    private CancellationTokenSource? _signIn;

    public GitHubSession(HttpClient httpClient, string clientId, string slug, TimeProvider? time = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _slug = slug ?? throw new ArgumentNullException(nameof(slug));
        _time = time ?? TimeProvider.System;
    }

    public bool IsAvailable => _clientId.Length > 0 && _slug.Length > 0;

    public string ManageAccessUrl => ManageAccessPage;

    public string InstallUrl => "https://github.com/apps/" + Uri.EscapeDataString(_slug) + "/installations/new";

    public GitHubSessionState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public event EventHandler? StateChanged;

    public async Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("This build of Borea has no GitHub App to sign in with.");

        var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            var refusal = _signIn is not null ? "A GitHub sign-in is already running."
                : _state.Status == GitHubSessionStatus.SignedIn ? "Already signed in to GitHub."
                : null;
            if (refusal is not null)
            {
                run.Dispose();
                throw new InvalidOperationException(refusal);
            }

            _signIn = run;
        }

        try
        {
            return await RunSignInAsync(run, progress).ConfigureAwait(false);
        }
        finally
        {
            EndSignIn(run);
            run.Dispose();
        }
    }

    public void SignOut()
    {
        CancellationTokenSource? running;
        bool changed;
        lock (_gate)
        {
            running = _signIn;
            _signIn = null;
            changed = _state.Status != GitHubSessionStatus.SignedOut;
            _token = null;
            _state = GitHubSessionState.SignedOut;
        }

        try
        {
            running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the sign-in ended in the meantime
        }

        if (changed)
            OnStateChanged();
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsApiUri(request.RequestUri))
            throw new ArgumentException("The GitHub token goes only to https://api.github.com.", nameof(request));

        string token;
        lock (_gate)
            token = _token ?? throw new InvalidOperationException("Not signed in to GitHub.");

        AddApiHeaders(request, token);
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // A followed redirect drops the Authorization header, so only a 401 to the token itself counts.
        if (response.StatusCode == HttpStatusCode.Unauthorized && request.Headers.Authorization is not null)
            SignOutRefused(token);

        return response;
    }

    private async Task<GitHubSignInResult> RunSignInAsync(CancellationTokenSource run, IProgress<GitHubDeviceCode>? progress)
    {
        var cancellationToken = run.Token;
        var deviceReply = await PostFormAsync(DeviceCodeUrl, [new("client_id", _clientId)], cancellationToken).ConfigureAwait(false);
        if (deviceReply is null)
            return new(GitHubSignInOutcome.NetworkError);

        var device = Parse<DeviceCodeDto>(deviceReply.Body);
        if (device?.Error is { } deviceError)
            return new(OutcomeOf(deviceError));

        if (device is null || string.IsNullOrEmpty(device.DeviceCode) || ToDeviceCode(device) is not { } code)
            return new(GitHubSignInOutcome.UnexpectedResponse);

        EnterWaiting(run, code);
        progress?.Report(code);

        var interval = device.Interval > 0 ? TimeSpan.FromSeconds(device.Interval) : DefaultInterval;
        var expiresAt = _time.GetUtcNow() + (device.ExpiresIn > 0 ? TimeSpan.FromSeconds(device.ExpiresIn) : DefaultLifetime);
        KeyValuePair<string, string>[] poll =
        [
            new("client_id", _clientId),
            new("device_code", device.DeviceCode),
            new("grant_type", DeviceCodeGrantType),
        ];

        var failedPolls = 0;
        while (true)
        {
            await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
            if (_time.GetUtcNow() >= expiresAt)
                return new(GitHubSignInOutcome.Expired);

            var reply = await PostFormAsync(AccessTokenUrl, poll, cancellationToken).ConfigureAwait(false);
            var answer = reply is null ? null : Parse<AccessTokenDto>(reply.Body);
            if (answer?.AccessToken is { Length: > 0 } token)
                return await CompleteAsync(run, token).ConfigureAwait(false);

            // The user may still be typing the code, so a lost or garbled poll is tried again.
            if (answer?.Error is null)
            {
                if (++failedPolls < MaxFailedPolls)
                    continue;

                return new(reply is null ? GitHubSignInOutcome.NetworkError : GitHubSignInOutcome.UnexpectedResponse);
            }

            failedPolls = 0;
            switch (answer.Error)
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval += SlowDownStep;
                    if (TimeSpan.FromSeconds(answer.Interval) > interval)
                        interval = TimeSpan.FromSeconds(answer.Interval);
                    break;
                default:
                    return new(OutcomeOf(answer.Error));
            }
        }
    }

    private async Task<GitHubSignInResult> CompleteAsync(CancellationTokenSource run, string token)
    {
        var cancellationToken = run.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, UserUrl);
        AddApiHeaders(request, token);
        var reply = await FetchAsync(request, cancellationToken).ConfigureAwait(false);
        if (reply is null)
            return new(GitHubSignInOutcome.NetworkError);

        if (reply.Status != HttpStatusCode.OK)
            return new(GitHubSignInOutcome.UnexpectedResponse);

        if (Parse<UserDto>(reply.Body)?.Login is not { Length: > 0 } login)
            return new(GitHubSignInOutcome.UnexpectedResponse);

        lock (_gate)
        {
            ThrowIfStopped(run);
            _token = token;
            _state = GitHubSessionState.SignedInAs(login);
        }

        OnStateChanged();
        return new(GitHubSignInOutcome.SignedIn, login);
    }

    private void EnterWaiting(CancellationTokenSource run, GitHubDeviceCode code)
    {
        lock (_gate)
        {
            ThrowIfStopped(run);
            _state = GitHubSessionState.WaitingFor(code);
        }

        OnStateChanged();
    }

    private void EndSignIn(CancellationTokenSource run)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_signIn, run))
                return;

            _signIn = null;
            if (_state.Status != GitHubSessionStatus.WaitingForCode)
                return;

            _state = GitHubSessionState.SignedOut;
        }

        OnStateChanged();
    }

    // SignOut forgets the run before it cancels it, so a run that is no longer current stops here.
    private void ThrowIfStopped(CancellationTokenSource run)
    {
        if (!ReferenceEquals(_signIn, run))
            throw new OperationCanceledException(run.Token);

        run.Token.ThrowIfCancellationRequested();
    }

    private void SignOutRefused(string token)
    {
        lock (_gate)
        {
            if (!string.Equals(_token, token, StringComparison.Ordinal))
                return;

            _token = null;
            _state = GitHubSessionState.SignedOut;
        }

        OnStateChanged();
    }

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private async Task<Reply?> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await FetchAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The answer, or null when GitHub could not be reached.</summary>
    private async Task<Reply?> FetchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new Reply(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

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

    private static GitHubSignInOutcome OutcomeOf(string? error) => error switch
    {
        "expired_token" => GitHubSignInOutcome.Expired,
        "access_denied" => GitHubSignInOutcome.AccessDenied,
        "incorrect_client_credentials" => GitHubSignInOutcome.IncorrectClientCredentials,
        "incorrect_device_code" => GitHubSignInOutcome.IncorrectDeviceCode,
        "unsupported_grant_type" => GitHubSignInOutcome.UnsupportedGrantType,
        "device_flow_disabled" => GitHubSignInOutcome.DeviceFlowDisabled,
        _ => GitHubSignInOutcome.UnexpectedResponse,
    };

    private static GitHubDeviceCode? ToDeviceCode(DeviceCodeDto device)
    {
        // Borea opens this page in the browser, so it must be GitHub's own.
        if (string.IsNullOrWhiteSpace(device.UserCode)
            || !Uri.TryCreate(device.VerificationUri, UriKind.Absolute, out var page)
            || page.Scheme != Uri.UriSchemeHttps
            || !string.Equals(page.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new GitHubDeviceCode(device.UserCode, page.AbsoluteUri);
    }

    private static bool IsApiUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase);

    private static void AddApiHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (request.Headers.Accept.Count == 0)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!request.Headers.Contains("X-GitHub-Api-Version"))
            request.Headers.Add("X-GitHub-Api-Version", BoreaReleaseCheck.ApiVersion);
    }

    private sealed record Reply(HttpStatusCode Status, string Body);

    private sealed class DeviceCodeDto
    {
        [JsonPropertyName("device_code")]
        public string? DeviceCode { get; set; }

        [JsonPropertyName("user_code")]
        public string? UserCode { get; set; }

        [JsonPropertyName("verification_uri")]
        public string? VerificationUri { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class AccessTokenDto
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class UserDto
    {
        [JsonPropertyName("login")]
        public string? Login { get; set; }
    }
}
