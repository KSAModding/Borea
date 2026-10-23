namespace Borea.Core.Preview;

/// <summary>
/// Why a placeholder box shows instead of a model.
/// </summary>
public enum PlaceholderReason
{
    /// <summary>No loaded mod has a Part with the Id that vehicle.xml names.</summary>
    UnknownPart,

    /// <summary>No loaded mod has a SubPart with the Id that a part template names.</summary>
    UnknownSubPart,

    /// <summary>No loaded mod has a mesh with the Id that a render model names.</summary>
    UnknownMesh,

    /// <summary>No loaded mod has a material with the Id that a render model names.</summary>
    UnknownMaterial,

    /// <summary>The mod that supplies the item is needed but not on disk.</summary>
    ModNotLoaded,

    /// <summary>The mesh file was found but could not be read or decoded.</summary>
    AssetFailed,

    /// <summary>The model was left out to keep the scene inside its budget.</summary>
    Budget,
}
