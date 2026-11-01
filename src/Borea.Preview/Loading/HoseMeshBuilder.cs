using System.Numerics;
using Borea.Core.Preview;
using Borea.Preview.Scene;

namespace Borea.Preview.Loading;

/// <summary>
/// The place of a hose in a texture atlas. A point of the hose has the UV
/// <see cref="Origin"/> + <see cref="Around"/> * a + <see cref="Along"/> * b, with a and b from 0 to 1.
/// </summary>
/// <param name="TileLength">The length in meters of the hose that one repeat of the patch covers.</param>
internal readonly record struct HoseUvPatch(Vector2 Origin, Vector2 Around, Vector2 Along, float TileLength)
{
    /// <summary>
    /// Finds the patch of a straight hose mesh along its local Z. The two ends of the mesh give the step of the UV along
    /// the hose, and the rest of the UV box is the way around it. The preview does not test which way the UVs run around
    /// the mesh, because a wrong guess only mirrors the texture around the tube.
    /// </summary>
    /// <returns>The patch, or null when the mesh has no UVs or is not such a hose.</returns>
    public static HoseUvPatch? Find(PreviewMesh mesh)
    {
        const float RelativeTolerance = 0.001f;

        if (mesh.Uvs is not { } uvs)
            return null;

        var tileLength = mesh.LocalBounds.Max.Z - mesh.LocalBounds.Min.Z;
        if (tileLength <= 1e-6f)
            return null;

        var tolerance = tileLength * RelativeTolerance;
        var start = -1;
        for (var i = 0; i < mesh.VertexCount && start < 0; i++)
        {
            if (Position(mesh, i).Z - mesh.LocalBounds.Min.Z <= tolerance)
                start = i;
        }

        // A seam can have two vertices at one place, so the nearest UV is the one on the same strip of the atlas.
        var startPosition = Position(mesh, start);
        var startUv = Uv(uvs, start);
        Vector2? along = null;
        var uvMin = startUv;
        var uvMax = startUv;
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var uv = Uv(uvs, i);
            uvMin = Vector2.Min(uvMin, uv);
            uvMax = Vector2.Max(uvMax, uv);

            var position = Position(mesh, i);
            var offsetXy = new Vector2(position.X - startPosition.X, position.Y - startPosition.Y);
            if (mesh.LocalBounds.Max.Z - position.Z > tolerance || offsetXy.Length() > tolerance)
                continue;

            var step = uv - startUv;
            if (along is not { } best || step.LengthSquared() < best.LengthSquared())
                along = step;
        }

        if (along is not { } alongStep || alongStep.LengthSquared() < 1e-12f)
            return null;

        var patch = MathF.Abs(alongStep.X) >= MathF.Abs(alongStep.Y)
            ? new HoseUvPatch(new Vector2(startUv.X, uvMin.Y), new Vector2(0f, uvMax.Y - uvMin.Y), new Vector2(alongStep.X, 0f), tileLength)
            : new HoseUvPatch(new Vector2(uvMin.X, startUv.Y), new Vector2(uvMax.X - uvMin.X, 0f), new Vector2(0f, alongStep.Y), tileLength);

        // Finite UVs far apart can give an infinite difference, and the tube would then have a UV that PreviewMesh rejects.
        return IsFinite(patch.Around) && IsFinite(patch.Along) ? patch : null;
    }

    private static Vector3 Position(PreviewMesh mesh, int index) =>
        new(mesh.Positions[3 * index], mesh.Positions[(3 * index) + 1], mesh.Positions[(3 * index) + 2]);

    private static Vector2 Uv(float[] uvs, int index) => new(uvs[2 * index], uvs[(2 * index) + 1]);

    private static bool IsFinite(Vector2 vector) => float.IsFinite(vector.X) && float.IsFinite(vector.Y);
}

