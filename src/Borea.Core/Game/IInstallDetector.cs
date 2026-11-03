using Borea.Core.Mods;

namespace Borea.Core.Game;

/// <summary>
/// Finds installs Borea can suggest. It reads only and saves nothing.
/// </summary>
public interface IInstallDetector
{
    /// <summary>
    /// Checks every candidate folder. A game needs the program file of its
    /// build on this system, KSA.exe for the Windows build and KSA for the
    /// native Linux build, and a KSA.dll with a readable version. A loader must
    /// pass the loader adoption check for one of <paramref name="loaders"/>.
    /// </summary>
    Task<InstallDetection> DetectAsync(IReadOnlyList<ModMetadata> loaders, CancellationToken cancellationToken = default);
}

public sealed record InstallDetection(IReadOnlyList<DetectedGame> Games, IReadOnlyList<DetectedLoader> Loaders);

public sealed record DetectedGame(string Directory, InstalledGameVersion Version);

public sealed record DetectedLoader(string LoaderId, string Directory, string? RawVersion);
