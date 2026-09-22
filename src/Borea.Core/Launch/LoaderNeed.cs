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

        var loaders = instance.Mods
            .Where(mod => mod.Metadata.Loader is not null)
            .GroupBy(mod => mod.Metadata.Loader!.LoaderId, ModIds.Comparer)
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

    internal static NeededLoader From(string loaderId, IReadOnlyList<InstalledMod> mods)
    {
        var requirements = mods.Select(mod => mod.Metadata.Loader!).ToList();
        var min = requirements.Select(requirement => requirement.MinVersion).Max();
        var maxima = requirements.Select(requirement => requirement.MaxVersion).OfType<ModVersion>().ToList();
        var max = maxima.Count == 0 ? (ModVersion?)null : maxima.Min();
        var bounds = mods
            .OrderBy(mod => mod.ModId, ModIds.Comparer)
            .Select(mod => new ModLoaderBounds(mod.ModId, mod.Metadata.Loader!.MinVersion, mod.Metadata.Loader.MaxVersion))
            .ToList();
        return new NeededLoader(loaderId, min, max, bounds);
    }

    /// <summary>Whether <paramref name="version"/> is inside the range. False when the range is empty.</summary>
    public bool Accepts(ModVersion version) =>
        !HasConflict && version.CompareTo(MinVersion) >= 0 && (MaxVersion is not { } max || version.CompareTo(max) <= 0);
}

/// <summary>The loader versions one mod accepts. A null maximum is open.</summary>
public sealed record ModLoaderBounds(string ModId, ModVersion MinVersion, ModVersion? MaxVersion);
