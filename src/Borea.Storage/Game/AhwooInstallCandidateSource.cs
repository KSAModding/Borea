using System.Security;
using System.Text.Json;
using Borea.Core.Game;

namespace Borea.Storage.Game;

/// <summary>
/// Reads the manifest that the Ahwoo client keeps on Linux for the KSA game
/// package. Each build in it names its folder relative to the games folder of
/// the client, which on Linux is ~/Games/Ahwoo. The file name is the package
/// id, so the package is found without the id fields, which Ahwoo will rename.
/// It writes nothing.
/// </summary>
public sealed class AhwooInstallCandidateSource : IInstallCandidateSource
{
    internal const string GameManifestFileName = "019f01fc-623a-773f-8d70-f0177b16a20c.adf";

    private readonly string _configFolder;
    private readonly string _gamesFolder;

    public AhwooInstallCandidateSource()
        : this(
            // ApplicationData follows XDG_CONFIG_HOME on Linux, as in the client
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ahwoo"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Ahwoo"))
    {
    }

    internal AhwooInstallCandidateSource(string configFolder, string gamesFolder)
    {
        _configFolder = configFolder ?? throw new ArgumentNullException(nameof(configFolder));
        _gamesFolder = gamesFolder ?? throw new ArgumentNullException(nameof(gamesFolder));
    }

    public IReadOnlyList<string> GetGameDirectories()
    {
        var folders = Read(Path.Combine(_configFolder, "Manifests", GameManifestFileName), root => root.GetProperty("Builds")
            .EnumerateArray()
            .Select(build => build.GetProperty("InstallDirectory").GetString())
            .OfType<string>()
            .ToList());

        // Path.Combine keeps a build folder that is already absolute
        return folders?.Select(folder => Path.Combine(_gamesFolder, folder)).ToList() ?? [];
    }

    public IReadOnlyList<string> GetLoaderDirectories() => [];

    private static T? Read<T>(string path, Func<JsonElement, T?> read)
        where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return read(document.RootElement);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or JsonException
            or KeyNotFoundException
            or InvalidOperationException)
        {
            // a missing or broken file offers no candidate
            return null;
        }
    }
}
