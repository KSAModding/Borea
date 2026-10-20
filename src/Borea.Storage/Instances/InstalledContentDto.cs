using Borea.Storage.Mods;

namespace Borea.Storage.Instances;

/// <summary>
/// The vehicles and saves Borea installed in an instance, saved as installed-content.toml.
/// </summary>
public sealed class InstalledContentDto
{
    public List<InstalledContentItemDto> Items { get; set; } = new();
}

public sealed class InstalledContentItemDto
{
    /// <summary>"vehicle" or "save".</summary>
    public string Type { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;

    public DateTimeOffset InstalledAt { get; set; }

    public ModVersionMetadataDto Metadata { get; set; } = new();

    /// <summary>A list of tables and not a table, because a path as a TOML key needs quoting.</summary>
    public List<InstalledFileDto> Files { get; set; } = new();
}

public sealed class InstalledFileDto
{
    public string Path { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}
