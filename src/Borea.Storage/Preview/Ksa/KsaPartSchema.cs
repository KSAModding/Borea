namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// The XML element and attribute names that the vehicle preview reads from KSA part files and vehicle files.
/// </summary>
/// <remarks>
/// <para>
/// These names are the one place to check again after a game update. They match KSA build 2026.10.19.5638, in the
/// XmlRoot, XmlElement, XmlAttribute and XmlType attributes of
/// AssetBundle, PartTemplate, PartInstance, PartModelModule.Template, PartModelDynamicModule, PartModelGlassModule,
/// PbrMaterialReference, MeshAtlasFileReference, MeshFileReference, FileReference, TransformReference,
/// Vector3Reference and VehicleSaveData.
/// </para>
/// <para>
/// The game compares Ids without case. SerializedId.SetHash stores KeyHash.Make(Id), SerializedCollection.Register
/// adds an item with TryAdd on that hash so the first registration wins, and ModLibrary.Find hashes the requested Id
/// the same way. KSA.KeyHash is in Planet.Render.Core.dll, not in KSA.dll. In build 2026.10.19.5638, KeyHash.Make
/// lowercases the Id with ToLowerInvariant and then takes Crc32.HashToUInt32 of its UTF-8 bytes, so
/// <see cref="KsaIdComparer.Registry"/> uses an ordinal comparison that ignores case.
/// </para>
/// <para>
/// Some kinds share one registry. Part and SubPart templates both go into ModLibrary.AllParts, because
/// SubPartTemplate derives from PartTemplate and PartTemplate.OnDataLoad calls ModLibrary.Register(this). PartGameData
/// and SubPartGameData both go into ModLibrary.AllPartGameDataReferences, because SubPartGameDataReference derives
/// from PartGameDataReference. The first registration wins across the two kinds of each pair, so a SubPart "X" drops
/// a later Part "x", and a SubPartGameData folds into a PartGameData with the same Id (PartGameDataReference.OnDataLoad
/// calls ApplyGameData on the one that Find returns). ModLibrary.AttachGameData then walks AllParts, which holds both
/// template kinds, and applies each GameData to the first template whose Id matches.
/// </para>
/// <para>
/// Element and attribute names are case-sensitive, as XmlSerializer reads them.
/// </para>
/// </remarks>
internal static class KsaPartSchema
{
    public const string VerifiedGameBuild = "2026.10.19.5638";

    public const string Assets = "Assets";
    public const string Part = "Part";
    public const string SubPart = "SubPart";
    public const string PartGameData = "PartGameData";
    public const string SubPartGameData = "SubPartGameData";
    public const string PbrMaterial = "PbrMaterial";
    public const string MeshAtlas = "MeshAtlas";
    public const string MeshFile = "MeshFile";

    public const string Material = "Material";
    public const string Diffuse = "Diffuse";
    public const string AoRoughMetal = "AoRoughMetal";

    public const string PartModel = "PartModel";
    public const string PartModelDynamic = "PartModelDynamic";
    public const string PartModelGlass = "PartModelGlass";
    public const string Mesh = "Mesh";
    public const string Internal = "Internal";
    public const string RayTracing = "RayTracing";

    /// <summary>The PartModelModule.RaytracingMode value of a model that the game does not draw.</summary>
    public const string ShadowProxy = "ShadowProxy";

    public const string Connector = "Connector";

    public const string Transform = "Transform";
    public const string Position = "Position";
    public const string Rotation = "Rotation";
    public const string Scale = "Scale";

    public const string RootPartRef = "RootPartRef";
    public const string PartRef = "PartRef";

    public const string Id = "Id";
    public const string Path = "Path";
    public const string HostOnly = "HostOnly";
    public const string InstanceOf = "InstanceOf";
    public const string LocalInstanceId = "LocalInstanceId";
    public const string X = "X";
    public const string Y = "Y";
    public const string Z = "Z";
}
