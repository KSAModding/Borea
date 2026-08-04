using System.Net.Http.Json;
using System.Text.Json;
using Borea.Core.Game;

namespace Borea.Network.MasterServer;

/// <summary>
/// ILatestVersionPing implementation against the master server endpoint the game
/// itself polls. Successful answers are cached for a minute, matching
/// VersionInfo.AllowUpdateCheck, and overlapping calls share one request.
/// The cache is per instance, so callers should share one.
/// </summary>
public sealed class LatestVersionPing : ILatestVersionPing
{
    private const string VersionUrl = "http://ksa-master1.rocketwerkz.com:8082/version";
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    private LatestVersionInfo? _lastAnswer;
    private DateTimeOffset _lastAnswerAt;
    private Task<LatestVersionInfo>? _inFlight;

    public LatestVersionPing(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<LatestVersionInfo> PingAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_lastAnswer is not null && _timeProvider.GetUtcNow() - _lastAnswerAt < MinimumInterval)
                return Task.FromResult(_lastAnswer);

            // Never join a completed task; finished fetches are only served via the cache.
            if (_inFlight is null || _inFlight.IsCompleted)
                _inFlight = FetchAsync();

            // A caller's token cancels only their wait, not the shared request.
            return _inFlight.WaitAsync(cancellationToken);
        }
    }

    private async Task<LatestVersionInfo> FetchAsync()
    {
        // The reply shape is KSA.VersionMetaInfo: {"Version": "...", "Url": "..."}.
        VersionMetaInfoDto? dto;
        try
        {
            dto = await _httpClient.GetFromJsonAsync<VersionMetaInfoDto>(VersionUrl).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // A non-JSON body is an unusable answer, not a transport error, same as
            // in VersionInfo.GetServerVersionAsync.
            dto = null;
        }

        var raw = dto?.Version ?? string.Empty;
        var answer = new LatestVersionInfo(
            GameVersion.TryParse(raw, out var version) ? version : null,
            raw,
            dto?.Url ?? string.Empty);

        lock (_gate)
        {
            _lastAnswer = answer;
            _lastAnswerAt = _timeProvider.GetUtcNow();
        }
        return answer;
    }
}

internal sealed class VersionMetaInfoDto
{
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
