using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Borea.App.Links;

/// <summary>Writes the borea URL protocol under Software\Classes of the given root, which is HKEY_CURRENT_USER outside tests.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsLinkRegistrar(RegistryKey root) : ILinkRegistrar
{
    private const string ClassPath = @"Software\Classes\borea";

    public bool Register(string executablePath)
    {
        using var protocol = root.CreateSubKey(ClassPath);
        using var icon = protocol.CreateSubKey("DefaultIcon");
        using var command = protocol.CreateSubKey(@"shell\open\command");
        var changed = Set(protocol, string.Empty, "URL:Borea link");
        changed |= Set(protocol, "URL Protocol", string.Empty);
        changed |= Set(icon, string.Empty, $"\"{executablePath}\",0");
        changed |= Set(command, string.Empty, Command(executablePath));
        return changed;
    }

    public bool Unregister(string executablePath)
    {
        using (var command = root.OpenSubKey(ClassPath + @"\shell\open\command"))
        {
            if (!string.Equals(command?.GetValue(null) as string, Command(executablePath), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        root.DeleteSubKeyTree(ClassPath, throwOnMissingSubKey: false);
        return true;
    }

    internal static string Command(string executablePath) => $"\"{executablePath}\" \"%1\"";

    private static bool Set(RegistryKey key, string name, string value)
    {
        if (key.GetValue(name) is string current && key.GetValueKind(name) == RegistryValueKind.String && current == value)
            return false;

        key.SetValue(name, value, RegistryValueKind.String);
        return true;
    }
}
