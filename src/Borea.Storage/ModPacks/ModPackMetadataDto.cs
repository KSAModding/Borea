namespace Borea.Storage.ModPacks;

public sealed class ModPackMetadataDto
{
    public string ModPackId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string BuiltForGameVersion { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? HomepageUrl { get; set; }
    public System.DateTimeOffset ReleasedAt { get; set; }
    public List<ModPackEntryDto> Mods { get; set; } = new();
}