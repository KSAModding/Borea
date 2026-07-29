using Borea.Core.Mods;
using Borea.Core.Game;
using System.Collections.ObjectModel;

namespace Borea.Core.ModPacks;

/// <summary>
/// Describes a named collection of specific mod versions intended to be
/// installed/activated together. Immutable once constructed.
/// </summary>
public sealed class ModPackMetadata
{
    public string ModPackId { get; }
    public string Source { get; }
    public string Name { get; }
    public string Author { get; }
    public ModVersion Version { get; }
    public GameVersion BuiltForGameVersion { get; }
    public string Description { get; }
    public string? HomepageUrl { get; }
    public DateTimeOffset ReleasedAt { get; }
    public IReadOnlyList<ModPackEntry> Mods { get; }

    public ModPackMetadata(
        string modPackId,
        string source,
        string name,
        string author,
        ModVersion version,
        GameVersion builtForGameVersion,
        string description,
        DateTimeOffset releasedAt,
        IReadOnlyList<ModPackEntry> mods,
        string? homepageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(modPackId))
            throw new ArgumentException("Mod pack ID cannot be null or whitespace.", nameof(modPackId));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));

        if (string.IsNullOrWhiteSpace(author))
            throw new ArgumentException("Author cannot be null or whitespace.", nameof(author));

        if (description is null)
            throw new ArgumentNullException(nameof(description));

        if (mods is null || mods.Count == 0)
            throw new ArgumentException("A mod pack must contain at least one mod.", nameof(mods));

        ModPackId = modPackId;
        Source = source;
        Name = name;
        Author = author;
        Version = version;
        BuiltForGameVersion = builtForGameVersion;
        Description = description;
        ReleasedAt = releasedAt;
        Mods = new ReadOnlyCollection<ModPackEntry>(mods.ToArray());
        HomepageUrl = homepageUrl;
    }
}

/// <summary>
/// A single mod+version entry within a mod pack.
/// </summary>
public readonly record struct ModPackEntry(string ModId, Mods.ModVersion Version);