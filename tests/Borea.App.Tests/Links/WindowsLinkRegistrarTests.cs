using System.Runtime.Versioning;
using Borea.App.Links;
using Microsoft.Win32;

namespace Borea.App.Tests.Links;

/// <summary>Runs against a throwaway key under HKEY_CURRENT_USER\Software, never against the real Software\Classes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsLinkRegistrarTests : IDisposable
{
    private const string Executable = @"C:\Games\Borea\borea.exe";

    private readonly string _rootPath = @"Software\Borea.Tests\" + Guid.NewGuid();
    private readonly RegistryKey? _root;

    public WindowsLinkRegistrarTests()
    {
        if (OperatingSystem.IsWindows())
            _root = Registry.CurrentUser.CreateSubKey(_rootPath);
    }

    public void Dispose()
    {
        _root?.Dispose();
        if (OperatingSystem.IsWindows())
            Registry.CurrentUser.DeleteSubKeyTree(_rootPath, throwOnMissingSubKey: false);
    }

    [WindowsFact("The registry exists only on Windows.")]
    public void Register_WritesTheUrlProtocol()
    {
        Assert.True(new WindowsLinkRegistrar(_root!).Register(Executable));

        using var protocol = _root!.OpenSubKey(@"Software\Classes\borea")!;
        Assert.Equal("URL:Borea link", protocol.GetValue(null));
        Assert.Equal(string.Empty, protocol.GetValue("URL Protocol"));
        Assert.Equal("\"C:\\Games\\Borea\\borea.exe\",0", protocol.OpenSubKey("DefaultIcon")!.GetValue(null));
        Assert.Equal("\"C:\\Games\\Borea\\borea.exe\" \"%1\"", protocol.OpenSubKey(@"shell\open\command")!.GetValue(null));
    }

    [WindowsFact("The registry exists only on Windows.")]
    public void Register_Again_WritesNothing()
    {
        var registrar = new WindowsLinkRegistrar(_root!);
        registrar.Register(Executable);

        Assert.False(registrar.Register(Executable));
        Assert.True(registrar.Register(@"D:\Other\borea.exe"));
    }

    [WindowsFact("The registry exists only on Windows.")]
    public void Unregister_ThisExecutable_RemovesTheKey()
    {
        var registrar = new WindowsLinkRegistrar(_root!);
        registrar.Register(Executable);

        Assert.True(registrar.Unregister(Executable.ToUpperInvariant()));

        Assert.Null(_root!.OpenSubKey(@"Software\Classes\borea"));
    }

    [WindowsFact("The registry exists only on Windows.")]
    public void Unregister_OtherExecutable_KeepsTheKey()
    {
        var registrar = new WindowsLinkRegistrar(_root!);
        registrar.Register(@"D:\Other\borea.exe");

        using var empty = Registry.CurrentUser.CreateSubKey(_rootPath + @"\empty");

        Assert.False(registrar.Unregister(Executable));
        Assert.False(new WindowsLinkRegistrar(empty).Unregister(Executable));

        Assert.NotNull(_root!.OpenSubKey(@"Software\Classes\borea"));
    }
}
