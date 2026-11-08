using Borea.Core.Launch;

namespace Borea.Core.ModLoaders;

/// <summary>
/// What Borea knows about one mod loader beyond its listing: how to read the output it leaves when it stops, which code
/// is its own, where players install it, and which table of a mod.toml it reads. <see cref="LoaderProfiles"/> picks the
/// profile by loader id, and a loader without a profile of its own gets <see cref="NeutralLoaderProfile"/>.
/// </summary>
public interface ILoaderProfile
{
    /// <summary>The content id of the loader, or null for the profile of every loader Borea knows nothing about.</summary>
    string? LoaderId { get; }

    /// <summary>The first namespace part of the loader's own code, so that a stack frame there blames no mod.</summary>
    IReadOnlyList<string> CodeNamespaces { get; }

    /// <summary>
    /// The table of a mod.toml that the loader reads, or null when it reads none. The table holds EntryAssembly, the
    /// assembly the loader loads, and [[ModDependencies]] blocks of ModId and Optional.
    /// </summary>
    string? ModTomlSection { get; }

    /// <summary>Whether the loader also loads a mod whose mod.toml lacks its table, from the DLL named after the mod.</summary>
    bool LoadsModWithoutTable { get; }

    /// <summary>The start of the display name of the loader's uninstall entry in the Windows registry, or null.</summary>
    string? UninstallDisplayNamePrefix { get; }

    /// <summary>The folders below Program Files where players unpack the loader.</summary>
    IReadOnlyList<string> InstallFolderNames { get; }

    /// <summary>
    /// The names, without extension, of the processes the loader runs the game in. They count as the game running even
    /// when Borea does not list the loader as installed.
    /// </summary>
    IReadOnlyList<string> ProcessNames { get; }

    /// <summary>Whether the output says that the loader found no KSA.dll at its game path.</summary>
    bool FoundNoGame(IEnumerable<string> output);

    /// <summary>Whether a stack frame in the output lies in the loader's mod loading.</summary>
    bool StoppedWhileLoadingMods(IEnumerable<string> output);

    /// <summary>Whether the loader stopped while it started itself, before it loaded a mod.</summary>
    bool StoppedWhileLoaderStarted(IEnumerable<string> output);

    /// <summary>The enabled mod the loader was likely loading when it stopped, or null.</summary>
    /// <param name="isWholeOutput">Whether the output holds every line the loader wrote, because none was cut from its start.</param>
    string? LikelyLoadingMod(IReadOnlyList<string> output, IReadOnlyList<LoadOrderMod> enabledMods, bool isWholeOutput = false);
}
