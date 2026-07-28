using Borea.Core.Game;
using Borea.Core.Mods;

namespace Borea.Core.Tests;

public sealed class ModMetadataTests
{
    private static ModMetadata Build(
        string modId = "test-mod",
        string name = "Test Mod",
        string author = "Author",
        string? description = "Description") =>
        new(
            modId,
            name,
            author,
            ModVersion.Parse("1.0.0"),
            GameVersion.Parse("2026.7.4.2131"),
            description!,
            DateTimeOffset.UtcNow,
            fileSizeBytes: 100);

    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var metadata = Build();

        Assert.Equal("test-mod", metadata.ModId);
        Assert.Equal(ModVersion.Parse("1.0.0"), metadata.Version);
        Assert.Equal(GameVersion.Parse("2026.7.4.2131"), metadata.BuiltForGameVersion);
        Assert.Empty(metadata.Dependencies);
        Assert.Empty(metadata.Tags);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        Assert.Throws<ArgumentException>(() => Build(modId: modId!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidName_ThrowsArgumentException(string? name)
    {
        Assert.Throws<ArgumentException>(() => Build(name: name!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidAuthor_ThrowsArgumentException(string? author)
    {
        Assert.Throws<ArgumentException>(() => Build(author: author!));
    }

    [Fact]
    public void Constructor_NullDescription_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Build(description: null));
    }

    [Fact]
    public void Constructor_NegativeFileSize_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModMetadata(
            "test-mod", "Name", "Author", ModVersion.Parse("1.0.0"),
            GameVersion.Parse("2026.7.4.2131"), "Description",
            DateTimeOffset.UtcNow, fileSizeBytes: -1));
    }

    [Fact]
    public void Constructor_NullDependenciesAndTags_DefaultToEmptyNotNull()
    {
        var metadata = Build();

        Assert.NotNull(metadata.Dependencies);
        Assert.NotNull(metadata.Tags);
    }
}