/// <summary>
/// Makes the tube of one fuel line in the vehicle frame with few triangles, because the preview draws every triangle on
/// the CPU. The curve is a Hermite span where a waypoint has a pinned direction and a centripetal Catmull-Rom span
/// (Yuksel, Schaefer and Keyser 2011) elsewhere. The tube has <see cref="Sides"/> sides and a ring only where the curve
/// has turned, so a straight hose costs very few triangles. It has no end caps.
/// </summary>
/// <remarks>
/// The renderer of the preview does not wrap a UV into the atlas patch, so a UV that runs past the patch would read
/// another part of the atlas. The tube therefore has a ring at each repeat of the patch, with two rows of vertices there,
/// one at the end of the patch and one at its start. A very long hose repeats the patch at most <see cref="MaxTiles"/>
/// times, which stretches the texture a little. The seam of the texture around the tube sits on an arbitrary side.
/// </remarks>
internal static class HoseMeshBuilder
{
    /// <summary>The radius of a hose, in meters.</summary>
    public const float Radius = 0.05f;

    public const int Sides = 8;

    public const int MaxRings = 64;

    public const int MaxTiles = 16;

    /// <summary>The most triangles of one tube.</summary>
    public const int MaxTriangles = (MaxRings + MaxTiles) * Sides * 2;

    /// <summary>The points of the curve for each span between two waypoints.</summary>
    private const int SamplesPerSpan = 16;

    /// <summary>A route shorter than this has no tube, in meters.</summary>
    private const float MinLength = 1e-4f;

    /// <summary>How far the curve turns before the next ring, as the cosine of 15 degrees.</summary>
    private const float RingTurnCosine = 0.9659f;

    private const float MinKnotDistance = 1e-5f;

    /// <returns>The tube, or null when the route is shorter than <see cref="MinLength"/> or has a number that cannot make a tube.</returns>
    public static PreviewMesh? Build(IReadOnlyList<HoseWaypoint> route, HoseUvPatch? patch)
    {
        if (route.Count < 2)
            return null;

        var curve = SampleCurve(route);
        var length = 0f;
        for (var i = 1; i < curve.Count; i++)
            length += Vector3.Distance(curve[i - 1], curve[i]);

        if (!float.IsFinite(length) || length < MinLength)
            return null;

        var tile = patch is { } uvPatch ? MathF.Max(uvPatch.TileLength, length / MaxTiles) : float.PositiveInfinity;
        var points = SplitAtTiles(curve, tile);
        var stations = PickRings(points);
        var tileEnds = stations.Count(index => points[index].IsTileEnd);
        var rowCount = stations.Count + tileEnds;
        var positions = new float[rowCount * (Sides + 1) * 3];
        var normals = new float[positions.Length];
        var uvs = patch is null ? null : new float[rowCount * (Sides + 1) * 2];
        var indices = new uint[(stations.Count - 1) * Sides * 6];

        var tangent = Vector3.UnitZ;
        var side = Vector3.Zero;
        var row = 0;
        var tiles = 0;
        var nextIndex = 0;
        for (var i = 0; i < stations.Count; i++)
        {
            var point = points[stations[i]];
            tangent = TangentAt(points, stations[i], tangent);
            side = SideOf(tangent, side);
            var along = patch is null ? 0f : Math.Clamp((point.Arc - (tiles * tile)) / tile, 0f, 1f);
            WriteRow(row, point.Position, tangent, side, along, patch, positions, normals, uvs);
            if (point.IsTileEnd)
            {
                row++;
                tiles++;
                WriteRow(row, point.Position, tangent, side, 0f, patch, positions, normals, uvs);
            }

            if (i < stations.Count - 1)
                nextIndex = WriteQuads(row, nextIndex, indices);

            row++;
        }

        return new PreviewMesh(positions, normals, uvs, indices);
    }

