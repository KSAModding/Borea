using Borea.Core.Dependencies;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Dependencies;

public sealed class ModDependencyTests
{
    [Fact]
    public void Constructor_ValidInput_SetsAllProperties()
    {
        var dependency = new ModDependency("cool-lib", ModDependencyKind.Required, ModVersion.Parse("1.2.0"), ModVersion.Parse("2.0.0"), MetadataSource.Authored);

        Assert.Equal("cool-lib", dependency.ModId);
        Assert.Equal(ModDependencyKind.Required, dependency.Kind);
        Assert.Equal(ModVersion.Parse("1.2.0"), dependency.MinVersion);
        Assert.Equal(ModVersion.Parse("2.0.0"), dependency.MaxVersion);
        Assert.Equal(MetadataSource.Authored, dependency.Source);
        Assert.False(dependency.IsAnyOf);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CON")]
    [InlineData("bad id")]
    public void Constructor_InvalidModId_ThrowsArgumentException(string? modId)
    {
        Assert.Throws<ArgumentException>(() => new ModDependency(modId!, ModDependencyKind.Required));
    }

    [Fact]
    public void Constructor_MaxBelowMin_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ModDependency("cool-lib", ModDependencyKind.Required, ModVersion.Parse("2.0.0"), ModVersion.Parse("1.0.0")));
    }

    [Theory]
    [InlineData("1.2.0", true)]
    [InlineData("1.0.0", false)]
    [InlineData("2.0.0", true)]
    [InlineData("2.0.1", false)]
    public void BoundsContain_BothBounds_IsInclusive(string version, bool expected)
    {
        var dependency = new ModDependency("cool-lib", ModDependencyKind.Required, ModVersion.Parse("1.2.0"), ModVersion.Parse("2.0.0"));

        Assert.Equal(expected, dependency.BoundsContain(ModVersion.Parse(version)));
    }

    [Fact]
    public void BoundsContain_NoBounds_ContainsEverything()
    {
        var dependency = new ModDependency("cool-lib", ModDependencyKind.Required);

        Assert.True(dependency.BoundsContain(ModVersion.Parse("0.0.1")));
        Assert.True(dependency.BoundsContain(ModVersion.Parse("99.0.0")));
    }

    [Fact]
    public void OfAlternatives_ValidInput_CreatesAnyOfEntry()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0")),
            new ModDependencyAlternative("lib-b", ModVersion.Parse("1.1.0")),
        });

        Assert.True(dependency.IsAnyOf);
        Assert.Null(dependency.ModId);
        Assert.Equal(2, dependency.AnyOf!.Count);
    }

    [Theory]
    [InlineData(ModDependencyKind.Optional)]
    [InlineData(ModDependencyKind.Suggests)]
    [InlineData(ModDependencyKind.Conflict)]
    public void OfAlternatives_InvalidKind_ThrowsArgumentException(ModDependencyKind kind)
    {
        Assert.Throws<ArgumentException>(() =>
            ModDependency.OfAlternatives(kind, new[] { new ModDependencyAlternative("lib-a") }));
    }

    [Fact]
    public void OfAlternatives_EmptyList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ModDependency.OfAlternatives(ModDependencyKind.Required, Array.Empty<ModDependencyAlternative>()));
    }

    [Fact]
    public void BoundsContain_AnyOfEntry_Throws()
    {
        var dependency = ModDependency.OfAlternatives(ModDependencyKind.Required, new[] { new ModDependencyAlternative("lib-a") });

        Assert.Throws<InvalidOperationException>(() => dependency.BoundsContain(ModVersion.Parse("1.0.0")));
    }

    [Fact]
    public void AlternativeConstructor_MaxBelowMin_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0"), ModVersion.Parse("1.0.0")));
    }

    [Fact]
    public void ToString_NamesTheModAndKind()
    {
        var dependency = new ModDependency("cool-lib", ModDependencyKind.Conflict, ModVersion.Parse("1.0.0"));

        Assert.Contains("cool-lib", dependency.ToString());
        Assert.Contains("Conflict", dependency.ToString());
    }
}
