using System.Numerics;
using System.Text;
using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;

namespace Borea.Storage.Tests.Preview;

public sealed class VehicleXmlReaderTests
{
    [Fact]
    public void AChildPartRef_KeepsItsOwnGlobalTransform_AndPartRefTextUnderSymmetryIsNotAPart()
    {
        const string body = """
            <RootPartRef InstanceOf="Capsule" LocalInstanceId="1">
              <Transform><Position X="5" Y="1" /><Rotation Z="0.5" /></Transform>
              <PartConnectorRef Index="0" ConnectedLocalInstanceId="2" />
              <SymmetryRef><Layer BaseLocalInstanceId="1" Order="2"><PartRef>7</PartRef><PartRef>8</PartRef></Layer></SymmetryRef>
              <SubPartRef InstanceOf="Hatch" LocalInstanceId="3" />
              <PartRef InstanceOf="Tank" LocalInstanceId="2">
                <Transform><Position X="5" Y="1" /><Rotation Z="0.5" /></Transform>
              </PartRef>
            </RootPartRef>
            """;

        var vehicle = VehicleXmlReader.Read(Vehicle(body));

        Assert.Empty(vehicle.Problems);
        Assert.Equal([("Capsule", 1u), ("Tank", 2u)], vehicle.Parts.Select(part => (part.InstanceOf, part.LocalInstanceId)));
        Assert.Equal(vehicle.Parts[0].Transform, vehicle.Parts[1].Transform);
        Assert.Equal(new Vector3(5, 1, 0), vehicle.Parts[1].Transform.Translation);
    }

    [Theory]
    [InlineData("dtd")]
    [InlineData("oversized")]
    [InlineData("noRoot")]
    [InlineData("malformed")]
    public void ABrokenFile_GivesAProblem_AndNoParts(string kind)
    {
        const string part = "<RootPartRef InstanceOf=\"Capsule\" />";
        var stream = kind switch
        {
            "dtd" => Text($"<?xml version=\"1.0\"?><!DOCTYPE VehicleSaveData [<!ENTITY a \"b\">]><VehicleSaveData>{part}</VehicleSaveData>"),
            "oversized" => Text($"<VehicleSaveData Id=\"{new string('a', (int)VehicleXmlReader.MaxCharacters)}\">{part}</VehicleSaveData>"),
            "noRoot" => Vehicle("<PartRef InstanceOf=\"Capsule\" />"),
            _ => Vehicle("<RootPartRef InstanceOf=\"Capsule\">"),
        };

        var vehicle = VehicleXmlReader.Read(stream);

        Assert.Empty(vehicle.Parts);
        Assert.Contains(vehicle.Problems, problem => problem is { Kind: PreviewNoticeKind.AssetFailed, Subject: VehicleXmlReader.FileName });
    }

    [Fact]
    public void TheFuelLinks_AreReadWithTheirNodes_AndAPartKnowsItsTreeParent()
    {
        const string body = """
            <RootPartRef InstanceOf="Capsule" LocalInstanceId="4">
              <PartRef InstanceOf="Tank" LocalInstanceId="9">
                <FuelLink PartA="70" PartB="71" />
                <PartRef InstanceOf="Port" LocalInstanceId="11" />
              </PartRef>
            </RootPartRef>
            <FuelLink PartA="4" PartB="9" Flow="AToB"><Node X="1" Y="2" Z="3" /><Node Y="-1" /><Other X="9" /></FuelLink>
            <FuelLink PartA="9" PartB="11" />
            <LaunchGameTime />
            """;

        var vehicle = VehicleXmlReader.Read(Vehicle(body));

        Assert.Empty(vehicle.Problems);
        Assert.Equal([(uint?)null, 4u, 9u], vehicle.Parts.Select(part => part.ParentLocalId));
        Assert.Equal(2, vehicle.FuelLinks.Count);
        Assert.Equal((4u, 9u), (vehicle.FuelLinks[0].PartA, vehicle.FuelLinks[0].PartB));
        Assert.Equal([new Vector3(1, 2, 3), new Vector3(0, -1, 0)], vehicle.FuelLinks[0].Nodes);
        Assert.Equal((9u, 11u), (vehicle.FuelLinks[1].PartA, vehicle.FuelLinks[1].PartB));
        Assert.Empty(vehicle.FuelLinks[1].Nodes);
    }

    [Theory]
    [InlineData("PartA=\"x\" PartB=\"2\"", "")]
    [InlineData("PartA=\"-1\" PartB=\"2\"", "")]
    [InlineData("PartA=\"1\" PartB=\"99999999999\"", "")]
    [InlineData("PartA=\"1\" PartB=\"2\"", "<Node X=\"abc\" />")]
    [InlineData("PartA=\"1\" PartB=\"2\"", "<Node X=\"NaN\" />")]
    [InlineData("PartA=\"1\" PartB=\"2\"", "<Node Y=\"INF\" />")]
    [InlineData("PartA=\"1\" PartB=\"2\"", "<Node Z=\"1E+300\" />")]
    [InlineData("PartA=\"1\" PartB=\"2\"", "<Node X=\"1001\" />")]
    public void AFuelLinkWithANumberThatCannotBeUsed_IsLeftOutAlone(string attributes, string nodes)
    {
        var body = $"""
            <RootPartRef InstanceOf="Capsule" LocalInstanceId="1" />
            <FuelLink {attributes}>{nodes}</FuelLink>
            <FuelLink PartA="5" PartB="6" />
            """;

        var vehicle = VehicleXmlReader.Read(Vehicle(body));

        Assert.Single(vehicle.Parts);
        Assert.Equal(5u, Assert.Single(vehicle.FuelLinks).PartA);
        Assert.Single(vehicle.Problems, problem => problem is { Kind: PreviewNoticeKind.AssetFailed, Subject: VehicleXmlReader.FileName });
    }

    [Fact]
    public void TheFuelLinksAndTheirNodes_StopAtTheirCaps()
    {
        var nodes = string.Concat(Enumerable.Repeat("<Node X=\"1\" />", VehicleXmlReader.MaxFuelLinkNodes + 1));
        var links = string.Concat(Enumerable.Repeat("<FuelLink PartA=\"1\" PartB=\"2\" />", VehicleXmlReader.MaxFuelLinks));
        var body = $"<RootPartRef InstanceOf=\"Capsule\" LocalInstanceId=\"1\" /><FuelLink PartA=\"1\" PartB=\"2\">{nodes}</FuelLink>{links}";

        var vehicle = VehicleXmlReader.Read(Vehicle(body));

        Assert.Equal(VehicleXmlReader.MaxFuelLinks, vehicle.FuelLinks.Count);
        Assert.Equal(VehicleXmlReader.MaxFuelLinkNodes, vehicle.FuelLinks[0].Nodes.Count);
        Assert.Single(vehicle.Problems, problem => problem.Kind == PreviewNoticeKind.Budget);
    }

    internal static MemoryStream Vehicle(string body) =>
        Text($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<VehicleSaveData Id=\"Test\">\n{body}\n</VehicleSaveData>\n");

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));
}
