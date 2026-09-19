using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>The share pages the landing site builds from the content index, one per listing.</summary>
public static class ShareLinks
{
    public const string SiteUrl = "https://ksamodding.github.io/Borea/";

    private const string IndexSource = "index";

    /// <summary>Null for a listing that is not from the index, because only those have a share page.</summary>
    public static string? For(ModMetadata listing) => For("mod", listing.Source, listing.ModId);

    /// <summary>Null for a pack that is not from the index.</summary>
    public static string? For(ModPackMetadata pack) => For("pack", pack.Source, pack.ModPackId);

    private static string? For(string kind, string source, string id)
        => source == IndexSource && ModIds.IsValid(id) ? $"{SiteUrl}{kind}/{id}/" : null;
}
