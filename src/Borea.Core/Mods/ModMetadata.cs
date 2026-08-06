using Borea.Core.Game;
using System.Collections.ObjectModel;
using Borea.Core.ModLoaders;

namespace Borea.Core.Mods;

/// <summary>
/// The authored metadata for a mod. Does not contain any information
/// about the versions of the mod.
/// </summary>
public sealed class ModMetadata
{
    /// <summary>
    /// The version of the metadata specification.
    /// </summary>
    public int SpecVersion { get; }

    /// <summary>
    /// The mod's unique identifier.
    /// </summary>
    public string ModId { get; }

    /// <summary>
    /// Which Source the mod metadata is from.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The display name of the mod.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The list of mod authors
    /// </summary>
    public string[] Authors { get; }

    /// <summary>
    /// Short 1-2 sentence(s) summary of the mod.
    /// </summary>
    public string Abstract { get; }

    /// <summary>
    /// Full description of the mod.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// SPDX license expression such as MIT or CC-BY-4.0.
    /// </summary>
    public string License { get; }

    /// <summary>
    /// Free-form lowercase tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// If the mod is active or deprecated.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// If deprecated which mod supersedes this one. Stores that mod's ID.
    /// </summary>
    public string SupersededBy { get; }

    /// <summary>
    /// Where the releases come from, if listed.
    /// </summary>
    public string ReleasesURL { get; }

    /// <summary>
    /// The URL to the mod's KSA forum thread.
    /// </summary>
    public string ForumURL { get; }

    /// <summary>
    /// The minimum game version that the mod is compatible with.
    /// </summary>
    public GameVersion MinGameVersion { get; }

    /// <summary>
    /// The maximum game version that the mod is compatible with, if any.
    /// </summary>
    public GameVersion? MaxGameVersion { get; }

    /// <summary>
    /// The operating systems that the mod is compatible with.
    /// </summary>
    public string[] OSCompatability { get; }

    /// <summary>
    /// The mod loader that the mod uses, if any.
    /// </summary>
    public string ModLoader { get; }

    /// <summary>
    /// The minimum version of the mod loader that the mod requires, if any.
    /// </summary>
    public string MinLoaderVersion { get; }

    /// <summary>
    /// The maximum version of the mod loader that the mod supports, if any.
    /// </summary>
    public string MaxLoaderVersion { get; }

    /// <param name="authors">Must contain at least one author.</param>
    /// <param name="abstractText">Can be "", not null.</param>
    /// <param name="description">Can be "", not null.</param>
    public ModMetadata(
        int specVersion,
        string modId,
        string source,
        string name,
        string[] authors,
        string abstractText,
        string description,
        string license,
        IReadOnlyList<string> tags,
        string forumURL,
        GameVersion minGameVersion,
        string[] osCompatability,
        string status = "active",
        string supersededBy = "",
        string releasesURL = "",
        GameVersion? maxGameVersion = null,
        string modLoader = "",
        string minLoaderVersion = "",
        string maxLoaderVersion = "")
    {
        if (specVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(specVersion), "Spec version must be a positive integer.");

        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or whitespace.", nameof(modId));

        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace.", nameof(name));

        if (authors is null || authors.Length == 0)
            throw new ArgumentException("At least one author is required.", nameof(authors));

        // Allow empty Abstract, but not null
        if (abstractText is null)
            throw new ArgumentNullException("Abstract cannot be null.", nameof(abstractText));

        // Allow empty Description, but not null
        if (description is null)
            throw new ArgumentNullException("Description cannot be null.", nameof(description));

        // Simply check that license exists. Not using SPDX license validation yet
        if (string.IsNullOrWhiteSpace(license))
            throw new ArgumentException("License cannot be null or whitespace.", nameof(license));

        if (string.IsNullOrWhiteSpace(forumURL))
            throw new ArgumentException("Forum URL cannot be null or whitespace.", nameof(forumURL));

        if (modLoader is null)
            throw new ArgumentNullException("Mod loader cannot be null.", nameof(modLoader));

        if (minLoaderVersion is null)
            throw new ArgumentNullException("Minimum loader version cannot be null.", nameof(minLoaderVersion));

        if (maxLoaderVersion is null)
            throw new ArgumentNullException("Maximum loader version cannot be null.", nameof(maxLoaderVersion));

        SpecVersion = specVersion;
        ModId = modId;
        Source = source;
        Name = name;
        Authors = authors;
        Abstract = abstractText;
        Description = description;
        License = license;
        Tags = tags is null ? Array.Empty<string>() : new ReadOnlyCollection<string>(tags.ToArray());
        ForumURL = forumURL;
        MinGameVersion = minGameVersion;
        OSCompatability = osCompatability ?? Array.Empty<string>();
        Status = status;
        SupersededBy = supersededBy;
        ReleasesURL = releasesURL;
        MaxGameVersion = maxGameVersion;
        ModLoader = modLoader;
        MinLoaderVersion = minLoaderVersion;
        MaxLoaderVersion = maxLoaderVersion;
    }
}
