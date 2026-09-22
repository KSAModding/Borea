using Borea.Core.Mods;

namespace Borea.Core.Updates;

/// <summary>Fetches the files that are published with a Borea release.</summary>
public interface IBoreaReleaseFiles
{
    /// <summary>The text of a small published file, or null when it cannot be read.</summary>
    Task<string?> ReadTextAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a published file to <paramref name="path"/> and returns the uppercase hex SHA-256 of
    /// what arrived. Nothing is left at <paramref name="path"/> when the transfer fails.
    /// </summary>
    /// <exception cref="HttpRequestException">The file did not arrive.</exception>
    Task<string> DownloadAsync(string url, string path, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}
