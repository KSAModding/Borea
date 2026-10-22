using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>
/// The mod loader setting of an instance. Without a loader id Borea chooses the loader, without a version it chooses
/// the newest release that fits the instance, and with both the instance keeps that exact release.
/// </summary>
public sealed record LoaderSetting
{
    public static LoaderSetting Automatic { get; } = new(null, null);

    public string? LoaderId { get; }

    public ModVersion? Version { get; }

    public bool IsPinned => Version is not null;

    private LoaderSetting(string? loaderId, ModVersion? version)
    {
        LoaderId = loaderId;
        Version = version;
    }

    /// <summary>The newest fitting release of the given loader.</summary>
    public static LoaderSetting ForLoader(string loaderId)
    {
        ModIds.Validate(loaderId, nameof(loaderId));
        return new(loaderId, null);
    }

    /// <summary>The exact release of the given loader, also when a newer release fits.</summary>
    public static LoaderSetting Pinned(string loaderId, ModVersion version)
    {
        ModIds.Validate(loaderId, nameof(loaderId));
        return new(loaderId, version);
    }

    /// <summary>Equal when the loader and the version are the same, also when the loader id is in another letter case.</summary>
    public bool Equals(LoaderSetting? other)
        => other is not null && ModIds.Equals(LoaderId, other.LoaderId) && Version == other.Version;

    public override int GetHashCode()
        => HashCode.Combine(LoaderId is null ? 0 : ModIds.Comparer.GetHashCode(LoaderId), Version);
}
