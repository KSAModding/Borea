using System.Globalization;
using Borea.Core.GitHub;
using Borea.Core.Listings;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

/// <summary>
/// IStewardRole from current_user_can_bypass of the active branch rulesets that hold ~DEFAULT_BRANCH, which GitHub fills only for the token's user.
/// Every such ruleset has to let the user bypass it.
/// </summary>
public sealed class GitHubStewardRole : IStewardRole, IDisposable
{
    internal const string DefaultBranch = "~DEFAULT_BRANCH";

    private static readonly string[] BypassModes = ["always", "pull_requests_only"];

    private readonly IGitHubSession _session;
    private readonly GitHubApi _api;
    private readonly object _gate = new();
    private int _generation;
    private StewardAccess? _current;
    private Task<StewardAccess?>? _running;
    private CancellationTokenSource? _stop;

    public GitHubStewardRole(IGitHubSession session, HttpClient http, TimeProvider? time = null)
        : this(session, new GitHubApi(session, http, time))
    {
    }

    internal GitHubStewardRole(IGitHubSession session, GitHubApi api)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _session.StateChanged += OnSessionChanged;
    }

    public StewardAccess? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public event EventHandler? Changed;

    public Task<StewardAccess?> CheckAsync(CancellationToken cancellationToken = default)
    {
        Task<StewardAccess?> running;
        lock (_gate)
        {
            if (_session.State is not { Status: GitHubSessionStatus.SignedIn, Login: { } login })
                return Task.FromResult<StewardAccess?>(null);

            if (_running is null)
            {
                var stop = _stop = new CancellationTokenSource();
                var generation = _generation;

                // Not run inside the lock, because a 401 signs the session out on the sending thread and so runs OnSessionChanged.
                _running = Task.Run(() => RunAsync(login, generation, stop), CancellationToken.None);
            }

            running = _running;
        }

        return running.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        _session.StateChanged -= OnSessionChanged;
        CancellationTokenSource? stop;
        lock (_gate)
            stop = Reset();

        Cancel(stop);
    }

    private async Task<StewardAccess?> RunAsync(string login, int generation, CancellationTokenSource stop)
    {
        StewardAccess? access = null;
        try
        {
            access = new StewardAccess(
                login,
                await CanBypassAsync(ListingPullRequestLinks.Repository, stop.Token).ConfigureAwait(false),
                await CanBypassAsync(StewardAccess.ReleasesRepository, stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // only a reset cancels, and it moves the generation on
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_stop, stop))
                    _stop = null;

                if (generation == _generation)
                {
                    _current = access;
                    _running = null;
                }
                else
                {
                    access = null;
                }
            }

            stop.Dispose();
        }

        if (access is not null)
            Changed?.Invoke(this, EventArgs.Empty);

        return access;
    }

    private async Task<bool> CanBypassAsync(string repository, CancellationToken cancellationToken)
    {
        var rulesets = $"{GitHubApi.Root}/repos/{repository}/rulesets";
        var found = false;
        try
        {
            await foreach (var listed in _api.GetPagesAsync<RulesetDto>(rulesets, cancellationToken).ConfigureAwait(false))
            {
                if (listed.Target != "branch")
                    continue;

                // Only a single ruleset carries its conditions and current_user_can_bypass.
                var ruleset = await _api.GetAsync<RulesetDto>($"{rulesets}/{listed.Id.ToString(CultureInfo.InvariantCulture)}", cancellationToken).ConfigureAwait(false);
                if (!IsActiveOnDefaultBranch(ruleset))
                    continue;

                if (!BypassModes.Contains(ruleset.CurrentUserCanBypass, StringComparer.Ordinal))
                    return false;

                found = true;
            }
        }
        catch (GitHubApiException)
        {
            return false;
        }

        return found;
    }

    private static bool IsActiveOnDefaultBranch(RulesetDto ruleset) =>
        ruleset.Enforcement == "active" && ruleset.Conditions?.RefName?.Include?.Contains(DefaultBranch, StringComparer.Ordinal) == true;

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        bool changed;
        CancellationTokenSource? stop;
        lock (_gate)
        {
            changed = _current is not null;
            stop = Reset();
        }

        Cancel(stop);
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the result and any running check. Returns the check to cancel once the lock is released.</summary>
    private CancellationTokenSource? Reset()
    {
        _generation++;
        _current = null;
        _running = null;
        var stop = _stop;
        _stop = null;
        return stop;
    }

    private static void Cancel(CancellationTokenSource? stop)
    {
        try
        {
            stop?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // the check ended in the meantime
        }
    }

    private sealed class RulesetDto
    {
        public long Id { get; set; }

        public string? Target { get; set; }

        public string? Enforcement { get; set; }

        public ConditionsDto? Conditions { get; set; }

        public string? CurrentUserCanBypass { get; set; }
    }

    private sealed class ConditionsDto
    {
        public RefNameDto? RefName { get; set; }
    }

    private sealed class RefNameDto
    {
        public List<string>? Include { get; set; }
    }
}
