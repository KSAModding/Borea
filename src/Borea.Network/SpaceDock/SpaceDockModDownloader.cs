using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Borea.Core.Mods;

namespace Borea.Network.SpaceDock;

/// <summary>
/// IModDownloader implementation against SpaceDock. Downloads the mod's zip
/// to a temp file, computes a SHA256 checksum, then extracts into
/// destinationDirectory. Resolves modId via SpaceDockResolver — a raw
/// integer placeholder (pre-mod.toml) or a previously-registered true
/// ModId (post-mod.toml) both work identically.
/// </summary>
public sealed class SpaceDockModDownloader : IModDownloader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;
    private readonly SpaceDockResolver _resolver;

    public SpaceDockModDownloader(HttpClient httpClient, SpaceDockResolver resolver)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public async Task<DownloadResult> DownloadAsync(
    string modId,
    ModVersion version,
    string destinationDirectory,
    IProgress<DownloadProgress>? progress = null,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("Destination directory cannot be null or whitespace.", nameof(destinationDirectory));

        if (!_resolver.TryResolveId(modId, out var spaceDockId))
            throw new InvalidOperationException($"Could not resolve SpaceDock mod '{modId}' to a numeric SpaceDock ID.");

        var dto = await _httpClient.GetFromJsonAsync<SpaceDockModDto>(
            $"api/mod/{spaceDockId}", JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"SpaceDock mod '{spaceDockId}' not found.");

        var matchingVersion = dto.Versions.FirstOrDefault(v =>
            SpaceDockVersionParsing.TryNormalize(v.FriendlyVersion, out var parsed) && parsed.Equals(version))
            ?? throw new InvalidOperationException($"Version '{version}' not found for SpaceDock mod '{spaceDockId}'.");

        using var response = await _httpClient.GetAsync(
            matchingVersion.DownloadPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"borea-spacedock-{Guid.NewGuid()}.zip");
        var stagingPath = Path.Combine(Path.GetTempPath(), $"borea-spacedock-extract-{Guid.NewGuid()}");

        try
        {
            long bytesDownloaded = 0;
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    bytesDownloaded += bytesRead;
                    progress?.Report(new DownloadProgress(bytesDownloaded, totalBytes));
                }
            }

            string checksum;
            await using (var checksumStream = File.OpenRead(tempZipPath))
            {
                var hash = await SHA256.HashDataAsync(checksumStream, cancellationToken).ConfigureAwait(false);
                checksum = Convert.ToHexString(hash);
            }

            ZipFile.ExtractToDirectory(tempZipPath, stagingPath, overwriteFiles: true);

            var contentRoot = ResolveContentRoot(stagingPath);

            Directory.CreateDirectory(destinationDirectory);
            CopyDirectoryContents(contentRoot, destinationDirectory);

            var trueModId = ModTomlReader.ReadModId(destinationDirectory);
            _resolver.Register(trueModId, spaceDockId);

            return new DownloadResult(trueModId, bytesDownloaded, checksum);
        }
        finally
        {
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);

            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);
        }
    }

    /// <summary>
    /// SpaceDock zips commonly wrap all content in a single extra top-level
    /// folder (e.g. "Mod A.zip" extracts to "Mod A\Mod A\..."). If extraction
    /// produced exactly one top-level entry and it's a directory with no
    /// sibling files, treat that folder as the real content root. Otherwise
    /// (files sit directly at top level, or there are multiple top-level
    /// entries), assume no wrapper and use the extracted path as-is.
    /// </summary>
    private static string ResolveContentRoot(string extractedPath)
    {
        var topLevelDirs = Directory.GetDirectories(extractedPath);
        var topLevelFiles = Directory.GetFiles(extractedPath);

        return topLevelDirs.Length == 1 && topLevelFiles.Length == 0
            ? topLevelDirs[0]
            : extractedPath;
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        foreach (var filePath in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(filePath);
            File.Copy(filePath, Path.Combine(destinationDir, fileName), overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subDirName = Path.GetFileName(subDir);
            var destSubDir = Path.Combine(destinationDir, subDirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectoryContents(subDir, destSubDir);
        }
    }
}
