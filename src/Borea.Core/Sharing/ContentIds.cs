using System.Collections;
using Borea.Core.Mods;

namespace Borea.Core.Sharing;

/// <summary>The kinds of game template a vehicle or a save names by id, and that a mod defines (RFC 0085).</summary>
public enum ContentIdKind
{
    /// <summary>A part or a subpart template, which the game finds both through ModLibrary.Get&lt;PartTemplate&gt;.</summary>
    Part,

    /// <summary>A phase of a substance, such as LOX(l).</summary>
    SubstancePhase,

    Character,

    /// <summary>A celestial body.</summary>
    Body,

    /// <summary>A star system.</summary>
    System,
}

public readonly record struct ContentId(ContentIdKind Kind, string Id);

/// <summary>
/// Ids in the order they were first added, each once. Ids compare ignoring case, as mod ids do; that the game compares template ids
/// this way is not verified.
/// </summary>
public sealed class ContentIds : IReadOnlyCollection<ContentId>
{
    private static readonly IEqualityComparer<ContentId> Comparer = EqualityComparer<ContentId>.Create(
        (left, right) => left.Kind == right.Kind && ModIds.Equals(left.Id, right.Id),
        id => HashCode.Combine(id.Kind, ModIds.Comparer.GetHashCode(id.Id)));

    private readonly List<ContentId> _order = [];
    private readonly HashSet<ContentId> _seen = new(Comparer);

    public int Count => _order.Count;

    /// <summary>Adds the id unless it is empty or already in the set.</summary>
    public bool Add(ContentIdKind kind, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_seen.Add(new ContentId(kind, id)))
            return false;

        _order.Add(new ContentId(kind, id));
        return true;
    }

    public bool Contains(ContentId id) => _seen.Contains(id);

    public IReadOnlyList<string> Of(ContentIdKind kind) => _order.Where(id => id.Kind == kind).Select(id => id.Id).ToList();

    public IEnumerator<ContentId> GetEnumerator() => _order.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
