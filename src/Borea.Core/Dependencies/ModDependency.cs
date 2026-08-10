using Borea.Core.Mods;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Borea.Core.Dependencies;

/// <summary>
/// One dependency entry: a mod id with optional inclusive bounds, or an any_of set of alternatives.
/// </summary>
public sealed class ModDependency
{
    /// <summary>
    /// The ID of the mod this entry refers to. Null when this is an any_of entry.
    /// </summary>
    public string? ModId { get; }

    /// <summary>
    /// The kind of dependency.
    /// </summary>
    public ModDependencyKind Kind { get; }

    /// <summary>
    /// Minimum version, inclusive. For conflict entries the bounds describe the conflicting range.
    /// </summary>
    public ModVersion? MinVersion { get; }

    /// <summary>
    /// Maximum version, inclusive. Absent means open.
    /// </summary>
    public ModVersion? MaxVersion { get; }

    /// <summary>
    /// Alternatives of an any_of entry, satisfied by any one of them. Null for a single-id entry.
    /// </summary>
    public IReadOnlyList<ModDependencyAlternative>? AnyOf { get; }

    /// <summary>
    /// True for an any_of entry.
    /// </summary>
    [MemberNotNullWhen(true, nameof(AnyOf))]
    [MemberNotNullWhen(false, nameof(ModId))]
    public bool IsAnyOf => AnyOf is not null;

    /// <summary>
    /// Where the entry came from in a generated release file, null in authored metadata.
    /// </summary>
    public MetadataSource? Source { get; }

    public ModDependency(string modId, ModDependencyKind kind, ModVersion? minVersion = null, ModVersion? maxVersion = null, MetadataSource? source = null)
    {
        ModIds.Validate(modId, nameof(modId));

        if (minVersion is { } min && maxVersion is { } max && max.CompareTo(min) < 0)
            throw new ArgumentOutOfRangeException(nameof(maxVersion), "The maximum version cannot be below the minimum.");

        ModId = modId;
        Kind = kind;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
        Source = source;
    }

    private ModDependency(ModDependencyKind kind, IReadOnlyList<ModDependencyAlternative> alternatives, MetadataSource? source)
    {
        Kind = kind;
        AnyOf = alternatives;
        Source = source;
    }

    /// <summary>
    /// Creates an any_of entry, satisfied by any one of the given alternatives.
    /// </summary>
    public static ModDependency OfAlternatives(ModDependencyKind kind, IReadOnlyList<ModDependencyAlternative> alternatives, MetadataSource? source = null)
    {
        if (kind is not (ModDependencyKind.Required or ModDependencyKind.Recommends))
            throw new ArgumentException("An any_of entry is only valid with kind required or recommends.", nameof(kind));

        if (alternatives is null || alternatives.Count == 0)
            throw new ArgumentException("An any_of entry needs at least one alternative.", nameof(alternatives));

        return new ModDependency(kind, new ReadOnlyCollection<ModDependencyAlternative>(alternatives.ToArray()), source);
    }

    /// <summary>
    /// Whether the version lies within the inclusive bounds. Throws for any_of entries.
    /// </summary>
    public bool BoundsContain(ModVersion version)
    {
        if (IsAnyOf)
            throw new InvalidOperationException("An any_of entry has no bounds of its own; evaluate its alternatives.");

        if (MinVersion is { } min && version.CompareTo(min) < 0)
            return false;

        if (MaxVersion is { } max && version.CompareTo(max) > 0)
            return false;

        return true;
    }

    public override string ToString()
    {
        if (AnyOf is not null)
            return $"{Kind} dependency on any of [{string.Join(", ", AnyOf.Select(a => a.ModId))}]";

        string versionRange = (MinVersion, MaxVersion) switch
        {
            (null, null) => "",
            (var min, null) => $" >= {min}",
            (null, var max) => $" <= {max}",
            (var min, var max) => $" >= {min} <= {max}"
        };
        return $"{Kind} dependency on mod '{ModId}'{versionRange}";
    }
}
