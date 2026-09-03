using System.Diagnostics;
using Borea.Core.Game;
using Borea.Core.Paths;

namespace Borea.Storage.Game;

/// <summary>
/// Reads the installed build from the version resource of KSA.dll, the first
/// two steps of RFC 0017's chain.
/// </summary>
public sealed class InstalledGameVersionProvider : IInstalledGameVersionProvider
{
    private const string GameAssemblyFileName = "KSA.dll";

    private readonly IGamePathProvider _pathProvider;

    public InstalledGameVersionProvider(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public InstalledGameVersion? GetInstalledVersion()
    {
        var gameDirectory = _pathProvider.GetGameDirectoryPath();
        if (string.IsNullOrWhiteSpace(gameDirectory))
            return null;

        FileVersionInfo info;
        try
        {
            info = FileVersionInfo.GetVersionInfo(Path.Combine(gameDirectory, GameAssemblyFileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        // FileVersion first per RFC 0017: the plain string a user is shown.
        // ProductVersion adds the commit hash, and a suffix on builds RFC 0017 scopes out.
        var raw = FirstNonBlank(info.FileVersion, info.ProductVersion);
        if (raw is null)
            return null;

        return new InstalledGameVersion(GameVersion.TryParse(raw, out var version) ? version : null, raw);
    }

    private static string? FirstNonBlank(string? fileVersion, string? productVersion) =>
        !string.IsNullOrWhiteSpace(fileVersion) ? fileVersion
        : !string.IsNullOrWhiteSpace(productVersion) ? productVersion
        : null;
}
