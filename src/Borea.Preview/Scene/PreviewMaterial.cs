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
    /// <summary>
    /// The index of the material in the scene description that the scene builder made this material from, or -1. The
    /// texture stage of the builder uses it to find the textures of a draw in a scene that has only flat colors.
    /// </summary>
    internal int SourceMaterial { get; init; } = -1;

    public static PreviewMaterial Flat(Vector3 baseColor) => new(baseColor, null, null, false);
}
