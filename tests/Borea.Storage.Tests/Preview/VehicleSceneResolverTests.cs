using System.Numerics;
using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;

namespace Borea.Storage.Tests.Preview;

public sealed class VehicleSceneResolverTests : IDisposable
{
    private const string MeshAtlas = "<MeshAtlas Path=\"Meshes/All.glb\" />";

    private readonly CatalogTestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void SubParts_ComposeUnderTheRotatedPart_GameDataAddsDrawnModels_AndChildPartsAreGlobal()
    {
        const string assets = """
            <PbrMaterial Id="Hull"><Diffuse Path="hull.png" /></PbrMaterial>
            <SubPart Id="Bolt"><PartModel Id="m"><Mesh Id="BoltMesh" /><Material Id="hull" /></PartModel></SubPart>
            <SubPart Id="Skin">
              <PartModel Id="m"><Mesh Id="SkinMesh" /></PartModel>
              <SubPart Id="Inner" InstanceOf="Bolt"><Transform><Position Y="1" /></Transform></SubPart>
            </SubPart>
            <Part Id="Body">
              <PartModel Id="body"><Mesh Id="BodyMesh" /><Material Id="Hull" /></PartModel>
              <PartModel Id="inside"><Mesh Id="BodyMesh" /><Internal>true</Internal></PartModel>
              <PartModel Id="shadow"><Mesh Id="BodyMesh" /><RayTracing>ShadowProxy</RayTracing></PartModel>
              <SubPart Id="Skin" InstanceOf="Skin"><Transform><Position X="1" /></Transform></SubPart>
            </Part>
            <PartGameData Id="Body">
              <PartModelGlass Id="window"><Mesh Id="ExtraMesh" /><Material Id="Hull" /></PartModelGlass>
              <SubPart Id="Added" InstanceOf="Bolt"><Transform><Position Z="2" /></Transform></SubPart>
            </PartGameData>
            """;
        const string transform = """<Transform><Position X="10" /><Rotation Z="1.5707963267948966" /></Transform>""";
        var (catalog, meshes) = Build(assets, "BodyMesh", "SkinMesh", "BoltMesh", "ExtraMesh");
        var vehicle = VehicleXmlReader.Read(VehicleXmlReaderTests.Vehicle(
            $"<RootPartRef InstanceOf=\"body\" LocalInstanceId=\"1\">{transform}<PartRef InstanceOf=\"BODY\" LocalInstanceId=\"2\">{transform}</PartRef></RootPartRef>"));

        var scene = VehicleSceneResolver.Resolve(vehicle.Parts, catalog, meshes, null);

        Assert.Empty(scene.Placeholders);
        Assert.Equal((2, 4), (scene.PartCount, scene.SkippedInternalCount));
        var first = scene.Draws.Take(5).ToList();
        Assert.Equal(["BodyMesh", "ExtraMesh", "SkinMesh", "BoltMesh", "BoltMesh"], first.Select(draw => scene.Meshes[draw.Mesh].Name));
        Assert.Equal([PreviewModelKind.Model, PreviewModelKind.Glass, PreviewModelKind.Model, PreviewModelKind.Model, PreviewModelKind.Model], first.Select(draw => draw.Kind));
        AssertNear(new Vector3(10, 0, 0), Origin(first[0]));
        AssertNear(new Vector3(10, 1, 0), Origin(first[2]));
        AssertNear(new Vector3(9, 1, 0), Origin(first[3]));
        AssertNear(new Vector3(10, 0, 2), Origin(first[4]));
        AssertNear(new Vector3(10, 2, 0), Vector3.Transform(Vector3.UnitX, first[2].Transform));

        // The child part has the Transform of its parent, so it lands at the same place and reuses the same refs.
        Assert.Equal(first.Select(draw => (draw.Mesh, draw.Material, draw.Transform)), scene.Draws.Skip(5).Select(draw => (draw.Mesh, draw.Material, draw.Transform)));
        Assert.Equal(4, scene.Meshes.Count);
        Assert.Equal([("Hull", false), ("Hull", true)], scene.Materials.Select(material => (material.Key, material.Glass)));
        Assert.Equal([0, 1, -1, 0, 0], first.Select(draw => draw.Material));
    }

