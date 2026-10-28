using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Borea.Core.GitHub;
using Borea.Core.Secrets;

namespace Borea.Network.GitHub;

/// <summary>
/// IGitHubSession through the device flow of <see cref="BoreaGitHubApp"/>,
/// which needs no client secret and no server, not even to refresh a user token.
/// Every call to the secret store runs on the thread pool, one after another in the order of the changes that caused them.
/// </summary>
public sealed class GitHubSession : IGitHubSession
{
    internal const string DeviceCodeUrl = "https://github.com/login/device/code";

    internal const string AccessTokenUrl = "https://github.com/login/oauth/access_token";

    internal const string UserUrl = "https://api.github.com/user";

    internal const string ManageAccessPage = "https://github.com/settings/apps/authorizations";

    private const string ApiHost = "api.github.com";

    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private const string RefreshTokenGrantType = "refresh_token";

    internal const string RefreshTokenSecret = "github-refresh-token";

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);

    private const int MaxFailedPolls = 3;

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _slug;
    private readonly TimeProvider _time;
    private readonly ISecretStore? _secrets;
    private readonly object _gate = new();
    private GitHubSessionState _state = GitHubSessionState.SignedOut;
    private string? _token;
    private string? _refreshToken;
    private long? _userId;
    private CancellationTokenSource? _signIn;
    private bool? _keepSignedIn;
    private SecretStoreProblem? _secretProblem;
    private Task _secretWork = Task.CompletedTask;

    /// <param name="secrets">Keeps the refresh token between starts. Null keeps every session in memory only.</param>
    public GitHubSession(HttpClient httpClient, string clientId, string slug, TimeProvider? time = null, ISecretStore? secrets = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _slug = slug ?? throw new ArgumentNullException(nameof(slug));
        _time = time ?? TimeProvider.System;
        _secrets = secrets;
        _secretProblem = secrets is null ? SecretStoreProblem.Unsupported : null;
    }

    public bool IsAvailable => _clientId.Length > 0 && _slug.Length > 0;

    public string ManageAccessUrl => ManageAccessPage;

    /// <summary>
    /// The install page of the App. With the account of the signed-in user as the suggested target,
    /// because GitHub otherwise opens the installation of an organization that the user administers.
    /// </summary>
    public string InstallUrl => InstallPage(null);

    public string InstallUrlFor(long repositoryId) => InstallPage(repositoryId);

    private string InstallPage(long? repositoryId)
    {
        var page = "https://github.com/apps/" + Uri.EscapeDataString(_slug) + "/installations/new";
        long? user;
        lock (_gate)
            user = _userId;
        if (user is null)
            return page;

        var suggested = $"{page}/permissions?suggested_target_id={user}";
        return repositoryId is null ? suggested : $"{suggested}&repository_ids[]={repositoryId}";
    }

    public GitHubSessionState State
    {
        get
        {
            lock (_gate)
                return _state;
        }
    }

    public event EventHandler? StateChanged;

    public event EventHandler? KeepSignedInProblemChanged;

    public bool KeepSignedIn
    {
        get
        {
            lock (_gate)
                return _keepSignedIn == true;
        }
        set
        {
            lock (_gate)
            {
                if (_keepSignedIn == value)
                    return;

                _keepSignedIn = value;
                if (!value)
                    QueueSecret(secrets => secrets.Delete(RefreshTokenSecret));
                else if (_state.Status == GitHubSessionStatus.SignedIn)
                    KeepRefreshToken(_refreshToken);
            }
        }
    }

    public SecretStoreProblem? KeepSignedInProblem
    {
        get
        {
            lock (_gate)
                return _secretProblem;
        }
    }

    /// <summary>Completes when every secret store call queued so far has run.</summary>
    internal Task WhenSecretsStoredAsync()
    {
        lock (_gate)
            return _secretWork;
    }

    public async Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = true, CancellationToken cancellationToken = default)
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
            return await RunSignInAsync(run, progress, keepSignedIn).ConfigureAwait(false);
        }
        finally
        {
            EndSignIn(run);
            run.Dispose();
        }
    }

    public async Task<GitHubResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            if (!IsAvailable || _keepSignedIn != true || _secrets is null || _signIn is not null || _state.Status != GitHubSessionStatus.SignedOut)
            {
                run.Dispose();
                return GitHubResumeOutcome.NothingKept;
            }

            _signIn = run;
        }

        try
        {
            if (await ReadSecretAsync().ConfigureAwait(false) is not { Length: > 0 } kept)
                return GitHubResumeOutcome.NothingKept;

            lock (_gate)
            {
                ThrowIfStopped(run);
                _state = GitHubSessionState.Resuming;
            }

            OnStateChanged();
            return await RefreshAsync(run, kept).ConfigureAwait(false);
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
            ForgetSession();

            // also while staying signed in is off, because turning it off may have failed to delete
            QueueSecret(secrets => secrets.Delete(RefreshTokenSecret));
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

    private async Task<GitHubSignInResult> RunSignInAsync(CancellationTokenSource run, IProgress<GitHubDeviceCode>? progress, bool keepSignedIn)
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
                return await CompleteAsync(run, token, keepSignedIn ? answer.RefreshToken : null).ConfigureAwait(false);

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

    private async Task<GitHubSignInResult> CompleteAsync(CancellationTokenSource run, string token, string? refreshToken)
    {
        var (user, reply) = await FetchUserAsync(token, run.Token).ConfigureAwait(false);
        if (reply is null)
            return new(GitHubSignInOutcome.NetworkError);

        if (user is null)
            return new(GitHubSignInOutcome.UnexpectedResponse);

        EnterSignedIn(run, token, refreshToken, user, keep: true);
        return new(GitHubSignInOutcome.SignedIn, user.Login);
    }

    /// <summary>
    /// Trades the kept refresh token for a new access token. GitHub spends the old refresh token and answers with a new one,
    /// so the new one is kept at once, even when the account cannot be fetched afterwards.
    /// </summary>
    private async Task<GitHubResumeOutcome> RefreshAsync(CancellationTokenSource run, string kept)
    {
        KeyValuePair<string, string>[] refresh =
        [
            new("client_id", _clientId),
            new("grant_type", RefreshTokenGrantType),
            new("refresh_token", kept),
        ];
        var reply = await PostFormAsync(AccessTokenUrl, refresh, run.Token).ConfigureAwait(false);

        // GitHub refuses a refresh token with status 200, so an error from a busy or failing server does not delete it.
        if (reply is null || reply.Status == HttpStatusCode.TooManyRequests || (int)reply.Status >= 500)
            return GitHubResumeOutcome.Unreachable;

        var answer = Parse<AccessTokenDto>(reply.Body);
        if (answer?.AccessToken is not { Length: > 0 } token)
            return answer?.Error is null ? GitHubResumeOutcome.Unreachable : ForgetKept(run);

        lock (_gate)
        {
            ThrowIfStopped(run);
            KeepRefreshToken(answer.RefreshToken);
        }

        var (user, userReply) = await FetchUserAsync(token, run.Token).ConfigureAwait(false);
        if (user is null)
            return userReply?.Status == HttpStatusCode.Unauthorized ? ForgetKept(run) : GitHubResumeOutcome.Unreachable;

        EnterSignedIn(run, token, answer.RefreshToken, user, keep: false);
        return GitHubResumeOutcome.SignedIn;
    }

    /// <summary>Deletes the kept token, unless a sign-out ended <paramref name="run"/> and so a newer session may own it.</summary>
    private GitHubResumeOutcome ForgetKept(CancellationTokenSource run)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_signIn, run))
                QueueSecret(secrets => secrets.Delete(RefreshTokenSecret));
        }

        return GitHubResumeOutcome.Refused;
    }

    /// <summary>The account of <paramref name="token"/>, or null with the reply that GitHub gave instead, which is null when GitHub could not be reached.</summary>
    private async Task<(UserDto? User, Reply? Reply)> FetchUserAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserUrl);
        AddApiHeaders(request, token);
        var reply = await FetchAsync(request, cancellationToken).ConfigureAwait(false);
        var user = reply?.Status == HttpStatusCode.OK ? Parse<UserDto>(reply.Body) : null;
        return (user is { Login.Length: > 0 } ? user : null, reply);
    }

    private void EnterSignedIn(CancellationTokenSource run, string token, string? refreshToken, UserDto user, bool keep)
    {
        lock (_gate)
        {
            ThrowIfStopped(run);
            _token = token;
            _refreshToken = refreshToken;
            _userId = user.Id;
            _state = GitHubSessionState.SignedInAs(user.Login!);
            if (keep)
                KeepRefreshToken(refreshToken);
        }

        OnStateChanged();
    }

    /// <summary>Called under the gate.</summary>
    private void ForgetSession()
    {
        _token = null;
        _refreshToken = null;
        _userId = null;
        _state = GitHubSessionState.SignedOut;
    }

    /// <summary>
    /// Replaces the kept token with <paramref name="refreshToken"/> while staying signed in is on. Called under the gate.
    /// Without a refresh token, as for an App whose user tokens do not expire or for a sign-in that is not kept, the kept token of an earlier session is deleted.
    /// </summary>
    private void KeepRefreshToken(string? refreshToken)
    {
        if (_keepSignedIn != true)
            return;

        if (refreshToken is { Length: > 0 })
            QueueSecret(secrets => secrets.Write(RefreshTokenSecret, refreshToken), write: true);
        else
            QueueSecret(secrets => secrets.Delete(RefreshTokenSecret));
    }

    /// <summary>Called under the gate, so the store sees the writes and deletes in the order of the changes that caused them.</summary>
    private void QueueSecret(Action<ISecretStore> work, bool write = false)
    {
        if (_secrets is not { } secrets)
            return;

        _secretWork = _secretWork.ContinueWith(_ => RunSecret(secrets, work, write), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Once a call failed, nothing more is written, but a delete is still tried, so that a sign-out never leaves a token behind.</summary>
    private void RunSecret(ISecretStore secrets, Action<ISecretStore> work, bool write)
    {
        lock (_gate)
        {
            if (write && _secretProblem is not null)
                return;
        }

        try
        {
            work(secrets);
        }
        catch (SecretStoreException exception)
        {
            RecordProblem(exception.Problem);
        }
    }

    private Task<string?> ReadSecretAsync()
    {
        lock (_gate)
        {
            var secrets = _secrets!;
            var read = _secretWork.ContinueWith(
                _ =>
                {
                    try
                    {
                        return secrets.Read(RefreshTokenSecret);
                    }
                    catch (SecretStoreException exception)
                    {
                        RecordProblem(exception.Problem);
                        return null;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            _secretWork = read;
            return read;
        }
    }

    private void RecordProblem(SecretStoreProblem problem)
    {
        lock (_gate)
        {
            if (_secretProblem is not null)
                return;

            _secretProblem = problem;
        }

        KeepSignedInProblemChanged?.Invoke(this, EventArgs.Empty);
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
            if (_state.Status is not (GitHubSessionStatus.WaitingForCode or GitHubSessionStatus.Resuming))
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

            // The kept refresh token stays, because an access token that expired after its 8 hours is refused the same way.
            ForgetSession();
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

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class UserDto
    {
        [JsonPropertyName("login")]
        public string? Login { get; set; }

        [JsonPropertyName("id")]
        public long? Id { get; set; }
    }
}
