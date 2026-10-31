using Borea.Core.Mods;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Sources;

/// <summary>
/// The <see cref="IPreviewAssetSource"/> of one preview: the files of the game content mods and of the needed mods,
/// each under the name that <see cref="PreviewAssetRef.Source"/> uses. It owns the sources and the entry cache that
/// its zip sources share, so <see cref="Dispose"/> closes the archives and drops the cached bytes.
/// </summary>
/// <remarks>
/// Source names compare with <see cref="ModIds.Comparer"/>, because they are mod ids. "Core" is a reserved mod id, so
/// a listed mod cannot take the name of the game content.
/// </remarks>
internal sealed class PreviewAssetSet : IPreviewAssetSource
{
    private readonly Dictionary<string, IPreviewFiles> _sources = new(ModIds.Comparer);
    private int _disposed;

    /// <param name="cache">The cache that the zip sources in <paramref name="sources"/> were made with.</param>
    /// <param name="sources">The sources by name. The first source with a name wins.</param>
    public PreviewAssetSet(PreviewEntryCache cache, IEnumerable<KeyValuePair<string, IPreviewFiles>> sources)
    {
        Cache = cache;
        foreach (var (name, files) in sources)
        {
            if (!_sources.TryAdd(name, files))
                files.Dispose();
        }
    }

    public PreviewEntryCache Cache { get; }

    public Stream? TryOpenRead(PreviewAssetRef asset) => Files(asset)?.TryOpen(asset.Path);

    public Stream? TryOpenHead(PreviewAssetRef asset) => Files(asset)?.TryOpenHead(asset.Path);

    public string GetCacheKey(PreviewAssetRef asset) =>
        Files(asset)?.Describe(asset.Path) ?? $"missing:{asset.Source}:{PreviewPath.Normalize(asset.Path) ?? asset.Path}";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var files in _sources.Values)
            files.Dispose();

        Cache.Clear();
    }

    private IPreviewFiles? Files(PreviewAssetRef asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _sources.GetValueOrDefault(asset.Source);
    }
}
