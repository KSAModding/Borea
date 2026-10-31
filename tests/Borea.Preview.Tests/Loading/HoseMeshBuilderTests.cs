using System.Numerics;
using Borea.Core.Preview;
using Borea.Preview.Loading;
using Borea.Preview.Scene;

namespace Borea.Preview.Tests.Loading;

public sealed class HoseMeshBuilderTests
{
    private const int RowSize = HoseMeshBuilder.Sides + 1;

    /// <summary>A patch whose along step runs against U and whose way around runs along V, as an atlas can have it.</summary>
    private static readonly HoseUvPatch Patch = new(new Vector2(0.3f, 0.1f), new Vector2(0f, 0.05f), new Vector2(-0.2f, 0f), 1f);

    [Fact]
    public void AStraightHose_HasTwoRings_AndEachFaceLooksOutOfTheTube()
    {
        var mesh = HoseMeshBuilder.Build(Line(0.4f), null);

        Assert.NotNull(mesh);
        Assert.Null(mesh.Uvs);
        Assert.Equal(2 * RowSize, mesh.VertexCount);
        Assert.Equal(HoseMeshBuilder.Sides * 2, mesh.TriangleCount);
        Assert.Equal(0f, mesh.LocalBounds.Min.X, 4);
        Assert.Equal(0.4f, mesh.LocalBounds.Max.X, 4);
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var position = Position(mesh, i);
            var fromAxis = position with { X = 0f };
            Assert.Equal(HoseMeshBuilder.Radius, fromAxis.Length(), 4);
            Assert.Equal(0f, Vector3.Distance(Vector3.Normalize(fromAxis), Normal(mesh, i)), 4);
        }

        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var (a, b, c) = (Position(mesh, (int)mesh.Indices[i]), Position(mesh, (int)mesh.Indices[i + 1]), Position(mesh, (int)mesh.Indices[i + 2]));
            var outward = ((a + b + c) / 3f) with { X = 0f };
            Assert.True(Vector3.Dot(Vector3.Cross(b - a, c - a), outward) > 0f, "A face looks into the tube.");
        }
    }

    [Fact]
    public void AHoseLongerThanATile_StartsThePatchAgainAtEachRepeat_WithoutACrossingStrip()
    {
        var mesh = HoseMeshBuilder.Build(Line(2.5f), Patch);

        Assert.NotNull(mesh);
        var uvs = Assert.IsType<float[]>(mesh.Uvs);

        // Rings at 0, 1, 2 and 2.5 meters, and the rings at 1 and 2 meters have a second row of vertices.
        Assert.Equal(6 * RowSize, mesh.VertexCount);
        Assert.Equal(3 * HoseMeshBuilder.Sides * 2, mesh.TriangleCount);
        var along = Enumerable.Range(0, 6).Select(row => MathF.Round((0.3f - uvs[2 * row * RowSize]) / 0.2f, 3)).ToArray();
        Assert.Equal([0f, 1f, 0f, 1f, 0f, 0.5f], along);
        Assert.Equal(new Vector2(0.3f, 0.1f), new Vector2(uvs[0], uvs[1]));
        Assert.Equal(0.15f, uvs[(2 * HoseMeshBuilder.Sides) + 1], 5);
        Assert.Equal(1f, Position(mesh, RowSize).X, 4);
        Assert.Equal(1f, Position(mesh, 2 * RowSize).X, 4);
    }

    [Fact]
    public void AVeryLongHose_RepeatsThePatchAtMostMaxTilesTimes()
    {
        var mesh = HoseMeshBuilder.Build(Line(1000f), Patch);

        Assert.NotNull(mesh);
        Assert.Equal(HoseMeshBuilder.MaxTiles * HoseMeshBuilder.Sides * 2, mesh.TriangleCount);
        Assert.InRange(mesh.TriangleCount, 1, HoseMeshBuilder.MaxTriangles);
    }

    [Fact]
    public void ACurveWithManyTurns_StaysInsideTheTriangleLimit_AndKeepsItsEnds()
    {
        var route = Enumerable.Range(0, 60).Select(i => new HoseWaypoint(new Vector3(i, i % 2, 0f), Vector3.Zero)).ToArray();

        var mesh = HoseMeshBuilder.Build(route, Patch);

        Assert.NotNull(mesh);
        Assert.InRange(mesh.TriangleCount, 200, HoseMeshBuilder.MaxTriangles);
        Assert.Equal(new Vector3(0f, 0f, 0f), Round(RingCenter(mesh, 0)));
        Assert.Equal(new Vector3(59f, 1f, 0f), Round(RingCenter(mesh, (mesh.VertexCount / RowSize) - 1)));
    }

    [Fact]
    public void APinnedDirection_SetsWhereTheHoseGoes_AndAFreeCurvePassesThroughItsWaypoints()
    {
        var pinned = HoseMeshBuilder.Build(
            [new HoseWaypoint(Vector3.Zero, Vector3.UnitY), new HoseWaypoint(Vector3.UnitX, Vector3.Zero)], null);
        var free = HoseMeshBuilder.Build(
            [new HoseWaypoint(Vector3.Zero, Vector3.Zero), new HoseWaypoint(Vector3.UnitX, Vector3.Zero), new HoseWaypoint(new Vector3(1f, 1f, 0f), Vector3.Zero)], null);

        Assert.NotNull(pinned);
        Assert.True(pinned.LocalBounds.Max.Y > 0.1f, "The hose does not leave along its pinned direction.");
        Assert.NotNull(free);
        var rows = free.VertexCount / RowSize;
        var nearest = Enumerable.Range(0, rows).Min(row => Vector3.Distance(RingCenter(free, row), Vector3.UnitX));
        Assert.InRange(nearest, 0f, 0.1f);
        Assert.InRange(rows, 3, HoseMeshBuilder.MaxRings);
    }

    [Theory]
    [InlineData("one waypoint")]
    [InlineData("no length")]
    [InlineData("shorter than the game draws")]
    [InlineData("not a number")]
    public void ARouteThatCannotMakeATube_GivesNoMesh(string kind)
    {
        HoseWaypoint[] route = kind switch
        {
            "one waypoint" => [new HoseWaypoint(Vector3.Zero, Vector3.Zero)],
            "no length" => [new HoseWaypoint(Vector3.One, Vector3.Zero), new HoseWaypoint(Vector3.One, Vector3.Zero)],
            "shorter than the game draws" => [new HoseWaypoint(Vector3.Zero, Vector3.Zero), new HoseWaypoint(new Vector3(5e-5f, 0f, 0f), Vector3.Zero)],
            _ => [new HoseWaypoint(Vector3.Zero, Vector3.Zero), new HoseWaypoint(new Vector3(float.NaN, 0f, 0f), Vector3.Zero)],
        };

        Assert.Null(HoseMeshBuilder.Build(route, null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePatchOfAHoseMesh_ComesFromItsTwoEnds_AlsoWithASeam(bool alongU)
    {
        // Two strips around a hose of 2 m with a seam at the first vertices, so that one place has two UVs on each end.
        var around = new[] { 0.1f, 0.2f, 0.4f };
        var positions = new List<float>();
        var uvs = new List<float>();
        void Add(float x, float y, float z, float along, float side)
        {
            positions.AddRange([x, y, z]);
            uvs.AddRange(alongU ? [along, side] : [side, along]);
        }

        Add(0.05f, 0f, -1f, 0.7f, around[0]);
        Add(0.05f, 0f, 1f, 0.5f, around[0]);
        Add(0f, 0.05f, -1f, 0.7f, around[1]);
        Add(0f, 0.05f, 1f, 0.5f, around[1]);
        Add(0.05f, 0f, -1f, 0.7f, around[2]);
        Add(0.05f, 0f, 1f, 0.5f, around[2]);
        var mesh = new PreviewMesh([.. positions], [], [.. uvs], [0, 1, 2]);

        var patch = HoseUvPatch.Find(mesh);

        Assert.NotNull(patch);
        Assert.Equal(2f, patch.Value.TileLength, 5);
        Assert.Equal(alongU ? new Vector2(-0.2f, 0f) : new Vector2(0f, -0.2f), Round(patch.Value.Along));
        Assert.Equal(alongU ? new Vector2(0f, 0.3f) : new Vector2(0.3f, 0f), Round(patch.Value.Around));
        Assert.Equal(alongU ? new Vector2(0.7f, 0.1f) : new Vector2(0.1f, 0.7f), Round(patch.Value.Origin));
    }

    [Theory]
    [InlineData("no UVs")]
    [InlineData("flat")]
    [InlineData("no vertex across")]
    [InlineData("no UV step")]
    [InlineData("a UV range around the hose that overflows")]
    [InlineData("a UV step along the hose that overflows")]
    public void AMeshThatIsNotSuchAHose_GivesNoPatch(string kind)
    {
        var mesh = kind switch
        {
            "no UVs" => new PreviewMesh([0, 0, -1, 0, 0, 1, 1, 0, 0], [], null, [0, 1, 2]),
            "flat" => new PreviewMesh([0, 0, 0, 1, 0, 0, 1, 1, 0], [], [0, 0, 1, 0, 1, 1], [0, 1, 2]),
            "no vertex across" => new PreviewMesh([0, 0, -1, 1, 0, 1, 2, 0, 0], [], [0, 0, 1, 0, 1, 1], [0, 1, 2]),
            "a UV range around the hose that overflows" => new PreviewMesh([0, 0, -1, 0, 0, 1, 1, 0, 0], [], [0, 2e38f, 1, 2e38f, 0, -2e38f], [0, 1, 2]),
            "a UV step along the hose that overflows" => new PreviewMesh([0, 0, -1, 0, 0, 1, 1, 0, 0], [], [2e38f, 0, -2e38f, 0, 0, 1], [0, 1, 2]),
            _ => new PreviewMesh([0, 0, -1, 0, 0, 1, 1, 0, 0], [], [0, 0, 0, 0, 1, 1], [0, 1, 2]),
        };

        Assert.Null(HoseUvPatch.Find(mesh));
    }

    private static HoseWaypoint[] Line(float length) =>
        [new HoseWaypoint(Vector3.Zero, Vector3.Zero), new HoseWaypoint(new Vector3(length, 0f, 0f), Vector3.Zero)];

    private static Vector3 Position(PreviewMesh mesh, int index) =>
        new(mesh.Positions[3 * index], mesh.Positions[(3 * index) + 1], mesh.Positions[(3 * index) + 2]);

    private static Vector3 Normal(PreviewMesh mesh, int index) =>
        new(mesh.Normals[3 * index], mesh.Normals[(3 * index) + 1], mesh.Normals[(3 * index) + 2]);

    private static Vector3 RingCenter(PreviewMesh mesh, int row)
    {
        var sum = Vector3.Zero;
        for (var i = 0; i < HoseMeshBuilder.Sides; i++)
            sum += Position(mesh, (row * RowSize) + i);

        return sum / HoseMeshBuilder.Sides;
    }

    private static Vector3 Round(Vector3 value) =>
        new(MathF.Round(value.X, 3) + 0f, MathF.Round(value.Y, 3) + 0f, MathF.Round(value.Z, 3) + 0f);

    private static Vector2 Round(Vector2 value) => new(MathF.Round(value.X, 3) + 0f, MathF.Round(value.Y, 3) + 0f);
}
