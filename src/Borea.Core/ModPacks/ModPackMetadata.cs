using Borea.Core.Mods;
using System.Collections.ObjectModel;

namespace Borea.Core.ModPacks;

/// <summary>
/// One version of a mod pack (RFC 0031): the shared authored core plus the pack extension.
/// Pure reference metadata, so no releases, no loader, and no install data.
/// </summary>
public sealed class ModPackMetadata
{
    public int SpecVersion { get; }

    /// <summary>Ids compare case-insensitively and share one namespace with every content type.</summary>
    public string ModPackId { get; }

    public ContentType Type => ContentType.ModPack;

    /// <summary>Which source this metadata came from. Borea-internal, not a format field.</summary>
    public string Source { get; }

    /// <summary>The display name.</summary>
    public string Name { get; }

    /// <summary>Display names of the authors.</summary>
    public IReadOnlyList<string> Authors { get; }

    /// <summary>One or two sentences for list and search views.</summary>
    public string Abstract { get; }

    /// <summary>Longer CommonMark text on top of the abstract.</summary>
    public string? Description { get; }

    /// <summary>SPDX expression. It covers the pack's own text, never a member's files.</summary>
    public string License { get; }

    /// <summary>Free-form lowercase tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The author's declaration about the pack.</summary>
    public ModStatus Status { get; }

    /// <summary>Only meaningful together with a deprecated status.</summary>
    public string? SupersededBy { get; }

    /// <summary>Keys compare case-insensitively; "forums" is required.</summary>
    public IReadOnlyDictionary<string, string> Links { get; }

    /// <summary>The required KSA forums thread for this pack.</summary>
    public string ForumUrl => Links["forums"];

    /// <summary>Oldest game version known to work.</summary>
    public string GameMin { get; }

    /// <summary>Null means no known upper limit.</summary>
    public string? GameMax { get; }

    /// <summary>Null means no known platform restriction.</summary>
    public IReadOnlyList<string>? Os { get; }

    /// <summary>The document is the release, so the version is authored.</summary>
    public ModVersion Version { get; }

    public DateTimeOffset ReleasedAt { get; }

    /// <summary>URL or free text for this pack version.</summary>
    public string? Changelog { get; }

    /// <summary>The curated mods, each an exact pin. At least one.</summary>
    public IReadOnlyList<ModPackEntry> Mods { get; }

    /// <summary>Pinned vehicles, the same entry shape as the mods. Empty when none.</summary>
    public IReadOnlyList<ModPackEntry> Vehicles { get; }

    /// <summary>Pinned saves, the same entry shape as the mods. Empty when none.</summary>
    public IReadOnlyList<ModPackEntry> Saves { get; }

    public ModPackMetadata(
        int specVersion,
        string modPackId,
        string source,
        string name,
        IReadOnlyList<string> authors,
        string abstractText,
        string license,
        IReadOnlyDictionary<string, string> links,
        string gameMin,
        ModVersion version,
        DateTimeOffset releasedAt,
        IReadOnlyList<ModPackEntry> mods,
        IReadOnlyList<string>? tags = null,
        string? description = null,
        ModStatus status = ModStatus.Active,
        string? supersededBy = null,
        string? gameMax = null,
        IReadOnlyList<string>? os = null,
        string? changelog = null,
        IReadOnlyList<ModPackEntry>? vehicles = null,
        IReadOnlyList<ModPackEntry>? saves = null)
    {
        if (specVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Spec version must be a positive integer.");

        ModIds.Validate(modPackId, nameof(modPackId));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));

        if (authors is null || authors.Count == 0)
            throw new ArgumentException("At least one author is required.", nameof(authors));

        if (abstractText is null)
            throw new ArgumentNullException(nameof(abstractText));

        if (string.IsNullOrWhiteSpace(license))
            throw new ArgumentException("License cannot be null or whitespace.", nameof(license));

        if (links is null || !links.Any(p => string.Equals(p.Key, "forums", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Value)))
            throw new ArgumentException("The links must contain a forums entry.", nameof(links));

        var duplicateKey = links.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (duplicateKey is not null)
            throw new ArgumentException($"Link key '{duplicateKey}' appears more than once when compared case-insensitively.", nameof(links));

        if (string.IsNullOrWhiteSpace(gameMin))
            throw new ArgumentException("The minimum game version is required.", nameof(gameMin));

        if (supersededBy is not null)
            ModIds.Validate(supersededBy, nameof(supersededBy));

        if (mods is null || mods.Count == 0)
            throw new ArgumentException("A mod pack must contain at least one mod.", nameof(mods));

        RejectRepeatedPins(
            (mods, nameof(mods)),
            (vehicles ?? Array.Empty<ModPackEntry>(), nameof(vehicles)),
            (saves ?? Array.Empty<ModPackEntry>(), nameof(saves)));

        SpecVersion = specVersion;
        ModPackId = modPackId;
        Source = source;
        Name = name;
        Authors = new ReadOnlyCollection<string>(authors.ToArray());
        Abstract = abstractText;
        Description = description;
        License = license;
        Tags = tags is null ? Array.Empty<string>() : new ReadOnlyCollection<string>(tags.ToArray());
        Status = status;
        SupersededBy = supersededBy;
        Links = new Dictionary<string, string>(links, StringComparer.OrdinalIgnoreCase);
        GameMin = gameMin;
        GameMax = gameMax;
        Os = os is null ? null : new ReadOnlyCollection<string>(os.ToArray());
        Version = version;
        ReleasedAt = releasedAt;
        Changelog = changelog;
        Mods = new ReadOnlyCollection<ModPackEntry>(mods.ToArray());
        Vehicles = vehicles is null ? Array.Empty<ModPackEntry>() : new ReadOnlyCollection<ModPackEntry>(vehicles.ToArray());
        Saves = saves is null ? Array.Empty<ModPackEntry>() : new ReadOnlyCollection<ModPackEntry>(saves.ToArray());
    }

    /// <summary>
    /// One id is pinned once across the whole document, since the namespace is global.
    /// The exception names the constructor parameter of the section the repeated pin is in.
    /// </summary>
    private static void RejectRepeatedPins(params (IReadOnlyList<ModPackEntry> Entries, string ParamName)[] sections)
    {
        var seen = new HashSet<string>(ModIds.Comparer);

        foreach (var (entries, paramName) in sections)
        {
            foreach (var entry in entries)
            {
                ModIds.Validate(entry.ContentId, paramName);

                if (!seen.Add(entry.ContentId))
                    throw new ArgumentException($"'{entry.ContentId}' is pinned more than once.", paramName);
            }
        }
    }
}
