using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>
/// The id of the modlist link that created an instance, and the mods that links with this id put into it. A later link with the
/// same id can update the instance, and that update removes only these mods, never a mod the player added.
/// </summary>
public sealed record LinkedModList
{
    public LinkedModList(string listId, IEnumerable<string> mods)
    {
        if (!ModIds.IsValid(listId))
            throw new ArgumentException("The id of a modlist must be a valid content id.", nameof(listId));

        ArgumentNullException.ThrowIfNull(mods);
        ListId = listId;
        Mods = mods.ToHashSet(ModIds.Comparer);
    }

    /// <summary>The id= value of the link.</summary>
    public string ListId { get; }

    public IReadOnlySet<string> Mods { get; }

    public LinkedModList Without(string modId) => new(ListId, Mods.Where(mod => !ModIds.Equals(mod, modId)));

    /// <summary>Equal when the list id and the mods are the same, also in another set or in another letter case.</summary>
    public bool Equals(LinkedModList? other)
        => other is not null && ModIds.Equals(ListId, other.ListId) && Mods.SetEquals(other.Mods);

    public override int GetHashCode() => HashCode.Combine(ModIds.Comparer.GetHashCode(ListId), Mods.Count);
}
