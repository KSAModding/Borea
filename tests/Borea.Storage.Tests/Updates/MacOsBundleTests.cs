using Borea.Storage.Updates;

namespace Borea.Storage.Tests.Updates;

public sealed class MacOsBundleTests
{
    private static readonly string Applications = Path.Combine(Path.GetTempPath(), "Applications");

    private static readonly string Bundle = Path.Combine(Applications, "Borea.app");

    private static readonly string Program = Path.Combine(Bundle, "Contents", "MacOS", "borea");

    [Fact]
    public void ForProgram_AProgramUnderContentsMacOS_NamesItsBundle()
    {
        var bundle = MacOsBundle.ForProgram(Program);

        Assert.NotNull(bundle);
        Assert.Equal(Program, bundle.ProgramPath);
        Assert.Equal(Bundle, bundle.BundlePath);
        Assert.Equal(Applications, bundle.Folder);
        Assert.Equal(Bundle + ".old", bundle.ReplacedPath);
        Assert.Equal(Path.Combine(Bundle, "Contents", "Resources"), bundle.ResourcesPath);
    }

    [Fact]
    public void ForProgram_TheBundleEnding_IsReadInAnyCase()
    {
        var bundle = MacOsBundle.ForProgram(Path.Combine(Applications, "Borea.APP", "Contents", "MacOS", "borea"));

        Assert.NotNull(bundle);
        Assert.Equal(Path.Combine(Applications, "Borea.APP"), bundle.BundlePath);
    }

    [Theory]
    [InlineData("Borea", "borea")]
    [InlineData("Borea.app", "borea")]
    [InlineData("Borea.app", "Contents", "borea")]
    [InlineData("Borea", "Contents", "MacOS", "borea")]
    [InlineData("Borea.app", "contents", "macos", "borea")]
    [InlineData(".app", "Contents", "MacOS", "borea")]
    public void ForProgram_AnotherLayout_IsNoBundle(params string[] path)
    {
        Assert.Null(MacOsBundle.ForProgram(Path.Combine([Applications, .. path])));
    }

    [Fact]
    public void ForProgram_NoPath_IsNoBundle()
    {
        Assert.Null(MacOsBundle.ForProgram(null));
        Assert.Null(MacOsBundle.ForProgram(string.Empty));
    }

    [Fact]
    public void ProgramIn_NamesTheSameProgramInsideAnotherCopyOfTheBundle()
    {
        var bundle = MacOsBundle.ForProgram(Program)!;
        var copy = Path.Combine(Applications, ".borea-update-1", "Borea.app");

        Assert.Equal(Path.Combine(copy, "Contents", "MacOS", "borea"), bundle.ProgramIn(copy));
    }

    [Fact]
    public void IsBundle_NeedsANameBeforeTheEnding()
    {
        Assert.True(MacOsBundle.IsBundle(Bundle));
        Assert.False(MacOsBundle.IsBundle(Path.Combine(Applications, ".app")));
        Assert.False(MacOsBundle.IsBundle(Path.Combine(Applications, "Borea")));
    }
}
