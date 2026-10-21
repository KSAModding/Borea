namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// The XML element and attribute names that the vehicle preview reads from KSA part files and vehicle files.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place that /update-game checks again after a game update. The names were verified against the
/// decompiled KSA build 2026.10.19.5638, in the XmlRoot, XmlElement, XmlAttribute and XmlType attributes of
/// AssetBundle, PartTemplate, PartInstance, PartModelModule.Template, PartModelDynamicModule, PartModelGlassModule,
/// PbrMaterialReference, MeshAtlasFileReference, MeshFileReference, FileReference, TransformReference,
/// Vector3Reference and VehicleSaveData.
/// </para>
/// <para>
/// The game compares Ids without case. SerializedId.SetHash stores KeyHash.Make(Id), SerializedCollection.Register
/// adds an item with TryAdd on that hash so the first registration wins, and ModLibrary.Find hashes the requested Id
/// the same way. KSA.KeyHash is in Planet.Render.Core.dll, not in KSA.dll. KeyHash.Make (decompiled from that file
/// for build 2026.10.19.5638) lowercases the Id with ToLowerInvariant and then takes Crc32.HashToUInt32 of its UTF-8
/// bytes, so <see cref="KsaIdComparer.Registry"/> uses an ordinal comparison that ignores case.
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
