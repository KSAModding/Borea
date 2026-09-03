namespace Borea.Storage.Mods;

/// <summary>
/// TOML-serializable representation of the authored LoaderProvides.
/// </summary>
public sealed class LoaderProvidesDto
{
    public string? Launch { get; set; }

    /// <summary>The anchor, lowercase.</summary>
    public string? ContentDir { get; set; }

    public string? ContentPath { get; set; }

    public LoaderConfigureDto? Configure { get; set; }
}

/// <summary>
/// TOML-serializable representation of the [provides.configure] table.
/// </summary>
public sealed class LoaderConfigureDto
{
    public string File { get; set; } = string.Empty;

    /// <summary>Lowercase ("json", "toml").</summary>
    public string Format { get; set; } = string.Empty;

    public string? GamePath { get; set; }
}
