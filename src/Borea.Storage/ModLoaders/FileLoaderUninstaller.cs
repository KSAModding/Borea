using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Settings;

namespace Borea.Storage.ModLoaders;

/// <summary>
/// File-backed <see cref="ILoaderUninstaller"/>.
/// </summary>
public sealed class FileLoaderUninstaller : ILoaderUninstaller
{
    private readonly IBoreaSettingsRepository _settings;

    public FileLoaderUninstaller(IBoreaSettingsRepository settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<LoaderUninstallResult> UninstallAsync(
        string loaderId,
        CancellationToken cancellationToken = default)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null || !settings.LoaderInstallations.TryGetValue(loaderId, out var installation))
            return new LoaderUninstallResult(loaderId, null, RecordRemoved: false, DirectoryRemoved: false);

        var directoryRemoved = false;
        if (!installation.IsAdopted && Directory.Exists(installation.DirectoryPath))
        {
            if (!Path.IsPathFullyQualified(installation.DirectoryPath))
                throw new InvalidOperationException($"The recorded directory for '{loaderId}' is not absolute, so Borea will not delete it.");

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(Path.GetFullPath(installation.DirectoryPath), recursive: true);
            directoryRemoved = true;
        }

        await _settings.SaveAsync(
            settings.WithoutLoaderInstallation(loaderId),
            cancellationToken).ConfigureAwait(false);

        return new LoaderUninstallResult(loaderId, installation.DirectoryPath, RecordRemoved: true, DirectoryRemoved: directoryRemoved);
    }
}
