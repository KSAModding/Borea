namespace Borea.Core.Listings;

/// <summary>
/// The folders of content-index that hold one document per listing, and which listing type each folder holds (RFC 0085).
/// A vehicle is at vehicles/&lt;id&gt;.toml, a save at saves/&lt;id&gt;.toml, and every other listing at listings/&lt;id&gt;.toml.
/// </summary>
public static class ListingFolders
{
    public const string VehicleType = "vehicle";

    public const string SaveType = "save";

    public const string Vehicles = "vehicles";

    public const string Saves = "saves";

    public static IReadOnlyList<string> All { get; } = [ListingDraft.ListingsFolder, Vehicles, Saves];

    /// <summary>The folder of a listing of the type. A type that is missing or not known goes to listings/.</summary>
    public static string Of(string? type) => type switch
    {
        VehicleType => Vehicles,
        SaveType => Saves,
        _ => ListingDraft.ListingsFolder,
    };

    /// <summary>
    /// Whether the path is a listing document directly in one of <see cref="All"/>, with a lower-case .toml, which is what the checks
    /// of content-index take.
    /// </summary>
    public static bool IsDocumentPath(string? path)
    {
        if (path?.Split('/') is not [var folder, var name])
            return false;

        return All.Contains(folder, StringComparer.Ordinal) && name.Length > ".toml".Length && name.EndsWith(".toml", StringComparison.Ordinal);
    }
}
