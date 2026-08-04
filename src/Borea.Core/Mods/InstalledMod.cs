namespace Borea.Core.Mods;

/// <summary>
/// Represents a mod currently installed in Borea's library, tracking which
/// version is installed, why it was installed (for orphan cleanup), and a
/// snapshot of its metadata as of install time.
/// </summary>
public sealed class InstalledMod
{
    public string ModId { get; }
    public ModVersion Version { get; }
    public InstallReason Reason { get; private set; }
    public DateTimeOffset InstalledAt { get; }
    public ModMetadata Metadata { get; }
    public string? Checksum { get; }

    public InstalledMod(string modId, ModVersion version, InstallReason reason, DateTimeOffset installedAt, ModMetadata metadata, string? checksum = null)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (metadata is null)
            throw new ArgumentNullException(nameof(metadata));

        if (!string.Equals(metadata.ModId, modId, StringComparison.Ordinal))
            throw new ArgumentException($"Metadata ModId '{metadata.ModId}' does not match '{modId}'.", nameof(metadata));

        if (!metadata.Version.Equals(version))
            throw new ArgumentException($"Metadata version '{metadata.Version}' does not match '{version}'.", nameof(metadata));

        ModId = modId;
        Version = version;
        Reason = reason;
        InstalledAt = installedAt;
        Metadata = metadata;
        Checksum = checksum;
    }

    public void MarkAsManuallyInstalled()
    {
        Reason = InstallReason.Manual;
    }
}
