using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.Logging;

public sealed class LoggingLauncher : ILauncher, IDisposable
{
    private readonly IBoreaLog _log;

    public ILauncher Inner { get; }

    public LoggingLauncher(ILauncher inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var target = $"instance {instance.InstanceId} with {loader?.ModId ?? "no loader"}";
        LaunchResult result;
        try
        {
            result = Inner.Launch(instance, loader, arguments);
        }
        catch (Exception exception)
        {
            _log.Write($"Launch of {target} failed.", exception);
            throw;
        }

        var plan = result.Plan is not null ? " " + Describe(result.Plan) : DescribeArguments(instance.LaunchArguments.Concat(arguments ?? []).ToList());
        _log.Write(result.Started
            ? $"Launch of {target} started process {result.ProcessId}.{plan}"
            : $"Launch of {target} did not start, {result.Outcome}: {result.Message}{plan}");
        return result;
    }

    public async Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var result = await Inner.WatchStartAsync(instance, started, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == LaunchOutcome.ExitedEarly)
        {
            var blamed = (result.CrashCause, result.BlamedModId) switch
            {
                (LoaderCrashCause.ModLoading, null) => "stopped while loading mods, no mod found",
                (LoaderCrashCause.ModLoading, var modId) => $"stopped while loading mods, likely {modId}",
                (_, null) => "no mod named",
                (_, var modId) => $"blames {modId}",
            };
            var exitCode = result.ExitCode is { } code ? LoaderExitCode.Describe(code, OperatingSystem.IsWindows()) : "unknown";
            _log.Write($"Launch of instance {instance.InstanceId} stopped early with exit code {exitCode}, {blamed}. Last output:{Environment.NewLine}{string.Join(Environment.NewLine, result.Output)}");
        }

        return result;
    }

    public bool IsRunning(Guid instanceId) => Inner.IsRunning(instanceId);

    public void Dispose()
    {
        if (Inner is IDisposable disposable)
            disposable.Dispose();
    }

    internal static string Describe(LaunchPlan plan)
    {
        var arguments = plan.Arguments.Count == 0 ? "none" : ArgumentLine.Join(plan.Arguments);
        var variables = plan.EnvironmentVariables.Count == 0 ? "none" : string.Join(" ", plan.EnvironmentVariables.Select(pair => $"{pair.Key}=\"{pair.Value}\""));
        return $"Executable: \"{plan.Executable}\". Arguments: {arguments}. Environment: {variables}. Working directory: \"{plan.WorkingDirectory}\".";
    }

    private static string DescribeArguments(IReadOnlyList<string> arguments)
        => arguments.Count == 0 ? "" : $" Arguments: {ArgumentLine.Join(arguments)}.";
}
