using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Borea.Preview.Scene;

namespace Borea.Preview.Loading;

/// <summary>
/// Decoded meshes that the least recently used order evicts when their array bytes go above a cap. The key is
/// <see cref="Core.Preview.IPreviewAssetSource.GetCacheKey"/> of the GLB file plus the mesh index, so a second scene
/// that uses the same file does not open it again. A mesh that is larger than the whole cap is not kept. All members
/// are safe to call from several threads.
/// </summary>
public sealed class PreviewMeshCache
{
    public const long DefaultCapacityBytes = 64L * 1024 * 1024;

    private readonly long _capacityBytes;
    private readonly LinkedList<(string Key, PreviewMesh Mesh)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, PreviewMesh Mesh)>> _entries = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _bytes;

    public PreviewMeshCache(long capacityBytes = DefaultCapacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        _capacityBytes = capacityBytes;
    }

    public bool TryGet(string fileKey, int meshIndex, [NotNullWhen(true)] out PreviewMesh? mesh)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(Key(fileKey, meshIndex), out var node))
            {
                mesh = null;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            mesh = node.Value.Mesh;
            return true;
        }
    }

    public void Add(string fileKey, int meshIndex, PreviewMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var key = Key(fileKey, meshIndex);
        var size = SizeOf(mesh);
        lock (_lock)
        {
            if (_entries.Remove(key, out var old))
            {
                _order.Remove(old);
                _bytes -= SizeOf(old.Value.Mesh);
            }

            if (size > _capacityBytes)
            {
                return;
            }

            _entries[key] = _order.AddFirst((key, mesh));
            _bytes += size;
            while (_bytes > _capacityBytes && _order.Last is { } last)
            {
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
                _bytes -= SizeOf(last.Value.Mesh);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _order.Clear();
            _bytes = 0;
        }
    }

    private static string Key(string fileKey, int meshIndex) => fileKey + "#" + meshIndex.ToString(CultureInfo.InvariantCulture);

    /// <summary>The bytes of the arrays of a decoded mesh, which is what the cache and the scene byte budget count.</summary>
    internal static long SizeOf(PreviewMesh mesh) =>
        4L * (mesh.Positions.Length + mesh.Normals.Length + (mesh.Uvs?.Length ?? 0) + mesh.Indices.Length);
}
