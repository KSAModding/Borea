using System.Collections.ObjectModel;
using System.Diagnostics;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;
using Borea.Storage.Files;
using Borea.Storage.Launch;

namespace Borea.Storage.ModLoaders;

public sealed class FileLoaderAdopter : ILoaderAdopter
{
    private readonly IBoreaSettingsRepository _settings;
    private readonly ILoaderConfigurationReader _configuration;
    private readonly string? _loadersRoot;

    /// <param name="loadersRoot">Borea's own Loaders root. A loader adopted below it is recorded as Borea-owned.</param>
    public FileLoaderAdopter(
        IBoreaSettingsRepository settings,
        ILoaderConfigurationReader configuration,
        string? loadersRoot = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _loadersRoot = loadersRoot;
    }

    public async Task<LoaderAdoptionResult> AdoptAsync(
        ModMetadata loader,
        IReadOnlyList<ModVersionMetadata> releases,
        string directory,
        CancellationToken cancellationToken = default)
    {
        var (result, settings, installation) = await InspectCoreAsync(loader, releases, directory, cancellationToken).ConfigureAwait(false);
        await _settings.SaveAsync(
            settings.WithLoaderInstallation(loader.ModId, installation),
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<LoaderAdoptionResult> InspectAsync(
        ModMetadata loader,
        IReadOnlyList<ModVersionMetadata> releases,
        string directory,
        CancellationToken cancellationToken = default)
    {
        var (result, _, _) = await InspectCoreAsync(loader, releases, directory, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<(LoaderAdoptionResult Result, BoreaSettings Settings, LoaderInstallation Installation)> InspectCoreAsync(
        ModMetadata loader,
        IReadOnlyList<ModVersionMetadata> releases,
        string directory,
        CancellationToken cancellationToken)
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
            && !FileSystemPaths.SamePath(recorded.DirectoryPath, loaderDirectory))
        {
            throw new InvalidOperationException(
                $"{loader.Name} is already recorded at '{recorded.DirectoryPath}'. Remove that record or adopt that directory instead.");
        }

        var rawVersion = ReadRawVersion(executable);
        var version = MatchVersion(loader, releases, rawVersion);
        string? configuredGameDirectory = null;
        string? configurationWarning = null;
        try
        {
            configuredGameDirectory = await _configuration
                .ReadConfiguredGamePathAsync(loader, loaderDirectory, settings.GameDirectoryPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            configurationWarning =
                $"Borea could not inspect the game directory configured for {loader.Name}. {exception.Message} The configuration was not changed.";
        }

        var gameDirectoryMatches = CompareGameDirectory(settings.GameDirectoryPath, configuredGameDirectory);

        var warnings = Warnings(
            loader,
            rawVersion,
            version,
            settings.GameDirectoryPath,
            configuredGameDirectory,
            gameDirectoryMatches,
            configurationWarning);
        var installation = new LoaderInstallation(
            loaderDirectory,
            version,
            rawVersion,
            isAdopted: (_loadersRoot is null || !LoaderLayoutMigration.IsBelowLoadersRoot(_loadersRoot, loaderDirectory)) && (recorded?.IsAdopted ?? true));

        var result = new LoaderAdoptionResult(
            loader.ModId,
            loaderDirectory,
            rawVersion,
            version,
            configuredGameDirectory,
            gameDirectoryMatches,
            new ReadOnlyCollection<string>(warnings));
        return (result, settings, installation);
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
        bool? gameDirectoryMatches,
        string? configurationWarning)
    {
        var warnings = new List<string>();

        if (rawVersion is null)
            warnings.Add($"Borea could not read a file version from the launch file of {loader.Name}. The loader version is unknown.");
        else if (version is null)
            warnings.Add($"The file version '{rawVersion}' does not match an indexed release of {loader.Name}. The loader version is unknown.");

        if (configurationWarning is not null)
        {
            warnings.Add(configurationWarning);
        }
        else if (gameDirectoryMatches == false)
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

    /// <summary>The file version of the launch file, or of the assembly beside an app host that carries none.</summary>
    internal static string? ReadRawVersion(string executable) =>
        // Outside Windows FileVersionInfo reads only assembly metadata, so a Windows app host shows no version there.
        ReadFileVersion(executable) ?? (DotnetHost.AssemblyBeside(executable) is { } assembly ? ReadFileVersion(assembly) : null);

    internal static string? ReadFileVersion(string executable)
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

    internal static bool TryParseFileVersion(string? rawVersion, out ModVersion version)
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

        return FileSystemPaths.SamePath(managed, configured);
    }

    private static string Absolute(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A loader directory is required.", paramName);

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("The loader directory must be absolute.", paramName);

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
