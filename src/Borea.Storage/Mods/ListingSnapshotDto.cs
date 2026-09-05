namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of a ListingSnapshot.
/// </summary>
public sealed class ListingSnapshotDto
{
    public string Name { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = new();
    public string Abstract { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string License { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Links { get; set; } = new();
}
