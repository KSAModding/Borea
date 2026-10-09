using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>Reads the archive of a stamped release for what its mod.toml declares, as validate.py of content-index-releases reads it.</summary>
public interface IReleaseArchiveReader
{
    /// <summary>
    /// The [[StarMap.ModDependencies]] of the mod.toml at the stamped install root, read as derived_dependencies of the stamper reads them.
    /// A release that is no mod, or an archive without a mod.toml there, declares none.
    /// </summary>
    /// <param name="releaseFileText">The stamped release file, which names the download URL, its mirrors, the sha256 and the install root.</param>
    /// <exception cref="ReleaseArchiveException">No URL serves the stamped bytes, or the archive or its mod.toml cannot be read.</exception>
    Task<IReadOnlyList<LocalModDependency>> DeclaredDependenciesAsync(string releaseFileText, CancellationToken cancellationToken = default);
}

/// <summary>The archive of a stamped release could not be downloaded or read.</summary>
public sealed class ReleaseArchiveException(string message, Exception? innerException = null) : Exception(message, innerException);
