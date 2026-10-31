namespace Borea.Core.Sharing;

/// <summary>
/// Which mod folder of an instance defines which ids. A template knows its mod only while the game runs (SerializedId.Mod is XmlIgnore),
/// so this reads the asset and system files that each mod.toml names, the way Mod.LoadAssetBundles and Mod.LoadSystems do.
/// </summary>
public interface IContentProviderMap
{
    Task<ContentProviders> ReadAsync(Guid instanceId, CancellationToken cancellationToken = default);
}

/// <param name="FolderName">The id in the manifest, which is the folder name.</param>
/// <param name="IsGameContent">The game's own content, which Content/manifest.toml lists, such as Core.</param>
public sealed record ContentProvider(string FolderName, bool Enabled, bool IsGameContent, ContentIds Defines);

/// <param name="Mods">In the order the game loads them, then the folders that the manifest does not name.</param>
/// <param name="UnreadableFiles">The files that could not be read, which were left out.</param>
public sealed record ContentProviders(IReadOnlyList<ContentProvider> Mods, IReadOnlyList<string> UnreadableFiles);
