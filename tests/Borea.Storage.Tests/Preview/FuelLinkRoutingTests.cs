using System.Numerics;
using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;

namespace Borea.Storage.Tests.Preview;

public sealed class FuelLinkRoutingTests : IDisposable
{
    private const string MeshAtlas = "<MeshAtlas Path=\"Meshes/All.glb\" />";

    private const string HoseMaterial = """<PbrMaterial Id="CoreFuelTankB_Material"><Diffuse Path="hose.png" /></PbrMaterial>""";

    /// <summary>
    /// A port part whose connector sits at 0.1 m along Z. In the frame of the connector its pivot is 0.1 m back, so it
    /// is at the origin of the part, and the tip is 0.1 m forward, so it is at 0.2 m along Z in the frame of the part.
    /// </summary>
    private const string Port = """
        <SubPart Id="Socket"><PartModel Id="m"><Mesh Id="SocketMesh" /></PartModel></SubPart>
        <SubPart Id="Plug"><PartModel Id="m"><Mesh Id="PlugMesh" /></PartModel></SubPart>
        <Part Id="Port">
          <SubPart Id="Socket" InstanceOf="Socket" />
          <SubPart Id="Plug" InstanceOf="Plug"><Transform><Position Z="0.1" /></Transform></SubPart>
          <FuelPort Connector="Plug" ConnectorMaxAngle="30" ConnectorLeadOut="0.5">
            <ConnectorPivot Z="-0.1" />
            <ConnectorTip Z="0.1" />
          </FuelPort>
        </Part>
        <Part Id="Tank"><Tank Id="t" /></Part>
        """;

    private readonly CatalogTestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void ALinkBetweenPartsWithoutPorts_RunsBetweenTheirOrigins_AndItsNodesFollowPartA()
    {
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <Transform><Position X="1" /><Rotation Z="1.5707963267948966" /></Transform>
              <PartRef InstanceOf="Tank" LocalInstanceId="2"><Transform><Position X="5" /></Transform></PartRef>
              <PartRef InstanceOf="Tank" LocalInstanceId="3"><Transform><Position X="9" /></Transform></PartRef>
            </RootPartRef>
            <FuelLink PartA="1" PartB="2" Flow="AToB"><Node X="2" /><Node Y="1" /></FuelLink>
            <FuelLink PartA="2" PartB="3" />
            """;

        var scene = Resolve(Port, body);

        Assert.Equal(2, scene.HoseRoutes.Count);
        AssertRoute(scene.HoseRoutes[0], (new Vector3(1, 0, 0), Vector3.Zero), (new Vector3(1, 2, 0), Vector3.Zero), (new Vector3(0, 0, 0), Vector3.Zero), (new Vector3(5, 0, 0), Vector3.Zero));

        // A link without a Node element is the normal saved form and has a hose too.
        AssertRoute(scene.HoseRoutes[1], (new Vector3(5, 0, 0), Vector3.Zero), (new Vector3(9, 0, 0), Vector3.Zero));
        Assert.Empty(scene.Notices);
    }

    [Fact]
    public void APortWithoutAConnector_GivesItsAnchorInTheFrameOfThePart()
    {
        const string assets = """
            <Part Id="Bare"><FuelPort><AnchorAsmb Y="2" /></FuelPort></Part>
            <Part Id="Wrong"><FuelPort Connector="NotASubPart"><AnchorAsmb Y="3" /></FuelPort></Part>
            """;
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Bare" LocalInstanceId="2"><Transform><Position X="1" /><Rotation Z="1.5707963267948966" /></Transform></PartRef>
              <PartRef InstanceOf="Tank" LocalInstanceId="9">
                <PartRef InstanceOf="Wrong" LocalInstanceId="3"><Transform><Position X="-1" /></Transform></PartRef>
              </PartRef>
            </RootPartRef>
            <FuelLink PartA="2" PartB="3" />
            """;

        var route = Assert.Single(Resolve(Port + assets, body).HoseRoutes);

        AssertRoute(route, (new Vector3(-1, 0, 0), Vector3.Zero), (new Vector3(-1, 3, 0), Vector3.Zero));
    }

