using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Instances;

namespace Borea.Storage.Tests.Instances;

public sealed class TomlModListFormatTests
{
    private readonly TomlModListFormat _format = new();

    [Fact]
    public void Write_FormatNameAndOneTablePerMod()
    {
        var text = _format.Write(new ModList("Main", [new ModListEntry("AdvancedFlightComputer", ModVersion.Parse("0.7.5"), enabled: true)]));

        Assert.Equal(
            """
            format = 1
            name = "Main"
            [[mods]]
            id = "AdvancedFlightComputer"
            version = "0.7.5"
            enabled = true
            """.ReplaceLineEndings("\n"),
            text.Trim().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void WriteThenRead_RoundTripsTheNameVersionsAndEnabledFlags()
    {
        var original = new ModList(
            "Career",
            [
                new ModListEntry("flight-tools", ModVersion.Parse("2.1.0-beta.1"), enabled: true),
                new ModListEntry("MeasureTools", ModVersion.Parse("1.1.10"), enabled: false),
            ]);

        var read = _format.Read(_format.Write(original));

        Assert.Equal("Career", read.Name);
        Assert.Equal(original.Mods, read.Mods);
    }

    [Fact]
    public void Read_NewerFormat_ThrowsNamingTheFormat()
    {
        var exception = Assert.Throws<UnsupportedModListFormatException>(() => _format.Read("""
            format = 2
            name = "Main"

            [[mods]]
            id = "AdvancedFlightComputer"
            version = "0.7.5"
            """));

        Assert.Equal(2, exception.Format);
    }

    [Fact]
    public void Read_KeysItDoesNotKnow_AreIgnored()
    {
        var modList = _format.Read("""
            format = 1
            created_by = "Borea 9.0"

            [[mods]]
            id = "AdvancedFlightComputer"
            version = "0.7.5"
            source = "index"
            """);

        var entry = Assert.Single(modList.Mods);
        Assert.Equal("AdvancedFlightComputer", entry.ModId);
        Assert.Equal(ModVersion.Parse("0.7.5"), entry.Version);
        Assert.True(entry.Enabled);
        Assert.Null(modList.Name);
    }

    [Theory]
    [InlineData("name = \"Main\"")]
    [InlineData("format = 0")]
    [InlineData("format = \"one\"")]
    [InlineData("format = ")]
    [InlineData("format = 1\n[[mods]]\nid = \"flight tools\"\nversion = \"1.0.0\"")]
    [InlineData("format = 1\n[[mods]]\nid = \"flight-tools\"\nversion = \"one\"")]
    [InlineData("format = 1\n[[mods]]\nid = \"flight-tools\"")]
    [InlineData("format = 1\n[[mods]]\nid = \"flight-tools\"\nversion = \"1.0.0\"\n[[mods]]\nid = \"Flight-Tools\"\nversion = \"2.0.0\"")]
    public void Read_NotAModlist_ThrowsFormatException(string text)
    {
        var exception = Assert.ThrowsAny<FormatException>(() => _format.Read(text));

        Assert.IsNotType<UnsupportedModListFormatException>(exception);
    }
}
