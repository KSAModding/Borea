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

    internal static MemoryStream Vehicle(string body) =>
        Text($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<VehicleSaveData Id=\"Test\">\n{body}\n</VehicleSaveData>\n");

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));
}
