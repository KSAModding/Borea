using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Tests.Mods;

public sealed class ModDependencyMapperTests
{
    [Fact]
    public void RoundTrip_SingleIdEntry_PreservesEveryField()
    {
        var original = new ModDependency("cool-lib", ModDependencyKind.Conflict, ModVersion.Parse("1.0.0"), ModVersion.Parse("2.0.0"), MetadataSource.Derived);

        var reloaded = ModDependencyMapper.FromDto(ModDependencyMapper.ToDto(original));

        Assert.Equal(original.ModId, reloaded.ModId);
        Assert.Equal(ModDependencyKind.Conflict, reloaded.Kind);
        Assert.Equal(original.MinVersion, reloaded.MinVersion);
        Assert.Equal(original.MaxVersion, reloaded.MaxVersion);
        Assert.Equal(MetadataSource.Derived, reloaded.Source);
        Assert.False(reloaded.IsAnyOf);
    }

    [Fact]
    public void RoundTrip_AnyOfEntry_PreservesAlternatives()
    {
        var original = ModDependency.OfAlternatives(ModDependencyKind.Required, new[]
        {
            new ModDependencyAlternative("lib-a", ModVersion.Parse("2.0.0"), ModVersion.Parse("3.0.0")),
            new ModDependencyAlternative("lib-b"),
        });

        var reloaded = ModDependencyMapper.FromDto(ModDependencyMapper.ToDto(original));

        Assert.True(reloaded.IsAnyOf);
        Assert.Null(reloaded.ModId);
        Assert.Equal(2, reloaded.AnyOf!.Count);
        Assert.Equal("lib-a", reloaded.AnyOf[0].ModId);
        Assert.Equal(ModVersion.Parse("3.0.0"), reloaded.AnyOf[0].MaxVersion);
        Assert.Null(reloaded.AnyOf[1].MinVersion);
    }

    [Fact]
    public void FromDto_UnknownKind_ParsesToUnknown()
    {
        var dto = new ModDependencyDto { ModId = "cool-lib", Kind = "breaks" };

        Assert.Equal(ModDependencyKind.Unknown, ModDependencyMapper.FromDto(dto).Kind);
    }

    [Fact]
    public void FromDto_UnknownSource_ParsesToUnknown()
    {
        var dto = new ModDependencyDto { ModId = "cool-lib", Kind = "required", Source = "handwritten" };

        Assert.Equal(MetadataSource.Unknown, ModDependencyMapper.FromDto(dto).Source);
    }

    [Fact]
    public void FromDto_NoModIdAndNoAnyOf_ThrowsFormatException()
    {
        var dto = new ModDependencyDto { Kind = "required" };

        Assert.Throws<FormatException>(() => ModDependencyMapper.FromDto(dto));
    }

    [Fact]
    public void FromDto_BothModIdAndAnyOf_ThrowsFormatException()
    {
        var dto = new ModDependencyDto
        {
            ModId = "cool-lib",
            Kind = "required",
            AnyOf = new List<ModDependencyAlternativeDto> { new() { ModId = "lib-a" } },
        };

        Assert.Throws<FormatException>(() => ModDependencyMapper.FromDto(dto));
    }

    [Fact]
    public void FromDto_EmptyAnyOfList_ThrowsFormatException()
    {
        var dto = new ModDependencyDto { Kind = "required", AnyOf = new List<ModDependencyAlternativeDto>() };

        Assert.Throws<FormatException>(() => ModDependencyMapper.FromDto(dto));
    }

    [Fact]
    public void FromDto_AnyOfWithUnknownKind_ThrowsFormatExceptionInsteadOfArgumentException()
    {
        var dto = new ModDependencyDto
        {
            Kind = "breaks",
            AnyOf = new List<ModDependencyAlternativeDto> { new() { ModId = "lib-a" } },
        };

        var exception = Assert.Throws<FormatException>(() => ModDependencyMapper.FromDto(dto));
        Assert.Contains("breaks", exception.Message);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("4")]
    [InlineData("required,optional")]
    public void FromDto_NumericOrCompositeKindTokens_ParseToUnknown(string kind)
    {
        var dto = new ModDependencyDto { ModId = "cool-lib", Kind = kind };

        Assert.Equal(ModDependencyKind.Unknown, ModDependencyMapper.FromDto(dto).Kind);
    }
}
