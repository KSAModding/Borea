namespace Borea.Core.Preview;

/// <summary>
/// A file inside one mod source. <paramref name="Source"/> names the source to an <see cref="IPreviewAssetSource"/>,
/// for example Core or a mod Id, and <paramref name="Path"/> is relative to the folder of that mod with '/' as the
/// separator, because the game resolves an asset path from the mod that declares it (FileReference.ModPath).
/// </summary>
public sealed record PreviewAssetRef(string Source, string Path);
