using Borea.Core.Mods;

namespace Borea.Core.Index;

/// <summary>A snapshot file that holds the entries of one content type, next to the main document.</summary>
/// <param name="ArrayKey">The root key of the array that holds the entries.</param>
public sealed record ContentIndexTypeFile(ContentType Type, string FileName, string ArrayKey)
{
    public Uri AddressFrom(Uri indexUri) => new(indexUri, FileName);

    public string CachePathFrom(string indexPath) => Path.Combine(Path.GetDirectoryName(indexPath) ?? string.Empty, FileName);
}

/// <summary>
/// The files of vehicles and saves. Their names and their place next to index.json follow the proposal on
/// RFC 0094, which is not decided yet, so they change here and nowhere else.
/// </summary>
public static class ContentIndexTypeFiles
{
    public static ContentIndexTypeFile Vehicles { get; } = new(ContentType.Vehicle, "snapshot-vehicles.json", "vehicles");

    public static ContentIndexTypeFile Saves { get; } = new(ContentType.Save, "snapshot-saves.json", "saves");

    /// <summary>Vehicles first, the order of the rollout.</summary>
    public static IReadOnlyList<ContentIndexTypeFile> All { get; } = [Vehicles, Saves];
}
