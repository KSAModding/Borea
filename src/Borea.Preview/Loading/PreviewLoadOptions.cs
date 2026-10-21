namespace Borea.Preview.Loading;

/// <summary>
/// The budgets of one scene build. The software renderer draws every triangle on the CPU, so the triangle budget
/// follows the core count: a machine with 4 cores or fewer gets <see cref="SmallMachineTriangles"/>, which keeps a
/// drag frame near the measured cost, and a larger machine gets <see cref="LargeMachineTriangles"/>.
/// </summary>
public sealed record PreviewLoadOptions
{
    public const int SmallMachineTriangles = 1_000_000;

    public const int LargeMachineTriangles = 2_000_000;

    public const long DefaultMaxRgbaBytes = 128L * 1024 * 1024;

    public const int DefaultTextureTarget = 512;

    /// <summary>The texture target that the build drops to when the textures at the full target do not fit the RGBA budget.</summary>
    public const int ReducedTextureTarget = 256;

    public PreviewLoadOptions()
        : this(Environment.ProcessorCount)
    {
    }

    /// <param name="processorCount">The core count that sets the triangle budget. Tests give it, and the App uses <see cref="Environment.ProcessorCount"/>.</param>
    public PreviewLoadOptions(int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        MaxTriangles = processorCount <= 4 ? SmallMachineTriangles : LargeMachineTriangles;
    }

    /// <summary>The most triangles that the draws of the scene may have together. A draw counts the triangles of its mesh each time.</summary>
    public int MaxTriangles
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    }

    /// <summary>The most bytes of decoded RGBA texture levels that the scene may hold.</summary>
    public long MaxRgbaBytes
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = DefaultMaxRgbaBytes;

    /// <summary>The largest side in pixels of a texture level, which is the idea of the closest mip level in TextureReference.Bind.</summary>
    public int TextureTarget
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultTextureTarget;
}
