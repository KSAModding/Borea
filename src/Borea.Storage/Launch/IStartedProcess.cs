namespace Borea.Storage.Launch;

/// <summary>
/// A process a starter started. Disposing releases the handle, the process
/// keeps running.
/// </summary>
public interface IStartedProcess : IDisposable
{
    /// <summary>The id of the process, which stays readable after the handle is released.</summary>
    int Id { get; }

    bool HasExited { get; }

    /// <summary>
    /// Whether the process has exited and no process it started still holds its
    /// output and error streams. A loader that restarts itself starts its new
    /// process with those streams, so this stays false while the restart runs.
    /// The default is for a process whose streams no other process can hold.
    /// </summary>
    bool HasEnded => HasExited;

    /// <summary>
    /// When the process started, which tells it apart from a later process that got the same id. Null when it is not
    /// known, and then no later Borea can find the process again.
    /// </summary>
    DateTime? StartedAt => null;

    /// <summary>The exit code once the process has exited, otherwise null.</summary>
    int? ExitCode { get; }

    /// <summary>The last lines the process wrote to its output and error streams, oldest first.</summary>
    IReadOnlyList<string> RecentOutput { get; }

    /// <summary>Waits up to <paramref name="timeout"/> for the process to end, see <see cref="HasEnded"/>. True when it has.</summary>
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