    [Fact]
    public void TwoPortsThatFaceEachOther_GiveTipAndLeadOut_AndTheirConnectorsStayAtRest()
    {
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Port" LocalInstanceId="2" />
              <PartRef InstanceOf="Tank" LocalInstanceId="9">
                <PartRef InstanceOf="Port" LocalInstanceId="3"><Transform><Position Z="5" /><Rotation X="3.141592653589793" /></Transform></PartRef>
              </PartRef>
            </RootPartRef>
            <FuelLink PartA="2" PartB="3" />
            """;

        var scene = Resolve(Port, body);

        var route = Assert.Single(scene.HoseRoutes);
        AssertRoute(
            route,
            (new Vector3(0, 0, 0.2f), Vector3.UnitZ),
            (new Vector3(0, 0, 0.7f), Vector3.UnitZ),
            (new Vector3(0, 0, 4.3f), Vector3.UnitZ),
            (new Vector3(0, 0, 4.8f), Vector3.UnitZ));
        AssertNear(new Vector3(0, 0, 0.1f), PlugOrigin(scene, 0));
        AssertNear(new Vector3(0, 0, 4.9f), PlugOrigin(scene, 1));
    }

    [Fact]
    public void AConnector_TurnsAboutItsPivotUpToItsMaxAngle_AndItsDrawAndRouteFollow()
    {
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Port" LocalInstanceId="2"><Transform><Position Y="3" /></Transform></PartRef>
              <PartRef InstanceOf="Tank" LocalInstanceId="3" />
            </RootPartRef>
            <FuelLink PartA="2" PartB="3"><Node X="4" /></FuelLink>
            """;

        var scene = Resolve(Port, body);

        // The node lies 90 degrees from the rest axis about +Y, and the limit of the port is 30 degrees.
        var axis = new Vector3(0.5f, 0f, MathF.Sqrt(3f) / 2f);
        var part = new Vector3(0f, 3f, 0f);
        var route = Assert.Single(scene.HoseRoutes);
        AssertRoute(
            route,
            (part + (axis * 0.2f), axis),
            (part + (axis * 0.7f), axis),
            (part + new Vector3(4, 0, 0), Vector3.Zero),
            (Vector3.Zero, Vector3.Zero));
        AssertNear(part + (axis * 0.1f), PlugOrigin(scene, 0));

        // The socket is not the connector, so it stays where the template puts it.
        AssertNear(part, Vector3.Transform(Vector3.Zero, scene.Draws.First(draw => scene.Meshes[draw.Mesh].Name == "SocketMesh").Transform));
    }

    [Fact]
    public void TwoPortsWithoutNodes_AimAtEachOthersLeadOut_UntilThePoseSettles()
    {
        const string assets = """
            <SubPart Id="WidePlug"><PartModel Id="m"><Mesh Id="PlugMesh" /></PartModel></SubPart>
            <Part Id="Wide">
              <SubPart Id="WidePlug" InstanceOf="WidePlug" />
              <FuelPort Connector="WidePlug" ConnectorMaxAngle="80" ConnectorLeadOut="0.3"><ConnectorTip Z="0.4" /></FuelPort>
            </Part>
            """;
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Wide" LocalInstanceId="2" />
              <PartRef InstanceOf="Tank" LocalInstanceId="9">
                <PartRef InstanceOf="Wide" LocalInstanceId="3"><Transform><Position X="1" Z="2" /><Rotation X="3.141592653589793" /></Transform></PartRef>
              </PartRef>
            </RootPartRef>
            <FuelLink PartA="2" PartB="3" />
            """;

        var route = Assert.Single(Resolve(Port + assets, body).HoseRoutes);

        // At rest the point is reached when each connector, seen from its pivot, looks at the lead-out of the other.
        var (pivotA, pivotB) = (Vector3.Zero, new Vector3(1, 0, 2));
        AssertNear(Vector3.Normalize(route[2].Position - pivotA), route[0].PinnedDirection);
        AssertNear(Vector3.Normalize(pivotB - route[1].Position), route[3].PinnedDirection);
        AssertNear(pivotA + (route[0].PinnedDirection * 0.4f), route[0].Position);
    }

    [Fact]
    public void APortWithSeveralLinks_KeepsThePoseOfItsLastLink_ForEachOfItsHoses()
    {
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <Transform><Position X="9" /></Transform>
              <PartRef InstanceOf="Port" LocalInstanceId="2" />
              <PartRef InstanceOf="Tank" LocalInstanceId="3"><Transform><Position X="4" Z="0.2" /></Transform></PartRef>
              <PartRef InstanceOf="Tank" LocalInstanceId="4"><Transform><Position X="-4" Z="0.2" /></Transform></PartRef>
            </RootPartRef>
            <FuelLink PartA="2" PartB="3" />
            <FuelLink PartA="2" PartB="4" />
            """;

        var scene = Resolve(Port, body);

        Assert.Equal(2, scene.HoseRoutes.Count);
        Assert.Equal(scene.HoseRoutes[0][0], scene.HoseRoutes[1][0]);
        Assert.True(scene.HoseRoutes[1][0].Position.X < -0.05f, "The connector does not look at the last link.");
    }

