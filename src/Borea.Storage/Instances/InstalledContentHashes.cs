using System.Security.Cryptography;

namespace Borea.Storage.Instances;

/// <summary>The hashes an install record keeps, see <see cref="Core.Instances.InstalledContentRecord.Files"/>.</summary>
public static class InstalledContentHashes
{
    internal static readonly EnumerationOptions EveryFile = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Every file in the folder and below it, by its path with "/" between the parts. Empty when the folder is missing.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ComputeAsync(string folder, CancellationToken cancellationToken = default)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
            return hashes;

        foreach (var file in Directory.EnumerateFiles(folder, "*", EveryFile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            hashes[Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/')] = Convert.ToHexStringLower(hash);
        }

        return hashes;
    }
}
