using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// The download facts of one release.
/// </summary>
public sealed class DownloadInfo
{
    /// <summary>
    /// Direct download of the release archive from its own host.
    /// </summary>
    public string Url { get; }

    /// <summary>
    /// Hex SHA-256 of the archive, normalized to uppercase. Verifies the download and keys caches.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Archive size in bytes.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// The archive format, such as "application/zip".
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Further URLs serving the identical archive.
    /// </summary>
    public IReadOnlyList<string> Mirrors { get; }

    public DownloadInfo(string url, string sha256, long sizeBytes, string contentType, IReadOnlyList<string>? mirrors = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Download url cannot be empty.", nameof(url));

        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Sha256 must be 64 hex characters.", nameof(sha256));

        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Size cannot be negative.");

        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type cannot be empty.", nameof(contentType));

        Url = url;
        Sha256 = sha256.ToUpperInvariant();
        SizeBytes = sizeBytes;
        ContentType = contentType;
        Mirrors = mirrors is null ? Array.Empty<string>() : new ReadOnlyCollection<string>(mirrors.ToArray());
    }

    /// <summary>
    /// Whether the given hex digest names the same bytes, compared case-insensitively.
    /// </summary>
    public bool HashMatches(string hexDigest) => string.Equals(Sha256, hexDigest, StringComparison.OrdinalIgnoreCase);
}
