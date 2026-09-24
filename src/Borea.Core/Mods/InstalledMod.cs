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
    /// The live versioned listing. Its dependencies can be found
    /// using <see cref="Metadata.Dependencies"/>.
    /// </summary>
    public ModVersionMetadata Metadata { get; }

    /// <summary>
    /// Hex SHA-256 of the installed archive, if known.
    /// </summary>
    public string? Checksum { get; }

    public ModInstallOwnership Ownership { get; }

    public string? OwnershipToken { get; }

    public ModStorage Storage { get; }

    /// <summary>
    /// A private folder proves ownership with its token, and a linked folder
    /// with a link to the stored release that <see cref="Checksum"/> names.
    /// </summary>
    public bool CanDeleteFiles => Ownership == ModInstallOwnership.Borea
        && (Storage == ModStorage.Linked || !string.IsNullOrWhiteSpace(OwnershipToken));

    public InstalledMod(
        string modId,
        ModVersion version,
        InstallReason reason,
        DateTimeOffset installedAt,
        ModVersionMetadata metadata,
        string? checksum = null,
        ModInstallOwnership ownership = ModInstallOwnership.Borea,
        string? ownershipToken = null,
        ModStorage storage = ModStorage.Private)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        if (!ModIds.Equals(metadata.ModId, modId))
            throw new ArgumentException($"Metadata ModId '{metadata.ModId}' does not match '{modId}'.", nameof(metadata));

        ModId = modId;
        Version = version;
        Reason = reason;
        InstalledAt = installedAt;
        Metadata = metadata;
        Checksum = checksum;
        Ownership = ownership;
        OwnershipToken = ownershipToken;
        Storage = storage;

        if (ownership == ModInstallOwnership.Foreign && !string.IsNullOrWhiteSpace(ownershipToken))
            throw new ArgumentException("A foreign mod cannot have a Borea ownership token.", nameof(ownershipToken));

        if (storage == ModStorage.Linked)
        {
            if (ownership != ModInstallOwnership.Borea)
                throw new ArgumentException("Only a mod Borea owns can link to a stored release.", nameof(storage));

            if (checksum is not { Length: 64 } || !checksum.All(Uri.IsHexDigit))
                throw new ArgumentException("A linked mod needs the SHA-256 that names its stored release, as 64 hex characters.", nameof(checksum));

            if (ownershipToken is not null)
                throw new ArgumentException("A linked mod proves ownership through its link, so it has no ownership token.", nameof(ownershipToken));
        }
    }

    public void MarkAsManuallyInstalled()
    {
        Reason = InstallReason.Manual;
    }
}
