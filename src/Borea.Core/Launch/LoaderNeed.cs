using Borea.Core.Instances;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Which mod loader the mods of an instance need, and which versions of it all
/// of them accept. <see cref="LaunchLoaderChoice"/> decides what starts the
/// instance; this says what the mods ask for, for the instance page and
/// <c>borea instance show</c>.
/// </summary>
public sealed class LoaderNeed
{
    /// <summary>Each loader the mods name, ordered by id, with the mods that name it.</summary>
    public IReadOnlyList<NeededLoader> Loaders { get; }

    /// <summary>No mod names a loader.</summary>
    public bool IsNone => Loaders.Count == 0;

    /// <summary>The mods name more than one loader, and one launch cannot start them all.</summary>
    public bool NeedsDifferentLoaders => Loaders.Count > 1;

    private LoaderNeed(IReadOnlyList<NeededLoader> loaders) => Loaders = loaders;

    public static LoaderNeed For(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Of(instance.Mods.Select(mod => (mod.ModId, mod.Metadata.Loader)));
    }

    /// <summary>
    /// What the mods of <paramref name="instance"/> need once <paramref name="releases"/> are installed, each in place of the
    /// installed copy of its mod. Without an instance only the releases count.
    /// </summary>
    public static LoaderNeed With(Instance? instance, IReadOnlyCollection<ModVersionMetadata> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);

        var kept = instance?.Mods.Where(mod => !releases.Any(release => ModIds.Equals(release.ModId, mod.ModId))) ?? [];
        return Of(kept.Select(mod => (mod.ModId, mod.Metadata.Loader)).Concat(releases.Select(release => (release.ModId, release.Loader))));
    }

    private static LoaderNeed Of(IEnumerable<(string ModId, LoaderRequirement? Loader)> mods)
    {
        var loaders = mods
            .Where(mod => mod.Loader is not null)
            .Select(mod => (mod.ModId, Loader: mod.Loader!))
            .GroupBy(mod => mod.Loader.LoaderId, ModIds.Comparer)
            .OrderBy(group => group.Key, ModIds.Comparer)
            .Select(group => NeededLoader.From(group.Key, group.ToList()))
            .ToList();

        return new LoaderNeed(loaders);
    }
}

/// <summary>
/// One loader the mods of an instance name. The range is what every one of
/// them accepts: the highest of their minimums and the lowest of their maximums.
/// </summary>
public sealed class NeededLoader
{
    public string LoaderId { get; }

    /// <summary>The oldest version every mod accepts.</summary>
    public ModVersion MinVersion { get; }

    /// <summary>The newest version every mod accepts, or null when none of them sets one.</summary>
    public ModVersion? MaxVersion { get; }

    /// <summary>The ids of the mods that need the loader, ordered by id.</summary>
    public IReadOnlyList<string> NeededBy { get; }

    /// <summary>What each of those mods asks for, in the order of <see cref="NeededBy"/>. On a conflict this says which mod asks for which end.</summary>
    public IReadOnlyList<ModLoaderBounds> Requirements { get; }

    /// <summary>The mods ask for versions that do not overlap, so no single version satisfies them all.</summary>
    public bool HasConflict => MaxVersion is { } max && max.CompareTo(MinVersion) < 0;

    private NeededLoader(string loaderId, ModVersion minVersion, ModVersion? maxVersion, IReadOnlyList<ModLoaderBounds> requirements)
    {
        LoaderId = loaderId;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
        Requirements = requirements;
        NeededBy = requirements.Select(requirement => requirement.ModId).ToList();
    }

    internal static NeededLoader From(string loaderId, IReadOnlyList<(string ModId, LoaderRequirement Loader)> mods)
    {
        var requirements = mods.Select(mod => mod.Loader).ToList();
        var min = requirements.Select(requirement => requirement.MinVersion).Max();
        var maxima = requirements.Select(requirement => requirement.MaxVersion).OfType<ModVersion>().ToList();
        var max = maxima.Count == 0 ? (ModVersion?)null : maxima.Min();
        var bounds = mods
            .OrderBy(mod => mod.ModId, ModIds.Comparer)
            .Select(mod => new ModLoaderBounds(mod.ModId, mod.Loader.MinVersion, mod.Loader.MaxVersion))
            .ToList();
        return new NeededLoader(loaderId, min, max, bounds);
    }

    /// <summary>Whether <paramref name="version"/> is inside the range. False when the range is empty.</summary>
    public bool Accepts(ModVersion version) =>
        !HasConflict && version.CompareTo(MinVersion) >= 0 && (MaxVersion is not { } max || version.CompareTo(max) <= 0);
}

/// <summary>The loader versions one mod accepts. A null maximum is open.</summary>
public sealed record ModLoaderBounds(string ModId, ModVersion MinVersion, ModVersion? MaxVersion);
