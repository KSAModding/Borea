namespace Borea.Core.Launch;

public sealed class LaunchResult
{
    public LaunchOutcome Outcome { get; }

    /// <summary>What happened and what to do about it, for the user.</summary>
    public string Message { get; }

    /// <summary>What was run, or would have been run. Null when no plan was built.</summary>
    public LaunchPlan? Plan { get; }

    /// <summary>Null unless the launch started.</summary>
    public int? ProcessId { get; }

    public bool Started => Outcome == LaunchOutcome.Started;

    /// <summary>The loader's exit code when it stopped early, otherwise null.</summary>
    public int? ExitCode { get; private init; }

    /// <summary>The last lines the loader wrote to its output and error streams, oldest first.</summary>
    public IReadOnlyList<string> Output { get; private init; } = [];

    /// <summary>
    /// The installed mod whose assembly the loader's error names, when it stopped
    /// early. Null when the output names none.
    /// </summary>
    public string? BlamedModId { get; private init; }

    /// <summary>The runtime or key of the loader's start entry that Borea does not know, when that stopped the launch.</summary>
    public string? UnknownName { get; private init; }

    private LaunchResult(LaunchOutcome outcome, string message, LaunchPlan? plan, int? processId)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A launch result needs a message for the user.", nameof(message));

        Outcome = outcome;
        Message = message;
        Plan = plan;
        ProcessId = processId;
    }

    public static LaunchResult Success(LaunchPlan plan, int processId, string message)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        return new LaunchResult(LaunchOutcome.Started, message, plan, processId);
    }

    /// <summary>A loader that stopped with an error before the game came up.</summary>
    public static LaunchResult ExitedEarly(LaunchPlan plan, int exitCode, IReadOnlyList<string> output, string? blamedModId, string message)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);

        return new LaunchResult(LaunchOutcome.ExitedEarly, message, plan, processId: null)
        {
            ExitCode = exitCode,
            Output = output.ToArray(),
            BlamedModId = blamedModId,
        };
    }

    /// <summary>A started launch that kept running, with what the loader wrote while it was watched.</summary>
    public LaunchResult WithOutput(IReadOnlyList<string> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new LaunchResult(Outcome, Message, Plan, ProcessId)
        {
            ExitCode = ExitCode,
            Output = output.ToArray(),
            BlamedModId = BlamedModId,
        };
    }

    public static LaunchResult Failed(LaunchOutcome outcome, string message, LaunchPlan? plan = null, string? unknownName = null)
    {
        if (outcome == LaunchOutcome.Started)
            throw new ArgumentException("A started launch is a success, not a failure.", nameof(outcome));

        return new LaunchResult(outcome, message, plan, processId: null) { UnknownName = unknownName };
    }
}