    /// <summary>
    /// The dense curve through the waypoints, without two points at one place, so each step has a direction. A point that
    /// is not a number fails the distance test and stays out, and an infinite point makes the length of the curve infinite.
    /// </summary>
    private static List<Vector3> SampleCurve(IReadOnlyList<HoseWaypoint> route)
    {
        var curve = new List<Vector3>(((route.Count - 1) * SamplesPerSpan) + 1) { route[0].Position };
        for (var span = 0; span < route.Count - 1; span++)
        {
            for (var i = 1; i <= SamplesPerSpan; i++)
            {
                var point = PointOnSpan(route, span, (float)i / SamplesPerSpan);
                if (Vector3.DistanceSquared(point, curve[^1]) > 1e-12f)
                    curve.Add(point);
            }
        }

        return curve;
    }

    private static Vector3 PointOnSpan(IReadOnlyList<HoseWaypoint> route, int span, float t)
    {
        var before = route[Math.Max(span - 1, 0)].Position;
        var start = route[span];
        var end = route[span + 1];
        var after = route[Math.Min(span + 2, route.Count - 1)].Position;

        if (start.PinnedDirection == Vector3.Zero && end.PinnedDirection == Vector3.Zero)
            return CentripetalCatmullRom(before, start.Position, end.Position, after, t);

        var scale = Vector3.Distance(start.Position, end.Position);
        var startTangent = start.PinnedDirection != Vector3.Zero ? start.PinnedDirection : NormalizeOrZero(end.Position - before);
        var endTangent = end.PinnedDirection != Vector3.Zero ? end.PinnedDirection : NormalizeOrZero(after - start.Position);
        return Hermite(start.Position, startTangent * scale, end.Position, endTangent * scale, t);
    }

    private static Vector3 Hermite(Vector3 start, Vector3 startTangent, Vector3 end, Vector3 endTangent, float t)
    {
        var square = t * t;
        var cube = square * t;
        return (start * ((2f * cube) - (3f * square) + 1f))
            + (startTangent * (cube - (2f * square) + t))
            + (end * ((3f * square) - (2f * cube)))
            + (endTangent * (cube - square));
    }

    /// <summary>The Barry and Goldman form of the spline, whose interpolations run past 0 and 1 by design.</summary>
    private static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var knot1 = Knot(p0, p1);
        var knot2 = knot1 + Knot(p1, p2);
        var knot3 = knot2 + Knot(p2, p3);
        var u = knot1 + (t * (knot2 - knot1));

