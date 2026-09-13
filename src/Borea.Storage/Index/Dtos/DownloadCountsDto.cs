namespace Borea.Storage.Index.Dtos;

/// <summary>The listing level of the optional downloads value of a snapshot listing.</summary>
public sealed class DownloadCountsDto
{
    public required long Total { get; set; }

    public required Dictionary<string, long> Hosts { get; set; }
}

/// <summary>One entry of the releases array inside a downloads value.</summary>
public sealed class ReleaseDownloadCountsDto
{
    public required string Version { get; set; }

    public required long Total { get; set; }

    public required Dictionary<string, long> Hosts { get; set; }
}
