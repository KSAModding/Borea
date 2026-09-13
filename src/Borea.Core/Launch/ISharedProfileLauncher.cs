namespace Borea.Core.Launch;

/// <summary>
/// Starts the game itself, without a mod loader and without an instance. The
/// game takes no instance path on its own, so it uses the shared profile under
/// My Games/Kitten Space Agency. This is a separate operation from
/// <see cref="ILauncher"/>, which starts an instance and never falls back to
/// the shared profile.
/// </summary>
public interface ISharedProfileLauncher
{
    /// <summary>
    /// Starts the game's executable from the game directory. Borea writes
    /// nothing to the shared profile and keeps no record of the process.
    /// </summary>
    SharedProfileLaunchResult Launch();
}
