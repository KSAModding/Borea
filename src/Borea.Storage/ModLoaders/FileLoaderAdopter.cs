using System.Collections.ObjectModel;
using System.Diagnostics;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// File-backed <see cref="ILoaderAdopter"/>.
/// </summary>
public sealed class FileLoaderAdopter : ILoaderAdopter
{
    private readonly IBoreaSettingsRepository _settings;
    private readonly ILoaderConfigurationReader _configuration;

    public FileLoaderAdopter(
        IBoreaSettingsRepository settings,
        ILoaderConfigurationReader configuration)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<LoaderAdoptionResult> AdoptAsync(
        ModMetadata loader,
        IReadOnlyList<ModVersionMetadata> releases,
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(releases);

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader can be adopted.", nameof(loader));

        var launch = loader.Provides?.Launch;
        if (launch is null)
            throw new NotSupportedException($"The listing of {loader.Name} does not say what to launch, so Borea cannot identify it on disk.");

        var loaderDirectory = Absolute(directory, nameof(directory));
        var executable = Path.GetFullPath(Path.Combine(loaderDirectory, launch.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(executable))
            throw new InvalidOperationException($"'{loaderDirectory}' does not hold the listed launch file '{launch}'.");

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false) ?? new BoreaSettings(null);
        if (settings.LoaderInstallations.TryGetValue(loader.ModId, out var recorded)
            && !SamePath(recorded.DirectoryPath, loaderDirectory))
        {
            throw new InvalidOperationException(
                $"{loader.Name} is already recorded at '{recorded.DirectoryPath}'. Remove that record or adopt that directory instead.");
        }

        var rawVersion = ReadFileVersion(executable);
        var version = MatchVersion(loader, releases, rawVersion);
        var configuredGameDirectory = await _configuration
            .ReadConfiguredGamePathAsync(loader, loaderDirectory, cancellationToken)
            .ConfigureAwait(false);
        var gameDirectoryMatches = CompareGameDirectory(settings.GameDirectoryPath, configuredGameDirectory);

        var warnings = Warnings(loader, rawVersion, version, settings.GameDirectoryPath, configuredGameDirectory, gameDirectoryMatches);
        var installations = settings.LoaderInstallations.ToDictionary(pair => pair.Key, pair => pair.Value, ModIds.Comparer);
        installations.Remove(loader.ModId);
        installations[loader.ModId] = new LoaderInstallation(loaderDirectory, version, rawVersion, isAdopted: true);

        await _settings.SaveAsync(
            new BoreaSettings(settings.GameDirectoryPath, loaderInstallations: installations),
            cancellationToken).ConfigureAwait(false);

        return new LoaderAdoptionResult(
            loader.ModId,
            loaderDirectory,
            rawVersion,
            version,
            configuredGameDirectory,
            gameDirectoryMatches,
            new ReadOnlyCollection<string>(warnings));
    }

    private static ModVersion? MatchVersion(
        ModMetadata loader,
        IReadOnlyList<ModVersionMetadata> releases,
        string? rawVersion)
    {
        if (!TryParseFileVersion(rawVersion, out var candidate))
            return null;

        var release = releases.FirstOrDefault(release =>
            release.Type == ContentType.ModLoader
            && ModIds.Equals(release.ModId, loader.ModId)
            && release.Version == candidate);
        return release?.Version;
    }

    private static List<string> Warnings(
        ModMetadata loader,
        string? rawVersion,
        ModVersion? version,
        string? managedGameDirectory,
        string? configuredGameDirectory,
        bool? gameDirectoryMatches)
    {
        var warnings = new List<string>();

        if (rawVersion is null)
            warnings.Add($"Borea could not read a file version from the launch file of {loader.Name}. The loader version is unknown.");
        else if (version is null)
            warnings.Add($"The file version '{rawVersion}' does not match an indexed release of {loader.Name}. The loader version is unknown.");

        if (gameDirectoryMatches == false)
        {
            warnings.Add(
                $"{loader.Name} is configured for '{configuredGameDirectory}', not the game directory Borea manages at '{managedGameDirectory}'. The configuration was not changed.");
        }
        else if (gameDirectoryMatches is null)
        {
            warnings.Add(
                $"Borea could not confirm the game directory configured for {loader.Name}. The configuration was not changed.");
        }

        return warnings;
    }

    private static string? ReadFileVersion(string executable)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            var version = !string.IsNullOrWhiteSpace(info.FileVersion)
                ? info.FileVersion
                : !string.IsNullOrWhiteSpace(info.ProductVersion)
                    ? info.ProductVersion
                    : null;
            return version?.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryParseFileVersion(string? rawVersion, out ModVersion version)
    {
        if (ModVersion.TryParse(rawVersion, out version))
            return true;

        var parts = rawVersion?.Split('.');
        return parts is { Length: 4 }
            && parts[3] == "0"
            && ModVersion.TryParse(string.Join('.', parts.Take(3)), out version);
    }

    private static bool? CompareGameDirectory(string? managed, string? configured)
    {
        if (string.IsNullOrWhiteSpace(managed) || configured is null)
            return null;

        if (string.IsNullOrWhiteSpace(configured))
            return false;

        if (!Path.IsPathFullyQualified(managed) || !Path.IsPathFullyQualified(configured))
            return false;

        return SamePath(managed, configured);
    }

    private static string Absolute(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A loader directory is required.", paramName);

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The loader directory must be absolute.", paramName);

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool SamePath(string left, string right)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}
