using System.Runtime.InteropServices;

namespace Borea.Core.Updates;

/// <summary>
/// The files a Borea release publishes, and which of them belongs to a running build.
/// The release workflow names one archive per product and platform and one checksums file.
/// </summary>
public static class BoreaArchive
{
    /// <summary>The published file that holds the SHA-256 of every other file of the same release.</summary>
    public const string ChecksumsFileName = "SHA256SUMS.txt";

    private const string AppPrefix = "Borea-";

    private const string CliPrefix = "Borea-Cli-";

    /// <summary>The platform name in the archive names, by the runtime identifier the archive was published for.</summary>
    private static readonly Dictionary<string, string> PlatformNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["win-x64"] = "win-x64",
        ["linux-x64"] = "linux-x64",
        ["osx-arm64"] = "macos-arm64",
    };

    /// <summary>The platform of the running build, or null when no release is published for it.</summary>
    public static string? RunningPlatform => PlatformOf(RuntimeInformation.RuntimeIdentifier);

    /// <summary>The platform name the archives use for <paramref name="runtimeIdentifier"/>, or null when no release is published for it.</summary>
    public static string? PlatformOf(string? runtimeIdentifier)
        => runtimeIdentifier is not null && PlatformNames.TryGetValue(runtimeIdentifier, out var platform) ? platform : null;

    /// <summary>How the archive of <paramref name="platform"/> is packed.</summary>
    public static string ExtensionOf(string platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return platform.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz";
    }

    /// <summary>Whether <paramref name="assetName"/> is the archive of <paramref name="product"/> for <paramref name="platform"/>.</summary>
    public static bool Matches(string? assetName, BoreaProduct product, string platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (assetName is null || !assetName.EndsWith($"-{platform}{ExtensionOf(platform)}", StringComparison.Ordinal))
            return false;

        // Every App archive name is also a prefix of a CLI archive name, so the App rules it out.
        return product == BoreaProduct.Cli
            ? assetName.StartsWith(CliPrefix, StringComparison.Ordinal)
            : assetName.StartsWith(AppPrefix, StringComparison.Ordinal) && !assetName.StartsWith(CliPrefix, StringComparison.Ordinal);
    }

    /// <summary>The archive of <paramref name="product"/> for <paramref name="platform"/> in <paramref name="release"/>, or null when the release has none.</summary>
    public static BoreaReleaseAsset? Find(BoreaRelease release, BoreaProduct product, string platform)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Assets.FirstOrDefault(asset => Matches(asset.Name, product, platform));
    }

    /// <summary>The checksums file of <paramref name="release"/>, or null when the release has none.</summary>
    public static BoreaReleaseAsset? FindChecksums(BoreaRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, ChecksumsFileName, StringComparison.Ordinal));
    }
}
