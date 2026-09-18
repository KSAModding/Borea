namespace Borea.Storage.Launch;

/// <summary>
/// The launches that still run, shared by every <see cref="LoaderLauncher"/>
/// built with this object, so a launcher built after a settings change still
/// knows the games an earlier one started.
/// </summary>
public sealed class RunningLaunches
{
    internal object Gate { get; } = new();

    internal Dictionary<Guid, IStartedProcess> Processes { get; } = new();

    internal Dictionary<Guid, (DateTime? GameLogAtLaunch, string LoaderName)> Starts { get; } = new();
}
