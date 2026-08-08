using Borea.Core.Dependencies;
using Borea.Core.ModLoaders;
using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// The authored metadata for a mod or mod loader (RFC 0031). Carries no version:
/// versions exist per release.
/// </summary>
public sealed class ModMetadata
{
    /// <summary>
    /// The version of the metadata specification.
    /// </summary>
    public int SpecVersion { get; }

    /// <summary>
    /// The mod's unique identifier. Ids compare case-insensitively.
    /// </summary>
    public string ModId { get; }

    /// <summary>
    /// The content type of the listing.
    /// </summary>
    public ContentType Type { get; }

    /// <summary>
    /// Which source this metadata came from. Borea-internal, not a format field.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The display name of the mod.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The list of mod authors.
    /// </summary>
    public IReadOnlyList<string> Authors { get; }

    /// <summary>
    /// Short one-or-two sentence summary for list and search views.
    /// </summary>
    public string Abstract { get; }

    /// <summary>
    /// Longer CommonMark description on top of the abstract, if any.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// SPDX license expression such as MIT or CC-BY-4.0.
    /// </summary>
    public string License { get; }

    /// <summary>
    /// Free-form lowercase tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// The author's declaration about the listing.
    /// </summary>
    public ModStatus Status { get; }

    /// <summary>
    /// The id of the successor, only meaningful together with a deprecated status.
    /// </summary>
    public string? SupersededBy { get; }

    /// <summary>
    /// All listing links. Keys keep their authored casing and compare case-insensitively; "forums" is required.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; }

    /// <summary>
    /// The required KSA forums thread for this listing.
    /// </summary>
    public string ForumUrl => Links["forums"];

    /// <summary>
    /// Where releases appear. Null means releases enter the index by pull request.
    /// </summary>
    public ReleaseSource? Releases { get; }

    /// <summary>
    /// Oldest game version known to work, as authored. May be a month such as "2026.7".
    /// </summary>
    public string GameMin { get; }

    /// <summary>
    /// Newest tested game version, as authored. Null means no known upper limit.
    /// </summary>
    public string? GameMax { get; }

    /// <summary>
    /// The platforms the mod is known to work on. Null means no known restriction.
    /// </summary>
    public IReadOnlyList<string>? Os { get; }

    /// <summary>
    /// The loader a code mod needs. Null means the mod runs without a loader.
    /// </summary>
    public LoaderRequirement? Loader { get; }

    /// <summary>
    /// The authored dependency entries.
    /// </summary>
    public IReadOnlyList<ModDependency> Dependencies { get; }

    /// <summary>
    /// Authored override of the install root for archives with an unusual layout.
    /// </summary>
    public string? InstallRootOverride { get; }

    public ModMetadata(
        int specVersion,
        string modId,
        string source,
        string name,
        IReadOnlyList<string> authors,
        string abstractText,
        string license,
        IReadOnlyDictionary<string, string> links,
        string gameMin,
        ContentType type = ContentType.Mod,
        IReadOnlyList<string>? tags = null,
        string? description = null,
        ModStatus status = ModStatus.Active,
        string? supersededBy = null,
        ReleaseSource? releases = null,
        string? gameMax = null,
        IReadOnlyList<string>? os = null,
        LoaderRequirement? loader = null,
        IReadOnlyList<ModDependency>? dependencies = null,
        string? installRootOverride = null)
    {
        if (specVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Spec version must be a positive integer.");

        ModIds.Validate(modId, nameof(modId));

        if (type == ContentType.ModPack)
            throw new ArgumentException("Packs have their own metadata type.", nameof(type));

        if (loader is not null && type != ContentType.Mod)
            throw new ArgumentException("Only a mod can declare a loader requirement.", nameof(loader));

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

        SpecVersion = specVersion;
        ModId = modId;
        Type = type;
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
        Releases = releases;
        GameMin = gameMin;
        GameMax = gameMax;
        Os = os is null ? null : new ReadOnlyCollection<string>(os.ToArray());
        Loader = loader;
        Dependencies = dependencies is null ? Array.Empty<ModDependency>() : new ReadOnlyCollection<ModDependency>(dependencies.ToArray());
        InstallRootOverride = installRootOverride;
    }
}
