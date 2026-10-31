namespace Borea.Storage.Mods;

public sealed class InstallDescriptorDto
{
    public string? Root { get; set; }

    /// <summary>The anchor, lowercase.</summary>
    public string? Target { get; set; }

    public string? Path { get; set; }
    public List<string>? Manages { get; set; }
    public List<string>? Steps { get; set; }
    public List<string>? Uninstall { get; set; }
}
