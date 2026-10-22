using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.ViewModels;

/// <summary>The share pages the landing site builds from the content index, one per listing.</summary>
public static class ShareLinks
{
    public const string SiteUrl = "https://ksamodding.github.io/Borea/";

    private const string IndexSource = "index";

    /// <summary>Null for a listing that is not from the index, and for a vehicle or save, because only mods and loaders have a share page yet.</summary>
    public static string? For(ModMetadata listing) => listing.Type.IsVehicleOrSave() ? null : For("mod", listing.Source, listing.ModId);

    /// <summary>Null for a pack that is not from the index.</summary>
    public static string? For(ModPackMetadata pack) => For("pack", pack.Source, pack.ModPackId);

    /// <summary>Whether content comes from the index under an id that the share pages and the report forms of content-index can name.</summary>
    public static bool IsFromIndex(string source, string id) => source == IndexSource && ModIds.IsValid(id);

    private static string? For(string kind, string source, string id)
        => IsFromIndex(source, id) ? $"{SiteUrl}{kind}/{id}/" : null;
}
