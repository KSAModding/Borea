namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of an InstallInfo.
/// </summary>
public sealed class InstallInfoDto
{
    /// <summary>Null means the archive root.</summary>
    public string? Root { get; set; }

    public bool Derived { get; set; }

    /// <summary>The anchor, lowercase. Null means the type default.</summary>
    public string? Target { get; set; }

    /// <summary>Null means the anchor itself.</summary>
    public string? Path { get; set; }
}
