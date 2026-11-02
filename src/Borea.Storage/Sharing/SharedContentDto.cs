namespace Borea.Storage.Sharing;

/// <summary>
/// What the player shared, saved as shared-content.toml.
/// </summary>
public sealed class SharedContentDto
{
    public SharedContentAuthorDto? Author { get; set; }

    public List<SharedContentItemDto> Items { get; set; } = new();
}

public sealed class SharedContentAuthorDto
{
    public List<string> Authors { get; set; } = new();

    public string License { get; set; } = string.Empty;
}

public sealed class SharedContentItemDto
{
    /// <summary>"vehicle" or "save".</summary>
    public string Type { get; set; } = string.Empty;

    public string HostKey { get; set; } = string.Empty;

    public string HostName { get; set; } = string.Empty;

    public string ListingId { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string? Page { get; set; }
}
