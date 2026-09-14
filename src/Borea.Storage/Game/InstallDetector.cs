using Borea.Core.Game;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Storage.Game;

public sealed class InstallDetector : IInstallDetector
{
    private const string GameExecutableFileName = "KSA.exe";

    private readonly IInstallCandidateSource _candidates;
    private readonly ILoaderAdopter _loaderAdopter;
    private readonly string _loadersRoot;

    /// <param name="loadersRoot">The folder Borea installs loaders into. Each loader's own folder there is a candidate too.</param>
    public InstallDetector(IInstallCandidateSource candidates, ILoaderAdopter loaderAdopter, string loadersRoot)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _loaderAdopter = loaderAdopter ?? throw new ArgumentNullException(nameof(loaderAdopter));
        ArgumentException.ThrowIfNullOrWhiteSpace(loadersRoot);
        _loadersRoot = loadersRoot;
    }

    public async Task<InstallDetection> DetectAsync(IReadOnlyList<ModMetadata> loaders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loaders);

        var detectedLoaders = new List<DetectedLoader>();
        var gameDirectories = new List<string>(_candidates.GetGameDirectories());

        // Borea's own loader folders come first, so a loader Borea placed wins over a copy elsewhere
        var loaderDirectories = loaders
            .Select(loader => Path.Combine(_loadersRoot, loader.ModId))
            .Concat(_candidates.GetLoaderDirectories());
        foreach (var directory in ExistingDirectories(loaderDirectories))
        {
            foreach (var loader in loaders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LoaderAdoptionResult result;
                try
                {
                    result = await _loaderAdopter.InspectAsync(loader, [], directory, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
                {
                    continue;
                }

                detectedLoaders.Add(new DetectedLoader(result.LoaderId, result.Directory, result.RawVersion));
                if (!string.IsNullOrWhiteSpace(result.ConfiguredGameDirectory))
                    gameDirectories.Add(GameDirectoryOf(result.ConfiguredGameDirectory));
                break;
            }
        }

        var games = new List<DetectedGame>();
        foreach (var directory in ExistingDirectories(gameDirectories))
        {
            if (!File.Exists(Path.Combine(directory, GameExecutableFileName)))
                continue;

            if (InstalledGameVersionProvider.Read(directory) is { } version)
                games.Add(new DetectedGame(directory, version));
        }

        return new InstallDetection(games, detectedLoaders);
    }

    // StarMap accepts the path of KSA.dll as well as the game folder
    private static string GameDirectoryOf(string configured) =>
        File.Exists(configured) ? Path.GetDirectoryName(configured) ?? configured : configured;

    private static List<string> ExistingDirectories(IEnumerable<string> candidates)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var seen = new HashSet<string>(comparer);
        var directories = new List<string>();
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
                continue;

            string directory;
            try
            {
                directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (Directory.Exists(directory) && seen.Add(directory))
                directories.Add(directory);
        }

        return directories;
    }
}
