using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// The loader a code mod needs: the loader's id with inclusive version bounds.
/// </summary>
public sealed class LoaderRequirement
{
    /// <summary>
    /// The loader's content id, referencing a mod-loader listing.
    /// </summary>
    public string LoaderId { get; }

    /// <summary>
    /// Oldest known-working version, inclusive.
    /// </summary>
    public ModVersion MinVersion { get; }

    /// <summary>
    /// Newest known-working version, inclusive. Absent means open.
    /// </summary>
    public ModVersion? MaxVersion { get; }

    /// <summary>
    /// Where the entry came from in a generated release file, null in authored metadata.
    /// </summary>
    public MetadataSource? Source { get; }

    public LoaderRequirement(string loaderId, ModVersion minVersion, ModVersion? maxVersion = null, MetadataSource? source = null)
    {
        ModIds.Validate(loaderId, nameof(loaderId));

        if (maxVersion is { } max && max.CompareTo(minVersion) < 0)
            throw new ArgumentOutOfRangeException(nameof(maxVersion), "The maximum version cannot be below the minimum.");

        LoaderId = loaderId;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
        Source = source;
    }

    /// <summary>
    /// Whether the given loader version lies within the inclusive bounds.
    /// </summary>
    public bool BoundsContain(ModVersion version)
    {
        if (version.CompareTo(MinVersion) < 0)
            return false;

        if (MaxVersion is { } max && version.CompareTo(max) > 0)
            return false;

        return true;
    }
}
