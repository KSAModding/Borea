using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Storage.Launch;

/// <summary>
/// The process names of the mod loaders that are installed, taken from the launch file of each loader listing.
/// A loader such as StarMap or Oxidizer runs the game inside its own process, so that process name says the game runs.
/// The result is kept for a few seconds so that a polling caller does not read the settings and folders each time.
/// A loader whose listing is not found is looked up again at most once a minute, so a later index load still resolves it.
/// </summary>
public sealed class LoaderProcessNames
{
    private static readonly TimeSpan ResultLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MissRetry = TimeSpan.FromMinutes(1);

    private readonly IModRepository _listings;
    private readonly ILoaderVersionStore _versions;
    private readonly OsPlatform? _platform;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, string?> _byLoader = new(ModIds.Comparer);
    private readonly object _gate = new();
    private IReadOnlyList<string> _result = [];
    private DateTimeOffset _resultExpires = DateTimeOffset.MinValue;
    private DateTimeOffset _nextMissLookup = DateTimeOffset.MinValue;

    public LoaderProcessNames(IModRepository listings, ILoaderVersionStore versions, OsPlatform? platform = null, TimeProvider? time = null)
    {
        _listings = listings ?? throw new ArgumentNullException(nameof(listings));
        _versions = versions ?? throw new ArgumentNullException(nameof(versions));
        _platform = platform ?? SharedProfileLauncher.CurrentPlatform();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The process names of the installed loaders. It is empty when nothing can be read.</summary>
    public IReadOnlyList<string> Current()
    {
        lock (_gate)
        {
            if (_time.GetUtcNow() < _resultExpires)
                return _result;
        }

        IReadOnlyList<string> result;
        try
        {
            // The work runs on the pool so that a caller with a synchronization context cannot deadlock on it.
            result = Task.Run(() => CurrentAsync()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The guard that asks must keep working with the fixed names, whatever failed here.
            return [];
        }

        lock (_gate)
        {
            _result = result;
            _resultExpires = _time.GetUtcNow() + ResultLifetime;
        }

        return result;
    }

    private async Task<IReadOnlyList<string>> CurrentAsync()
    {
        var installed = (await _versions.ListAsync().ConfigureAwait(false))
            .Select(version => version.LoaderId)
            .Distinct(ModIds.Comparer)
            .ToList();

        List<string> missing;
        lock (_gate)
        {
            var retry = _time.GetUtcNow() >= _nextMissLookup;
            missing = installed.Where(id => !_byLoader.TryGetValue(id, out var name) || (name is null && retry)).ToList();
        }

        if (missing.Count > 0)
        {
            var listings = await _listings.GetAvailableModsAsync().ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var id in missing)
                    _byLoader[id] = null;

                foreach (var listing in listings)
                {
                    if (listing.Type == ContentType.ModLoader && missing.Contains(listing.ModId, ModIds.Comparer))
                        _byLoader[listing.ModId] = ProcessName(listing);
                }

                _nextMissLookup = _time.GetUtcNow() + MissRetry;
            }
        }

        lock (_gate)
        {
            return installed
                .Select(id => _byLoader.GetValueOrDefault(id))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private string? ProcessName(ModMetadata loader)
    {
        var entry = _platform is { } platform ? loader.Provides?.Platforms.GetValueOrDefault(platform) : null;

        // A file that a runtime starts, such as a dll, runs in the process of the runtime, which says nothing about the game.
        if (entry is { Runtime: not null })
            return null;

        var launch = entry?.Launch ?? loader.Provides?.Launch;
        var name = launch is null ? null : Path.GetFileNameWithoutExtension(launch);
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