    [Fact]
    public void TheFirstPortOfASubPart_ComesBeforeThePortOfThePartItself()
    {
        const string assets = """
            <SubPart Id="PortHolder"><FuelPort><AnchorAsmb X="7" /></FuelPort></SubPart>
            <Part Id="Holder">
              <SubPart Id="h" InstanceOf="PortHolder" />
              <FuelPort><AnchorAsmb X="9" /></FuelPort>
            </Part>
            """;
        const string body = """
            <RootPartRef InstanceOf="Holder" LocalInstanceId="1">
              <PartRef InstanceOf="Tank" LocalInstanceId="2" />
            </RootPartRef>
            <FuelLink PartA="1" PartB="2" />
            """;

        var route = Assert.Single(Resolve(Port + assets, body).HoseRoutes);

        AssertRoute(route, (new Vector3(7, 0, 0), Vector3.Zero), (Vector3.Zero, Vector3.Zero));
    }

    [Theory]
    [InlineData(99, 2, true)]
    [InlineData(2, 99, true)]
    [InlineData(6, 2, true)]
    [InlineData(5, 2, false)]
    [InlineData(2, 5, false)]
    public void ALinkThatCannotRun_IsLeftOut_TheOtherLinkStays_AndOnlyAPartWithoutATemplateGivesNoNotice(uint partA, uint partB, bool hasNotice)
    {
        const string farPort = """<Part Id="FarPort"><FuelPort><AnchorAsmb X="INF" /></FuelPort></Part>""";
        var body = $"""
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Tank" LocalInstanceId="2" />
              <PartRef InstanceOf="Tank" LocalInstanceId="3" />
              <PartRef InstanceOf="NoSuchPart" LocalInstanceId="5" />
              <PartRef InstanceOf="FarPort" LocalInstanceId="6" />
            </RootPartRef>
            <FuelLink PartA="{partA}" PartB="{partB}" />
            <FuelLink PartA="2" PartB="3" />
            """;

        var scene = Resolve(Port + farPort, body);

        Assert.Single(scene.HoseRoutes);
        Assert.Equal(hasNotice ? [VehicleXmlReader.FileName] : [], scene.Notices.Where(notice => notice.Kind == PreviewNoticeKind.AssetFailed).Select(notice => notice.Subject));
    }

    [Fact]
    public void ALinkThatTheGameDrops_GivesNoHoseAndNoNotice()
    {
        const string assets = """
            <Part Id="TankPort"><Tank Id="t" /><FuelPort /></Part>
            <SubPart Id="TankSub"><Tank Id="t" /></SubPart>
            <Part Id="SubTankPort"><SubPart Id="s" InstanceOf="TankSub" /><FuelPort /></Part>
            """;
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Port" LocalInstanceId="2" />
              <PartRef InstanceOf="Tank" LocalInstanceId="3">
                <PartRef InstanceOf="Port" LocalInstanceId="4" />
                <PartRef InstanceOf="Port" LocalInstanceId="5" />
                <PartRef InstanceOf="TankPort" LocalInstanceId="6" />
                <PartRef InstanceOf="SubTankPort" LocalInstanceId="7" />
              </PartRef>
            </RootPartRef>
            <FuelLink PartA="2" PartB="2" />
            <FuelLink PartA="2" PartB="1" />
            <FuelLink PartA="4" PartB="5" />
            <FuelLink PartA="2" PartB="3" />
            <FuelLink PartA="3" PartB="2" />
            <FuelLink PartA="1" PartB="3" />
            <FuelLink PartA="4" PartB="1" />
            <FuelLink PartA="6" PartB="3" />
            <FuelLink PartA="7" PartB="3" />
            """;

        var scene = Resolve(Port + assets, body);

        // Part 2 counts as its parent 1, and 4 and 5 count as their parent 3, but a port with its own tank counts as itself.
        // So 2 to 3, 4 to 1, 6 to 3 and 7 to 3 stay. The end at part 1 already has a link that touches part 3 when 1 to 3 comes.
        Assert.Equal(4, scene.HoseRoutes.Count);
        Assert.Empty(scene.Notices);
    }

    [Fact]
    public void TheHoseNamesItsMeshAndMaterial_WhenTheFilesHaveThem_AndAVehicleWithoutLinksNamesNeither()
    {
        const string withLink = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1"><PartRef InstanceOf="Tank" LocalInstanceId="2" /></RootPartRef>
            <FuelLink PartA="1" PartB="2" />
            """;
        const string withoutLink = """<RootPartRef InstanceOf="Tank" LocalInstanceId="1" />""";

        var hose = Resolve(HoseMaterial + Port, withLink, KsaPartSchema.HoseMesh);
        var none = Resolve(HoseMaterial + Port, withoutLink, KsaPartSchema.HoseMesh);
        var plain = Resolve(Port, withLink);

        Assert.Equal(KsaPartSchema.HoseMesh, hose.Meshes[hose.HoseMesh].Name);
        Assert.Equal((KsaPartSchema.HoseMaterial, false), (hose.Materials[hose.HoseMaterial].Key, hose.Materials[hose.HoseMaterial].Glass));
        Assert.Equal((-1, -1, 0), (none.HoseMesh, none.HoseMaterial, none.HoseRoutes.Count));
        Assert.Equal((-1, -1), (plain.HoseMesh, plain.HoseMaterial));
        Assert.Empty(plain.Placeholders);
        Assert.Empty(plain.Notices);
    }

