using Borea.Core.Listings;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>
/// The ids the documents of content-index hold on one commit, which a state has to name: listings/&lt;id&gt;.toml, vehicles/&lt;id&gt;.toml,
/// saves/&lt;id&gt;.toml, and packs/&lt;id&gt;/&lt;version&gt;.toml, where the checks of the index keep the file name equal to the version.
/// Ids compare case-insensitively, as the index does.
/// </summary>
public sealed class IndexContents
{
    public const string PacksFolder = "packs";

    private readonly Dictionary<string, (string Id, string Folder)> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _packs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _versions = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="paths">Every file path of the commit, relative to the repository root.</param>
    public static IndexContents FromPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var contents = new IndexContents();
        foreach (var path in paths)
        {
            if (!path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = path.Split('/');
            var stem = parts[^1][..^".toml".Length];
            if (parts is [var folder, _] && ListingFolders.All.Contains(folder, StringComparer.Ordinal))
            {
                contents._listings.TryAdd(stem, (stem, folder));
            }
            else if (parts is [PacksFolder, var pack, _])
            {
                contents._packs.TryAdd(pack, pack);
                if (!contents._versions.TryGetValue(pack, out var versions))
                    contents._versions[pack] = versions = new HashSet<string>(StringComparer.Ordinal);

                versions.Add(stem);
            }
        }

        return contents;
    }

    /// <summary>The id as its listing file spells it, in any folder of <see cref="ListingFolders.All"/>, or null when there is no such listing.</summary>
    public string? ListingId(string id) => _listings.TryGetValue(id, out var listing) ? listing.Id : null;

    /// <summary>The path of the listing file, such as vehicles/ExampleRover.toml, or null when there is no such listing.</summary>
    public string? ListingPath(string id) => _listings.TryGetValue(id, out var listing) ? $"{listing.Folder}/{listing.Id}.toml" : null;

    /// <summary>The id as its pack folder spells it, or null when there is no such pack.</summary>
    public string? PackId(string id) => _packs.GetValueOrDefault(id);

    public bool HasPackVersion(string id, string version) =>
        _versions.TryGetValue(id, out var versions) && versions.Contains(version);

    /// <summary>
    /// The version as the file name of the pack spells it, or null when the pack has no such version. A version that Borea shows
    /// without its build metadata finds the one file that differs from it only there.
    /// </summary>
    public string? PackVersion(string id, string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!_versions.TryGetValue(id, out var versions))
            return null;
        if (versions.Contains(version))
            return version;
        if (!ModVersion.TryParse(version, out var wanted))
            return null;

        var same = versions.Where(stem => ModVersion.TryParse(stem, out var parsed) && parsed == wanted).Take(2).ToList();
        return same.Count == 1 ? same[0] : null;
    }
}
