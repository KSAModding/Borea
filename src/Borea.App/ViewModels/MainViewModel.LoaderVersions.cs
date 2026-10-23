using System;
using System.IO;
using System.Threading.Tasks;
using Borea.Composition;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>
/// The loader version that starts an instance, chosen from the installed versions with the setting of the instance.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// The game build that the loader of <paramref name="instance"/> starts, and its folder. Every instance uses the
    /// game in the settings today, and per-instance game builds replace the body of this method.
    /// </summary>
    private static (GameVersion? Version, string? Directory) LoaderGameOf(BoreaServices services, Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return (services.InstalledVersion.GetInstalledVersion()?.Version, services.Paths.GetGameDirectoryPath());
    }

    /// <summary>
    /// The loader and its version for a launch of <paramref name="instance"/>. The listings come from the index that
    /// Borea holds and only else from the live index, and the versions are rated only with the held releases, so a
    /// launch never asks the network for a release.
    /// </summary>
    /// <exception cref="InvalidOperationException">No listings could be read.</exception>
    /// <exception cref="IOException">No listings could be read.</exception>
    private static async Task<InstanceLoaderResolution> ResolveLaunchLoaderAsync(BoreaServices services, Instance instance)
    {
        var installed = await services.LoaderVersions.ListAsync();
        IModRepository? releases = services.OfflineContentIndex;
        try
        {
            await services.OfflineContentIndex.GetAvailableModsAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Borea holds no index it can read, so nothing is rated or offered, as when it is offline
            releases = null;
        }

        // only the index lists mod loaders, so SpaceDock cannot change the choice and is not asked
        return await InstanceLoaderResolver.ResolveAsync(
            instance,
            installed,
            services.OfflineContentIndex,
            services.ContentIndex,
            releases,
            services.Settings.ReleaseChannel,
            LoaderGameOf(services, instance).Version);
    }
}
