using System.Numerics;

namespace Borea.Core.Preview;

/// <summary>
/// The scene of one vehicle as the resolver finds it in the game and mod files, before any mesh or texture is decoded.
/// Every matrix uses the <see cref="System.Numerics"/> row-vector order, so a point moves as v_world = v_local * M.
/// World space is the vehicle assembly frame of the game (Part.PositionVehicleAsmb), in meters, with +X along the stack.
/// </summary>
/// <param name="Meshes">The distinct meshes the draws use. A draw names one by its index.</param>
/// <param name="Materials">The distinct materials the draws use. A draw names one by its index.</param>
/// <param name="Draws">One entry for each render model of each part and subpart that resolved.</param>
/// <param name="Placeholders">One box for each part, subpart, mesh or material that did not resolve or was left out.</param>
/// <param name="Notices">What the player should know about the preview, for example a duplicate Id or a mod that is not loaded.</param>
/// <param name="PartCount">The number of full parts in vehicle.xml, resolved or not.</param>
/// <param name="SkippedInternalCount">The number of render models left out because the game does not draw them outside, such as Internal models and shadow proxies.</param>
public sealed record ResolvedVehicle(
    IReadOnlyList<PreviewMeshRef> Meshes,
    IReadOnlyList<PreviewMaterialRef> Materials,
    IReadOnlyList<ResolvedDraw> Draws,
    IReadOnlyList<ResolvedPlaceholder> Placeholders,
    IReadOnlyList<PreviewNotice> Notices,
    int PartCount,
    int SkippedInternalCount);

/// <summary>
/// One mesh of a glTF file. <paramref name="MeshIndex"/> is the position in the glTF meshes array, and
/// <paramref name="Name"/> is the Id the game registers the mesh under (MeshAtlasFileReference.DoLoad).
/// </summary>
public sealed record PreviewMeshRef(PreviewAssetRef File, int MeshIndex, string Name);

/// <summary>
/// One PBR material. <paramref name="Key"/> is the material Id, or a generated key for a material that is written
/// inline. A texture that the material does not name is null, and the renderer then uses a flat color.
/// </summary>
public sealed record PreviewMaterialRef(string Key, PreviewAssetRef? Diffuse, PreviewAssetRef? AoRoughMetal, bool Glass);

/// <summary>
/// One mesh drawn with one material. <paramref name="Mesh"/> indexes <see cref="ResolvedVehicle.Meshes"/>, and
/// <paramref name="Material"/> indexes <see cref="ResolvedVehicle.Materials"/>, or is -1 when the model names no
/// material, which gives the flat default color. <paramref name="Transform"/> takes the mesh from its own space into
/// the vehicle assembly frame.
/// </summary>
public sealed record ResolvedDraw(int Mesh, int Material, Matrix4x4 Transform, string PartId, PreviewModelKind Kind);

/// <summary>
/// A box drawn where something did not resolve. The box has its center at the local origin and extends
/// <paramref name="HalfSize"/> along each local axis, and <paramref name="Transform"/> takes it into the vehicle
/// assembly frame. <paramref name="ModId"/> names the mod that would supply the missing item when it is known.
/// </summary>
public sealed record ResolvedPlaceholder(Matrix4x4 Transform, Vector3 HalfSize, string MissingId, PlaceholderReason Reason, string? ModId);
