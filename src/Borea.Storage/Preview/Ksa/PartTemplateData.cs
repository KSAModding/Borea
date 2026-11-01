using System.Numerics;
using Borea.Core.Preview;

namespace Borea.Storage.Preview.Ksa;

/// <summary>
/// What the preview keeps of one Part, SubPart, PartGameData or SubPartGameData element: its subpart instances, its
/// render models, its connectors, its fuel ports and whether it has a tank. A GameData uses the same type, because it
/// holds the same kind of content.
/// </summary>
internal sealed class PartTemplateData
{
    private readonly List<SubPartInstanceData> _subParts;
    private readonly List<RenderModelData> _renderModels;
    private readonly List<ConnectorData> _connectors;
    private readonly List<FuelPortData> _fuelPorts;

    public PartTemplateData(
        string id,
        bool isSubPart,
        IEnumerable<SubPartInstanceData> subParts,
        IEnumerable<RenderModelData> renderModels,
        IEnumerable<ConnectorData> connectors,
        IEnumerable<FuelPortData> fuelPorts,
        bool hasTank)
    {
        Id = id;
        IsSubPart = isSubPart;
        _subParts = [.. subParts];
        _renderModels = [.. renderModels];
        _connectors = [.. connectors];
        _fuelPorts = [.. fuelPorts];
        HasTank = hasTank;
    }

    public string Id { get; }

    public bool IsSubPart { get; }

    public IReadOnlyList<SubPartInstanceData> SubParts => _subParts;

    public IReadOnlyList<RenderModelData> RenderModels => _renderModels;

    public IReadOnlyList<ConnectorData> Connectors => _connectors;

    public IReadOnlyList<FuelPortData> FuelPorts => _fuelPorts;

    public bool HasTank { get; private set; }

    /// <summary>The box around the connector positions in the frame of the part, or null when it has no connector.</summary>
    public PartExtents? ConnectorExtents
    {
        get
        {
            if (_connectors.Count == 0)
                return null;

            var min = _connectors[0].Position;
            var max = min;
            foreach (var connector in _connectors)
            {
                min = Vector3.Min(min, connector.Position);
                max = Vector3.Max(max, connector.Position);
            }

            return new PartExtents(min, max);
        }
    }

    /// <summary>
    /// Merges a GameData into this item for the parts the preview draws.
    /// A subpart instance replaces the present Transform components of the first instance with the same Id, and is
    /// appended otherwise. The render models and the fuel ports are appended. A connector is appended only when no
    /// connector has its Id, because the game then only merges its Flags and Capabilities, which the preview does not need.
    /// </summary>
    public void ApplyGameData(PartTemplateData gameData)
    {
        foreach (var instance in gameData.SubParts)
            ApplyOrAddSubPartInstance(instance);

        var added = gameData.Connectors
            .Where(connector => !_connectors.Exists(existing => KsaIdComparer.Instance.Equals(existing.Id, connector.Id)))
            .ToList();
        _connectors.AddRange(added);

        _renderModels.AddRange(gameData.RenderModels);
        _fuelPorts.AddRange(gameData.FuelPorts);
        HasTank |= gameData.HasTank;
    }

    private void ApplyOrAddSubPartInstance(SubPartInstanceData instance)
    {
        var index = _subParts.FindIndex(existing => KsaIdComparer.Instance.Equals(existing.Id, instance.Id));
        if (index < 0)
        {
            _subParts.Add(instance);
            return;
        }

        // PartInstance.ApplyGameData calls Transform?.Apply, so an instance without a Transform keeps none.
        var existing = _subParts[index];
        if (existing.Transform is { } transform && instance.Transform is { } change)
            _subParts[index] = existing with { Transform = transform.Apply(change) };
    }
}

/// <summary>
/// A SubPart element inside a part template. <paramref name="Transform"/> is null when the element has no Transform,
/// which the game keeps apart from an empty Transform.
/// </summary>
internal sealed record SubPartInstanceData(string Id, string InstanceOf, KsaTransform? Transform);

/// <summary>
/// One PartModel, PartModelDynamic or PartModelGlass element. <paramref name="MaterialKey"/> is the Id of the
/// material, the generated key of an inline material without an Id, or null when the model names no material.
/// <paramref name="Internal"/> and <paramref name="ShadowProxy"/> can only be true for a PartModel, because only
/// PartModelModule.Template has the Internal element and the RaytracingMode value ShadowProxy.
/// </summary>
internal sealed record RenderModelData(
    PreviewModelKind Kind,
    string ModelId,
    string? MeshId,
    string? MaterialKey,
    bool Internal,
    bool ShadowProxy);

/// <summary>A Connector element. Only its position counts for the preview, because it sizes a placeholder.</summary>
internal sealed record ConnectorData(string Id, Vector3 Position);

/// <summary>
/// A FuelPort element. <paramref name="Anchor"/> is in the frame of the full part, and
/// <paramref name="Pivot"/> and <paramref name="Tip"/> are in the frame of the connector subpart. A missing vector is
/// zero.
/// </summary>
/// <param name="ConnectorSubPartId">The Id of the connector subpart, or null when the port has none.</param>
/// <param name="MaxAngleRadians">How far the connector can turn from its rest pose toward the hose.</param>
/// <param name="LeadOut">How far the hose runs straight out of the tip, in meters.</param>
internal sealed record FuelPortData(string? ConnectorSubPartId, Vector3 Anchor, float MaxAngleRadians, Vector3 Pivot, Vector3 Tip, float LeadOut);

/// <summary>An axis-aligned box in the frame of a part, in meters.</summary>
internal readonly record struct PartExtents(Vector3 Min, Vector3 Max);

/// <summary>
/// One PbrMaterial with textures, from the top level of an asset file or inline in a render model. A texture is null
/// when the material does not name it by a path. <paramref name="Normal"/> is the path of the Normal element, which
/// PbrMaterialReference.NormalReference reads as a TexturePowerReference.
/// </summary>
internal sealed record MaterialData(string Key, PreviewAssetRef? Diffuse, PreviewAssetRef? AoRoughMetal, PreviewAssetRef? Normal);

/// <summary>Whether a mesh file declaration is a MeshAtlas, whose meshes register under their glTF names, or a MeshFile.</summary>
internal enum MeshFileKind
{
    Atlas,
    File,
}

/// <summary>
/// A top-level MeshAtlas or MeshFile element. <paramref name="File"/> is resolved against the declaring mod.
/// <paramref name="Id"/> is the Id attribute, which names the mesh of a MeshFile.
/// </summary>
internal sealed record MeshFileDeclaration(MeshFileKind Kind, PreviewAssetRef File, string? Id, bool HostOnly);
