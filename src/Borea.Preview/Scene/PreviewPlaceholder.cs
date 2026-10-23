using System.Numerics;

namespace Borea.Preview.Scene;

/// <summary>
/// A box drawn in place of a part, subpart, mesh or material that the preview could not show. The box
/// spans <paramref name="HalfSize"/> on each side of the origin of <paramref name="Transform"/>.
/// </summary>
/// <param name="HalfSize">Finite and not negative on each axis, else the constructor throws <see cref="ArgumentOutOfRangeException"/>.</param>
/// <param name="MissingId">The Id that did not resolve, as the vehicle or the part template names it.</param>
/// <param name="ModId">The needed mod that holds the part when the reason is <see cref="PreviewPlaceholderReason.ModNotLoaded"/>.</param>
public sealed record PreviewPlaceholder(Matrix4x4 Transform, Vector3 HalfSize, string MissingId, PreviewPlaceholderReason Reason, string? ModId = null)
{
    public Vector3 HalfSize { get; } = ValidHalfSize(HalfSize);

    public PreviewBounds Bounds => PreviewBounds.FromCenter(Vector3.Zero, HalfSize).Transform(Transform);

    private static Vector3 ValidHalfSize(Vector3 halfSize)
    {
        if (!float.IsFinite(halfSize.X) || !float.IsFinite(halfSize.Y) || !float.IsFinite(halfSize.Z)
            || halfSize.X < 0f || halfSize.Y < 0f || halfSize.Z < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(halfSize), halfSize, "The half size must be finite and not negative.");
        }

        return halfSize;
    }
}

/// <summary>
/// Why a placeholder is drawn. It has the same values as the reason in the scene description of
/// Borea.Core.
/// </summary>
public enum PreviewPlaceholderReason
{
    UnknownPart,
    UnknownSubPart,
    UnknownMesh,
    UnknownMaterial,
    ModNotLoaded,
    AssetFailed,
    Budget,
}
