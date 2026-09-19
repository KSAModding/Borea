using Borea.App.Links;

namespace Borea.App.Tests.Links;

public sealed class LinuxLinkRegistrarTests : IDisposable
{
    private const string Executable = "/opt/Borea/borea";

    private readonly string _dataHome = Path.Combine(Path.GetTempPath(), "BoreaLinks_" + Guid.NewGuid());
    private readonly List<string> _xdgMimeCalls = [];
    private string? _defaultHandler;
    private bool _xdgMimeMissing;

    public void Dispose()
    {
        if (Directory.Exists(_dataHome))
            Directory.Delete(_dataHome, recursive: true);
    }

    private LinuxLinkRegistrar Registrar() => new(_dataHome, () => [1, 2, 3], arguments =>
    {
        _xdgMimeCalls.Add(string.Join(' ', arguments));
        if (_xdgMimeMissing)
            return null;
        if (arguments[0] == "default")
            _defaultHandler = arguments[1];
        return arguments[0] == "query" ? (_defaultHandler ?? string.Empty) + "\n" : string.Empty;
    });

    [Fact]
    public void Register_WritesTheEntryAndTheIcon_AndSetsTheDefault()
    {
        var registrar = Registrar();

        Assert.True(registrar.Register(Executable));

        var entry = File.ReadAllText(registrar.DesktopFilePath);
        Assert.Contains("Exec=\"/opt/Borea/borea\" %u\n", entry, StringComparison.Ordinal);
        Assert.Contains("MimeType=x-scheme-handler/borea;\n", entry, StringComparison.Ordinal);
        Assert.Contains($"Icon={registrar.IconPath.Replace(@"\", @"\\", StringComparison.Ordinal)}\n", entry, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(registrar.IconPath));
        Assert.Equal(Path.Combine(_dataHome, "applications", "borea.desktop"), registrar.DesktopFilePath);
        Assert.Equal(["query default x-scheme-handler/borea", "default borea.desktop x-scheme-handler/borea"], _xdgMimeCalls);
    }

    [Fact]
    public void Register_Again_WritesNothing()
    {
        var registrar = Registrar();
        registrar.Register(Executable);
        var written = File.GetLastWriteTimeUtc(registrar.DesktopFilePath);
        _xdgMimeCalls.Clear();

        Assert.False(registrar.Register(Executable));

        Assert.Equal(written, File.GetLastWriteTimeUtc(registrar.DesktopFilePath));
        Assert.Equal(["query default x-scheme-handler/borea"], _xdgMimeCalls);
    }

    [Fact]
    public void Register_OtherExecutable_RewritesTheEntry()
    {
        var registrar = Registrar();
        registrar.Register(Executable);

        Assert.True(registrar.Register("/home/me/Borea 2/borea"));

        Assert.Contains("Exec=\"/home/me/Borea 2/borea\" %u", File.ReadAllText(registrar.DesktopFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Register_PathWithReservedCharacters_QuotesAndEscapesThem()
    {
        var registrar = Registrar();

        registrar.Register("/home/me/100% \"odd\" $dir`/borea");

        Assert.Contains("Exec=\"/home/me/100%% \\\\\"odd\\\\\" \\\\$dir\\\\`/borea\" %u", File.ReadAllText(registrar.DesktopFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Register_PathWithANewline_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => Registrar().Register("/home/me/a\nb/borea"));
    }

    [Fact]
    public void Register_WithoutXdgMime_StillWritesTheEntry()
    {
        _xdgMimeMissing = true;
        var registrar = Registrar();

        Assert.True(registrar.Register(Executable));
        Assert.False(registrar.Register(Executable));

        Assert.True(File.Exists(registrar.DesktopFilePath));
        Assert.All(_xdgMimeCalls, call => Assert.StartsWith("query", call, StringComparison.Ordinal));
    }

    [Fact]
    public void Unregister_EntryOfThisExecutable_RemovesIt()
    {
        var registrar = Registrar();
        registrar.Register(Executable);

        Assert.True(registrar.Unregister(Executable));

        Assert.False(File.Exists(registrar.DesktopFilePath));
        Assert.False(File.Exists(registrar.IconPath));
    }

    [Fact]
    public void Unregister_EntryOfAnotherExecutable_KeepsIt()
    {
        var registrar = Registrar();
        registrar.Register("/opt/Other/borea");

        Assert.False(registrar.Unregister(Executable));
        Assert.False(Registrar().Unregister(Executable + "2"));

        Assert.True(File.Exists(registrar.DesktopFilePath));
    }

    [Fact]
    public void Unregister_NoEntry_DoesNothing()
    {
        Assert.False(Registrar().Unregister(Executable));
    }

    [Theory]
    [InlineData("/data", true)]
    [InlineData("relative/data", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DataHome_UsesAnAbsoluteXdgDataHome_ElseTheDefault(string? value, bool used)
    {
        var home = Path.Combine(Path.GetTempPath(), "home");

        var dataHome = LinuxLinkRegistrar.DataHome(name => name == "XDG_DATA_HOME" ? value : null, home);

        Assert.Equal(used ? value : Path.Combine(home, ".local", "share"), dataHome);
    }
}
