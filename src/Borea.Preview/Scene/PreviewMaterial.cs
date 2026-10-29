using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>
/// How a draw is shaded. <see cref="BaseColor"/> is a linear color from 0 to 1, used when
/// <see cref="Diffuse"/> is null. <see cref="AoRoughMetal"/> is the AoRoughMetal texture of a
/// PbrMaterialReference. The preview reads ambient occlusion from R, roughness from G and metalness
/// from B, which follows the name and is not verified in the decompiled source.
/// </summary>
/// <param name="Glass">The draw comes from a PartModelGlass, which the preview draws opaque with a dark tint.</param>
public sealed record PreviewMaterial(Vector3 BaseColor, PreviewImage? Diffuse, PreviewImage? AoRoughMetal, bool Glass)
{
    public static PreviewMaterial Flat(Vector3 baseColor) => new(baseColor, null, null, false);
}
