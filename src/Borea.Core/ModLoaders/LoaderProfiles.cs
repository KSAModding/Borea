using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>The loader profiles by loader id, which is compared the way mod ids are.</summary>
public sealed class LoaderProfiles
{
    private readonly Dictionary<string, ILoaderProfile> _byId = new(ModIds.Comparer);

    /// <summary>The profiles of the loaders Borea knows beyond their listing.</summary>
    public static LoaderProfiles Known { get; } = new([new StarMapLoaderProfile()]);

    public LoaderProfiles(IEnumerable<ILoaderProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        foreach (var profile in profiles)
        {
            if (profile.LoaderId is not { } id)
                throw new ArgumentException("A loader profile in the registry needs a loader id.", nameof(profiles));

            _byId.Add(id, profile);
        }

        All = _byId.Values.ToList();
    }

    /// <summary>The profiles in the registry, without the neutral one.</summary>
    public IReadOnlyList<ILoaderProfile> All { get; }

    /// <summary>The profile of the loader, or <see cref="NeutralLoaderProfile"/> when the registry has none for it.</summary>
    public ILoaderProfile For(string? loaderId) =>
        loaderId is not null && _byId.TryGetValue(loaderId, out var profile) ? profile : NeutralLoaderProfile.Instance;

    /// <summary>The first profile whose mod.toml table a mod.toml has, or null.</summary>
    /// <param name="hasTable">Whether the mod.toml has a top-level key of this name.</param>
    public ILoaderProfile? ForModToml(Func<string, bool> hasTable)
    {
        ArgumentNullException.ThrowIfNull(hasTable);
        return All.FirstOrDefault(profile => profile.ModTomlSection is { } section && hasTable(section));
    }

    /// <summary>The first profile whose loader loads a mod without its mod.toml table, or null.</summary>
    public ILoaderProfile? ForModWithoutTable() => All.FirstOrDefault(profile => profile.LoadsModWithoutTable);
}
