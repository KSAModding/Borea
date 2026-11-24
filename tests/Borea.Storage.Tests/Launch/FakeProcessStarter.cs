using Borea.Core.Launch;
using Borea.Storage.Launch;

namespace Borea.Storage.Tests.Launch;

/// <summary>
/// Records every plan and hands out processes whose exit a test controls.
/// </summary>
internal sealed class FakeProcessStarter : IProcessStarter
{
    private int _nextId = 1000;

    public List<LaunchPlan> Plans { get; } = new();

    public List<FakeStartedProcess> Processes { get; } = new();

    /// <summary>Thrown by the next start when set.</summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Whether the processes carry a start time, so a launcher records them and a later launcher finds them. Off by
    /// default, because a find holds the process of another test as well.
    /// </summary>
    public bool GivesStartTimes { get; set; }

    public IStartedProcess Start(LaunchPlan plan)
    {
        Plans.Add(plan);

        if (Failure is not null)
            throw Failure;

        var process = new FakeStartedProcess(_nextId++);
        if (GivesStartTimes)
            process.StartedAt = new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc).AddSeconds(process.Id);
        Processes.Add(process);
        return process;
    }

    /// <summary>A view of a started process that still runs, as a later Borea finds it. Disposing the view keeps the process.</summary>
    public IStartedProcess? Find(int processId, DateTime startedAt)
    {
        var process = Processes.FirstOrDefault(process => process.Id == processId && process.StartedAt == startedAt && !process.HasExited);
        return process is null ? null : new FoundProcess(process);
    }

    private sealed class FoundProcess(FakeStartedProcess process) : IStartedProcess
    {
        public int Id => process.Id;

        public DateTime? StartedAt => process.StartedAt;

        public bool HasExited => process.HasExited;

        public int? ExitCode => null;

        public IReadOnlyList<string> RecentOutput => [];

        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => process.WaitForExitAsync(timeout, cancellationToken);

        public void Dispose()
        {
        }
    }
}

internal sealed class FakeStartedProcess : IStartedProcess
{
    public FakeStartedProcess(int id)
    {
        Id = id;
    }

    public int Id { get; }

    public bool HasExited { get; set; }

    private bool? _hasEnded;

    /// <summary>Follows <see cref="HasExited"/> unless a test sets it, for example to a restart that still runs.</summary>
    public bool HasEnded
    {
        get => _hasEnded ?? HasExited;
        set => _hasEnded = value;
    }

    public DateTime? StartedAt { get; set; }

    public int? ExitCode { get; set; }

    public List<string> Output { get; } = new();

    public IReadOnlyList<string> RecentOutput => Output;

    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Ends the process, which also ends a wait without an end.</summary>
    public void Exit(int exitCode)
    {
        ExitCode = exitCode;
        HasExited = true;
        _exited.TrySetResult();
    }

    /// <summary>
    /// Answers at once, so a watch over a fake never waits for real time. Only
    /// a wait without an end waits, until <see cref="Exit"/>.
    /// </summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout == Timeout.InfiniteTimeSpan && !HasEnded)
            await _exited.Task.WaitAsync(cancellationToken);
        return HasEnded;
    }

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
