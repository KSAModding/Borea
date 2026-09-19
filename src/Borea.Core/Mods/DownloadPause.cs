namespace Borea.Core.Mods;

/// <summary>
/// The pause of one download. <see cref="Planning.InstallStop"/> makes one for
/// each operation it runs, and a downloader finds it through <see cref="Current"/>.
/// </summary>
public sealed class DownloadPause
{
    private static readonly AsyncLocal<DownloadPause?> CurrentPause = new();

    private readonly Lock _gate = new();
    private CancellationTokenSource _pausing = new();
    private TaskCompletionSource? _resumed;

    internal DownloadPause()
    {
    }

    /// <summary>The pause of the operation that runs on this flow, or null outside an install that can pause.</summary>
    public static DownloadPause? Current
    {
        get => CurrentPause.Value;
        internal set => CurrentPause.Value = value;
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
                return _resumed is not null;
        }
    }

    /// <summary>Canceled when a pause starts, and it stays canceled until the resume.</summary>
    public CancellationToken Token
    {
        get
        {
            lock (_gate)
                return _pausing.Token;
        }
    }

    /// <summary>Completes at once when the download is not paused.</summary>
    public Task WaitForResumeAsync(CancellationToken cancellationToken)
    {
        Task resumed;
        lock (_gate)
            resumed = _resumed?.Task ?? Task.CompletedTask;

        return resumed.WaitAsync(cancellationToken);
    }

    internal void Pause()
    {
        CancellationTokenSource pausing;
        lock (_gate)
        {
            if (_resumed is not null)
                return;

            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pausing = _pausing;
        }

        _ = pausing.CancelAsync();
    }

    internal void Resume()
    {
        TaskCompletionSource? resumed;
        lock (_gate)
        {
            resumed = _resumed;
            if (resumed is null)
                return;

            _resumed = null;
            _pausing = new CancellationTokenSource();
        }

        resumed.TrySetResult();
    }
}
