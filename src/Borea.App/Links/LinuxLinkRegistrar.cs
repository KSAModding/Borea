using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Borea.App.Links;

/// <summary>
/// Writes borea.desktop and the icon under the XDG data folder and makes the entry the default handler through xdg-mime.
/// </summary>
/// <param name="runXdgMime">Runs xdg-mime with the arguments and returns its output, or null when xdg-mime is missing.</param>
internal sealed class LinuxLinkRegistrar(string dataHome, Func<byte[]> icon, Func<IReadOnlyList<string>, string?> runXdgMime) : ILinkRegistrar
{
    internal const string DesktopFileName = "borea.desktop";

    private const string MimeType = "x-scheme-handler/borea";

    internal string DesktopFilePath => Path.Combine(dataHome, "applications", DesktopFileName);

    internal string IconPath => Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", "borea.png");

    /// <summary>Reads $XDG_DATA_HOME and falls back to ~/.local/share, as the XDG base directory spec says.</summary>
    internal static string DataHome(Func<string, string?> environment, string home)
        => environment("XDG_DATA_HOME") is { Length: > 0 } value && Path.IsPathRooted(value) ? value : Path.Combine(home, ".local", "share");

    public bool Register(string executablePath)
    {
        var changed = WriteIfDifferent(IconPath, icon());
        changed |= WriteIfDifferent(DesktopFilePath, Encoding.UTF8.GetBytes(DesktopEntry(executablePath)));

        if (runXdgMime(["query", "default", MimeType]) is { } current && current.Trim() != DesktopFileName)
        {
            runXdgMime(["default", DesktopFileName, MimeType]);
            changed = true;
        }

        return changed;
    }

    public bool Unregister(string executablePath)
    {
        if (!File.Exists(DesktopFilePath) || ExecLine(File.ReadAllText(DesktopFilePath)) != ExecLine(DesktopEntry(executablePath)))
            return false;

        File.Delete(DesktopFilePath);
        File.Delete(IconPath);
        return true;
    }

    internal string DesktopEntry(string executablePath) => string.Join('\n',
        "[Desktop Entry]",
        "Type=Application",
        "Name=Borea",
        "Comment=Opens borea:// links in Borea",
        $"Exec={Escape(Quote(executablePath)).Replace("%", "%%", StringComparison.Ordinal)} %u",
        $"Icon={Escape(IconPath)}",
        "Terminal=false",
        "NoDisplay=true",
        $"MimeType={MimeType};",
        string.Empty);

    private static string? ExecLine(string entry)
        => entry.Split('\n').FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal))?.TrimEnd('\r');

    /// <summary>Quotes an Exec argument, which escapes the characters the desktop entry spec reserves inside quotes.</summary>
    private static string Quote(string argument)
    {
        if (argument.Any(char.IsControl))
            throw new ArgumentException("The program path has a control character, which a desktop entry cannot hold.", nameof(argument));

        var quoted = new StringBuilder("\"");
        foreach (var character in argument)
        {
            if (character is '"' or '`' or '$' or '\\')
                quoted.Append('\\');
            quoted.Append(character);
        }

        return quoted.Append('"').ToString();
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static bool WriteIfDifferent(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return true;
    }
}
