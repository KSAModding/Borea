using System.Collections.ObjectModel;

namespace Borea.Core.Mods;

/// <summary>
/// The listing facts as they stood when the release was stamped. Display history
/// only: listing and search views render live from the authored metadata.
/// </summary>
public sealed class ListingSnapshot
{
    /// <summary>
    /// The display name at stamp time.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The authors at stamp time.
    /// </summary>
    public IReadOnlyList<string> Authors { get; }

    /// <summary>
    /// The short summary at stamp time.
    /// </summary>
    public string Abstract { get; }

    /// <summary>
    /// The longer description at stamp time, if any.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// The SPDX license expression at stamp time.
    /// </summary>
    public string License { get; }

    /// <summary>
    /// The tags at stamp time.
    /// </summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// The links at stamp time. Keys keep their authored casing and compare case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; }

    public ListingSnapshot(
        string name,
        IReadOnlyList<string> authors,
        string abstractText,
        string license,
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, string>? links = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        if (authors is null || authors.Count == 0)
            throw new ArgumentException("At least one author is required.", nameof(authors));

        if (abstractText is null)
            throw new ArgumentNullException(nameof(abstractText));

        if (string.IsNullOrWhiteSpace(license))
            throw new ArgumentException("License cannot be empty.", nameof(license));

        if (links is not null)
        {
            var duplicateKey = links.Keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicateKey is not null)
                throw new ArgumentException($"Link key '{duplicateKey}' appears more than once when compared case-insensitively.", nameof(links));
        }

        Name = name;
        Authors = new ReadOnlyCollection<string>(authors.ToArray());
        Abstract = abstractText;
        License = license;
        Tags = tags is null ? Array.Empty<string>() : new ReadOnlyCollection<string>(tags.ToArray());
        Links = links is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(links, StringComparer.OrdinalIgnoreCase);
        Description = description;
    }
}
