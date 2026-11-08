using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Borea.Core.Preview;
using Borea.Storage.Preview.Sources;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Preview.Ksa;

/// <summary>One mod that gives assets to a preview, under the name that <see cref="PreviewAssetRef.Source"/> uses.</summary>
internal sealed record CatalogMod(string ModId, IPreviewFiles Files);

/// <summary>
/// The part templates, materials, textures and mesh files of the game content and of the mods a vehicle needs, as the
/// game registers them when it loads the mods in order (ContentLoader.ReadSource and ContentLoader.Load).
/// </summary>
/// <remarks>
/// <para>
/// The catalog reads the top-level rows of every asset file in load order and merges the Part, SubPart, PbrMaterial,
/// Texture, MeshAtlas and MeshFile rows by tag and Id with <see cref="AssetRowMerge"/>, as ContentPrePass.Resolve does.
/// A merged row then registers, and the first registration of an Id wins in each registry (see
/// <see cref="KsaPartSchema"/>). A path resolves against the mod of the row that defines the asset (FileReference.ModPath).
/// </para>
/// <para>
/// A template is read in both layouts. In the layout of build 2026.11.10.5801 the subpart instances are the SubPart
/// items of SubParts with Key and Ref, the connectors are the Connector items of Connectors, and the render models,
/// fuel ports and tanks are items of Components. A render model names its mesh and its material with Ref, and a
/// material names a Texture asset with Ref in each slot. In the older layout these elements sit directly in the
/// template with Id and InstanceOf, a material slot holds a Path, a render model can hold its own material, and a
/// PartGameData or SubPartGameData adds to the template with its Id after all mods (ModLibrary.AttachGameData). A
/// file that is not well-formed gives nothing, and a row with a value that cannot be read is left out.
/// </para>
/// </remarks>
internal sealed class PartCatalog
{
    private const string ModFileName = "mod.toml";
    private const string AssetsKey = "assets";

    /// <summary>
    /// A mod.toml key that patch loaders outside the game read. The Mod class of the game has no field for it, and the
    /// preview does not apply patches, so it only reports a mod that has them.
    /// </summary>
    private const string PatchesKey = "patches";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    /// <summary>The tags whose rows merge by tag and Id. The GameData tags of the older layout fold on their own.</summary>
    private static readonly HashSet<string> MergedTags = new(StringComparer.Ordinal)
    {
        KsaPartSchema.Part, KsaPartSchema.SubPart, KsaPartSchema.PbrMaterial, KsaPartSchema.Texture, KsaPartSchema.MeshAtlas, KsaPartSchema.MeshFile,
    };

    private readonly Dictionary<string, PartTemplateData> _templates;
    private readonly Dictionary<string, MaterialData> _materials;

    private PartCatalog(
        Dictionary<string, PartTemplateData> templates,
        Dictionary<string, MaterialData> materials,
        IReadOnlyList<MeshFileDeclaration> meshFiles,
        IReadOnlyList<PreviewNotice> notices)
    {
        _templates = templates;
        _materials = materials;
        MeshFiles = meshFiles;
        Notices = notices;
    }

    /// <summary>The MeshAtlas and MeshFile declarations in load order, for <see cref="MeshIndex"/>.</summary>
    public IReadOnlyList<MeshFileDeclaration> MeshFiles { get; }

    public IReadOnlyList<PreviewNotice> Notices { get; }

    public int TemplateCount => _templates.Count;

    /// <summary>Finds a Part or SubPart template after all merges, as ModLibrary.Get&lt;PartTemplate&gt; does.</summary>
    public bool TryGetTemplate(string id, [NotNullWhen(true)] out PartTemplateData? template) =>
        _templates.TryGetValue(id, out template);

    /// <summary>Finds a material by its Id or by the key of an inline material of the older layout.</summary>
    public bool TryGetMaterial(string key, [NotNullWhen(true)] out MaterialData? material) =>
        _materials.TryGetValue(key, out material);

