using Borea.Core.Launch;

namespace Borea.Core.Logging;

public sealed class LoggingSharedProfileLauncher : ISharedProfileLauncher
{
    private readonly IBoreaLog _log;

    public ISharedProfileLauncher Inner { get; }

    public LoggingSharedProfileLauncher(ISharedProfileLauncher inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public SharedProfileLaunchResult Launch(IReadOnlyList<string>? arguments = null)
    {
        SharedProfileLaunchResult result;
        try
        {
            result = Inner.Launch(arguments);
        }
        catch (Exception exception)
        {
            _log.Write("Launch without a mod loader failed.", exception);
            throw;
        }

        var plan = result.Plan is null ? "" : " " + LoggingLauncher.Describe(result.Plan);
        _log.Write(result.Started
            ? $"Launch without a mod loader started process {result.ProcessId}.{plan}"
            : $"Launch without a mod loader did not start, {result.Outcome}: {result.Message}{plan}");
        return result;
    }
}
