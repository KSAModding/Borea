namespace Borea.Storage.Mods;

/// <summary>
/// Flat, TOML-serializable representation of an InstallInfo.
/// </summary>
public sealed class InstallInfoDto
{
    public string Root { get; set; } = string.Empty;
    public bool Derived { get; set; }
}
