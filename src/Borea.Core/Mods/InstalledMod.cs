using Borea.Core.Dependencies;
using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// Represents a mod currently installed in Borea's library, tracking which
/// version is installed, why it was installed (for orphan cleanup), and the
/// release facts the resolver needs.
/// </summary>
public sealed class InstalledMod
{
    public string ModId { get; }
    public ModVersion Version { get; }
    public InstallReason Reason { get; private set; }
    public DateTimeOffset InstalledAt { get; }

    /// <summary>
    /// The live authored listing. Its Dependencies are the authored entries;
    /// the resolver acts on <see cref="Dependencies"/>, the stamped list, instead.
    /// </summary>
    public ModMetadata Metadata { get; }

    /// <summary>
    /// The dependency list of the installed release, as its release file stamped it.
    /// </summary>
    public IReadOnlyList<ModDependency> Dependencies { get; }

    /// <summary>
    /// Hex SHA-256 of the installed archive, if known.
    /// </summary>
    public string? Checksum { get; }

    public InstalledMod(string modId, ModVersion version, InstallReason reason, DateTimeOffset installedAt, ModMetadata metadata, IReadOnlyList<ModDependency> dependencies, string? checksum = null)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        if (dependencies is null)
            throw new ArgumentNullException(nameof(dependencies));

        if (!ModIds.Equals(metadata.ModId, modId))
            throw new ArgumentException($"Metadata ModId '{metadata.ModId}' does not match '{modId}'.", nameof(metadata));

        ModId = modId;
        Version = version;
        Reason = reason;
        InstalledAt = installedAt;
        Metadata = metadata;
        Dependencies = new ReadOnlyCollection<ModDependency>(dependencies.ToArray());
        Checksum = checksum;
    }

    public void MarkAsManuallyInstalled()
    {
        Reason = InstallReason.Manual;
    }
}
