using System.Text;
using System.Xml;
using Borea.Storage.Preview.Sources;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// Tells whether the part files of a game use a newer layout than the one <see cref="PartCatalog"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// In the newer layout a file merges into the data of the game with a Modify attribute, a texture is its own Texture
/// asset that a material names with a Ref attribute, and the subparts, connectors and components of a part sit in
/// SubParts, Connectors and Components elements. The catalog reads the direct children of an asset, so on that layout
/// it finds no models, no connectors and no textures, and it takes every merge for a second asset with the same Id.
/// </para>
/// <para>
/// The probe reads the asset files that the content mods of the game list, in their order, and stops at the first
/// mark of the newer layout. It looks at element names and attributes only. A file that is not well-formed gives no
/// mark, because the catalog reports such a file on its own. A listed file that is there but cannot be opened or read,
/// for example while the game updates in its folder, makes the answer uncertain, so that the caller can ask again later.
/// </para>
/// <para>
/// A Texture asset alone is no mark, because the asset files of today already declare textures for the gauges and the
/// planets that way. Only a material that names a texture with Ref shows the newer layout.
/// </para>
/// </remarks>
internal static class PartFormatProbe
{
    public const string Modify = "Modify";
    public const string Ref = "Ref";
    public const string SubParts = "SubParts";
    public const string Connectors = "Connectors";
    public const string Components = "Components";

    private const string ModFileName = "mod.toml";
    private const string AssetsKey = "assets";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    /// <summary>The layout of the asset files that the content mods of the game list.</summary>
    public static PartLayout Read(GameCoreFiles core, CancellationToken cancellationToken)
    {
        var uncertain = false;
        foreach (var (_, files) in core.Mods)
        {
            if (AssetPaths(files) is not { } paths)
            {
                uncertain = true;
                continue;
            }

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (ReadFile(files, path, cancellationToken))
                {
                    case PartLayout.Newer:
                        return PartLayout.Newer;
                    case PartLayout.Uncertain:
                        uncertain = true;
                        break;
                }
            }
        }

        return uncertain ? PartLayout.Uncertain : PartLayout.Today;
    }

    /// <summary>True when the Assets document in the stream has a mark of the newer layout. A document with another root has none.</summary>
    /// <exception cref="XmlException">The document is not well-formed before the first mark.</exception>
    internal static bool HasNewerLayoutMark(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = XmlReader.Create(stream, XmlSettings);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != KsaPartSchema.Assets)
            return false;

        // The names of the open elements from the root down, so that each element knows its parent.
        var open = new List<string>();
        do
        {
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                open.RemoveAt(open.Count - 1);
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            var name = reader.LocalName;
            var parent = open.Count > 0 ? open[^1] : null;
            if (IsNewerLayoutMark(reader, name, parent))
                return true;

            if (!reader.IsEmptyElement)
                open.Add(name);
        }
        while (reader.Read());

        return false;
    }

    private static bool IsNewerLayoutMark(XmlReader element, string name, string? parent) =>
        element.GetAttribute(Modify) is not null
        || (parent == KsaPartSchema.PbrMaterial && element.GetAttribute(Ref) is not null)
        || (parent is KsaPartSchema.Part or KsaPartSchema.SubPart && name is SubParts or Connectors or Components);

    /// <summary>The layout of one listed file. A file that is not there gives the layout of today.</summary>
    private static PartLayout ReadFile(IPreviewFiles files, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = files.TryOpen(path);
            if (stream is null)
                return files.Exists(path) ? PartLayout.Uncertain : PartLayout.Today;

            return HasNewerLayoutMark(stream, cancellationToken) ? PartLayout.Newer : PartLayout.Today;
        }
        catch (XmlException)
        {
            return PartLayout.Today;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PartLayout.Uncertain;
        }
    }

    /// <summary>
    /// The asset paths of the mod.toml that stay inside the mod folder. None when the mod has no mod.toml or the file is
    /// not valid TOML, and null when the file is there but cannot be opened or read.
    /// </summary>
    private static List<string>? AssetPaths(IPreviewFiles files)
    {
        TomlTable table;
        try
        {
            using var stream = files.TryOpen(ModFileName);
            if (stream is null)
                return files.Exists(ModFileName) ? null : [];

            using var reader = new StreamReader(stream, Encoding.UTF8);
            table = TomlSerializer.Deserialize<TomlTable>(reader.ReadToEnd()) ?? new TomlTable();
        }
        catch (Exception exception) when (exception is TomlException or DecoderFallbackException)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return table.TryGetValue(AssetsKey, out var value) && value is TomlArray array
            ? [.. array.OfType<string>().Select(PreviewPath.Normalize).OfType<string>()]
            : [];
    }
}