    /// <param name="mods">The mods in load order: the game content first, then the mods the vehicle needs.</param>
    public static PartCatalog Build(IReadOnlyList<CatalogMod> mods, CancellationToken cancellationToken)
    {
        var builder = new Builder();
        foreach (var mod in mods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AddMod(mod, cancellationToken);
        }

        return builder.Finish(cancellationToken);
    }

    private sealed class Builder
    {
        private readonly List<AssetRow> _rows = [];
        private readonly List<AssetRow> _gameDataRows = [];
        private readonly List<PreviewNotice> _notices = [];
        private readonly RowReader _reader = new();

        private readonly Dictionary<string, PartTemplateData> _templates = new(KsaIdComparer.Registry);
        private readonly Dictionary<string, PartTemplateData> _gameData = new(KsaIdComparer.Registry);
        private readonly List<PartTemplateData> _gameDataOrder = [];
        private readonly Dictionary<string, MaterialData> _materials = new(KsaIdComparer.Registry);

        /// <summary>The Ids of ModLibrary.AllFiles, with the path of each Texture.</summary>
        private readonly Dictionary<string, PreviewAssetRef?> _files = new(KsaIdComparer.Registry);

        private readonly HashSet<string> _textureIds = new(KsaIdComparer.Registry);
        private readonly List<MeshFileDeclaration> _meshFiles = [];
        private int _order;

        public void AddMod(CatalogMod mod, CancellationToken cancellationToken)
        {
            foreach (var path in ReadAssetList(mod))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFile(mod, path);
            }
        }

        public PartCatalog Finish(CancellationToken cancellationToken)
        {
            var merged = AssetRowMerge.Merge(_rows, cancellationToken);
            _notices.AddRange(merged.Notices);

            var rows = merged.Rows.Concat(_gameDataRows).OrderBy(row => row.Order);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Register(row);
            }

            foreach (var duplicate in merged.Duplicates)
                ReportDuplicate(duplicate);

            foreach (var gameData in _gameDataOrder)
            {
                if (_templates.TryGetValue(gameData.Id, out var template) && KsaIdComparer.Instance.Equals(template.Id, gameData.Id))
                    template.ApplyGameData(gameData);
            }

            foreach (var material in _reader.Materials)
            {
                var resolved = Resolve(material);
                if (!_materials.TryAdd(resolved.Key, resolved))
                    ReportMaterialCopy(resolved, material.Subject);
            }

