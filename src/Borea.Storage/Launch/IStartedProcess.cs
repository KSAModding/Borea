namespace Borea.Storage.Launch;

/// <summary>
/// A process a starter started. Disposing releases the handle, the process
/// keeps running.
/// </summary>
public interface IStartedProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    /// <summary>The exit code once the process has exited, otherwise null.</summary>
    int? ExitCode { get; }

    /// <summary>The last lines the process wrote to its output and error streams, oldest first.</summary>
    IReadOnlyList<string> RecentOutput { get; }

    /// <summary>Waits up to <paramref name="timeout"/> for the process to exit. True when it has.</summary>
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
