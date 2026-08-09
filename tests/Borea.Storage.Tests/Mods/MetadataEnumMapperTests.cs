using Borea.Core.Dependencies;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Tests.Mods;

public sealed class MetadataEnumMapperTests
{
    [Theory]
    [InlineData(ContentType.Mod, "mod")]
    [InlineData(ContentType.ModPack, "modpack")]
    [InlineData(ContentType.ModLoader, "mod-loader")]
    [InlineData(ContentType.Unknown, "unknown")]
    public void ContentType_WritesTheFormatSpelling(ContentType type, string expected)
    {
        Assert.Equal(expected, MetadataEnumMapper.ToDto(type));
        Assert.Equal(type, MetadataEnumMapper.ParseContentType(expected));
    }

    [Theory]
    [InlineData(ModStatus.Active, "active")]
    [InlineData(ModStatus.Deprecated, "deprecated")]
    [InlineData(ModStatus.Unknown, "unknown")]
    public void ModStatus_WritesTheFormatSpelling(ModStatus status, string expected)
    {
        Assert.Equal(expected, MetadataEnumMapper.ToDto(status));
        Assert.Equal(status, MetadataEnumMapper.ParseModStatus(expected));
    }

    [Theory]
    [InlineData(ReleaseStatus.Stable, "stable")]
    [InlineData(ReleaseStatus.Testing, "testing")]
    [InlineData(ReleaseStatus.Dev, "dev")]
    [InlineData(ReleaseStatus.Unknown, "unknown")]
    public void ReleaseStatus_WritesTheFormatSpelling(ReleaseStatus status, string expected)
    {
        Assert.Equal(expected, MetadataEnumMapper.ToDto(status));
        Assert.Equal(status, MetadataEnumMapper.ParseReleaseStatus(expected));
    }

    [Theory]
    [InlineData(ModDependencyKind.Required, "required")]
    [InlineData(ModDependencyKind.Optional, "optional")]
    [InlineData(ModDependencyKind.Recommends, "recommends")]
    [InlineData(ModDependencyKind.Suggests, "suggests")]
    [InlineData(ModDependencyKind.Conflict, "conflict")]
    [InlineData(ModDependencyKind.Unknown, "unknown")]
    public void DependencyKind_WritesTheFormatSpelling(ModDependencyKind kind, string expected)
    {
        Assert.Equal(expected, MetadataEnumMapper.ToDto(kind));
        Assert.Equal(kind, MetadataEnumMapper.ParseKind(expected));
    }

    [Theory]
    [InlineData(MetadataSource.Authored, "authored")]
    [InlineData(MetadataSource.Derived, "derived")]
    [InlineData(MetadataSource.Unknown, "unknown")]
    public void MetadataSource_WritesTheFormatSpelling(MetadataSource source, string expected)
    {
        Assert.Equal(expected, MetadataEnumMapper.ToDto(source));
        Assert.Equal(source, MetadataEnumMapper.ParseSource(expected));
    }

    [Fact]
    public void MetadataSource_NullStaysNull()
    {
        Assert.Null(MetadataEnumMapper.ToDto(null));
        Assert.Null(MetadataEnumMapper.ParseSource(null));
    }
}
