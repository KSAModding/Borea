using System.Security.Cryptography;
using Borea.Core.Mods;

namespace Borea.Storage.Tests.Mods;

/// <summary>
/// Hands out configured bytes as the archive, or fails, and remembers where it was asked to write.
/// </summary>
internal sealed class FakeModDownloader : IModDownloader
{
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    public Exception? Failure { get; set; }

    public List<string> ArchivePaths { get; } = new();

    public IProgress<DownloadProgress>? LastProgress { get; private set; }

    public CancellationToken LastToken { get; private set; }

    public async Task<DownloadResult> DownloadAsync(
        ModVersionMetadata release,
        string archivePath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArchivePaths.Add(archivePath);
        LastProgress = progress;
        LastToken = cancellationToken;

        if (Failure is not null)
            throw Failure;

        await File.WriteAllBytesAsync(archivePath, Bytes, cancellationToken);
        return new DownloadResult(release.Download.Url, Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)));
    }
}
