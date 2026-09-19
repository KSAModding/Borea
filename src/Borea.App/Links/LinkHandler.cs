using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using Borea.Core.Logging;
using Microsoft.Win32;

namespace Borea.App.Links;

/// <summary>Registers this Borea as the borea:// handler. A failure is logged and never stops the App.</summary>
internal sealed class LinkHandler(ILinkRegistrar registrar, string executablePath)
{
    private static readonly TimeSpan XdgMimeTimeout = TimeSpan.FromSeconds(10);

    internal string ExecutablePath => executablePath;

    /// <summary>Null on macOS, whose handler comes from the app bundle, and for a process that is not a published borea program.</summary>
    internal static LinkHandler? ForThisProcess()
    {
        if (PublishedExecutable(Environment.ProcessPath, isSingleFile: string.IsNullOrEmpty(typeof(LinkHandler).Assembly.Location)) is not { } executable)
            return null;

        if (OperatingSystem.IsWindows())
            return new LinkHandler(new WindowsLinkRegistrar(Registry.CurrentUser), executable);

        if (OperatingSystem.IsLinux())
        {
            var dataHome = LinuxLinkRegistrar.DataHome(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            return new LinkHandler(new LinuxLinkRegistrar(dataHome, ReadIcon, RunXdgMime), executable);
        }

        return null;
    }

    /// <summary>
    /// The release ships the App as one single file, so a build that runs through the dotnet host
    /// or from a build folder is left out and does not take over the links of an installed Borea.
    /// </summary>
    internal static string? PublishedExecutable(string? processPath, bool isSingleFile)
    {
        if (!isSingleFile || string.IsNullOrEmpty(processPath))
            return null;

        return Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? null : processPath;
    }

    internal void Apply(bool enabled, IBoreaLog? log)
    {
        try
        {
            if (enabled && registrar.Register(executablePath))
                log?.Write($"Registered {executablePath} as the handler of borea:// links.");
            else if (!enabled && registrar.Unregister(executablePath))
                log?.Write($"Removed {executablePath} as the handler of borea:// links.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            log?.Write(enabled ? "Borea could not register as the handler of borea:// links." : "Borea could not remove itself as the handler of borea:// links.", exception);
        }
    }

    private static byte[] ReadIcon()
    {
        using var stream = typeof(LinkHandler).Assembly.GetManifestResourceStream("Borea.App.borea.png")
            ?? throw new InvalidOperationException("The Borea icon is missing from the App.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string? RunXdgMime(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("xdg-mime") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(XdgMimeTimeout))
            {
                process.Kill();
                throw new InvalidOperationException("xdg-mime did not end in time.");
            }

            return output.GetAwaiter().GetResult();
        }
    }
}
