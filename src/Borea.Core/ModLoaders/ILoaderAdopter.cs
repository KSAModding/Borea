using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Validates and records a mod loader that Borea did not install.
/// </summary>
public interface ILoaderAdopter
{
    /// <summary>
    /// Checks the launch target and its file version, reads the configured game
    /// path without changing it, and records the loader as adopted.
    /// </summary>
    Task<LoaderAdoptionResult> AdoptAsync(
        ModMetadata loader,
        IReadOnlyList<ModVersionMetadata> releases,
        string directory,
        CancellationToken cancellationToken = default);
}

public sealed record LoaderAdoptionResult(
    string LoaderId,
    string Directory,
    string? RawVersion,
    ModVersion? Version,
    string? ConfiguredGameDirectory,
    bool? GameDirectoryMatches,
    IReadOnlyList<string> Warnings);
