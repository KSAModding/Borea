using System.Diagnostics.CodeAnalysis;
using Borea.Core.Mods;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// Maps the mesh Ids that render models name to a mesh in a glTF file. A MeshAtlas registers each of its meshes under
/// its glTF name (MeshAtlasFileReference.DoLoad), and a MeshFile registers mesh 0 under its own Id
/// (MeshFileReference.DoLoad). The keys compare with <see cref="KsaIdComparer.Registry"/>, and the first registration
/// wins.
/// </summary>
/// <remarks>
/// The game loads the mesh files on parallel jobs (ModLibrary.LoadAll), so when two files have a mesh with the same
/// name, which one it keeps is not fixed. The preview keeps the one that comes first in load order. A HostOnly file
/// gives no GPU mesh in the game, so the preview leaves it out. Only the GLB header and JSON chunk are read here.
/// The stock atlases repeat many collision mesh names, so a duplicate name gives no notice here. It goes into
/// <see cref="DuplicateNames"/>, and the resolver reports it only for a mesh that a draw uses.
/// </remarks>
internal sealed class MeshIndex
{
    private readonly Dictionary<string, PreviewMeshRef> _meshes;

    private MeshIndex(Dictionary<string, PreviewMeshRef> meshes, IReadOnlySet<string> duplicateNames, IReadOnlyList<PreviewNotice> notices)
    {
        _meshes = meshes;
        DuplicateNames = duplicateNames;
        Notices = notices;
    }

    /// <summary>The mesh names that a later file had too. The first mesh with the name is the one that counts.</summary>
    public IReadOnlySet<string> DuplicateNames { get; }

    /// <summary>The mesh files that could not be read.</summary>
    public IReadOnlyList<PreviewNotice> Notices { get; }

    public int Count => _meshes.Count;

    public bool TryFind(string meshId, [NotNullWhen(true)] out PreviewMeshRef? mesh) => _meshes.TryGetValue(meshId, out mesh);

    /// <param name="mods">The mods that <see cref="PartCatalog.Build"/> read, which give the files by their mod Id.</param>
    /// <param name="declarations">The mesh files in load order, from <see cref="PartCatalog.MeshFiles"/>.</param>
    public static MeshIndex Build(IReadOnlyList<CatalogMod> mods, IReadOnlyList<MeshFileDeclaration> declarations, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, CatalogMod>(ModIds.Comparer);
        foreach (var mod in mods)
            files.TryAdd(mod.ModId, mod);

        var meshes = new Dictionary<string, PreviewMeshRef>(KsaIdComparer.Registry);
        var duplicates = new HashSet<string>(KsaIdComparer.Registry);
        var notices = new List<PreviewNotice>();

        void Register(PreviewMeshRef mesh)
        {
            if (!meshes.TryAdd(mesh.Name, mesh))
                duplicates.Add(mesh.Name);
        }

        foreach (var declaration in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declaration.HostOnly)
                continue;

            if (declaration.Kind == MeshFileKind.File)
            {
                if (declaration.Id is { } id)
                    Register(new PreviewMeshRef(declaration.File, 0, id));
                continue;
            }

            if (ReadMeshNames(files.GetValueOrDefault(declaration.File.Source), declaration.File, notices) is not { } names)
                continue;

            for (var index = 0; index < names.Count; index++)
            {
                if (!string.IsNullOrEmpty(names[index]))
                    Register(new PreviewMeshRef(declaration.File, index, names[index]!));
            }
        }

        return new MeshIndex(meshes, duplicates, notices);
    }

    private static IReadOnlyList<string?>? ReadMeshNames(CatalogMod? mod, PreviewAssetRef file, List<PreviewNotice> notices)
    {
        try
        {
            using var stream = mod?.Files.TryOpenHead(file.Path);
            if (stream is null)
            {
                notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, Subject(file), "The file is missing."));
                return null;
            }

            return GlbContainer.Read(stream).MeshNames;
        }
        // A zipped entry with broken deflate data throws InvalidDataException on a read, not on the open, and that
        // exception is not an IOException.
        catch (Exception exception) when (exception is GlbFormatException or IOException or InvalidDataException)
        {
            notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, Subject(file), exception.Message));
            return null;
        }
    }

    private static string Subject(PreviewAssetRef file) => $"{file.Source}/{file.Path}";
}
