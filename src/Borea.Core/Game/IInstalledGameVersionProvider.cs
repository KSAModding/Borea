namespace Borea.Core.Game;

public interface IInstalledGameVersionProvider
{
    /// <summary>
    /// The installed build, or null when no version could be read at all.
    /// </summary>
    InstalledGameVersion? GetInstalledVersion();
}
