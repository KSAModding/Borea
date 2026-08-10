namespace Borea.Storage.Mods;

/// <summary>
/// TOML-serializable representation of a ReleaseSource.
/// </summary>
public sealed class ReleaseSourceDto
{
    public List<ReleaseHostDto> Hosts { get; set; } = new();
    public string? Authority { get; set; }
}

/// <summary>
/// One release host entry: the host key and the reference on that host.
/// </summary>
public sealed class ReleaseHostDto
{
    public string Host { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}
