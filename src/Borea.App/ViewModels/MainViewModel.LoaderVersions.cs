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
    /// The loader and its version for a launch of <paramref name="instance"/>. The listings come from the index that
    /// Borea holds and only else from the live index, and the versions are rated only with the held releases, so a
    /// launch never asks the network for a release.
    /// </summary>
    /// <exception cref="InvalidOperationException">No listings could be read.</exception>
    /// <exception cref="IOException">No listings could be read.</exception>
    private static async Task<InstanceLoaderResolution> ResolveLaunchLoaderAsync(BoreaServices services, Instance instance)
    {
        var installed = await services.LoaderVersions.ListAsync();

        // only the index lists mod loaders, so SpaceDock cannot change the choice and is not asked
        return await InstanceLoaderResolver.ResolveAsync(
            instance,
            installed,
            services.OfflineContentIndex,
            services.ContentIndex,
            await HeldLoaderReleasesAsync(services),
            services.Settings.ReleaseChannel,
            services.InstanceGames.Resolve(instance).Version?.Version);
    }

    /// <summary>The releases that rate and offer loader versions without a request, or null when Borea holds no index it can read.</summary>
    private static async Task<IModRepository?> HeldLoaderReleasesAsync(BoreaServices services)
    {
        try
        {
            await services.OfflineContentIndex.GetAvailableModsAsync();
            return services.OfflineContentIndex;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // nothing is rated or offered then, as when Borea is offline
            return null;
        }
    }
}
