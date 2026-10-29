namespace Borea.Core.Stewardship;

/// <summary>The line of a steward pull request that mentions the owners of a listing or pack, so GitHub tells them.</summary>
internal static class OwnerMention
{
    /// <returns>A line such as "@alice owns `MyMod`.", or null when nobody is named.</returns>
    public static string? Of(IReadOnlyCollection<string> owners, string id)
    {
        return owners.Count == 0 ? null : $"{string.Join(' ', owners.Select(owner => "@" + owner))} {(owners.Count == 1 ? "owns" : "own")} `{id}`.";
    }
}
