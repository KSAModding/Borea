using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using System.Xml;
using Borea.Core.Preview;
using Borea.Storage.Preview.Sources;
using Tomlyn;
using Tomlyn.Model;

namespace Borea.Storage.Preview.Ksa;

/// <summary>One mod that gives assets to a preview, under the name that <see cref="PreviewAssetRef.Source"/> uses.</summary>
internal sealed record CatalogMod(string ModId, IPreviewFiles Files);

/// <summary>
/// The part templates, GameData, materials and mesh files of the game content and of the mods a vehicle needs, as the
/// game registers them when it loads the mods in order (ModLibrary.LoadAll, Mod.LoadAssetBundles).
/// </summary>
/// <remarks>
/// <para>
/// Every registry key compares with <see cref="KsaIdComparer.Registry"/>, and the first registration wins. Part and
/// SubPart templates share one registry, and PartGameData and SubPartGameData share another, as in ModLibrary.
/// A GameData whose Id is already registered folds into the first one (PartGameDataReference.OnDataLoad), and after
/// all mods each GameData applies to the template with the same Id (ModLibrary.AttachGameData).
/// </para>
/// <para>
/// The game reads each asset file with XmlSerializer and registers nothing from a file it cannot read
/// (XmlLoader.Load), so a file that fails here also gives nothing. Render models are the PartModel,
/// PartModelDynamic and PartModelGlass elements, which XmlHelper maps into PartTemplate.Components by their XmlType
/// names. Paths resolve against the mod that declares them (FileReference.ModPath).
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

    /// <summary>Finds a Part or SubPart template after the GameData merge, as ModLibrary.Get&lt;PartTemplate&gt; does.</summary>
    public bool TryGetTemplate(string id, [NotNullWhen(true)] out PartTemplateData? template) =>
        _templates.TryGetValue(id, out template);

    /// <summary>Finds a material by its Id or by the key of an inline material, as PbrMaterialReference.Get does.</summary>
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

        return builder.Finish();
    }

    private sealed class Builder
    {
        private readonly Dictionary<string, PartTemplateData> _templates = new(KsaIdComparer.Registry);
        private readonly Dictionary<string, PartTemplateData> _gameData = new(KsaIdComparer.Registry);
        private readonly List<PartTemplateData> _gameDataOrder = [];
        private readonly Dictionary<string, MaterialData> _materials = new(KsaIdComparer.Registry);
        private readonly List<MeshFileDeclaration> _meshFiles = [];
        private readonly List<PreviewNotice> _notices = [];

        public void AddMod(CatalogMod mod, CancellationToken cancellationToken)
        {
            foreach (var path in ReadAssetList(mod))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFile(mod, path, cancellationToken);
            }
        }

        public PartCatalog Finish()
        {
            foreach (var gameData in _gameDataOrder)
            {
                if (_templates.TryGetValue(gameData.Id, out var template) && KsaIdComparer.Instance.Equals(template.Id, gameData.Id))
                    template.ApplyGameData(gameData);
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

        private void AddFile(CatalogMod mod, string rawPath, CancellationToken cancellationToken)
        {
            var path = PreviewPath.Normalize(rawPath);
            var subject = $"{mod.ModId}/{path ?? rawPath}";
            if (path is null)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, "The path is not inside the mod folder."));
                return;
            }

            var file = new AssetFileReader(mod.ModId, path);
            try
            {
                using var stream = mod.Files.TryOpen(path);
                if (stream is null)
                {
                    var detail = mod.Files.Exists(path) ? "The file is too large or cannot be read." : "The file is missing.";
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, detail));
                    return;
                }

                file.Read(stream, cancellationToken);
            }
            catch (Exception exception) when (exception is XmlException or FormatException or OverflowException or IOException)
            {
                _notices.Add(new PreviewNotice(PreviewNoticeKind.AssetFailed, subject, exception.Message));
                return;
            }

            Commit(file, subject);
        }

        private void Commit(AssetFileReader file, string subject)
        {
            foreach (var material in file.Materials)
            {
                // PbrMaterialReference.OnDataLoad turns a material whose Id is taken into a reference to the first one.
                // A copy with the same textures changes nothing, so only a material that looks different gets a notice.
                if (_materials.TryAdd(material.Key, material))
                    continue;

                var first = _materials[material.Key];
                if (first.Diffuse != material.Diffuse || first.AoRoughMetal != material.AoRoughMetal || first.Normal != material.Normal)
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, material.Key, $"A material in {subject} is left out, because a material with this Id comes first."));
            }

            _meshFiles.AddRange(file.MeshFiles);

            foreach (var (template, isGameData) in file.Templates)
            {
                if (template.Id.Length == 0)
                    continue;

                if (isGameData)
                {
                    if (_gameData.TryGetValue(template.Id, out var first))
                    {
                        first.ApplyGameData(template);
                    }
                    else
                    {
                        _gameData.Add(template.Id, template);
                        _gameDataOrder.Add(template);
                    }
                }
                else if (!_templates.TryAdd(template.Id, template))
                {
                    _notices.Add(new PreviewNotice(PreviewNoticeKind.DuplicateId, template.Id, $"The template in {subject} is left out, because '{_templates[template.Id].Id}' comes first."));
                }
            }
        }
    }

    /// <summary>
    /// Reads one asset file into lists, so that a file that turns out to be broken registers nothing.
    /// </summary>
    private sealed class AssetFileReader(string modId, string filePath)
    {
        private int _anonymousMaterials;

        public List<(PartTemplateData Template, bool IsGameData)> Templates { get; } = [];

        /// <summary>The materials with textures in document order, which is the order PbrMaterialReference.OnDataLoad registers them in.</summary>
        public List<MaterialData> Materials { get; } = [];

        public List<MeshFileDeclaration> MeshFiles { get; } = [];

        public void Read(Stream stream, CancellationToken cancellationToken)
        {
            using var reader = XmlReader.Create(stream, XmlSettings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != KsaPartSchema.Assets)
                throw new FormatException("The root element is not Assets.");

            ForEachChild(reader, asset =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadAsset(asset);
            });
        }

        private void ReadAsset(XmlReader asset)
        {
            switch (asset.LocalName)
            {
                case KsaPartSchema.Part:
                    Templates.Add((ReadTemplate(asset, isSubPart: false), false));
                    break;
                case KsaPartSchema.SubPart:
                    Templates.Add((ReadTemplate(asset, isSubPart: true), false));
                    break;
                case KsaPartSchema.PartGameData:
                    Templates.Add((ReadTemplate(asset, isSubPart: false), true));
                    break;
                case KsaPartSchema.SubPartGameData:
                    Templates.Add((ReadTemplate(asset, isSubPart: true), true));
                    break;
                case KsaPartSchema.PbrMaterial:
                    ReadMaterial(asset);
                    break;
                case KsaPartSchema.MeshAtlas:
                    AddMeshFile(asset, MeshFileKind.Atlas);
                    break;
                case KsaPartSchema.MeshFile:
                    AddMeshFile(asset, MeshFileKind.File);
                    break;
            }
        }

        private void AddMeshFile(XmlReader element, MeshFileKind kind)
        {
            var hostOnly = element.GetAttribute(KsaPartSchema.HostOnly) is { } text && XmlConvert.ToBoolean(text);
            if (Resolve(element.GetAttribute(KsaPartSchema.Path)) is { } file)
                MeshFiles.Add(new MeshFileDeclaration(kind, file, NonEmpty(element.GetAttribute(KsaPartSchema.Id)), hostOnly));
        }

        private PartTemplateData ReadTemplate(XmlReader element, bool isSubPart)
        {
            var id = element.GetAttribute(KsaPartSchema.Id) ?? string.Empty;
            var subParts = new List<SubPartInstanceData>();
            var models = new List<RenderModelData>();
            var connectors = new List<ConnectorData>();
            var fuelPorts = new List<FuelPortData>();
            var hasTank = false;

            ForEachChild(element, child =>
            {
                switch (child.LocalName)
                {
                    case KsaPartSchema.SubPart:
                        subParts.Add(ReadSubPartInstance(child));
                        break;
                    case KsaPartSchema.Connector:
                        connectors.Add(new ConnectorData(child.GetAttribute(KsaPartSchema.Id) ?? string.Empty, ReadTransform(child)?.Position ?? default));
                        break;
                    case KsaPartSchema.PartModel:
                        models.Add(ReadRenderModel(child, PreviewModelKind.Model));
                        break;
                    case KsaPartSchema.PartModelDynamic:
                        models.Add(ReadRenderModel(child, PreviewModelKind.Dynamic));
                        break;
                    case KsaPartSchema.PartModelGlass:
                        models.Add(ReadRenderModel(child, PreviewModelKind.Glass));
                        break;
                    case KsaPartSchema.FuelPort:
                        if (TryReadFuelPort(child) is { } port)
                            fuelPorts.Add(port);
                        break;
                    case KsaPartSchema.Tank:
                        hasTank = true;
                        break;
                }
            });

            return new PartTemplateData(id, isSubPart, subParts, models, connectors, fuelPorts, hasTank);
        }

        /// <summary>
        /// Reads a FuelPort element, or gives null when a value is not a number. The port only places the ends of fuel
        /// lines, so a bad value must not cost the parts of the file, also for a vehicle without fuel lines. A link to such a
        /// part then ends at the origin of the part.
        /// </summary>
        private static FuelPortData? TryReadFuelPort(XmlReader element)
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

        /// <summary>Reads a FuelPort element. A missing attribute has its default, and the largest angle in the file is in degrees.</summary>
        private static FuelPortData ReadFuelPort(XmlReader element)
        {
            const float DefaultMaxAngleDegrees = 10f;
            const float DefaultLeadOut = 0.15f;

            var connector = NonEmpty(element.GetAttribute(KsaPartSchema.Connector));
            var maxAngle = element.GetAttribute(KsaPartSchema.ConnectorMaxAngle) is { } angle ? XmlConvert.ToSingle(angle) : DefaultMaxAngleDegrees;
            var leadOut = element.GetAttribute(KsaPartSchema.ConnectorLeadOut) is { } lead ? XmlConvert.ToSingle(lead) : DefaultLeadOut;
            Vector3? anchor = null;
            Vector3? pivot = null;
            Vector3? tip = null;

            ForEachChild(element, child =>
            {
                switch (child.LocalName)
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
            });

            return new FuelPortData(connector, anchor ?? default, float.DegreesToRadians(maxAngle), pivot ?? default, tip ?? default, leadOut);
        }

        private static SubPartInstanceData ReadSubPartInstance(XmlReader element)
        {
            var id = element.GetAttribute(KsaPartSchema.Id) ?? string.Empty;
            var instanceOf = element.GetAttribute(KsaPartSchema.InstanceOf) ?? string.Empty;
            return new SubPartInstanceData(id, instanceOf, ReadTransform(element));
        }

        /// <summary>Reads the first Transform child of the element, or null when it has none.</summary>
        private static KsaTransform? ReadTransform(XmlReader element)
        {
            KsaTransform? transform = null;
            ForEachChild(element, child =>
            {
                if (child.LocalName == KsaPartSchema.Transform && transform is null)
                    transform = KsaTransform.Parse(child);
            });

            return transform;
        }

        private RenderModelData ReadRenderModel(XmlReader element, PreviewModelKind kind)
        {
            var modelId = element.GetAttribute(KsaPartSchema.Id) ?? string.Empty;
            string? meshId = null;
            var seenMesh = false;
            string? materialKey = null;
            var seenMaterial = false;
            var isInternal = false;
            var shadowProxy = false;

            ForEachChild(element, child =>
            {
                switch (child.LocalName)
                {
                    case KsaPartSchema.Mesh when !seenMesh:
                        seenMesh = true;
                        meshId = NonEmpty(child.GetAttribute(KsaPartSchema.Id));
                        break;
                    case KsaPartSchema.Material when !seenMaterial:
                        seenMaterial = true;
                        materialKey = ReadMaterial(child);
                        break;
                    case KsaPartSchema.Internal when kind == PreviewModelKind.Model:
                        isInternal = XmlConvert.ToBoolean(child.ReadElementContentAsString());
                        break;
                    case KsaPartSchema.RayTracing when kind == PreviewModelKind.Model:
                        shadowProxy = ReadRaytracingMode(child.ReadElementContentAsString());
                        break;
                    case KsaPartSchema.RayTracing when kind == PreviewModelKind.Glass:
                        XmlConvert.ToBoolean(child.ReadElementContentAsString());
                        break;
                }
            });

            return new RenderModelData(kind, modelId, meshId, materialKey, isInternal, shadowProxy);
        }

        /// <summary>
        /// Reads a PbrMaterial or Material element and gives the key a render model finds it by. A material without
        /// Diffuse, Normal and AoRoughMetal is a reference to the material with its Id. A material without an Id gets
        /// a key from its position in the file, which stays the same while the file does not change, as the game names
        /// it Anon_ with a hash.
        /// </summary>
        private string ReadMaterial(XmlReader element)
        {
            var id = NonEmpty(element.GetAttribute(KsaPartSchema.Id));
            var textured = false;
            string? diffuse = null;
            string? aoRoughMetal = null;
            string? normal = null;

            ForEachChild(element, child =>
            {
                switch (child.LocalName)
                {
                    case KsaPartSchema.Diffuse:
                        diffuse ??= child.GetAttribute(KsaPartSchema.Path) ?? string.Empty;
                        textured = true;
                        break;
                    case KsaPartSchema.Normal:
                        normal ??= child.GetAttribute(KsaPartSchema.Path) ?? string.Empty;
                        textured = true;
                        break;
                    case KsaPartSchema.AoRoughMetal:
                        aoRoughMetal ??= child.GetAttribute(KsaPartSchema.Path) ?? string.Empty;
                        textured = true;
                        break;
                }
            });

            var key = id ?? $"anon:{modId}/{filePath}#{++_anonymousMaterials}";
            if (textured)
                Materials.Add(new MaterialData(key, Resolve(diffuse), Resolve(aoRoughMetal), Resolve(normal)));

            return key;
        }

        /// <summary>
        /// Whether a PartModelModule.RaytracingMode value is ShadowProxy. XmlSerializer accepts only the names of the
        /// enum, so another value fails the file.
        /// </summary>
        private static bool ReadRaytracingMode(string text) => text.Trim() switch
        {
            KsaPartSchema.ShadowProxy => true,
            KsaPartSchema.RayTracingDisabled or KsaPartSchema.RayTracingEnabled => false,
            _ => throw new FormatException($"'{text}' is not a RayTracing mode."),
        };

        /// <summary>A path of this mod as an asset reference, or null when it is empty or leaves the mod folder.</summary>
        private PreviewAssetRef? Resolve(string? path) =>
            PreviewPath.Normalize(path) is { } normalized ? new PreviewAssetRef(modId, normalized) : null;

        private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

        /// <summary>
        /// Calls <paramref name="visit"/> for each child element of the element the reader is on, with a reader over
        /// that child alone, and leaves the reader after the end of the element.
        /// </summary>
        private static void ForEachChild(XmlReader reader, Action<XmlReader> visit)
        {
            if (reader.IsEmptyElement)
            {
                reader.Read();
                return;
            }

            var depth = reader.Depth;
            reader.Read();
            while (!reader.EOF && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                using (var child = reader.ReadSubtree())
                {
                    child.Read();
                    visit(child);
                }

                reader.Read();
            }

            reader.Read();
        }
    }
}