    [Fact]
    public void WhatDoesNotResolve_GivesSizedPlaceholders_AndASelfReferenceStopsWithACycle()
    {
        const string assets = """
            <SubPart Id="Holder">
              <Connector Id="a"><Transform><Position X="-1" /></Transform></Connector>
              <Connector Id="b"><Transform><Position X="3" /></Transform></Connector>
              <PartModel Id="m"><Mesh Id="NoSuchMesh" /></PartModel>
            </SubPart>
            <Part Id="Frame">
              <SubPart Id="h" InstanceOf="Holder"><Transform><Position Y="2" /></Transform></SubPart>
              <SubPart Id="gone" InstanceOf="ModSubPart" />
            </Part>
            <Part Id="KittenBackPackPart" />
            <Part Id="Loop"><PartModel Id="m"><Mesh Id="BodyMesh" /></PartModel><SubPart Id="self" InstanceOf="loop" /></Part>
            """;
        const string body = """
            <RootPartRef InstanceOf="Frame" LocalInstanceId="1">
              <Transform><Position Z="5" /></Transform>
              <PartRef InstanceOf="Unknown_Part" LocalInstanceId="2"><Transform><Position X="7" /></Transform></PartRef>
              <PartRef InstanceOf="KittenBackPackPart" LocalInstanceId="3" />
              <PartRef InstanceOf="Loop" LocalInstanceId="4" />
            </RootPartRef>
            """;
        var (catalog, meshes) = Build(assets, "BodyMesh");
        var hint = new Dictionary<string, string>(KsaIdComparer.Registry) { ["modsubpart"] = "SomeMod" };

        var scene = VehicleSceneResolver.Resolve(VehicleXmlReader.Read(VehicleXmlReaderTests.Vehicle(body)).Parts, catalog, meshes, hint);

        Assert.Collection(scene.Placeholders,
            mesh =>
            {
                Assert.Equal(("NoSuchMesh", PlaceholderReason.UnknownMesh, (string?)null), (mesh.MissingId, mesh.Reason, mesh.ModId));
                AssertNear(new Vector3(1, 2, 5), mesh.Transform.Translation);
                Assert.Equal(new Vector3(2, 1, 1), mesh.HalfSize);
            },
            notLoaded =>
            {
                Assert.Equal(("ModSubPart", PlaceholderReason.ModNotLoaded, "SomeMod"), (notLoaded.MissingId, notLoaded.Reason, notLoaded.ModId));
                AssertNear(new Vector3(0, 0, 5), notLoaded.Transform.Translation);
            },
            part =>
            {
                Assert.Equal(("Unknown_Part", PlaceholderReason.UnknownPart), (part.MissingId, part.Reason));
                AssertNear(new Vector3(7, 0, 0), part.Transform.Translation);
                Assert.Equal(new Vector3(VehicleSceneResolver.DefaultHalfSize), part.HalfSize);
            });
        var loop = Assert.Single(scene.Draws);
        Assert.Equal("Loop", loop.PartId);
        Assert.Contains(scene.Notices, notice => notice is { Kind: PreviewNoticeKind.Cycle, Subject: "Loop" });
    }

    private (PartCatalog Catalog, MeshIndex Meshes) Build(string assets, params string[] meshNames)
    {
        var core = _mods.Mod("Core", ["Parts.xml"], ("Parts.xml", CatalogTestMods.Assets(MeshAtlas + assets)));
        var json = $"{{\"asset\":{{\"version\":\"2.0\"}},\"meshes\":[{string.Join(",", meshNames.Select(name => $"{{\"name\":\"{name}\"}}"))}]}}";
        _mods.Write("Core", "Meshes/All.glb", PreviewFilesTests.Glb(json, []));

        var catalog = PartCatalog.Build([core], CancellationToken.None);
        Assert.Empty(catalog.Notices);
        return (catalog, MeshIndex.Build([core], catalog.MeshFiles, CancellationToken.None));
    }

    private static Vector3 Origin(ResolvedDraw draw) => Vector3.Transform(Vector3.Zero, draw.Transform);

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");
}
