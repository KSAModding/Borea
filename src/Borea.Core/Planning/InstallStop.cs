using Borea.Core.Mods;

namespace Borea.Core.Planning;

/// <summary>
/// Stops an install, an update or a pack install at a safe point. Planning and
/// a download stop at once, an operation past its download finishes, because a
/// half-extracted mod is worse than a finished one, and no operation starts
/// after the request. A download can also pause and resume.
/// </summary>
public sealed class InstallStop
{
    private readonly CancellationTokenSource _requested = new();
    private DownloadWatch? _running;

    public bool IsRequested => _requested.IsCancellationRequested;

    /// <summary>Canceled by the request. Only work that writes nothing, such as planning, may use it.</summary>
    public CancellationToken Token => _requested.Token;

    /// <summary>
    /// Returns at once. The download stops on a thread pool thread, so a UI
    /// thread that asks for the stop does not run the cleanup of the download.
    /// </summary>
    public void Request() => _ = _requested.CancelAsync();

    /// <summary>
    /// Pauses the download of the running operation and keeps what it received.
    /// Returns false when no operation downloads now.
    /// </summary>
    public bool Pause() => Volatile.Read(ref _running)?.Pause() == true;

    public void Resume() => Volatile.Read(ref _running)?.Resume();

    internal async Task<T> RunCoreAsync<T>(
        Func<IProgress<InstallProgress>?, CancellationToken, Task<T>> operation,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRequested)
            throw new InstallStoppedException(0, 1);

        using var download = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watch = new DownloadWatch(download, progress);
        Volatile.Write(ref _running, watch);
        DownloadPause.Current = watch.Signal;
        try
        {
            using (_requested.Token.Register(watch.Stop))
                return await operation(watch, download.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (watch.IsStopped && !cancellationToken.IsCancellationRequested)
        {
            throw new InstallStoppedException(0, 1, exception);
        }
        finally
        {
            Interlocked.CompareExchange(ref _running, null, watch);
        }
    }

    /// <summary>
    /// Follows one operation. Its download can pause and stop until the first
    /// report of a later phase, which also ends a pause that came too late.
    /// </summary>
    private sealed class DownloadWatch(CancellationTokenSource download, IProgress<InstallProgress>? inner) : IProgress<InstallProgress>
    {
        private const int Downloading = 0;
        private const int Paused = 1;
        private const int Downloaded = 2;
        private const int Stopped = 3;

        private readonly Lock _gate = new();
        private int _state;

        public DownloadPause Signal { get; } = new();

        public bool IsStopped => Volatile.Read(ref _state) == Stopped;

        public bool IsPaused => Volatile.Read(ref _state) == Paused;

        public void Stop()
        {
            if (Move(from: Downloading, to: Stopped) || Move(from: Paused, to: Stopped))
                download.Cancel();
        }

        public bool Pause()
        {
            Move(from: Downloading, to: Paused);
            return IsPaused;
        }

        public void Resume() => Move(from: Paused, to: Downloading);

        public void Report(InstallProgress value)
        {
            if (value.Phase != InstallPhase.Downloading)
            {
                Move(from: Downloading, to: Downloaded);
                Move(from: Paused, to: Downloaded);
            }

            inner?.Report(value);
        }

        private bool Move(int from, int to)
        {
            lock (_gate)
            {
                if (_state != from)
                    return false;

                Volatile.Write(ref _state, to);
                if (to == Paused)
                    Signal.Pause();
                else if (from == Paused && to != Stopped)
                    Signal.Resume();

                return true;
            }
        }
    }
}

public static class InstallStopExtensions
{
    /// <summary>
    /// Runs one operation under the stop rule, or as it is without a stop. The
    /// first report of the operation after <see cref="InstallPhase.Downloading"/>
    /// ends its download, so the operation must report its phases to the
    /// progress it gets.
    /// </summary>
    /// <exception cref="InstallStoppedException">
    /// The stop came before the operation started or while it downloaded, and
    /// the operation left nothing behind.
    /// </exception>
    public static Task<T> RunAsync<T>(
        this InstallStop? stop,
        Func<IProgress<InstallProgress>?, CancellationToken, Task<T>> operation,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return stop is null ? operation(progress, cancellationToken) : stop.RunCoreAsync(operation, progress, cancellationToken);
    }
}

/// <summary>
/// An <see cref="InstallStop"/> ended an install before all of its operations
/// ran. The first <see cref="Completed"/> operations stay.
/// </summary>
public sealed class InstallStoppedException : OperationCanceledException
{
    public int Completed { get; }

    public int Total { get; }

    public InstallStoppedException(int completed, int total, Exception? innerException = null)
        : base($"The install stopped after {completed} of {total} operations.", innerException)
    {
        Completed = completed;
        Total = total;
    }
}
