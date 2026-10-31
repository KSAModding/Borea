using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>
/// How a draw is shaded. <see cref="BaseColor"/> is a linear color from 0 to 1, used when
/// <see cref="Diffuse"/> is null. <see cref="AoRoughMetal"/> is the AoRoughMetal texture of a
/// PbrMaterialReference. The preview reads ambient occlusion from R, roughness from G and metalness
/// from B, which is the order that the game shader ThumbnailMesh.frag reads.
/// </summary>
/// <param name="Glass">The draw comes from a PartModelGlass, which the preview draws opaque with a dark tint.</param>
public sealed record PreviewMaterial(Vector3 BaseColor, PreviewImage? Diffuse, PreviewImage? AoRoughMetal, bool Glass)
{
    /// <summary>
    /// The index of the material in the scene description that the scene builder made this material from, or -1. The
    /// texture stage of the builder uses it to find the textures of a draw in a scene that has only flat colors.
    /// </summary>
    internal int SourceMaterial { get; init; } = -1;

    /// <summary>
    /// The Normal texture of the PbrMaterialReference, or null. It is unorm data and not a color, so the renderer
    /// reads its .rg without the gamma curve of <see cref="Diffuse"/>, as ThumbnailMesh.frag does.
    /// </summary>
    public PreviewImage? Normal { get; init; }

    public static PreviewMaterial Flat(Vector3 baseColor) => new(baseColor, null, null, false);
}
