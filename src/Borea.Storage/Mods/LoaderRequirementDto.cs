namespace Borea.Storage.Mods;

public sealed class LoaderRequirementDto
{
    public string LoaderId { get; set; } = string.Empty;
    public string MinVersion { get; set; } = string.Empty;
    public string? MaxVersion { get; set; }

    /// <summary>"authored" or "derived" in release data, null in authored metadata.</summary>
    public string? Source { get; set; }
}