    [Fact]
    public void AConnectorWithAnIdFromItsTemplate_IsFound_AndAPartScaleMovesTheTip()
    {
        const string assets = """
            <SubPart Id="NamedPlug"><PartModel Id="m"><Mesh Id="PlugMesh" /></PartModel></SubPart>
            <Part Id="Named">
              <SubPart InstanceOf="NamedPlug" />
              <FuelPort Connector="NamedPlug" ConnectorLeadOut="0"><ConnectorPivot Z="-1" /><ConnectorTip Z="1" /></FuelPort>
            </Part>
            """;
        const string body = """
            <RootPartRef InstanceOf="Tank" LocalInstanceId="1">
              <PartRef InstanceOf="Named" LocalInstanceId="2"><Transform><Scale X="2" Y="2" Z="2" /></Transform></PartRef>
              <PartRef InstanceOf="Tank" LocalInstanceId="3"><Transform><Position Z="9" /></Transform></PartRef>
            </RootPartRef>
            <FuelLink PartA="2" PartB="3" />
            """;

        var route = Assert.Single(Resolve(Port + assets, body).HoseRoutes);

        AssertRoute(route, (new Vector3(0, 0, 2), Vector3.UnitZ), (new Vector3(0, 0, 9), Vector3.Zero));
    }

    private ResolvedVehicle Resolve(string assets, string body, params string[] extraMeshes)
    {
        string[] names = ["SocketMesh", "PlugMesh", .. extraMeshes];
        var core = _mods.Mod("Core", ["Parts.xml"], ("Parts.xml", CatalogTestMods.Assets(MeshAtlas + assets)));
        var json = $"{{\"asset\":{{\"version\":\"2.0\"}},\"meshes\":[{string.Join(",", names.Select(name => $"{{\"name\":\"{name}\"}}"))}]}}";
        _mods.Write("Core", "Meshes/All.glb", PreviewFilesTests.Glb(json, []));
        var catalog = PartCatalog.Build([core], CancellationToken.None);
        Assert.Empty(catalog.Notices);
        var meshes = MeshIndex.Build([core], catalog.MeshFiles, CancellationToken.None);
        var vehicle = VehicleXmlReader.Read(VehicleXmlReaderTests.Vehicle(body));
        Assert.Empty(vehicle.Problems);
        return VehicleSceneResolver.Resolve(vehicle.Parts, catalog, meshes, null, vehicle.FuelLinks);
    }

    private static Vector3 PlugOrigin(ResolvedVehicle scene, int index) =>
        Vector3.Transform(Vector3.Zero, scene.Draws.Where(draw => scene.Meshes[draw.Mesh].Name == "PlugMesh").ElementAt(index).Transform);

    private static void AssertRoute(IReadOnlyList<HoseWaypoint> route, params (Vector3 Position, Vector3 Pinned)[] expected)
    {
        Assert.Equal(expected.Length, route.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            AssertNear(expected[i].Position, route[i].Position);
            AssertNear(expected[i].Pinned, route[i].PinnedDirection);
        }
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}.");
}
