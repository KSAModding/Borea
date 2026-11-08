namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// The XML element and attribute names that the vehicle preview reads from KSA part files and vehicle files.
/// </summary>
/// <remarks>
/// <para>
/// These names are the one place to check again after a game update. They match KSA build 2026.11.10.5801, in the
/// XmlElement, XmlArray, XmlArrayItem, XmlAttribute and XmlType attributes of PartTemplate, TransformDataReference,
/// DataReference, KeyReference, PartModelModule.Template, PartModelDynamicModule.Template,
/// PartModelGlassModule.Template, FuelPort.TemplateData, PbrMaterialReference, TextureReference,
/// MeshAtlasFileReference, MeshFileReference, FileReference, TransformReference, Vector3Reference and
/// VehicleSaveData, and in the tag table of GameContentTags.Build and the reserved attributes of ReservedAttributes.
/// The catalog also reads the older layout of build 2026.10.25.5707 and before: subparts, connectors and render models
/// directly in a Part or SubPart, Id on nested elements, texture paths in a material, and the PartGameData and
/// SubPartGameData elements.
/// </para>
/// <para>
/// The game compares Ids without case. SerializedId.SetHash stores KeyHash.Make(Id), and a collection registers an
/// item with TryRegister on that hash, so the first registration wins (ContentLoader.RegisterRows). KSA.KeyHash is in
/// Planet.Render.Core.dll, not in KSA.dll; it lowercases the Id with ToLowerInvariant and then takes
/// Crc32.HashToUInt32 of its UTF-8 bytes, so <see cref="KsaIdComparer.Registry"/> uses an ordinal comparison that
/// ignores case.
/// </para>
/// <para>
/// Some tags share one registry. GameContentTags.Build binds Part and SubPart to ModLibrary.AllParts, and Texture,
/// MeshAtlas and MeshFile to ModLibrary.AllFiles, so a SubPart "X" keeps a later Part "x" out, and a Texture keeps a
/// later MeshAtlas with its Id out. In the older layout, PartGameData and SubPartGameData share
/// ModLibrary.AllPartGameDataReferences, a GameData folds into the first one with its Id, and ModLibrary.AttachGameData
/// applies each GameData to the first template in AllParts whose Id is equal.
/// </para>
/// <para>
/// Element and attribute names are case-sensitive, as XmlSerializer reads them.
/// </para>
/// </remarks>
internal static class KsaPartSchema
{
    public const string VerifiedGameBuild = "2026.11.10.5801";

    public const string Assets = "Assets";
    public const string Part = "Part";
    public const string SubPart = "SubPart";
    public const string PartGameData = "PartGameData";
    public const string SubPartGameData = "SubPartGameData";
    public const string PbrMaterial = "PbrMaterial";
    public const string MeshAtlas = "MeshAtlas";
    public const string MeshFile = "MeshFile";
    public const string Texture = "Texture";

    public const string SubParts = "SubParts";
    public const string Connectors = "Connectors";
    public const string Components = "Components";

    public const string Material = "Material";
    public const string Diffuse = "Diffuse";
    public const string Normal = "Normal";
    public const string AoRoughMetal = "AoRoughMetal";

    public const string PartModel = "PartModel";
    public const string PartModelDynamic = "PartModelDynamic";
    public const string PartModelGlass = "PartModelGlass";
    public const string Mesh = "Mesh";
    public const string Internal = "Internal";
    public const string RayTracing = "RayTracing";

    /// <summary>
    /// The PartModelModule.RaytracingMode values. These three are all the names of the enum, and the game does not
    /// draw a model with ShadowProxy.
    /// </summary>
    public const string RayTracingDisabled = "Disabled";
    public const string RayTracingEnabled = "Enabled";
    public const string ShadowProxy = "ShadowProxy";

    public const string Connector = "Connector";

    public const string Transform = "Transform";
    public const string Position = "Position";
    public const string Rotation = "Rotation";
    public const string Scale = "Scale";

    public const string VehicleSaveData = "VehicleSaveData";
    public const string RootPartRef = "RootPartRef";
    public const string PartRef = "PartRef";
    public const string Sequence = "Sequence";

    public const string FuelLink = "FuelLink";
    public const string PartA = "PartA";
    public const string PartB = "PartB";
    public const string Node = "Node";

    public const string FuelPort = "FuelPort";
    public const string AnchorAsmb = "AnchorAsmb";
    public const string ConnectorPivot = "ConnectorPivot";
    public const string ConnectorTip = "ConnectorTip";
    public const string ConnectorMaxAngle = "ConnectorMaxAngle";
    public const string ConnectorLeadOut = "ConnectorLeadOut";
    public const string Tank = "Tank";

    public const string HoseMesh = "CoreFuelTankB_Subpart_FuelLineHose";
    public const string HoseMaterial = "CoreFuelTankB_Material";

    public const string Id = "Id";
    public const string Key = "Key";
    public const string Ref = "Ref";
    public const string KeyRef = "KeyRef";
    public const string Parent = "Parent";
    public const string Abstract = "Abstract";
    public const string Modify = "Modify";
    public const string Path = "Path";
    public const string HostOnly = "HostOnly";
    public const string InstanceOf = "InstanceOf";
    public const string LocalInstanceId = "LocalInstanceId";
    public const string X = "X";
    public const string Y = "Y";
    public const string Z = "Z";
}
