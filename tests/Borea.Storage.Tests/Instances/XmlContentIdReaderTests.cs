using Borea.Core.Instances;
using Borea.Core.Sharing;
using Borea.Storage.Instances;

namespace Borea.Storage.Tests.Instances;

/// <summary>The files are written by hand in the shape of research/vehicles-and-saves.md, "The content a file names".</summary>
public sealed class XmlContentIdReaderTests : IDisposable
{
    internal const string Vehicle = """
        <?xml version="1.0" encoding="utf-8"?>
        <VehicleSaveData Id="Example Rover" Character="EvaKitten">
          <RootPartRef InstanceOf="CoreCommandPod" LocalInstanceId="1">
            <PartRef InstanceOf="RoverWheel" LocalInstanceId="2" />
            <PartRef InstanceOf="RoverWheel" LocalInstanceId="3">
              <SymmetryRef>
                <Layer BaseLocalInstanceId="2"><PartRef>3</PartRef></Layer>
              </SymmetryRef>
            </PartRef>
            <SubPartRef InstanceOf="ExamplePartsMod_Antenna" LocalInstanceId="4" />
            <TankData InstanceOf="ModuleTemplateNotAPart">
              <Mole SubstancePhaseId="LOX(l)"><Mass>12</Mass></Mole>
              <Mole SubstancePhaseId="LOX(l)"><Mass>3</Mass></Mole>
            </TankData>
          </RootPartRef>
        </VehicleSaveData>
        """;

    internal const string Universe = """
        <?xml version="1.0" encoding="utf-8"?>
        <UniverseData>
          <GameTime>12</GameTime>
          <System>
            <Id Id="Sol" />
            <Vehicle Id="Rover 1" Character="EvaKitten">
              <ParentBody Id="Moon" />
              <RootPartRef InstanceOf="CoreCommandPod" />
            </Vehicle>
            <Vehicle Id="Probe">
              <ParentBody Id="Earth" />
              <RootPartRef InstanceOf="ProbeCore" />
            </Vehicle>
            <GroundClutter>
              <Celestial>Mars</Celestial>
              <Ecotype>RedDunes</Ecotype>
            </GroundClutter>
          </System>
          <KittenRoster>
            <Kitten Name="Val" Character="ScientistKitten" />
            <Kitten Name="Bob" Character="EvaKitten" />
          </KittenRoster>
        </UniverseData>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly XmlContentIdReader _reader = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_Vehicle_GivesEachPartOnceAndNoModuleTemplate()
    {
        var folder = Write("Example Rover", "vehicle.xml", Vehicle);

        var content = await _reader.ReadAsync(folder, GameSaveKind.Vehicle);

        Assert.Equal("Example Rover", content.Name);
        Assert.Equal("v2026.10.19.5638", content.GameBuild);
        Assert.Equal(["CoreCommandPod", "RoverWheel", "ExamplePartsMod_Antenna"], content.Ids.Of(ContentIdKind.Part));
        Assert.Equal(["LOX(l)"], content.Ids.Of(ContentIdKind.SubstancePhase));
        Assert.Equal(["EvaKitten"], content.Ids.Of(ContentIdKind.Character));
        Assert.Empty(content.Ids.Of(ContentIdKind.Body));
        Assert.Empty(content.ExtraFiles);
    }

    [Fact]
    public async Task ReadAsync_Save_GivesBodiesClutterCharactersAndTheSystem()
    {
        var folder = Write("Moon Base", "universe.xml", Universe);
        File.WriteAllText(Path.Combine(folder, "unscience.json"), "{}");
        Directory.CreateDirectory(Path.Combine(folder, "mod data"));
        File.WriteAllText(Path.Combine(folder, "mod data", "state.txt"), "x");

        var content = await _reader.ReadAsync(folder, GameSaveKind.Save);

        Assert.Equal(["Moon", "Earth", "Mars"], content.Ids.Of(ContentIdKind.Body));
        Assert.Equal([new GroundClutterRef("Mars", "RedDunes")], content.Clutter);
        Assert.Equal(["EvaKitten", "ScientistKitten"], content.Ids.Of(ContentIdKind.Character));
        Assert.Equal(["Sol"], content.Ids.Of(ContentIdKind.System));
        Assert.Equal(["CoreCommandPod", "ProbeCore"], content.Ids.Of(ContentIdKind.Part));
        Assert.Equal(["mod data/state.txt", "unscience.json"], content.ExtraFiles);
    }

    [Fact]
    public async Task ReadAsync_CutOffFile_NamesTheFile()
    {
        var folder = Write("Example Rover", "vehicle.xml", Vehicle[..(Vehicle.IndexOf("<SubPartRef", StringComparison.Ordinal) + 20)]);

        var exception = await Assert.ThrowsAsync<GameFolderException>(() => _reader.ReadAsync(folder, GameSaveKind.Vehicle));

        Assert.Equal(GameFolderProblem.GameFileNotWellFormed, exception.Problem);
        Assert.Contains(Path.Combine(folder, "vehicle.xml"), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name = [", GameFolderProblem.MetadataUnreadable)]
    [InlineData("version = \"v2026.10.19.5638\"", GameFolderProblem.NameMissing)]
    [InlineData("name = 3", GameFolderProblem.NameMissing)]
    [InlineData(null, GameFolderProblem.MetadataMissing)]
    public async Task ReadAsync_BadMetadata_NamesTheFile(string? metadata, GameFolderProblem problem)
    {
        var folder = Write("Example Rover", "vehicle.xml", Vehicle);
        var path = Path.Combine(folder, "meta.toml");
        if (metadata is null)
            File.Delete(path);
        else
            File.WriteAllText(path, metadata);

        var exception = await Assert.ThrowsAsync<GameFolderException>(() => _reader.ReadAsync(folder, GameSaveKind.Vehicle));

        Assert.Equal(problem, exception.Problem);
        Assert.StartsWith(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_NoGameFile_NamesTheFile()
    {
        var folder = Write("Moon Base", "vehicle.xml", Vehicle);

        var exception = await Assert.ThrowsAsync<GameFolderException>(() => _reader.ReadAsync(folder, GameSaveKind.Save));

        Assert.Equal(GameFolderProblem.GameFileMissing, exception.Problem);
        Assert.Equal(Path.Combine(folder, "universe.xml"), exception.Path);
    }

    private string Write(string name, string gameFile, string xml)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\nversion = \"v2026.10.19.5638\"\n");
        File.WriteAllText(Path.Combine(folder, gameFile), xml);
        return folder;
    }
}
