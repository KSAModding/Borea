using Borea.Core.Links;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Links;

public sealed class BoreaLinkTests
{
    [Theory]
    [InlineData("borea://mod/MeasureTools", BoreaLinkKind.Mod, "MeasureTools")]
    [InlineData("borea://mod/MeasureTools/", BoreaLinkKind.Mod, "MeasureTools")]
    [InlineData("BOREA://MOD/measuretools", BoreaLinkKind.Mod, "measuretools")]
    [InlineData("borea://pack/starter-pack", BoreaLinkKind.Pack, "starter-pack")]
    [InlineData("borea://install/Some_Mod.Parts", BoreaLinkKind.Install, "Some_Mod.Parts")]
    [InlineData("borea://mod/Measure%54ools", BoreaLinkKind.Mod, "MeasureTools")]
    public void TryParse_ValidLink_ReadsKindAndId(string text, BoreaLinkKind kind, string id)
    {
        Assert.True(BoreaLink.TryParse(text, out var link, out var refusal));
        Assert.Null(refusal);
        Assert.Equal(kind, link.Kind);
        Assert.Equal(id, link.Id);
        Assert.Null(link.Version);
    }

    [Theory]
    [InlineData("borea://install/MeasureTools?version=1.1.10", "1.1.10")]
    [InlineData("borea://install/MeasureTools/?version=2.0.0-beta.1", "2.0.0-beta.1")]
    [InlineData("borea://install/MeasureTools?version=1.0.0%2Bbuild.5", "1.0.0")]
    [InlineData("borea://install/MeasureTools?version=1.0.0+build.5", "1.0.0")]
    public void TryParse_InstallWithVersion_ReadsTheVersion(string text, string version)
    {
        Assert.True(BoreaLink.TryParse(text, out var link, out _));
        Assert.Equal(BoreaLinkKind.Install, link.Kind);
        Assert.Equal(ModVersion.Parse(version), link.Version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("borea:mod/MeasureTools")]
    [InlineData("https://mod/MeasureTools")]
    [InlineData("borea://mods/MeasureTools")]
    [InlineData("borea://launch/MeasureTools")]
    [InlineData("borea://mod")]
    [InlineData("borea://mod/")]
    [InlineData("borea://mod//")]
    [InlineData("borea://mod/MeasureTools/extra")]
    [InlineData("borea://mod/MeasureTools//")]
    [InlineData("borea://mod/Measure Tools")]
    [InlineData("borea://mod/-bad")]
    [InlineData("borea://mod/CON")]
    [InlineData("borea://mod/..")]
    [InlineData("borea://mod/a%2Fb")]
    [InlineData("borea://mod/a%2")]
    [InlineData("borea://mod/a%zz")]
    [InlineData("borea://mod/%C3%A9")]
    [InlineData("borea://mod/%252E%252E")]
    [InlineData("borea://mod/MeasureTools#top")]
    [InlineData("borea://mod/MeasureTools?version=1.0.0")]
    [InlineData("borea://pack/starter-pack?x=1")]
    [InlineData("borea://user@mod/MeasureTools")]
    [InlineData("borea://mod:80/MeasureTools")]
    [InlineData("borea://install/MeasureTools?")]
    [InlineData("borea://install/MeasureTools?version")]
    [InlineData("borea://install/MeasureTools?version=")]
    [InlineData("borea://install/MeasureTools?version=1.0")]
    [InlineData("borea://install/MeasureTools?version=01.0.0")]
    [InlineData("borea://install/MeasureTools?Version=1.0.0")]
    [InlineData("borea://install/MeasureTools?version=1.0.0&version=1.0.1")]
    [InlineData("borea://install/MeasureTools?version=1.0.0&x=1")]
    [InlineData("borea://install/MeasureTools?channel=dev")]
    [InlineData("borea://mod/Measure\\u00e9Tools")]
    [InlineData("borea://mod/MeasureTools\n")]
    [InlineData("borea://mod/MeasureTools%0A")]
    [InlineData("borea://mod/Measure%20Tools")]
    [InlineData("borea://install/MeasureTools?version=1.0.0%0A")]
    public void TryParse_AnythingElse_IsRefusedWithAReason(string? text)
    {
        Assert.False(BoreaLink.TryParse(text, out var link, out var refusal));
        Assert.Null(link);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    [Fact]
    public void TryParse_LongerThanTheCap_IsRefused()
    {
        var tooLong = "borea://install/MeasureTools?version=1.0.0-" + new string('a', BoreaLink.MaxLength);

        Assert.False(BoreaLink.TryParse(tooLong, out _, out var refusal));
        Assert.Contains("longer", refusal);
    }

    [Theory]
    [InlineData("borea://mod/x", true)]
    [InlineData("BOREA:anything", true)]
    [InlineData("borea", false)]
    [InlineData("boreas://mod/x", false)]
    [InlineData("--help", false)]
    [InlineData(null, false)]
    public void HasScheme_ComparesTheSchemeCaseInsensitively(string? text, bool expected)
    {
        Assert.Equal(expected, BoreaLink.HasScheme(text));
    }

    [Fact]
    public void ToString_WritesTheCanonicalLink()
    {
        Assert.Equal("borea://mod/MeasureTools", new BoreaLink(BoreaLinkKind.Mod, "MeasureTools").ToString());
        Assert.Equal("borea://pack/starter-pack", new BoreaLink(BoreaLinkKind.Pack, "starter-pack").ToString());
        Assert.Equal("borea://install/MeasureTools?version=1.1.10", new BoreaLink(BoreaLinkKind.Install, "MeasureTools", ModVersion.Parse("1.1.10")).ToString());
    }

    [Fact]
    public void ForLog_ReplacesUnprintableCharactersAndCutsLongText()
    {
        Assert.Equal("borea://mod/a?b", BoreaLink.ForLog("borea://mod/a\nb"));
        Assert.Equal(203, BoreaLink.ForLog(new string('a', 500)).Length);
        Assert.Equal("(none)", BoreaLink.ForLog(null));
    }
}
