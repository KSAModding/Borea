namespace Borea.Core.Updates;

/// <summary>One file published with a Borea release, such as a platform archive or the checksums.</summary>
public sealed record BoreaReleaseAsset(string Name, string Url, long SizeBytes = 0)
{
    /// <summary>
    /// The GitHub API address of the same file, which serves the same bytes when the download link
    /// <see cref="Url"/> fails. Null when the release list named no such address.
    /// </summary>
    public string? ApiUrl { get; init; }
}
