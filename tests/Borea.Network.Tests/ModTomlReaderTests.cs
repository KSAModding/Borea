using System;
using System.IO;
using Borea.Network;
using Xunit;

namespace Borea.Network.Tests;

public sealed class ModTomlReaderTests : IDisposable
{
    private readonly string _tempRoot;

    public ModTomlReaderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void ReadModId_ValidModToml_ReturnsName()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "mod.toml"), """
            name="ModMenu"
            version="0.2.1"
            author="MrJeranimo"
            """);

        var modId = ModTomlReader.ReadModId(_tempRoot);

        Assert.Equal("ModMenu", modId);
    }

    [Fact]
    public void ReadModId_RealWorldSample_ParsesCorrectly()
    {
        // Directly from the actual mod.toml sample provided, including the
        // StarMap table — confirms extra sections don't break parsing.
        File.WriteAllText(Path.Combine(_tempRoot, "mod.toml"), """
            name="ModMenu"
            version="0.2.1"
            author="MrJeranimo"
            description="This is a mod that adds a \"Mods\" tab to the main KSA game next to the \"View\" tab. It also provides functionality for mods to add their own sub menus to the new \"Mods\" tab."

            [StarMap]
            EntryAssembly="ModMenu"
            """);

        var modId = ModTomlReader.ReadModId(_tempRoot);

        Assert.Equal("ModMenu", modId);
    }

    [Fact]
    public void ReadModId_WithStarMapModDependencies_StillParsesName()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "mod.toml"), """
            name = "MyAmazingMod"

            [StarMap]
            EntryAssembly = "MyAmazingMod"

            [[StarMap.ModDependencies]]
            ModId = "MyOtherAmazingMod"
            Optional = false
            ImportedAssemblies = ["MyDependency"]
            """);

        var modId = ModTomlReader.ReadModId(_tempRoot);

        Assert.Equal("MyAmazingMod", modId);
    }

    [Fact]
    public void ReadModId_MissingFile_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => ModTomlReader.ReadModId(_tempRoot));
    }

    [Fact]
    public void ReadModId_MissingNameField_ThrowsInvalidOperationException()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "mod.toml"), """
            version="1.0.0"
            author="Someone"
            """);

        Assert.Throws<InvalidOperationException>(() => ModTomlReader.ReadModId(_tempRoot));
    }

    [Theory]
    [InlineData("name=\"\"")]
    [InlineData("name=\"   \"")]
    public void ReadModId_BlankName_ThrowsInvalidOperationException(string tomlContent)
    {
        File.WriteAllText(Path.Combine(_tempRoot, "mod.toml"), tomlContent);

        Assert.Throws<InvalidOperationException>(() => ModTomlReader.ReadModId(_tempRoot));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}