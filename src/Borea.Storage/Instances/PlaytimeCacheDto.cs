namespace Borea.Storage.Instances;

/// <summary>
/// The sessions read from archived game logs, saved as playtime.toml.
/// </summary>
public sealed class PlaytimeCacheDto
{
    public List<CachedSessionDto> Sessions { get; set; } = new();
}

public sealed class CachedSessionDto
{
    /// <summary>The name of the archive the session was read from.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>The size of the archive when it was read, in bytes.</summary>
    public long Size { get; set; }

    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public bool Clean { get; set; }
}
