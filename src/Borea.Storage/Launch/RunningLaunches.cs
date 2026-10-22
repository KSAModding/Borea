using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Storage.Launch;

/// <summary>
/// The launches that still run, shared by every <see cref="LoaderLauncher"/>
/// and <see cref="SharedProfileLauncher"/> built with this object, so a
/// launcher built after a settings change still knows the games an earlier one
/// started. Every member is used under <see cref="Gate"/>.
/// </summary>
public sealed class RunningLaunches
{
    internal object Gate { get; } = new();

    /// <summary>
    /// The processes by instance. A launch without a loader through a Wine
    /// wrapper has a key of its own that no instance has.
    /// </summary>
    internal Dictionary<Guid, IStartedProcess> Processes { get; } = new();

    internal Dictionary<Guid, (DateTime? GameLogAtLaunch, DateTime? CrashLogAtLaunch, string LoaderName, string LoaderReleases)> Starts { get; } = new();

    /// <summary>
    /// The last launch per instance that ended, until the next launch of the
    /// instance, so an exit watch that starts after the end still finds it.
    /// </summary>
    internal Dictionary<Guid, (IStartedProcess Process, DateTime? CrashLogAtLaunch)> Ended { get; } = new();

    /// <summary>The Wine wrapper install each launch through a wrapper started, by the key of its process.</summary>
    internal Dictionary<Guid, WineInstall> Wrappers { get; } = new();

    /// <summary>
    /// The game path that the last launch per instance wrote for its kept
    /// build, with the loader that reads it, until the next launch of the
    /// instance, so the exit watch can write the game folder of the settings back.
    /// </summary>
    internal Dictionary<Guid, (int ProcessId, ModMetadata Loader, string LoaderDirectory, string GamePath)> KeptBuilds { get; } = new();

    /// <summary>
    /// Whether a launch that still runs went through the wrapper app at
    /// <paramref name="bundlePath"/>. The paths are compared without case,
    /// because the file system of macOS ignores case by default.
    /// </summary>
    internal bool RunsThrough(string bundlePath) =>
        Wrappers.Values.Any(running => string.Equals(running.Wrapper!.BundlePath, bundlePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Drops the launches that ended, or every launch, and releases their handles.
    /// A loader launch that ended stays in <see cref="Ended"/> for its exit watch.
    /// </summary>
    /// <param name="hasEnded">Whether a launch has ended. Null counts a launch whose process exited.</param>
    internal void Forget(bool ended, Func<IStartedProcess, bool>? hasEnded = null)
    {
        if (!ended)
        {
            Ended.Clear();
            KeptBuilds.Clear();
        }

        foreach (var (key, process) in Processes.ToArray())
        {
            if (ended && !(hasEnded?.Invoke(process) ?? process.HasExited))
                continue;

            if (ended && Starts.TryGetValue(key, out var start))
                Ended[key] = (process, start.CrashLogAtLaunch);
            Processes.Remove(key);
            Starts.Remove(key);
            Wrappers.Remove(key);
            process.Dispose();
        }
    }
}
