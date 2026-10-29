using System.Numerics;
using Borea.Preview.Scene;

namespace Borea.Preview.Rendering;

/// <summary>
/// Bilinear texture lookups with a repeating wrap. Texel row 0 is at v = 0 and there is no V flip, which
/// is the UV origin of glTF and KTX2. The four texels go to linear values through a table before they
/// are blended, so a diffuse is filtered in linear space.
/// </summary>
internal static class TextureSampler
{
    /// <summary>
    /// The power curve of gammaToLinear in the game shader Shared.glsl, pow(value, 2.2), which
    /// ThumbnailMesh.frag applies to the sampled diffuse.
    /// </summary>
    private static readonly float[] GammaToLinearTable = BuildTable(value => MathF.Pow(value, 2.2f));

    private static readonly float[] UnormTable = BuildTable(value => value);

    /// <summary>The linear color of <paramref name="image"/> at the UV, with the alpha ignored.</summary>
    public static Vector3 SampleDiffuse(PreviewImage image, float u, float v) => Sample(image, u, v, GammaToLinearTable);

    /// <summary>The R, G and B bytes of <paramref name="image"/> at the UV as values from 0 to 1, for data such as AoRoughMetal.</summary>
    public static Vector3 SampleData(PreviewImage image, float u, float v) => Sample(image, u, v, UnormTable);

    /// <summary>
    /// The tangent space normal of a normal map at the UV. Only red and green are read, as unorm data, and
    /// they give x and y from -1 to 1. Then z is rebuilt as sqrt(1 - clamp(x * x + y * y, 0, 1)), as
    /// getNormalFromMap_ShaderX in the game shader Common/SharedFrag.glsl does for a BC5 texture, which has
    /// only these two channels. The result is not normalized, which the shader leaves to the frame.
    /// </summary>
    public static Vector3 SampleNormal(PreviewImage image, float u, float v)
    {
        var (x0, x1, fx) = Footprint(u, image.Width);
        var (y0, y1, fy) = Footprint(v, image.Height);
        var rgba = image.Rgba;
        var row0 = y0 * image.Width;
        var row1 = y1 * image.Width;

        var top = Vector2.Lerp(TexelRg(rgba, row0 + x0), TexelRg(rgba, row0 + x1), fx);
        var bottom = Vector2.Lerp(TexelRg(rgba, row1 + x0), TexelRg(rgba, row1 + x1), fx);
        var xy = Vector2.Lerp(top, bottom, fy) * 2f - Vector2.One;
        var z = MathF.Sqrt(1f - Math.Clamp(Vector2.Dot(xy, xy), 0f, 1f));
        return new Vector3(xy, z);
    }

    private static Vector3 Sample(PreviewImage image, float u, float v, float[] table)
    {
        var (x0, x1, fx) = Footprint(u, image.Width);
        var (y0, y1, fy) = Footprint(v, image.Height);
        var rgba = image.Rgba;
        var row0 = y0 * image.Width;
        var row1 = y1 * image.Width;

        var top = Vector3.Lerp(Texel(rgba, row0 + x0, table), Texel(rgba, row0 + x1, table), fx);
        var bottom = Vector3.Lerp(Texel(rgba, row1 + x0, table), Texel(rgba, row1 + x1, table), fx);
        return Vector3.Lerp(top, bottom, fy);
    }

    /// <summary>
    /// The two texels on one axis that a coordinate falls between, and the weight of the second. Texel i
    /// has its center at (i + 0.5) / size, so a coordinate at a texel center gives that texel alone.
    /// </summary>
    private static (int First, int Second, float Weight) Footprint(float coordinate, int size)
    {
        var position = coordinate * size - 0.5f;
        if (!float.IsFinite(position))
        {
            position = 0f;
        }

        var floor = MathF.Floor(position);
        var remainder = (double)floor % size;
        var first = (int)(remainder < 0 ? remainder + size : remainder);
        var second = first + 1 == size ? 0 : first + 1;
        return (first, second, position - floor);
    }

    private static Vector3 Texel(byte[] rgba, int texel, float[] table)
    {
        var offset = texel * 4;
        return new Vector3(table[rgba[offset]], table[rgba[offset + 1]], table[rgba[offset + 2]]);
    }

    private static Vector2 TexelRg(byte[] rgba, int texel)
    {
        var offset = texel * 4;
        return new Vector2(UnormTable[rgba[offset]], UnormTable[rgba[offset + 1]]);
    }

    private static float[] BuildTable(Func<float, float> curve)
    {
        var table = new float[256];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = curve(i / 255f);
        }

        return table;
    }
}
