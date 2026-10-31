using Borea.Core.Game;
using Borea.Core.Instances;

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
    /// The installed mod the loader likely stopped on, when it stopped early.
    /// Null when the output points to none.
    /// </summary>
    public string? BlamedModId { get; private init; }

    /// <summary>
    /// The other enabled code mods, in load order, that reference the missing
    /// game assembly of the error or another game assembly that the installed
    /// KSA does not have. They were likely made for another KSA build too.
    /// </summary>
    public IReadOnlyList<string> OtherBlamedModIds { get; private init; } = [];

    /// <summary>What the output of a loader that stopped early shows about the cause.</summary>
    public LoaderCrashCause CrashCause { get; private init; }

    /// <summary>The runtime or key of the loader's start entry that Borea does not know, when that stopped the launch.</summary>
    public string? UnknownName { get; private init; }

    /// <summary>
    /// The Wine prefix around a Windows build that stopped the launch, or the
    /// wrapper install of a launch through a Wine wrapper that did not go on.
    /// Null when Borea found none.
    /// </summary>
    public WineInstall? Wine { get; private init; }

    /// <summary>The host path that no drive of the Wine prefix holds, when that stopped the launch.</summary>
    public string? UnmappedPath { get; private init; }

    /// <summary>The .NET runtime that the loader needs and the Wine prefix lacks, when that stopped the launch.</summary>
    public DotnetRuntimeNeed? MissingRuntime { get; private init; }

    /// <summary>The configuration file of the loader that Borea could not read, when that stopped the launch.</summary>
    public string? ConfigurationFile { get; private init; }

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

    /// <summary>
    /// A loader that stopped with an error before the game came up, or the
    /// Wine wrapper of <paramref name="wine"/> that stopped before the game
    /// wrote its log.
    /// </summary>
    public static LaunchResult ExitedEarly(LaunchPlan plan, int exitCode, IReadOnlyList<string> output, string? blamedModId, string message, LoaderCrashCause crashCause = LoaderCrashCause.Unknown, WineInstall? wine = null, IReadOnlyList<string>? otherBlamedModIds = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);

        return new LaunchResult(LaunchOutcome.ExitedEarly, message, plan, processId: null)
        {
            ExitCode = exitCode,
            Output = output.ToArray(),
            BlamedModId = blamedModId,
            CrashCause = crashCause,
            Wine = wine,
            OtherBlamedModIds = otherBlamedModIds?.ToArray() ?? [],
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
            CrashCause = CrashCause,
        };
    }

    public static LaunchResult Failed(LaunchOutcome outcome, string message, LaunchPlan? plan = null, string? unknownName = null, WineInstall? wine = null, string? unmappedPath = null, DotnetRuntimeNeed? missingRuntime = null, string? configurationFile = null)
    {
        if (outcome == LaunchOutcome.Started)
            throw new ArgumentException("A started launch is a success, not a failure.", nameof(outcome));

        return new LaunchResult(outcome, message, plan, processId: null) { UnknownName = unknownName, Wine = wine, UnmappedPath = unmappedPath, MissingRuntime = missingRuntime, ConfigurationFile = configurationFile };
    }

    /// <summary>The refusal of an instance whose kept build has a problem, also for a caller that stops before the launch.</summary>
    /// <param name="buildFolder">The folder of the kept build.</param>
    public static LaunchResult GameBuildMissing(Instance instance, string buildFolder)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return Failed(
            LaunchOutcome.GameBuildMissing,
            $"Instance '{instance.Name}' runs on KSA {instance.GameBuild}, and '{buildFolder}' is gone or holds another build. Borea starts nothing, so the instance does not start on another build. To use the game folder of the settings instead, select \"Use the current game build\" in the instance menu of the desktop App, or remove GameBuild from the instance.toml of the instance.");
    }
}
