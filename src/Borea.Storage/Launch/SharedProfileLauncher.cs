using System.ComponentModel;
using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Core.Paths;

namespace Borea.Storage.Launch;

/// <summary>
/// ISharedProfileLauncher over the configured game directory and a process
/// starter. It releases the process handle at once, because Borea writes
/// nothing to the shared profile and so has nothing to hold back while the
/// game runs.
/// </summary>
public sealed class SharedProfileLauncher : ISharedProfileLauncher
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IProcessStarter _starter;
    private readonly OsPlatform? _platform;

    public SharedProfileLauncher(IGamePathProvider pathProvider, IProcessStarter starter)
        : this(pathProvider, starter, CurrentPlatform())
    {
    }

    /// <param name="platform">
    /// The platform whose executable name is used. Null is a system that is
    /// not Windows, Linux or macOS, where nothing is started.
    /// </param>
    public SharedProfileLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _platform = platform;
    }

    public SharedProfileLaunchResult Launch(IReadOnlyList<string>? arguments = null)
    {
        // The platform comes first, because no directory setting can fix an unknown executable.
        var fileName = _platform is { } platform ? GameExecutable.FileName(platform) : null;
        if (fileName is null)
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.UnknownExecutable,
                $"Borea does not know the name of the game's executable on {PlatformName(_platform)}, so it starts nothing.");
        }

        var gameDirectory = _pathProvider.GetGameDirectoryPath();
        if (string.IsNullOrWhiteSpace(gameDirectory))
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.NoGameDirectory,
                "Borea does not know where the game is installed. Set the game directory in the settings.");
        }

        var plan = GameExecutable.Plan(Path.GetFullPath(gameDirectory), fileName, arguments);

        if (!File.Exists(plan.Executable))
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.ExecutableMissing,
                $"'{plan.Executable}' is not there. Reinstall the game or correct the game directory in the settings.",
                plan);
        }

        IStartedProcess process;
        try
        {
            process = _starter.Start(plan);
        }
        catch (Win32Exception exception)
        {
            return SharedProfileLaunchResult.Failed(
                SharedProfileLaunchOutcome.StartFailed,
                $"The system did not start '{plan.Executable}': {exception.Message}",
                plan);
        }

        using (process)
        {
            return SharedProfileLaunchResult.Success(
                plan,
                process.Id,
                "Started the game without a mod loader. It uses the shared profile, not a Borea instance.");
        }
    }

    internal static OsPlatform? CurrentPlatform() =>
        OperatingSystem.IsWindows() ? OsPlatform.Windows
        : OperatingSystem.IsLinux() ? OsPlatform.Linux
        : OperatingSystem.IsMacOS() ? OsPlatform.MacOs
        : null;

    private static string PlatformName(OsPlatform? platform) => platform switch
    {
        OsPlatform.Windows => "Windows",
        OsPlatform.Linux => "Linux",
        OsPlatform.MacOs => "macOS",
        _ => "this operating system",
    };
}
