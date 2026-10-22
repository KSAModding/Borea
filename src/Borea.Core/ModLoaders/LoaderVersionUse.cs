using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>An instance that starts a loader version, and whether its setting pins that version.</summary>
public sealed record LoaderVersionUser(Instance Instance, bool IsPinned);

/// <summary>
/// One installed loader version and the instances that start it.
/// </summary>
public sealed record LoaderVersionUse(InstalledLoaderVersion Version, IReadOnlyList<LoaderVersionUser> Users)
{
    public bool IsUsed => Users.Count > 0;

    /// <summary>
    /// Every installed version with the instances that start it, in the order of <paramref name="installed"/>. Each instance is
    /// resolved with <see cref="InstanceLoaderResolver"/> and the same inputs as its launch, so a version that a launch would
    /// start is never reported as unused. An instance whose loader is only missing uses no installed version.
    /// </summary>
    /// <param name="gameOf">The game build of an instance.</param>
    public static async Task<IReadOnlyList<LoaderVersionUse>> UsedByAsync(
        IEnumerable<Instance> instances,
        IReadOnlyList<InstalledLoaderVersion> installed,
        IModRepository held,
        IModRepository sources,
        IModRepository? channelMods,
        ReleaseChannel channel,
        Func<Instance, GameVersion?> gameOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(gameOf);

        var users = new Dictionary<InstalledLoaderVersion, List<LoaderVersionUser>>(ReferenceEqualityComparer.Instance);
        foreach (var version in installed)
            users[version] = [];

        foreach (var instance in instances)
        {
            var resolution = await InstanceLoaderResolver.ResolveAsync(instance, installed, held, sources, channelMods, channel, gameOf(instance), givenLoaderId: null, cancellationToken).ConfigureAwait(false);
            if (resolution.Version is { Start: { } start } version && users.TryGetValue(start, out var list))
                list.Add(new LoaderVersionUser(instance, version.IsPinned));
        }

        return installed.Select(version => new LoaderVersionUse(version, users[version])).ToList();
    }
}
