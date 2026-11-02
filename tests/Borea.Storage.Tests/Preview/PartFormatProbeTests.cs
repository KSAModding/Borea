using System.Text;
using Borea.Storage.Preview.Ksa;
using Borea.Storage.Preview.Sources;

namespace Borea.Storage.Tests.Preview;

/// <summary>The asset files are small and written by hand, in the layout of today and in the newer layout with Modify, Ref and the wrapped collections.</summary>
public sealed class PartFormatProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("""<Part Id="CoreFuelTankA" Modify="Patch"><Mass>100</Mass></Part>""")]
    [InlineData("""<PartGameData Id="CoreFuelTankA" Modify="PatchIfExists" />""")]
    [InlineData("""<Texture Id="X_Diffuse" Path="Textures/X.ktx2" /><PbrMaterial Id="X"><Diffuse Ref="X_Diffuse" /></PbrMaterial>""")]
    [InlineData("""<Part Id="X"><SubParts><SubPart Key="Body" Ref="X_Body" /></SubParts></Part>""")]
    [InlineData("""<SubPart Id="X_Body"><Connectors><Connector Key="Top" /></Connectors></SubPart>""")]
    [InlineData("""<SubPart Id="X_Body"><Components><PartModel Key="Model" /></Components><EditorTags /></SubPart>""")]
    public void AFileInTheNewerLayout_HasAMark(string assets) =>
        Assert.True(PartFormatProbe.HasNewerLayoutMark(Stream(assets), CancellationToken.None));

    [Theory]
    [InlineData("""<Part Id="X"><SubPart Id="Body" InstanceOf="X_Body"><Transform /></SubPart><Connector Id="Top" /></Part>""")]
    [InlineData("""<SubPart Id="X_Body"><PartModel Id="Model"><Mesh Id="XMesh" /><Material Id="XMat" /></PartModel></SubPart>""")]
    [InlineData("""<PbrMaterial Id="X"><Diffuse Path="Textures/X_Diffuse.png" /><Normal Path="Textures/X_Normal.png" /></PbrMaterial>""")]
    [InlineData("""<PartGameData Id="X"><Tank /></PartGameData>""")]
    [InlineData("""<Texture Id="GaugeFontAtlas" Path="Textures/GaugeFontAtlas.png" />""")]
    [InlineData("""<Gauge Id="G"><Components /><Connectors /></Gauge>""")]
    public void AFileInTheLayoutOfToday_HasNoMark(string assets) =>
        Assert.False(PartFormatProbe.HasNewerLayoutMark(Stream(assets), CancellationToken.None));

    [Fact]
    public void AnotherRoot_HasNoMark() =>
        Assert.False(PartFormatProbe.HasNewerLayoutMark(new MemoryStream(Encoding.UTF8.GetBytes("""<Systems><Part Id="X" Modify="Patch" /></Systems>""")), CancellationToken.None));

    [Fact]
    public void TheGame_UsesTheNewerLayout_WhenAnyListedFileOfAContentModHasAMark_AndNotForAFileThatNoModLists()
    {
        var content = Path.Combine(_root, "Content");
        Write(Path.Combine(content, "manifest.toml"), "[[mods]]\nid = \"Core\"\nenabled = true\n[[mods]]\nid = \"Extra\"\nenabled = true\n");
        Write(Path.Combine(content, "Core", "mod.toml"), "name = \"Core\"\nassets = [\"Parts.xml\", \"Missing.xml\", \"Broken.xml\"]\n");
        Write(Path.Combine(content, "Core", "Parts.xml"), Assets("""<Part Id="X"><PartModel Id="m"><Mesh Id="M" /></PartModel></Part>"""));
        Write(Path.Combine(content, "Core", "Broken.xml"), "<Assets><Part");
        Write(Path.Combine(content, "Core", "NotListed.xml"), Assets("""<Part Id="X" Modify="Patch" />"""));
        Write(Path.Combine(content, "Extra", "mod.toml"), "name = \"Extra\"\nassets = [\"Extra.xml\"]\n");
        Write(Path.Combine(content, "Extra", "Extra.xml"), Assets("""<PbrMaterial Id="X"><Diffuse Path="Textures/X.png" /></PbrMaterial>"""));

        Assert.Equal(PartLayout.Today, PartFormatProbe.Read(GameCoreFiles.Open(_root), CancellationToken.None));

        Write(Path.Combine(content, "Extra", "Extra.xml"), Assets("""<PbrMaterial Id="X"><Diffuse Ref="X_Diffuse" /></PbrMaterial>"""));

        Assert.Equal(PartLayout.Newer, PartFormatProbe.Read(GameCoreFiles.Open(_root), CancellationToken.None));
    }

    [Theory]
    [InlineData("mod.toml")]
    [InlineData("Parts.xml")]
    public void AListedFileThatCannotBeOpened_MakesTheAnswerUncertain_UnlessAnotherFileHasAMark(string locked)
    {
        var content = Path.Combine(_root, "Content");
        Write(Path.Combine(content, "manifest.toml"), "[[mods]]\nid = \"Core\"\nenabled = true\n[[mods]]\nid = \"Extra\"\nenabled = true\n");
        Write(Path.Combine(content, "Core", "mod.toml"), "name = \"Core\"\nassets = [\"Parts.xml\"]\n");
        Write(Path.Combine(content, "Core", "Parts.xml"), Assets("""<Part Id="X"><PartModel Id="m"><Mesh Id="M" /></PartModel></Part>"""));
        var extra = Path.Combine(content, "Extra", "Extra.xml");
        Write(Path.Combine(content, "Extra", "mod.toml"), "name = \"Extra\"\nassets = [\"Extra.xml\"]\n");
        Write(extra, Assets("""<PbrMaterial Id="X"><Diffuse Path="Textures/X.png" /></PbrMaterial>"""));

        using var stream = new FileStream(Path.Combine(content, "Core", locked), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var uncertain = PartFormatProbe.Read(GameCoreFiles.Open(_root), CancellationToken.None);
        Write(extra, Assets("""<PbrMaterial Id="X"><Diffuse Ref="X_Diffuse" /></PbrMaterial>"""));
        var newer = PartFormatProbe.Read(GameCoreFiles.Open(_root), CancellationToken.None);

        Assert.Equal((PartLayout.Uncertain, PartLayout.Newer), (uncertain, newer));
    }

    private static MemoryStream Stream(string assets) => new(Encoding.UTF8.GetBytes(Assets(assets)));

    private static string Assets(string body) => $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Assets>\n{body}\n</Assets>\n";

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