        var a1 = Vector3.Lerp(p0, p1, u / knot1);
        var a2 = Vector3.Lerp(p1, p2, (u - knot1) / (knot2 - knot1));
        var a3 = Vector3.Lerp(p2, p3, (u - knot2) / (knot3 - knot2));
        var b1 = Vector3.Lerp(a1, a2, u / knot2);
        var b2 = Vector3.Lerp(a2, a3, (u - knot1) / (knot3 - knot1));
        return Vector3.Lerp(b1, b2, (u - knot1) / (knot2 - knot1));
    }

    private static float Knot(Vector3 a, Vector3 b) => MathF.Sqrt(MathF.Max(Vector3.Distance(a, b), MinKnotDistance));

    /// <summary>The curve with a marked point at each end of a repeat of the patch, and the distance along the curve at each point.</summary>
    private static List<CurvePoint> SplitAtTiles(List<Vector3> curve, float tile)
    {
        var points = new List<CurvePoint>(curve.Count + MaxTiles) { new(curve[0], 0f, false) };
        var tolerance = tile * 1e-3f;
        var arc = 0f;
        var nextEnd = tile;
        for (var i = 1; i < curve.Count; i++)
        {
            var step = Vector3.Distance(curve[i - 1], curve[i]);
            var end = arc + step;
            while (nextEnd < end - tolerance)
            {
                if (nextEnd <= arc + tolerance)
                    points[^1] = points[^1] with { IsTileEnd = true };
                else
                    points.Add(new CurvePoint(Vector3.Lerp(curve[i - 1], curve[i], (nextEnd - arc) / step), nextEnd, true));

                nextEnd += tile;
            }

            points.Add(new CurvePoint(curve[i], end, false));
            arc = end;
        }

        return points;
    }

    /// <summary>
    /// The points that get a ring: both ends, each end of a repeat of the patch, and each point where the curve has turned
    /// enough since the last ring. A hose with more than <see cref="MaxRings"/> of them keeps every n-th one.
    /// </summary>
    private static List<int> PickRings(List<CurvePoint> points)
    {
        var rings = new List<int> { 0 };
        var heading = Vector3.Normalize(points[1].Position - points[0].Position);
        for (var i = 1; i < points.Count - 1; i++)
        {
            var direction = NormalizeOrZero(points[i + 1].Position - points[i].Position);
            if (direction != Vector3.Zero && (points[i].IsTileEnd || Vector3.Dot(direction, heading) < RingTurnCosine))
            {
                rings.Add(i);
                heading = direction;
            }
            else if (points[i].IsTileEnd)
            {
                rings.Add(i);
            }
        }

        rings.Add(points.Count - 1);
        if (rings.Count <= MaxRings)
            return rings;

        var stride = (rings.Count / MaxRings) + 1;
        return [.. rings.Where((point, position) => position % stride == 0 || points[point].IsTileEnd || point == points.Count - 1)];
    }

    private static Vector3 TangentAt(List<CurvePoint> points, int index, Vector3 previous)
    {
        var tangent = NormalizeOrZero(points[Math.Min(index + 1, points.Count - 1)].Position - points[Math.Max(index - 1, 0)].Position);
        return tangent == Vector3.Zero ? previous : tangent;
    }

    /// <summary>The previous side direction made square to the tangent, so the tube does not twist more than the curve asks.</summary>
    private static Vector3 SideOf(Vector3 tangent, Vector3 previous)
    {
        var side = NormalizeOrZero(previous - (tangent * Vector3.Dot(previous, tangent)));
        if (side != Vector3.Zero)
            return side;

        var reference = MathF.Abs(tangent.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        return Vector3.Normalize(Vector3.Cross(tangent, reference));
    }

    private static void WriteRow(int row, Vector3 center, Vector3 tangent, Vector3 side, float along, HoseUvPatch? patch, float[] positions, float[] normals, float[]? uvs)
    {
        var up = Vector3.Cross(tangent, side);
        for (var i = 0; i <= Sides; i++)
        {
            var around = (float)i / Sides;
            var angle = around * MathF.Tau;
            var normal = (side * MathF.Cos(angle)) + (up * MathF.Sin(angle));
            var position = center + (normal * Radius);
            var vertex = (row * (Sides + 1)) + i;
            positions[3 * vertex] = position.X;
            positions[(3 * vertex) + 1] = position.Y;
            positions[(3 * vertex) + 2] = position.Z;
            normals[3 * vertex] = normal.X;
            normals[(3 * vertex) + 1] = normal.Y;
            normals[(3 * vertex) + 2] = normal.Z;
            if (patch is { } uvPatch && uvs is not null)
            {
                var uv = uvPatch.Origin + (uvPatch.Around * around) + (uvPatch.Along * along);
                uvs[2 * vertex] = uv.X;
                uvs[(2 * vertex) + 1] = uv.Y;
            }
        }
    }

    /// <summary>Writes the quads from a row to the next one, counterclockwise as seen from outside the tube.</summary>
    private static int WriteQuads(int row, int nextIndex, uint[] indices)
    {
        for (var i = 0; i < Sides; i++)
        {
            var near = (uint)((row * (Sides + 1)) + i);
            var far = near + Sides + 1;
            indices[nextIndex++] = near;
            indices[nextIndex++] = near + 1;
            indices[nextIndex++] = far + 1;
            indices[nextIndex++] = near;
            indices[nextIndex++] = far + 1;
            indices[nextIndex++] = far;
        }

        return nextIndex;
    }

    private static Vector3 NormalizeOrZero(Vector3 vector)
    {
        var length = vector.Length();
        return length > 1e-6f && float.IsFinite(length) ? vector / length : Vector3.Zero;
    }

    private readonly record struct CurvePoint(Vector3 Position, float Arc, bool IsTileEnd);
}