            return new PartCatalog(_templates, _materials, _meshFiles, _notices);
        }

        private List<string> ReadAssetList(CatalogMod mod)
        {
            var subject = $"{mod.ModId}/{ModFileName}";
            TomlTable table;
            try
            {
                using var stream = mod.Files.TryOpen(ModFileName);
                if (stream is null)
                {
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, "The file is missing or too large."));
                    return [];
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                table = TomlSerializer.Deserialize<TomlTable>(reader.ReadToEnd()) ?? new TomlTable();
            }
            catch (Exception exception) when (exception is TomlException or IOException or DecoderFallbackException)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, exception.Message));
                return [];
            }

            if (table.ContainsKey(PatchesKey))
                _notices.Add(new PreviewNotice(PreviewNoticeKind.PatchMod, mod.ModId));

            return table.TryGetValue(AssetsKey, out var value) && value is TomlArray array
                ? [.. array.OfType<string>()]
                : [];
        }

        private void AddFile(CatalogMod mod, string rawPath)
        {
            var path = PreviewPath.Normalize(rawPath);
            var subject = $"{mod.ModId}/{path ?? rawPath}";
            if (path is null)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, "The path is not inside the mod folder."));
                return;
            }

            XElement root;
            try
            {
                using var stream = mod.Files.TryOpen(path);
                if (stream is null)
                {
                    var detail = mod.Files.Exists(path) ? "The file is too large or cannot be read." : "The file is missing.";
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, detail));
                    return;
                }

                using var reader = XmlReader.Create(stream, XmlSettings);
                root = XDocument.Load(reader).Root!;
            }
            catch (Exception exception) when (exception is XmlException or IOException)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, exception.Message));
                return;
            }

            if (root.Name.LocalName != KsaPartSchema.Assets)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, "The root element is not Assets."));
                return;
            }

            foreach (var element in root.Elements())
            {
                var row = new AssetRow(element, mod.ModId, path, _order++);
                if (MergedTags.Contains(row.Tag))
                    _rows.Add(row);
                else if (row.Tag is KsaPartSchema.PartGameData or KsaPartSchema.SubPartGameData)
                    _gameDataRows.Add(row);
            }
        }

        private void Register(AssetRow row)
        {
            try
            {
                switch (row.Tag)
                {
                    case KsaPartSchema.Part or KsaPartSchema.SubPart:
                        RegisterTemplate(_reader.ReadTemplate(row), row);
                        break;
                    case KsaPartSchema.PartGameData or KsaPartSchema.SubPartGameData:
                        FoldGameData(_reader.ReadTemplate(row));
                        break;
                    case KsaPartSchema.PbrMaterial:
                        _reader.ReadMaterial(row.Element, row);
                        break;
                    case KsaPartSchema.Texture:
                        if (RegisterFile(row) is { } id)
                        {
                            _files[id] = _reader.ResolvePath(row, row.Element.Attribute(KsaPartSchema.Path)?.Value);
                            _textureIds.Add(id);
                        }
                        break;
                    case KsaPartSchema.MeshAtlas:
                        AddMeshFile(row, MeshFileKind.Atlas);
                        break;
                    case KsaPartSchema.MeshFile:
                        AddMeshFile(row, MeshFileKind.File);
                        break;
                }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, row.Subject, $"The {row.Tag} '{row.Id}' is left out. {exception.Message}"));
            }
        }

        private void RegisterTemplate(PartTemplateData template, AssetRow row)
        {
            if (template.Id.Length > 0 && !_templates.TryAdd(template.Id, template))
                _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, template.Id, $"The template in {row.Subject} is left out, because '{_templates[template.Id].Id}' comes first."));
        }

        private void FoldGameData(PartTemplateData gameData)
        {
            if (gameData.Id.Length == 0)
                return;

            if (_gameData.TryGetValue(gameData.Id, out var first))
            {
                first.ApplyGameData(gameData);
                return;
            }

            _gameData.Add(gameData.Id, gameData);
            _gameDataOrder.Add(gameData);
        }

        /// <summary>The Id under which a row of ModLibrary.AllFiles registers, or null when it has none or another file has it.</summary>
        private string? RegisterFile(AssetRow row)
        {
            if (row.Id is not { } id)
                return null;
            if (_files.TryAdd(id, null))
                return id;

            _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, id, $"The {row.Tag} in {row.Subject} is left out, because a file with this Id comes first."));
            return null;
        }

        private void AddMeshFile(AssetRow row, MeshFileKind kind)
        {
            var element = row.Element;
            var hostOnly = element.Attribute(KsaPartSchema.HostOnly)?.Value is { } text && XmlConvert.ToBoolean(text);
            if (row.Id is not null && RegisterFile(row) is null)
                return;

            if (_reader.ResolvePath(row, element.Attribute(KsaPartSchema.Path)?.Value) is { } file)
                _meshFiles.Add(new MeshFileDeclaration(kind, file, row.Id, hostOnly));
        }

        private void ReportDuplicate(DuplicateRow duplicate)
        {
            var (dropped, first) = (duplicate.Dropped, duplicate.First);
            switch (dropped.Tag)
            {
                case KsaPartSchema.Part or KsaPartSchema.SubPart:
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, dropped.Id!, $"The template in {dropped.Subject} is left out, because '{first.Id}' comes first."));
                    break;
                case KsaPartSchema.PbrMaterial:
                    // The first material wins. A copy with the same textures changes nothing, so only a material that
                    // looks different gets a notice.
                    var copy = new RowReader();
                    copy.ReadMaterial(dropped.Element, dropped);
                    foreach (var material in copy.Materials)
                        ReportMaterialCopy(Resolve(material), dropped.Subject);
                    break;
                default:
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, dropped.Id!, $"The {dropped.Tag} in {dropped.Subject} is left out, because a {first.Tag} with this Id comes first."));
                    break;
            }
        }

        private void ReportMaterialCopy(MaterialData material, string subject)
        {
            if (!_materials.TryGetValue(material.Key, out var first))
                return;

            if (first.Diffuse != material.Diffuse || first.AoRoughMetal != material.AoRoughMetal || first.Normal != material.Normal)
                _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, material.Key, $"A material in {subject} is left out, because a material with this Id comes first."));
        }

        private MaterialData Resolve(PendingMaterial material) =>
            new(material.Key, Resolve(material.Diffuse), Resolve(material.AoRoughMetal), Resolve(material.Normal));

        /// <summary>The file of a texture slot. A Ref that names no Texture gives no file, as DataReference.Resolve gives no Value.</summary>
        private PreviewAssetRef? Resolve(TextureSlot? slot) => slot switch
        {
            { TextureRef: { } textureRef } => _textureIds.Contains(textureRef) ? _files[textureRef] : null,
            { File: { } file } => file,
            _ => null,
        };
    }

    /// <summary>A texture slot of a material: a Ref to a Texture asset, or the path of the older layout.</summary>
    private sealed record TextureSlot(string? TextureRef, PreviewAssetRef? File);

    /// <summary>A material with textures whose Refs resolve once every Texture has registered.</summary>
    private sealed record PendingMaterial(string Key, TextureSlot? Diffuse, TextureSlot? AoRoughMetal, TextureSlot? Normal, string Subject);

    /// <summary>Reads merged rows into templates and materials. A value that cannot be read throws, so that its row is left out.</summary>
    private sealed class RowReader
    {
        private readonly Dictionary<string, int> _anonymousMaterials = new(StringComparer.Ordinal);

        /// <summary>The materials with textures in the order they were read, which is the order they register in.</summary>
        public List<PendingMaterial> Materials { get; } = [];

        public PartTemplateData ReadTemplate(AssetRow row)
        {
            var element = row.Element;
            var isSubPart = row.Tag is KsaPartSchema.SubPart or KsaPartSchema.SubPartGameData;
            var subParts = new List<SubPartInstanceData>();
            var models = new List<RenderModelData>();
            var connectors = new List<ConnectorData>();
            var fuelPorts = new List<FuelPortData>();
            var materials = new List<PendingMaterial>();
            var hasTank = false;

            void ReadComponent(XElement child)
            {
                switch (child.Name.LocalName)
                {
                    case KsaPartSchema.PartModel:
                        models.Add(ReadRenderModel(child, PreviewModelKind.Model, row, materials));
                        break;
                    case KsaPartSchema.PartModelDynamic:
                        models.Add(ReadRenderModel(child, PreviewModelKind.Dynamic, row, materials));
                        break;
                    case KsaPartSchema.PartModelGlass:
                        models.Add(ReadRenderModel(child, PreviewModelKind.Glass, row, materials));
                        break;
                    case KsaPartSchema.FuelPort:
                        if (TryReadFuelPort(child) is { } port)
                            fuelPorts.Add(port);
                        break;
                    case KsaPartSchema.Tank:
                        hasTank = true;
                        break;
                }
            }

            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case KsaPartSchema.SubParts:
                        subParts.AddRange(child.Elements(KsaPartSchema.SubPart).Select(ReadSubPartInstance));
                        break;
                    case KsaPartSchema.SubPart:
                        subParts.Add(ReadSubPartInstance(child));
                        break;
                    case KsaPartSchema.Connectors:
                        connectors.AddRange(child.Elements(KsaPartSchema.Connector).Select(ReadConnector));
                        break;
                    case KsaPartSchema.Connector:
                        connectors.Add(ReadConnector(child));
                        break;
                    case KsaPartSchema.Components:
                        foreach (var component in child.Elements())
                            ReadComponent(component);
                        break;
                    default:
                        ReadComponent(child);
                        break;
                }
            }

            Materials.AddRange(materials);
            return new PartTemplateData(row.Id ?? string.Empty, isSubPart, subParts, models, connectors, fuelPorts, hasTank);
        }

        /// <summary>
        /// Reads a PbrMaterial or an inline Material and gives the key a render model finds it by. A material without
        /// Diffuse, Normal and AoRoughMetal is only a reference to the material with its Id. An inline material without
        /// an Id gets a key from its place in the file, which stays the same while the file does not change, as the
        /// game named it Anon_ with a hash.
        /// </summary>
        public string ReadMaterial(XElement element, AssetRow row) => ReadMaterial(element, row, Materials);

        /// <summary>A path of the mod of the row as an asset reference, or null when it is empty or leaves the mod folder.</summary>
        public PreviewAssetRef? ResolvePath(AssetRow row, string? path) =>
            PreviewPath.Normalize(path) is { } normalized ? new PreviewAssetRef(row.ModId, normalized) : null;

        private string ReadMaterial(XElement element, AssetRow row, List<PendingMaterial> into)
        {
            TextureSlot? diffuse = null;
            TextureSlot? normal = null;
            TextureSlot? aoRoughMetal = null;
            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case KsaPartSchema.Diffuse:
                        diffuse ??= ReadSlot(child, row);
                        break;
                    case KsaPartSchema.Normal:
                        normal ??= ReadSlot(child, row);
                        break;
                    case KsaPartSchema.AoRoughMetal:
                        aoRoughMetal ??= ReadSlot(child, row);
                        break;
                }
            }

            var key = NonEmpty(element.Attribute(KsaPartSchema.Id)?.Value) ?? AnonymousKey(row);
            if (diffuse is not null || normal is not null || aoRoughMetal is not null)
                into.Add(new PendingMaterial(key, diffuse, aoRoughMetal, normal, row.Subject));

            return key;
        }

        private TextureSlot ReadSlot(XElement slot, AssetRow row) =>
            NonEmpty(slot.Attribute(KsaPartSchema.Ref)?.Value) is { } textureRef
                ? new TextureSlot(textureRef, null)
                : new TextureSlot(null, ResolvePath(row, slot.Attribute(KsaPartSchema.Path)?.Value));

        private string AnonymousKey(AssetRow row)
        {
            var count = _anonymousMaterials.GetValueOrDefault(row.Subject) + 1;
            _anonymousMaterials[row.Subject] = count;
            return $"anon:{row.Subject}#{count}";
        }

        private RenderModelData ReadRenderModel(XElement element, PreviewModelKind kind, AssetRow row, List<PendingMaterial> materials)
        {
            var modelId = KeyOrId(element);
            string? meshId = null;
            var seenMesh = false;
            string? materialKey = null;
            var seenMaterial = false;
            var isInternal = false;
            var shadowProxy = false;

            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case KsaPartSchema.Mesh when !seenMesh:
                        seenMesh = true;
                        meshId = NonEmpty(child.Attribute(KsaPartSchema.Ref)?.Value) ?? NonEmpty(child.Attribute(KsaPartSchema.Id)?.Value);
                        break;
                    case KsaPartSchema.Material when !seenMaterial:
                        seenMaterial = true;
                        materialKey = NonEmpty(child.Attribute(KsaPartSchema.Ref)?.Value) ?? ReadMaterial(child, row, materials);
                        break;
                    case KsaPartSchema.Internal when kind == PreviewModelKind.Model:
                        isInternal = XmlConvert.ToBoolean(child.Value);
                        break;
                    case KsaPartSchema.RayTracing when kind == PreviewModelKind.Model:
                        shadowProxy = ReadRaytracingMode(child.Value);
                        break;
                    case KsaPartSchema.RayTracing when kind == PreviewModelKind.Glass:
                        XmlConvert.ToBoolean(child.Value);
                        break;
                }
            }

            return new RenderModelData(kind, modelId, meshId, materialKey, isInternal, shadowProxy);
        }

        /// <summary>
        /// Reads a FuelPort element, or gives null when a value is not a number. The port only places the ends of fuel
        /// lines, so a bad value must not cost the part, also for a vehicle without fuel lines. A link to such a part
        /// then ends at the origin of the part.
        /// </summary>
        private static FuelPortData? TryReadFuelPort(XElement element)
        {
            try
            {
                return ReadFuelPort(element);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a FuelPort element. A missing attribute has its default, and the largest angle in the file is in degrees.
        /// The connector subpart is the KeyRef of its SubPart element (FuelPort.TemplateData.SubPart), or the Connector
        /// attribute in the older layout.
        /// </summary>
        private static FuelPortData ReadFuelPort(XElement element)
        {
            const float DefaultMaxAngleDegrees = 10f;
            const float DefaultLeadOut = 0.15f;

            var connector = NonEmpty(element.Element(KsaPartSchema.SubPart)?.Attribute(KsaPartSchema.KeyRef)?.Value)
                ?? NonEmpty(element.Attribute(KsaPartSchema.Connector)?.Value);
            var maxAngle = element.Attribute(KsaPartSchema.ConnectorMaxAngle)?.Value is { } angle ? XmlConvert.ToSingle(angle) : DefaultMaxAngleDegrees;
            var leadOut = element.Attribute(KsaPartSchema.ConnectorLeadOut)?.Value is { } lead ? XmlConvert.ToSingle(lead) : DefaultLeadOut;
            Vector3? anchor = null;
            Vector3? pivot = null;
            Vector3? tip = null;

            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case KsaPartSchema.AnchorAsmb:
                        anchor ??= KsaTransform.ReadVector(child);
                        break;
                    case KsaPartSchema.ConnectorPivot:
                        pivot ??= KsaTransform.ReadVector(child);
                        break;
                    case KsaPartSchema.ConnectorTip:
                        tip ??= KsaTransform.ReadVector(child);
                        break;
                }
            }

            return new FuelPortData(connector, anchor ?? default, float.DegreesToRadians(maxAngle), pivot ?? default, tip ?? default, leadOut);
        }

        /// <summary>A SubPart item of SubParts (Key and Ref), or a SubPart element of the older layout (Id and InstanceOf).</summary>
        private static SubPartInstanceData ReadSubPartInstance(XElement element) =>
            new(KeyOrId(element), NonEmpty(element.Attribute(KsaPartSchema.Ref)?.Value) ?? element.Attribute(KsaPartSchema.InstanceOf)?.Value ?? string.Empty, ReadTransform(element));

        private static ConnectorData ReadConnector(XElement element) =>
            new(KeyOrId(element), ReadTransform(element)?.Position ?? default);

        /// <summary>Reads the first Transform child of the element, or null when it has none.</summary>
        private static KsaTransform? ReadTransform(XElement element) =>
            element.Element(KsaPartSchema.Transform) is { } transform ? KsaTransform.Parse(transform) : null;

        /// <summary>
        /// Whether a PartModelModule.RaytracingMode value is ShadowProxy. XmlSerializer accepts only the names of the
        /// enum, so another value fails the row.
        /// </summary>
        private static bool ReadRaytracingMode(string text) => text.Trim() switch
        {
            KsaPartSchema.ShadowProxy => true,
            KsaPartSchema.RayTracingDisabled or KsaPartSchema.RayTracingEnabled => false,
            _ => throw new FormatException($"'{text}' is not a RayTracing mode."),
        };

        /// <summary>The Key of a child, or its Id in the older layout.</summary>
        private static string KeyOrId(XElement element) =>
            NonEmpty(element.Attribute(KsaPartSchema.Key)?.Value) ?? element.Attribute(KsaPartSchema.Id)?.Value ?? string.Empty;

        private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
    }
}
